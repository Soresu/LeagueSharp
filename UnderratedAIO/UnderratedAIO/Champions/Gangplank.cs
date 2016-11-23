using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Color = System.Drawing.Color;
using LeagueSharp;
using LeagueSharp.Common;
using SharpDX;
using SharpDX.Direct3D9;
using UnderratedAIO.Helpers;
using UnderratedAIO.Properties;
using Environment = UnderratedAIO.Helpers.Environment;
using Orbwalking = UnderratedAIO.Helpers.Orbwalking;

namespace UnderratedAIO.Champions
{
    internal class Gangplank
    {
        public static Menu config;
        public static Orbwalking.Orbwalker orbwalker;
        public static AutoLeveler autoLeveler;
        public static Spell Q, W, E, R;
        public static readonly Obj_AI_Hero player = ObjectManager.Player;
        public static bool justQ, justE, chain, blockedE, movingToBarrel, comboWithMiddle;
        public Vector3 ePos, thirdEpos;
        public const int BarrelExplosionRange = 325;
        public const int BarrelConnectionRange = 670;
        public List<Barrel> savedBarrels = new List<Barrel>();
        public List<CastedBarrel> castedBarrels = new List<CastedBarrel>();
        public double[] Rwave = new double[] { 50, 70, 90 };
        public double[] EDamage = new double[] { 60, 90, 120, 150, 180 };
        public Obj_AI_Minion NeedToBeDestroyed;
        private List<Render.Sprite> ExclamationMarks = new List<Render.Sprite>();

        public Gangplank()
        {
            InitGangPlank();
            InitMenu();
            //Game.PrintChat("<font color='#9933FF'>Soresu </font><font color='#FFFFFF'>- Gangplank</font>");
            Drawing.OnDraw += Game_OnDraw;
            Game.OnUpdate += Game_OnGameUpdate;
            Jungle.setSmiteSlot();
            HpBarDamageIndicator.DamageToUnit = ComboDamage;
            Obj_AI_Base.OnProcessSpellCast += Game_ProcessSpell;
            GameObject.OnCreate += GameObjectOnOnCreate;
            GameObject.OnDelete += GameObject_OnDelete;
            Spellbook.OnCastSpell += Spellbook_OnCastSpell;
            for (int i = 0; i < 6; i++)
            {
                var mark = new Render.Sprite(Resources.Exclamation_small_r, new Vector2(0, 0))
                {
                    Color = new ColorBGRA(255f, 255f, 255f, 0.8f)
                };
                mark.Add(i);
                ExclamationMarks.Add(mark);
            }
        }

        private void Spellbook_OnCastSpell(Spellbook sender, SpellbookCastSpellEventArgs args)
        {
            if (orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.Combo ||
                (orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.Mixed && config.Item("useeH", true).GetValue<bool>()))
            {
                return;
            }

            if (args.Slot == SpellSlot.E && config.Item("barrelCorrection", true).GetValue<bool>() &&
                Game.CursorPos.Distance(args.StartPosition) < 50)
            {
                var barrel =
                    GetBarrels()
                        .Where(
                            b =>
                                b.Distance(Game.CursorPos) > BarrelConnectionRange &&
                                b.Distance(Game.CursorPos) < BarrelConnectionRange * 2 &&
                                b.Position.Extend(Game.CursorPos, BarrelConnectionRange).Distance(args.StartPosition) <
                                BarrelExplosionRange)
                        .OrderBy(b => b.Distance(Game.CursorPos))
                        .FirstOrDefault();
                if (barrel != null && !blockedE)
                {
                    args.Process = false;
                    blockedE = true;
                    Utility.DelayAction.Add(
                        5, () =>
                        {
                            E.Cast(barrel.Position.Extend(Game.CursorPos, BarrelConnectionRange));
                            blockedE = false;
                        });
                }
            }
        }


        private void GameObject_OnDelete(GameObject sender, EventArgs args)
        {
            for (int i = 0; i < savedBarrels.Count; i++)
            {
                if (savedBarrels[i].barrel != null &&
                    (savedBarrels[i].barrel.NetworkId == sender.NetworkId || savedBarrels[i].barrel.IsDead))
                {
                    savedBarrels.RemoveAt(i);
                    return;
                }
            }
        }

        private void GameObjectOnOnCreate(GameObject sender, EventArgs args)
        {
            if (sender.Name == "Barrel")
            {
                savedBarrels.Add(new Barrel(sender as Obj_AI_Minion, System.Environment.TickCount));
            }
        }

        private IEnumerable<Obj_AI_Minion> GetBarrels()
        {
            return savedBarrels.Where(b => b.barrel != null).Select(b => b.barrel);
        }

        private bool KillableBarrel(Obj_AI_Base targetB,
            bool melee = false,
            float delay = 0,
            Obj_AI_Hero sender = null,
            float missileTravelTime = -1)
        {
            if (targetB.Health < 2)
            {
                return true;
            }
            if (sender == null)
            {
                sender = player;
            }
            if (missileTravelTime == -1)
            {
                missileTravelTime = GetQTime(targetB);
            }
            var barrel = savedBarrels.FirstOrDefault(b => b.barrel.NetworkId == targetB.NetworkId);
            if (barrel != null)
            {
                var t = System.Environment.TickCount - barrel.time - 2 * getEActivationDelay() * 1000;
                t = Math.Abs(Math.Min(t, 0)) + delay;
                if (t - ((melee ? (sender.AttackCastDelay) : missileTravelTime) * 1000) <= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private float GetQTime(Obj_AI_Base targetB)
        {
            return player.Distance(targetB) / (player.Crit < 0.05f ? 2600f : 3000f) + Q.Delay;
        }

        private void InitGangPlank()
        {
            Q = new Spell(SpellSlot.Q, 605f); //2600f
            Q.SetTargetted(0.25f, 2600f);
            W = new Spell(SpellSlot.W);
            E = new Spell(SpellSlot.E, 950);
            E.SetSkillshot(0.8f, 50, float.MaxValue, false, SkillshotType.SkillshotCircle);
            R = new Spell(SpellSlot.R);
            R.SetSkillshot(1f, 100, float.MaxValue, false, SkillshotType.SkillshotCircle);
        }

        private void Game_OnGameUpdate(EventArgs args)
        {
            if (FpsBalancer.CheckCounter())
            {
                return;
            }
            orbwalker.SetOrbwalkingPoint(Game.CursorPos);
            orbwalker.SetAttack(true);
            orbwalker.SetMovement(true);
            movingToBarrel = false;
            var barrels =
                GetBarrels()
                    .Where(
                        o =>
                            o.IsValid && !o.IsDead && o.Distance(player) < 3000 && o.SkinName == "GangplankBarrel" &&
                            o.GetBuff("gangplankebarrellife").Caster.IsMe)
                    .ToList();
            var QMana = Q.ManaCost < player.Mana;
            var shouldAAbarrel = (!Q.IsReady() ||
                                  config.Item("comboPrior", true).GetValue<StringList>().SelectedIndex == 1 ||
                                  (Q.IsReady() && !QMana) || !config.Item("useq", true).GetValue<bool>());


            if (thirdEpos.IsValid() && GetAmmo() > 0)
            {
                orbwalker.SetAttack(false);
                orbwalker.SetMovement(false);
                player.IssueOrder(GameObjectOrder.HoldPosition, player.ServerPosition);
                Console.WriteLine("Castolni kéne");
                if (E.Cast(thirdEpos))
                {
                    Console.WriteLine("\tDone");
                    //thirdEpos = Vector3.Zero;
                }
                return;
            }
            switch (orbwalker.ActiveMode)
            {
                case Orbwalking.OrbwalkingMode.Combo:
                    Combo(barrels, shouldAAbarrel, QMana);
                    break;
                case Orbwalking.OrbwalkingMode.Mixed:
                    Harass(barrels, shouldAAbarrel, QMana);
                    break;
                case Orbwalking.OrbwalkingMode.LaneClear:
                    Clear();
                    break;
                case Orbwalking.OrbwalkingMode.LastHit:
                    if (config.Item("useqLHH", true).GetValue<bool>() && !justE)
                    {
                        Lasthit(config.Item("useqLHHOOAA", true).GetValue<bool>());
                    }
                    break;
                default:
                    break;
            }
            if (config.Item("AutoR", true).GetValue<bool>() && R.IsReady())
            {
                foreach (var enemy in
                    HeroManager.Enemies.Where(
                        e =>
                            ((e.UnderTurret(true) &&
                              e.MaxHealth / 100 * config.Item("Rhealt", true).GetValue<Slider>().Value * 0.75f >
                              e.Health - Program.IncDamages.GetEnemyData(e.NetworkId).DamageTaken) ||
                             (!e.UnderTurret(true) &&
                              e.MaxHealth / 100 * config.Item("Rhealt", true).GetValue<Slider>().Value >
                              e.Health - Program.IncDamages.GetEnemyData(e.NetworkId).DamageTaken)) &&
                            e.HealthPercent > config.Item("RhealtMin", true).GetValue<Slider>().Value &&
                            e.IsValidTarget() && e.Distance(player) > 1500))
                {
                    var pred = Program.IncDamages.GetEnemyData(enemy.NetworkId);
                    if (pred != null && pred.DamageTaken < enemy.Health)
                    {
                        var ally =
                            HeroManager.Allies.OrderBy(a => a.Health).FirstOrDefault(a => enemy.Distance(a) < 1000);
                        if (ally != null)
                        {
                            var pos = Prediction.GetPrediction(enemy, 0.75f);
                            if (pos.CastPosition.Distance(enemy.Position) < 450 && pos.Hitchance >= HitChance.VeryHigh)
                            {
                                if (enemy.IsMoving)
                                {
                                    R.Cast(enemy.Position.Extend(pos.CastPosition, 450));
                                }
                                else
                                {
                                    R.Cast(enemy.ServerPosition);
                                }
                            }
                        }
                    }
                }
            }
            if (config.Item("EQtoCursor", true).GetValue<KeyBind>().Active && E.IsReady() && Q.IsReady())
            {
                orbwalker.SetMovement(false);
                var barrel =
                    GetBarrels()
                        .Where(
                            o =>
                                o.IsValid && !o.IsDead && o.Distance(player) < Q.Range &&
                                o.SkinName == "GangplankBarrel" && o.GetBuff("gangplankebarrellife").Caster.IsMe &&
                                KillableBarrel(o, false, -260))
                        .OrderBy(o => o.Distance(Game.CursorPos))
                        .FirstOrDefault();
                if (barrel != null)
                {
                    var cp = Game.CursorPos;
                    var cursorPos = barrel.Distance(cp) > BarrelConnectionRange
                        ? barrel.Position.Extend(cp, BarrelConnectionRange)
                        : cp;
                    var points =
                        CombatHelper.PointsAroundTheTarget(player.Position, E.Range - 200, 15, 6)
                            .Where(p => p.Distance(player.Position) < E.Range);
                    var cursorPos2 = cursorPos.Distance(cp) > BarrelConnectionRange
                        ? cursorPos.Extend(cp, BarrelConnectionRange)
                        : cp;
                    var middle = GetMiddleBarrel(barrel, points, cursorPos);
                    var threeBarrel = cursorPos.Distance(cp) > BarrelExplosionRange && GetAmmo() >= 2 &&
                                      Game.CursorPos.Distance(player.Position) < E.Range && middle.IsValid();
                    var firsDelay = threeBarrel ? 500 : 265;
                    if (cursorPos.IsValid() && cursorPos.Distance(player.Position) < E.Range)
                    {
                        E.Cast(threeBarrel ? middle : cursorPos);
                        Utility.DelayAction.Add(firsDelay, () => Q.CastOnUnit(barrel));
                        if (threeBarrel)
                        {
                            if (player.IsMoving)
                            {
                                player.IssueOrder(GameObjectOrder.Stop, player.Position);
                            }
                            Utility.DelayAction.Add(801, () => E.Cast(middle.Extend(cp, BarrelConnectionRange)));
                            Utility.DelayAction.Add(650, () => Q.CastOnUnit(barrel));
                        }
                        else
                        {
                            if (Orbwalking.CanMove(100))
                            {
                                orbwalker.SetMovement(true);
                                Orbwalking.MoveTo(Game.CursorPos);
                            }
                        }
                    }
                }
                else
                {
                    if (Orbwalking.CanMove(100))
                    {
                        orbwalker.SetMovement(true);
                        Orbwalking.MoveTo(Game.CursorPos, 60);
                    }
                }
            }
            else if (config.Item("EQtoCursor", true).GetValue<KeyBind>().Active)
            {
                if (Orbwalking.CanMove(100))
                {
                    orbwalker.SetMovement(true);
                    Orbwalking.MoveTo(Game.CursorPos, 60);
                }
            }
            if (config.Item("QbarrelCursor", true).GetValue<KeyBind>().Active)
            {
                var meleeRangeBarrel =
                    GetBarrels()
                        .OrderBy(o => o.Distance(Game.CursorPos))
                        .FirstOrDefault(
                            o =>
                                o.Health > 1 && o.Distance(player) < Orbwalking.GetRealAutoAttackRange(o) &&
                                !KillableBarrel(o, true, 265));
                if (meleeRangeBarrel != null && Orbwalking.CanAttack() && Q.IsReady())
                {
                    orbwalker.ForceTarget(meleeRangeBarrel);
                    return;
                }
                var barrel =
                    GetBarrels()
                        .Where(
                            o =>
                                o.IsValid && !o.IsDead && o.Distance(player) < Q.Range &&
                                o.SkinName == "GangplankBarrel" && o.GetBuff("gangplankebarrellife").Caster.IsMe &&
                                KillableBarrel(o))
                        .OrderBy(o => o.Distance(Game.CursorPos))
                        .FirstOrDefault();
                if (barrel != null && Q.IsReady())
                {
                    Q.CastOnUnit(barrel);
                }
                else
                {
                    Orbwalking.MoveTo(Game.CursorPos, 80);
                }
            }
            if (NeedToBeDestroyed != null && NeedToBeDestroyed.IsValidTarget() && Orbwalking.CanAttack() &&
                NeedToBeDestroyed.IsInAttackRange())
            {
                player.IssueOrder(GameObjectOrder.AttackUnit, NeedToBeDestroyed);
            }
            if (config.Item("AutoQBarrel", true).GetValue<bool>() && !movingToBarrel)
            {
                var target = DrawHelper.GetBetterTarget(
                    E.Range + BarrelExplosionRange / 2f, TargetSelector.DamageType.Physical, true,
                    HeroManager.Enemies.Where(h => h.IsInvulnerable));
                if (target != null && BlowUpBarrel(barrels, shouldAAbarrel, false, target))
                {
                    if (!chain)
                    {
                        chain = true;
                        Utility.DelayAction.Add(450, () => chain = false);
                    }
                }
                if (Q.IsReady())
                {
                    foreach (var barrel in
                        barrels.Where(b => KillableBarrel(b) && b.CountEnemiesInRange(BarrelExplosionRange - 25) > 0))
                    {
                        Console.WriteLine("#1 : " + barrel.CountEnemiesInRange(BarrelExplosionRange));
                        Q.Cast(barrel);
                        return;
                    }
                }
            }
            for (int i = 0; i < castedBarrels.Count; i++)
            {
                if (castedBarrels[i].shouldDie())
                {
                    castedBarrels.RemoveAt(i);
                    break;
                }
            }
        }

        private Vector3 GetMiddleBarrel(Obj_AI_Minion barrel, IEnumerable<Vector3> points, Vector3 cursorPos)
        {
            var middle =
                points.Where(
                    p =>
                        !p.IsWall() && p.Distance(barrel.Position) < BarrelConnectionRange &&
                        p.Distance(barrel.Position) > BarrelExplosionRange &&
                        p.Distance(cursorPos) < BarrelConnectionRange && p.Distance(cursorPos) > BarrelExplosionRange &&
                        p.Distance(barrel.Position) + p.Distance(cursorPos) > BarrelExplosionRange * 2 - 100)
                    .OrderByDescending(p => p.CountEnemiesInRange(BarrelExplosionRange))
                    .ThenByDescending(p => p.Distance(barrel.Position))
                    .FirstOrDefault();
            return middle;
        }

        private void Lasthit(bool outOfAA = false)
        {
            if (player.IsWindingUp)
            {
                return;
            }
            if (Q.IsReady())
            {
                var mini =
                    MinionManager.GetMinions(Q.Range, MinionTypes.All, MinionTeam.NotAlly)
                        .Where(
                            m =>
                                m.Health < Q.GetDamage(m) && m.SkinName != "GangplankBarrel" &&
                                (m.Distance(player) > Orbwalking.GetRealAutoAttackRange(m) || !outOfAA))
                        .OrderByDescending(m => m.MaxHealth)
                        .ThenByDescending(m => m.Distance(player))
                        .FirstOrDefault();

                if (mini != null && !justE)
                {
                    Q.CastOnUnit(mini);
                }
            }
        }


        private void Harass(List<Obj_AI_Minion> barrels, bool shouldAAbarrel, bool qMana)
        {
            float perc = config.Item("minmanaH", true).GetValue<Slider>().Value / 100f;
            if (player.Mana < player.MaxMana * perc)
            {
                return;
            }
            Obj_AI_Hero target = DrawHelper.GetBetterTarget(
                E.Range + BarrelExplosionRange / 2f, TargetSelector.DamageType.Physical);

            if (config.Item("useqLHH", true).GetValue<bool>())
            {
                Lasthit(config.Item("useqLHHOOAA", true).GetValue<bool>());
            }

            if (target == null || Environment.Minion.KillableMinion(player.AttackRange + 50))
            {
                return;
            }
            var dontQ = false;
            //Blow up barrels
            if (config.Item("useqH", true).GetValue<bool>() &&
                BlowUpBarrel(barrels, shouldAAbarrel, config.Item("movetoBarrel", true).GetValue<bool>(), target))
            {
                if (!chain)
                {
                    chain = true;
                    Utility.DelayAction.Add(450, () => chain = false);
                }
                return;
            }

            //Cast E to chain
            if (E.IsReady() && GetAmmo() > 0 && !justQ && !chain && config.Item("useeH", true).GetValue<bool>() &&
                config.Item("eStacksH", true).GetValue<Slider>().Value < GetAmmo())
            {
                if (barrels.Any())
                {
                    var bestEMelee = GetEPos(barrels, target, true);
                    comboWithMiddle = false;
                    var bestEQ = GetEPos(barrels, target, false);
                    if (bestEMelee.Item1.IsValid() && shouldAAbarrel)
                    {
                        dontQ = true;
                        E.Cast(bestEMelee.Item1);
                    }
                    else if (bestEQ.Item1.IsValid() && config.Item("useqH", true).GetValue<bool>() && Q.IsReady())
                    {
                        dontQ = true;
                        if (comboWithMiddle && bestEQ.Item1.Distance(player.Position) < E.Range && E.IsReady() &&
                            Q.CastOnUnit(barrels.FirstOrDefault(b => b.Position.Distance(bestEQ.Item2) < 10)))
                        {
                            Console.WriteLine("#2 : " + bestEQ.Item1.CountEnemiesInRange(BarrelExplosionRange));
                            thirdEpos = bestEQ.Item1;
                            Utility.DelayAction.Add(500, () => thirdEpos = Vector3.Zero);
                        }
                        else
                        {
                            E.Cast(bestEQ.Item1);
                        }
                    }
                }
            }
            if (config.Item("useqH", true).GetValue<bool>() && Q.IsReady() && !dontQ)
            {
                Q.CastOnUnit(target);
            }
        }

        private void Clear()
        {
            float perc = config.Item("minmana", true).GetValue<Slider>().Value / 100f;
            if (player.Mana < player.MaxMana * perc)
            {
                return;
            }

            var barrel =
                GetBarrels()
                    .FirstOrDefault(
                        o =>
                            o.IsValid && !o.IsDead && o.Distance(player) < Q.Range && o.SkinName == "GangplankBarrel" &&
                            o.GetBuff("gangplankebarrellife").Caster.IsMe &&
                            Environment.Minion.countMinionsInrange(o.Position, BarrelExplosionRange) >= 1);
            if (barrel != null)
            {
                var minis = MinionManager.GetMinions(
                    barrel.Position, BarrelExplosionRange, MinionTypes.All, MinionTeam.NotAlly);
                var Killable = minis.Where(e => Q.GetDamage(e) + ItemHandler.GetSheenDmg(e) >= e.Health && e.Health > 3);
                if (Q.IsReady() && config.Item("useqLCQB", true).GetValue<bool>() && KillableBarrel(barrel) &&
                    Killable.Any(t => HealthPrediction.LaneClearHealthPrediction(t, 1000) <= 0))
                {
                    Q.CastOnUnit(barrel);
                }

                if (config.Item("ePrep", true).GetValue<bool>())
                {
                    if (Q.IsReady() && config.Item("useqLCQB", true).GetValue<bool>() && minis.Count == Killable.Count() &&
                        KillableBarrel(barrel))
                    {
                        Q.CastOnUnit(barrel);
                    }
                    else
                    {
                        foreach (var m in
                            minis.Where(e => Q.GetDamage(e) + ItemHandler.GetSheenDmg(e) <= e.Health && e.Health > 3)
                                .OrderBy(t => t.Distance(player))
                                .ThenByDescending(t => t.Health))
                        {
                            orbwalker.ForceTarget(m);
                            return;
                        }
                    }
                }
                else if (Q.IsReady() && config.Item("useqLCQB", true).GetValue<bool>() && KillableBarrel(barrel) &&
                         minis.Count >= config.Item("eMinHit", true).GetValue<Slider>().Value)
                {
                    Q.CastOnUnit(barrel);
                }

                return;
            }

            if (config.Item("useqLC", true).GetValue<bool>() && !justE)
            {
                Lasthit(config.Item("useqLCOOAA", true).GetValue<bool>());
            }
            if (config.Item("useeLC", true).GetValue<bool>() && E.IsReady() &&
                config.Item("eStacksLC", true).GetValue<Slider>().Value < GetAmmo())
            {
                MinionManager.FarmLocation bestPositionE =
                    E.GetCircularFarmLocation(
                        MinionManager.GetMinions(
                            ObjectManager.Player.ServerPosition, E.Range, MinionTypes.All, MinionTeam.NotAlly),
                        BarrelExplosionRange);

                if (bestPositionE.MinionsHit >= config.Item("eMinHit", true).GetValue<Slider>().Value &&
                    bestPositionE.Position.Distance(ePos) > 400)
                {
                    E.Cast(bestPositionE.Position);
                }
            }
        }

        private void Combo(List<Obj_AI_Minion> barrels, bool shouldAAbarrel, bool Qmana)
        {
            var target = DrawHelper.GetBetterTarget(
                E.Range + BarrelExplosionRange / 2f, TargetSelector.DamageType.Physical, true,
                HeroManager.Enemies.Where(h => h.IsInvulnerable));
            if (target == null)
            {
                return;
            }
            var ignitedmg = (float) player.GetSummonerSpellDamage(target, Damage.SummonerSpell.Ignite);
            bool hasIgnite = player.Spellbook.CanUseSpell(player.GetSpellSlot("SummonerDot")) == SpellState.Ready;
            if (config.Item("useIgnite", true).GetValue<bool>() && ignitedmg > target.Health && hasIgnite &&
                !CombatHelper.CheckCriticalBuffs(target) && !Q.IsReady() && !justQ)
            {
                player.Spellbook.CastSpell(player.GetSpellSlot("SummonerDot"), target);
            }
            var data = Program.IncDamages.GetAllyData(player.NetworkId);
            if (!data.AnyCC &&
                (config.Item("usew", true).GetValue<Slider>().Value / 100f) * player.MaxHealth >
                player.Health - data.DamageTaken && player.CountEnemiesInRange(500) > 0)
            {
                W.Cast();
            }
            if (R.IsReady() && config.Item("user", true).GetValue<bool>())
            {
                var Rtarget =
                    HeroManager.Enemies.FirstOrDefault(e => e.HealthPercent < 50 && e.CountAlliesInRange(660) > 0);
                if (Rtarget != null)
                {
                    R.CastIfWillHit(Rtarget, config.Item("Rmin", true).GetValue<Slider>().Value);
                }
            }
            if (config.Item("useItems").GetValue<bool>())
            {
                ItemHandler.UseItems(target, config, ComboDamage(target), config.Item("AutoW", true).GetValue<bool>());
            }
            var dontQ = false;

            //Blow up barrels
            if (BlowUpBarrel(barrels, shouldAAbarrel, config.Item("movetoBarrel", true).GetValue<bool>(), target))
            {
                if (!chain)
                {
                    chain = true;
                    Utility.DelayAction.Add(450, () => chain = false);
                }
                return;
            }

            //Cast E to chain
            if (E.IsReady() && GetAmmo() > 0 && !justQ && !chain &&
                config.Item("detoneateTarget", true).GetValue<bool>())
            {
                if (barrels.Any())
                {
                    var bestEMelee = GetEPos(barrels, target, true);
                    comboWithMiddle = false;
                    var bestEQ = GetEPos(barrels, target, false);
                    if (bestEMelee.Item1.IsValid() && shouldAAbarrel)
                    {
                        dontQ = true;
                        E.Cast(bestEMelee.Item1);
                    }
                    else if (bestEQ.Item1.IsValid() && config.Item("useq", true).GetValue<bool>() && Q.IsReady())
                    {
                        dontQ = true;
                        if (comboWithMiddle && bestEQ.Item1.Distance(player.Position) < E.Range && E.IsReady() &&
                            Q.CastOnUnit(barrels.FirstOrDefault(b => b.Position.Distance(bestEQ.Item2) < 10)))
                        {
                            Console.WriteLine("#3 : " + bestEQ.Item1.CountEnemiesInRange(BarrelExplosionRange));
                            thirdEpos = bestEQ.Item1;
                            Utility.DelayAction.Add(500, () => thirdEpos = Vector3.Zero);
                        }
                        else
                        {
                            E.Cast(bestEQ.Item1);
                        }
                    }
                }
            }


            if (config.Item("useeAlways", true).GetValue<bool>() && E.IsReady() && player.Distance(target) < E.Range &&
                !justE && target.Health > Q.GetDamage(target) + player.GetAutoAttackDamage(target) &&
                Orbwalking.CanMove(100) && config.Item("eStacksC", true).GetValue<Slider>().Value < GetAmmo())
            {
                CastE(target, barrels);
            }
            var Qbarrelsb =
                GetBarrels()
                    .FirstOrDefault(
                        o =>
                            o.Distance(player) < Q.Range &&
                            o.Distance(target) < BarrelConnectionRange + BarrelExplosionRange);
            if (Qbarrelsb != null && GetAmmo() > 0 && Q.IsReady() && target.Health > Q.GetDamage(target))
            {
                dontQ = true;
            }
            if (config.Item("useq", true).GetValue<bool>() && Q.CanCast(target) && Orbwalking.CanMove(100) && !justE &&
                (!config.Item("useqBlock", true).GetValue<bool>() || !dontQ))
            {
                CastQonHero(target, barrels);
            }
        }

        private bool BlowUpBarrel(List<Obj_AI_Minion> barrels,
            bool shouldAAbarrel,
            bool movetoBarrel,
            Obj_AI_Hero target)
        {
            if (barrels.Any())
            {
                var moveDist = movetoBarrel ? config.Item("movetoBarrelDist", true).GetValue<Slider>().Value : 0;
                var bestBarrelMelee = GetBestBarrel(barrels, true, target, moveDist);
                var bestBarrelQ = GetBestBarrel(barrels, false, target);
                if (bestBarrelMelee != null && shouldAAbarrel &&
                    HeroManager.Enemies.FirstOrDefault(
                        e => e.Distance(bestBarrelMelee) < player.Distance(bestBarrelMelee)) == null)
                {
                    if (Orbwalking.GetRealAutoAttackRange(bestBarrelMelee) < player.Distance(bestBarrelMelee))
                    {
                        if (orbwalker.ActiveMode != Orbwalking.OrbwalkingMode.Combo &&
                            orbwalker.ActiveMode != Orbwalking.OrbwalkingMode.Mixed)
                        {
                            Orbwalking.Orbwalk(bestBarrelMelee, bestBarrelMelee.LeashedPosition);
                        }
                        movingToBarrel = true;
                        orbwalker.SetOrbwalkingPoint(bestBarrelMelee.Position);
                        orbwalker.SetAttack(false);
                    }
                    else
                    {
                        if (KillableBarrel(bestBarrelMelee, true))
                        {
                            if (orbwalker.ActiveMode != Orbwalking.OrbwalkingMode.Combo &&
                                orbwalker.ActiveMode != Orbwalking.OrbwalkingMode.Mixed)
                            {
                                Orbwalking.Orbwalk(bestBarrelMelee, bestBarrelMelee.LeashedPosition);
                            }
                            orbwalker.ForceTarget(bestBarrelMelee);
                        }
                    }
                    return true;
                }
                if (bestBarrelQ != null && config.Item("useq", true).GetValue<bool>())
                {
                    Console.WriteLine("#4 : " + bestBarrelQ.CountEnemiesInRange(BarrelExplosionRange));
                    Q.CastOnUnit(bestBarrelQ);
                    return true;
                }
            }
            return false;
        }

        private bool EnemiesInBarrelRange(Vector3 barrel, float delay, Obj_AI_Base target)
        {
            var pred = Prediction.GetPrediction(target, delay * 0.75f);
            if (pred.UnitPosition.Distance(barrel) < BarrelExplosionRange &&
                HeroManager.Enemies.Count(
                    enemy => enemy.IsValidTarget() && enemy.Distance(barrel) < BarrelExplosionRange) > 0)
            {
                return true;
            }
            return false;
        }

        private Obj_AI_Minion GetBestBarrel(List<Obj_AI_Minion> barrels,
            bool isMelee,
            Obj_AI_Hero target,
            float moveDist = 0f)
        {
            var meleeBarrels =
                barrels.Where(
                    b =>
                        player.Distance(b) <
                        (isMelee
                            ? Orbwalking.GetRealAutoAttackRange(b) +
                              (CombatHelper.IsFacing(player, b.Position) ? moveDist : 0f)
                            : Q.Range) && KillableBarrel(b, isMelee, 265));
            var secondaryBarrels =
                barrels.Select(b => b.Position).Concat(castedBarrels.Where(c => c.pos.IsValid()).Select(c => c.pos));
            var meleeDelay = isMelee ? 0.25f : 0;
            if (moveDist > 0f)
            {
                meleeDelay -= (moveDist / player.MoveSpeed);
            }
            foreach (var melee in meleeBarrels)
            {
                var secondBarrels =
                    secondaryBarrels.Where(
                        b =>
                            !meleeBarrels.Any(n => b.Distance(n.Position) < 10) &&
                            melee.Distance(b) < BarrelConnectionRange);
                foreach (var second in secondBarrels)
                {
                    var thirdBarrels =
                        secondaryBarrels.Where(
                            b =>
                                !secondBarrels.Any(n => b.Distance(n) < 10) &&
                                !meleeBarrels.Any(n => b.Distance(n.Position) < 10) &&
                                second.Distance(b) < BarrelConnectionRange);
                    foreach (var third in thirdBarrels)
                    {
                        if (EnemiesInBarrelRange(third, 1.25f - meleeDelay, target))
                        {
                            return melee;
                        }
                    }
                    if (EnemiesInBarrelRange(second, 1f - meleeDelay, target))
                    {
                        return melee;
                    }
                }
                if (EnemiesInBarrelRange(melee.Position, 0.75f - meleeDelay, target))
                {
                    return melee;
                }
            }
            return null;
        }

        private Vector3 GetE(Vector3 barrel, Obj_AI_Hero target, float delay, List<Vector3> barrels)
        {
            var enemies =
                HeroManager.Enemies.Where(
                    e =>
                        e.IsValidTarget(1650) && e.Distance(barrel) > BarrelExplosionRange &&
                        !barrels.Any(b => b.Distance(e.Position) < BarrelExplosionRange));
            var targetPred = Prediction.GetPrediction(target, delay);
            var pos = Vector3.Zero;
            pos =
                GetBarrelPoints(barrel)
                    .Where(
                        p =>
                            !p.IsWall() && p.Distance(barrel) < BarrelConnectionRange &&
                            p.Distance(player.Position) < E.Range &&
                            barrels.Count(b => b.Distance(p) < BarrelExplosionRange) == 0 &&
                            targetPred.CastPosition.Distance(p) < BarrelExplosionRange / 2f &&
                            target.Distance(p) < BarrelExplosionRange)
                    .OrderByDescending(p => enemies.Count(e => e.Distance(p) < BarrelExplosionRange))
                    .ThenBy(p => p.Distance(targetPred.CastPosition))
                    .FirstOrDefault();
            return pos;
        }

        private Vector3 GetMiddleE(Vector3 barrel, Obj_AI_Hero target, float delay, List<Vector3> barrels)
        {
            if (GetAmmo() < 2)
            {
                return Vector3.Zero;
            }
            var enemies =
                HeroManager.Enemies.Where(
                    e =>
                        e.IsValidTarget(1650) && e.Distance(barrel) > BarrelExplosionRange &&
                        !barrels.Any(b => b.Distance(e.Position) < BarrelExplosionRange));
            var targetPred = Prediction.GetPrediction(target, delay);
            var pos = Vector3.Zero;
            pos =
                GetBarrelPoints(barrel)
                    .Where(
                        p =>
                            p.Distance(barrel) < BarrelConnectionRange && p.Distance(player.Position) < E.Range &&
                            barrels.Count(b => b.Distance(p) < BarrelExplosionRange) == 0 &&
                            targetPred.CastPosition.Distance(p) < (BarrelExplosionRange - 25) * 2)
                    .OrderByDescending(p => enemies.Count(e => e.Distance(p) < BarrelExplosionRange))
                    .ThenBy(p => p.Distance(targetPred.CastPosition))
                    .FirstOrDefault();
            return pos;
        }

        private Tuple<Vector3, Vector3> GetEPos(List<Obj_AI_Minion> barrels, Obj_AI_Hero target, bool isMelee)
        {
            var barrelPositions =
                barrels.Select(b => b.Position)
                    .Concat(castedBarrels.Where(c => c.pos.IsValid()).Select(c => c.pos))
                    .ToList();
            var moveDist = config.Item("movetoBarrel", true).GetValue<bool>()
                ? config.Item("movetoBarrelDist", true).GetValue<Slider>().Value
                : 0;
            var barrelsInCloseRange =
                barrels.Where(
                    b =>
                        player.Distance(b) < target.Distance(b) &&
                        player.Distance(b) <
                        (isMelee
                            ? Orbwalking.GetRealAutoAttackRange(b) +
                              (CombatHelper.IsFacing(player, b.Position) ? moveDist : 0f)
                            : Q.Range) && KillableBarrel(b, isMelee, -265))
                    .Select(b => b.Position)
                    .Concat(castedBarrels.Select(c => c.pos));
            var meleeDelay = isMelee ? 0.25f : 0;

            foreach (var melee in barrelsInCloseRange)
            {
                var secondPos = GetE(melee, target, 1.265f - meleeDelay, barrelPositions);
                var middle = GetMiddleE(melee, target, 1.265f - meleeDelay, barrelPositions);
                if (secondPos.IsValid())
                {
                    return new Tuple<Vector3, Vector3>(secondPos, melee);
                }
                var secondBarrels = barrelPositions.Where(b => melee.Distance(b) < BarrelConnectionRange).ToList();
                foreach (var secondBarrel in secondBarrels)
                {
                    var thirdE = GetE(secondBarrel, target, 1.265f - meleeDelay, barrelPositions);
                    if (thirdE.IsValid() && config.Item("threeBarrel", true).GetValue<bool>())
                    {
                        comboWithMiddle = true;
                        return new Tuple<Vector3, Vector3>(thirdE, melee);
                    }
                }
                if (middle.IsValid())
                {
                    return new Tuple<Vector3, Vector3>(middle, melee);
                }
            }
            return new Tuple<Vector3, Vector3>(Vector3.Zero, Vector3.Zero);
        }


        private void CastQonHero(Obj_AI_Hero target, List<Obj_AI_Minion> barrels)
        {
            if (barrels.FirstOrDefault(b => target.Distance(b.Position) < BarrelExplosionRange) != null &&
                target.Health > Q.GetDamage(target))
            {
                return;
            }
            Q.CastOnUnit(target);
        }

        private void CastE(Obj_AI_Hero target, List<Obj_AI_Minion> barrels)
        {
            if (barrels.Count(b => b.CountEnemiesInRange(BarrelConnectionRange) > 0) < 1)
            {
                if (config.Item("useeAlways", true).GetValue<bool>())
                {
                    CastEtarget(target);
                }
                return;
            }
            var enemies =
                HeroManager.Enemies.Where(e => e.IsValidTarget() && e.Distance(player) < E.Range)
                    .Select(e => Prediction.GetPrediction(e, 0.35f));
            List<Vector3> points = new List<Vector3>();
            foreach (var barrel in
                barrels.Where(b => b.Distance(player) < Q.Range && KillableBarrel(b)))
            {
                if (barrel != null)
                {
                    var newP = GetBarrelPoints(barrel.Position).Where(p => !p.IsWall());
                    if (newP.Any())
                    {
                        points.AddRange(newP.Where(p => p.Distance(player.Position) < E.Range));
                    }
                }
            }
            var bestPoint =
                points.Where(b => enemies.Count(e => e.UnitPosition.Distance(b) < BarrelExplosionRange) > 0)
                    .OrderByDescending(b => enemies.Count(e => e.UnitPosition.Distance(b) < BarrelExplosionRange))
                    .FirstOrDefault();
            if (bestPoint.IsValid() &&
                !savedBarrels.Any(b => b.barrel.Position.Distance(bestPoint) < BarrelConnectionRange))
            {
                E.Cast(bestPoint);
            }
        }

        private void CastEtarget(Obj_AI_Hero target)
        {
            var ePred = Prediction.GetPrediction(target, 1);
            var pos = target.Position.Extend(ePred.CastPosition, BarrelExplosionRange);
            if (pos.Distance(ePos) > 400 && !justE)
            {
                E.Cast(pos);
            }
        }

        private void Game_OnDraw(EventArgs args)
        {
            DrawHelper.DrawCircle(config.Item("drawqq", true).GetValue<Circle>(), Q.Range);
            DrawHelper.DrawCircle(config.Item("drawee", true).GetValue<Circle>(), E.Range);
            var drawecr = config.Item("draweecr", true).GetValue<Circle>();
            if (drawecr.Active)
            {
                foreach (var barrel in GetBarrels().Where(b => b.Distance(player) < E.Range + BarrelConnectionRange))
                {
                    Render.Circle.DrawCircle(barrel.Position, BarrelConnectionRange, drawecr.Color, 7);
                }
            }
            if (config.Item("drawMTB", true).GetValue<bool>())
            {
                Render.Circle.DrawCircle(
                    player.Position,
                    Math.Max(
                        config.Item("movetoBarrelDist", true).GetValue<Slider>().Value + 200 - player.BoundingRadius -
                        60, 250), Color.DarkSlateGray, 5);
            }
            HpBarDamageIndicator.Enabled = config.Item("drawcombo", true).GetValue<bool>();
            if (config.Item("drawW", true).GetValue<bool>())
            {
                if (W.IsReady() && player.HealthPercent < 100)
                {
                    float Heal = new int[] { 50, 75, 100, 125, 150 }[W.Level - 1] +
                                 (player.MaxHealth - player.Health) * 0.15f + player.FlatMagicDamageMod * 0.9f;
                    float mod = Math.Max(100f, player.Health + Heal) / player.MaxHealth;
                    float xPos = (float) ((double) player.HPBarPosition.X + 36 + 103.0 * mod);
                    Drawing.DrawLine(
                        xPos, player.HPBarPosition.Y + 8, xPos, (float) ((double) player.HPBarPosition.Y + 17), 2f,
                        Color.Coral);
                }
            }
            var tokens = player.GetBuff("gangplankbilgewatertoken");
            if (player.InFountain() && config.Item("drawQpass", true).GetValue<bool>() && tokens != null &&
                tokens.Count > 500)
            {
                var second = DateTime.Now.Second.ToString();
                var time = int.Parse(second[second.Length - 1].ToString());
                var color = Color.DeepSkyBlue;
                if (time >= 3 && time < 6)
                {
                    color = Color.GreenYellow;
                }
                if (time >= 6 && time < 8)
                {
                    color = Color.Yellow;
                }
                if (time >= 8)
                {
                    color = Color.Orange;
                }
                Drawing.DrawText(
                    Drawing.WorldToScreen(Game.CursorPos).X - 150, Drawing.WorldToScreen(Game.CursorPos).Y - 50, color,
                    "Spend your Silver Serpents, landlubber!");
            }
            if (config.Item("drawKillableSL", true).GetValue<StringList>().SelectedIndex != 0 && R.IsReady())
            {
                var text = new List<string>();
                foreach (var enemy in HeroManager.Enemies.Where(e => e.IsValidTarget()))
                {
                    if (getRDamage(enemy) > enemy.Health)
                    {
                        text.Add(enemy.ChampionName + "(" + Math.Ceiling(enemy.Health / Rwave[R.Level - 1]) + " wave)");
                    }
                }
                if (text.Count > 0)
                {
                    var result = string.Join(", ", text);
                    switch (config.Item("drawKillableSL", true).GetValue<StringList>().SelectedIndex)
                    {
                        case 2:
                            drawText(2, result);
                            break;
                        case 1:
                            drawText(1, result);
                            break;
                        default:
                            return;
                    }
                }
            }

            try
            {
                if (Q.IsReady() && config.Item("drawEQ", true).GetValue<bool>())
                {
                    var points =
                        CombatHelper.PointsAroundTheTarget(player.Position, E.Range - 200, 15, 6)
                            .Where(p => p.Distance(player.Position) < E.Range);


                    var barrel =
                        GetBarrels()
                            .Where(o => o.IsValid && !o.IsDead && o.Distance(player) < Q.Range && KillableBarrel(o))
                            .OrderBy(o => o.Distance(Game.CursorPos))
                            .FirstOrDefault();
                    if (barrel != null)
                    {
                        var cp = Game.CursorPos;
                        var cursorPos = barrel.Distance(cp) > BarrelConnectionRange
                            ? barrel.Position.Extend(cp, BarrelConnectionRange)
                            : cp;
                        var cursorPos2 = cursorPos.Distance(cp) > BarrelConnectionRange
                            ? cursorPos.Extend(cp, BarrelConnectionRange)
                            : cp;
                        var middle = GetMiddleBarrel(barrel, points, cursorPos);
                        var threeBarrel = cursorPos.Distance(cp) > BarrelExplosionRange && GetAmmo() >= 2 &&
                                          cursorPos2.Distance(player.Position) < E.Range && middle.IsValid();
                        if (threeBarrel)
                        {
                            Render.Circle.DrawCircle(
                                middle.Extend(cp, BarrelConnectionRange), BarrelExplosionRange, Color.DarkOrange, 6);
                            Render.Circle.DrawCircle(middle, BarrelExplosionRange, Color.DarkOrange, 6);
                            Drawing.DrawLine(
                                Drawing.WorldToScreen(barrel.Position),
                                Drawing.WorldToScreen(middle.Extend(barrel.Position, BarrelExplosionRange)), 2,
                                Color.DarkOrange);
                        }
                        else if (GetAmmo() > 0)
                        {
                            Drawing.DrawLine(
                                Drawing.WorldToScreen(barrel.Position),
                                Drawing.WorldToScreen(cursorPos.Extend(barrel.Position, BarrelExplosionRange)), 2,
                                Color.DarkOrange);
                            Render.Circle.DrawCircle(cursorPos, BarrelExplosionRange, Color.DarkOrange, 6);
                        }
                    }
                }
            }
            catch (Exception) {}
            foreach (var ex in ExclamationMarks)
            {
                ex.Hide();
            }
            if (config.Item("drawWcd", true).GetValue<bool>())
            {
                var bar = GetBarrels().OrderBy(b => b.Distance(player.Position)).Take(6);
                for (int i = 0; i < bar.Count(); i++)
                {
                    var barrelData = savedBarrels[i];
                    float time =
                        Math.Min(
                            System.Environment.TickCount - barrelData.time -
                            barrelData.barrel.Health * getEActivationDelay() * 1000f, 0) / 1000f;
                    if (time < 0)
                    {
                        Drawing.DrawText(
                            barrelData.barrel.HPBarPosition.X - -20, barrelData.barrel.HPBarPosition.Y - 20,
                            Color.DarkOrange, string.Format("{0:0.00}", time).Replace("-", ""));
                    }
                    try
                    {
                        var pos = Drawing.WorldToScreen(barrelData.barrel.Position);
                        var percent = 1f - (Math.Abs(time) / (barrelData.barrel.Health * getEActivationDelay()));
                        var diff = (Drawing.WorldToScreen(barrelData.barrel.Position).X - Drawing.Width / 2f) /
                                   (Drawing.Width / 2f);
                        ExclamationMarks[i].Position = new Vector2(pos.X - 25 + 10 * diff, pos.Y - 265);
                        var orig = ExclamationMarks[i].Color;
                        ExclamationMarks[i].Color = new ColorBGRA(
                            orig.R, orig.G, orig.B, Math.Max(Math.Min(percent, 0.7f), 0.3f));
                        ExclamationMarks[i].Show();
                    }
                    catch (Exception)
                    {
                        Console.WriteLine("Fail");
                    }
                }
            }
            if (config.Item("drawEmini", true).GetValue<bool>())
            {
                try
                {
                    var barrels =
                        GetBarrels()
                            .Where(
                                o =>
                                    o.IsValid && !o.IsDead && o.Distance(player) < E.Range &&
                                    o.SkinName == "GangplankBarrel" && o.GetBuff("gangplankebarrellife").Caster.IsMe);
                    foreach (var b in barrels)
                    {
                        var minis = MinionManager.GetMinions(
                            b.Position, BarrelExplosionRange, MinionTypes.All, MinionTeam.NotAlly);
                        foreach (var m in
                            minis.Where(e => Q.GetDamage(e) + ItemHandler.GetSheenDmg(e) >= e.Health && e.Health > 3))
                        {
                            Render.Circle.DrawCircle(m.Position, 57, Color.Yellow, 7);
                        }
                    }
                }
                catch (Exception) {}
            }
        }

        public int GetAmmo()
        {
            if (config.Item("AmmoFix", true).GetValue<Slider>().Value > 0)
            {
                return config.Item("AmmoFix", true).GetValue<Slider>().Value;
            }
            return E.Instance.Ammo;
        }

        public void drawText(int mode, string result)
        {
            const string baseText = "Killable with R: ";
            if (mode == 1)
            {
                Drawing.DrawText(
                    Drawing.Width / 2 - (baseText + result).Length * 5, Drawing.Height * 0.75f, Color.Red,
                    baseText + result);
            }
            else
            {
                Drawing.DrawText(
                    player.HPBarPosition.X - (baseText + result).Length * 5 + 110, player.HPBarPosition.Y + 250,
                    Color.Red, baseText + result);
            }
        }

        private float getRDamage(Obj_AI_Hero enemy)
        {
            return
                (float)
                    Damage.CalcDamage(
                        player, enemy, Damage.DamageType.Magical,
                        (Rwave[R.Level - 1] + 0.1 * player.FlatMagicDamageMod) * waveLength());
        }

        public int waveLength()
        {
            if (player.HasBuff("GangplankRUpgrade1"))
            {
                return 18;
            }
            else
            {
                return 12;
            }
        }

        private static float ComboDamage(Obj_AI_Hero hero)
        {
            double damage = 0;
            if (Q.IsReady())
            {
                damage += Damage.GetSpellDamage(player, hero, SpellSlot.Q);
            }
            //damage += ItemHandler.GetItemsDamage(hero);
            var ignitedmg = player.GetSummonerSpellDamage(hero, Damage.SummonerSpell.Ignite);
            if (player.Spellbook.CanUseSpell(player.GetSpellSlot("summonerdot")) == SpellState.Ready &&
                hero.Health < damage + ignitedmg)
            {
                damage += ignitedmg;
            }
            return (float) damage;
        }

        private void Game_ProcessSpell(Obj_AI_Base sender, GameObjectProcessSpellCastEventArgs args)
        {
            if (sender.IsMe)
            {
                if (args.SData.Name == "GangplankQWrapper")
                {
                    if (!justQ)
                    {
                        justQ = true;
                        Utility.DelayAction.Add(200, () => justQ = false);
                    }

                    var targetBarrel = GetBarrels().FirstOrDefault(b => b.Distance(args.End) < 10);
                    if (targetBarrel != null && targetBarrel.Distance(player) > Q.Range * 0.985f)
                    {
                        var enemy =
                            HeroManager.Enemies.Where(
                                e => e.IsValidTarget() && !GetBarrels().Any(b => b.Distance(e) < BarrelExplosionRange))
                                .OrderBy(e => e.Health)
                                .FirstOrDefault();

                        if (enemy != null && E.IsReady())
                        {
                            var pred = Prediction.GetPrediction(enemy, 0.55f).CastPosition;
                            if (pred.Distance(player.Position) < E.Range)
                            {
                                thirdEpos = targetBarrel.Position.Extend(pred, BarrelConnectionRange);
                                Utility.DelayAction.Add(500, () => thirdEpos = Vector3.Zero);
                            }
                        }
                    }
                }
                if (args.SData.Name == "GangplankE")
                {
                    ePos = args.End;
                    if (!justE)
                    {
                        justE = true;
                        Utility.DelayAction.Add(500, () => justE = false);
                        castedBarrels.Add(new CastedBarrel(ePos, System.Environment.TickCount));
                    }
                }
            }
            if (sender.IsEnemy && args.Target != null && sender is Obj_AI_Hero && sender.Distance(player) < 2000)
            {
                var targetBarrel =
                    savedBarrels.FirstOrDefault(b => b.barrel != null && b.barrel.NetworkId == args.Target.NetworkId);
                if (targetBarrel != null)
                {
                    if (KillableBarrel(targetBarrel.barrel, true, 0, (Obj_AI_Hero) sender, args.SData.MissileSpeed))
                    {
                        savedBarrels.Remove(targetBarrel);
                        return;
                    }


                    var delay = (int) (sender.Distance(targetBarrel.barrel.Position) / args.SData.MissileSpeed * 1000f);
                    var t = System.Environment.TickCount - targetBarrel.time - 1 * getEActivationDelay() * 1000;
                    t = Math.Abs(Math.Min(t, 0)) - GetQTime(targetBarrel.barrel);
                    if (t - delay <= 1 * getEActivationDelay() &&
                        !KillableBarrel(targetBarrel.barrel, true, 0, (Obj_AI_Hero) sender))
                    {
                        targetBarrel.time -= 5000;
                    }
                }
            }
        }

        private IEnumerable<Vector3> GetBarrelPoints(Vector3 point)
        {
            return
                CombatHelper.PointsAroundTheTarget(point, BarrelConnectionRange, 15f)
                    .Where(p => !p.IsWall() && p.Distance(point) > BarrelExplosionRange * 0.75f);
        }

        private float getEActivationDelay()
        {
            if (player.Level >= 13)
            {
                return 0.475f;
            }
            if (player.Level >= 7)
            {
                return 0.975f;
            }
            return 1.975f;
        }

        private void InitMenu()
        {
            config = new Menu("Gangplank ", "Gangplank", true);
            // Target Selector
            Menu menuTS = new Menu("Selector", "tselect");
            TargetSelector.AddToMenu(menuTS);
            config.AddSubMenu(menuTS);
            // Orbwalker
            Menu menuOrb = new Menu("Orbwalker", "orbwalker");
            orbwalker = new Orbwalking.Orbwalker(menuOrb);
            config.AddSubMenu(menuOrb);
            // Draw settings
            Menu menuD = new Menu("Drawings ", "dsettings");
            menuD.AddItem(new MenuItem("drawqq", "Draw Q range", true))
                .SetValue(new Circle(false, Color.FromArgb(180, 100, 146, 166)));
            menuD.AddItem(new MenuItem("drawW", "Draw W", true)).SetValue(true);
            menuD.AddItem(new MenuItem("drawee", "Draw E range", true))
                .SetValue(new Circle(false, Color.FromArgb(180, 100, 146, 166)));
            menuD.AddItem(new MenuItem("draweecr", "Draw Connection ranges", true))
                .SetValue(new Circle(false, Color.FromArgb(180, 167, 141, 56)));
            menuD.AddItem(new MenuItem("drawWcd", "Draw E countdown", true)).SetValue(true);
            menuD.AddItem(new MenuItem("drawEmini", "Draw killable minions around E", true)).SetValue(true);
            menuD.AddItem(new MenuItem("drawcombo", "Draw combo damage", true)).SetValue(true);
            menuD.AddItem(new MenuItem("drawEQ", "Draw EQ to cursor", true)).SetValue(false);
            menuD.AddItem(new MenuItem("drawMTB", "Draw Move to barrel range", true)).SetValue(false);
            menuD.AddItem(new MenuItem("drawKillableSL", "Show killable targets with R", true))
                .SetValue(new StringList(new[] { "OFF", "Above HUD", "Under GP" }, 1));
            menuD.AddItem(new MenuItem("drawQpass", "Draw notification about Silver serpents", true)).SetValue(true);
            config.AddSubMenu(menuD);
            // Combo Settings
            Menu menuC = new Menu("Combo ", "csettings");
            menuC.AddItem(new MenuItem("useq", "Use Q", true)).SetValue(true);
            menuC.AddItem(new MenuItem("useqBlock", "   Block Q to save for EQ", true)).SetValue(false);
            menuC.AddItem(new MenuItem("detoneateTarget", "   Blow up target with E", true)).SetValue(true);
            menuC.AddItem(new MenuItem("threeBarrel", "   Use 3 barrel", true)).SetValue(true);
            menuC.AddItem(new MenuItem("usew", "Use W under health", true)).SetValue(new Slider(20, 0, 100));
            menuC.AddItem(new MenuItem("AutoW", "Use W with QSS options", true)).SetValue(true);
            menuC.AddItem(new MenuItem("useeAlways", "Use E always", true))
                .SetTooltip(
                    "NOT RECOMMENDED, If there is no barrel around the target, this will put one to a predicted position")
                .SetValue(false);
            menuC.AddItem(new MenuItem("eStacksC", "   Keep stacks", true))
                .SetTooltip("You can set up how many barrels you want to keep to \"Use E to extend range\"")
                .SetValue(new Slider(0, 0, 5));
            menuC.AddItem(new MenuItem("movetoBarrel", "Move to barrel to AA", true)).SetValue(false);
            menuC.AddItem(new MenuItem("movetoBarrelDist", "   Max distance", true)).SetValue(new Slider(300, 0, 450));
            menuC.AddItem(new MenuItem("EQtoCursor", "EQ to cursor", true))
                .SetValue(new KeyBind("T".ToCharArray()[0], KeyBindType.Press))
                .SetFontStyle(System.Drawing.FontStyle.Bold, SharpDX.Color.Orange);
            menuC.AddItem(new MenuItem("QbarrelCursor", "Q barrel at cursor", true))
                .SetValue(new KeyBind("H".ToCharArray()[0], KeyBindType.Press))
                .SetFontStyle(System.Drawing.FontStyle.Bold, SharpDX.Color.Orange);
            menuC.AddItem(new MenuItem("user", "Use R", true)).SetValue(true);
            menuC.AddItem(new MenuItem("Rmin", "   R min", true)).SetValue(new Slider(2, 1, 5));
            menuC.AddItem(new MenuItem("useIgnite", "Use Ignite", true)).SetValue(true);
            menuC = ItemHandler.addItemOptons(menuC);
            config.AddSubMenu(menuC);
            // Harass Settings
            Menu menuH = new Menu("Harass ", "Hsettings");
            menuH.AddItem(new MenuItem("useqH", "Use Q harass", true)).SetValue(true);
            menuH.AddItem(new MenuItem("useqLHH", "Use Q lasthit", true)).SetValue(true);
            menuH.AddItem(new MenuItem("useqLHHOOAA", "   Only out of AA range", true)).SetValue(false);
            menuH.AddItem(new MenuItem("useeH", "Use E", true)).SetValue(true);
            menuH.AddItem(new MenuItem("eStacksH", "   Keep stacks", true)).SetValue(new Slider(0, 0, 5));
            menuH.AddItem(new MenuItem("minmanaH", "Keep X% mana", true)).SetValue(new Slider(1, 1, 100));
            config.AddSubMenu(menuH);
            // LaneClear Settings
            Menu menuLC = new Menu("LaneClear ", "Lcsettings");
            menuLC.AddItem(new MenuItem("useqLC", "Use Q", true)).SetValue(true);
            menuLC.AddItem(new MenuItem("useqLCOOAA", "   Only out of AA range", true)).SetValue(false);
            menuLC.AddItem(new MenuItem("useqLCQB", "   On barrel", true)).SetValue(true);
            menuLC.AddItem(new MenuItem("useeLC", "Use E", true)).SetValue(true);
            menuLC.AddItem(new MenuItem("eMinHit", "   Min hit", true)).SetValue(new Slider(3, 1, 6));
            menuLC.AddItem(new MenuItem("eStacksLC", "   Keep stacks", true)).SetValue(new Slider(0, 0, 5));
            menuLC.AddItem(new MenuItem("ePrep", "   Prepare minions", true)).SetValue(true);
            menuLC.AddItem(new MenuItem("minmana", "Keep X% mana", true)).SetValue(new Slider(1, 1, 100));
            config.AddSubMenu(menuLC);
            Menu menuM = new Menu("Misc ", "Msettings");
            menuM.AddItem(new MenuItem("AutoR", "Cast R to get assists", true)).SetValue(false);
            menuM.AddItem(new MenuItem("Rhealt", "   Enemy health %", true))
                .SetTooltip("Max enemy healthpercent to cast R")
                .SetValue(new Slider(35, 0, 100));
            menuM.AddItem(new MenuItem("RhealtMin", "   Enemy min health %", true))
                .SetTooltip("min enemy healthpercent to prevent KS or casting too late")
                .SetValue(new Slider(10, 0, 100));
            menuM.AddItem(new MenuItem("AutoQBarrel", "AutoQ barrel near enemies", true)).SetValue(false);
            menuM.AddItem(new MenuItem("comboPrior", "   Combo priority", true))
                .SetValue(new StringList(new[] { "E-Q", "E-AA", }, 0));
            menuM.AddItem(new MenuItem("barrelCorrection", "Barrel placement correction", true)).SetValue(true);
            menuM.AddItem(new MenuItem("AmmoFix", "Ammo fix", true)).SetValue(new Slider(0, 0, 3));
            menuM = DrawHelper.AddMisc(menuM);
            config.AddSubMenu(menuM);

            config.AddItem(new MenuItem("UnderratedAIO", "by Soresu v" + Program.version.ToString().Replace(",", ".")));
            config.AddToMainMenu();
        }
    }

    internal class Barrel
    {
        public Obj_AI_Minion barrel;
        public float time;
        public Vector3 pos;

        public Barrel(Obj_AI_Minion objAiBase, int tickCount)
        {
            barrel = objAiBase;
            time = tickCount;
        }
    }

    internal class CastedBarrel
    {
        public float time;
        public Vector3 pos;

        public CastedBarrel(Vector3 position, int tickCount)
        {
            pos = position;
            time = tickCount;
        }

        public bool shouldDie()
        {
            return System.Environment.TickCount - time > 260;
        }
    }
}