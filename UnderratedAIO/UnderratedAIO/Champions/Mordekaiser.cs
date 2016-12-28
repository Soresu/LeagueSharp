using System;
using System.Collections.Generic;
using System.Linq;
using Color = System.Drawing.Color;
using LeagueSharp;
using LeagueSharp.Common;
using UnderratedAIO.Helpers;
using Environment = UnderratedAIO.Helpers.Environment;
using Orbwalking = UnderratedAIO.Helpers.Orbwalking;

namespace UnderratedAIO.Champions
{
    internal class Mordekaiser
    {
        public static Menu config;
        private static Orbwalking.Orbwalker orbwalker;
        public static readonly Obj_AI_Hero player = ObjectManager.Player;
        public static Spell Q, W, E, R;
        public static bool justW;
        public static int wWidth = 225;
        public static AutoLeveler autoLeveler;
        public Obj_AI_Hero IgniteTarget;

        public Mordekaiser()
        {
            InitMordekaiser();
            InitMenu();
            //Game.PrintChat("<font color='#9933FF'>Soresu </font><font color='#FFFFFF'>- Mordekaiser</font>");
            Game.OnUpdate += Game_OnGameUpdate;
            Orbwalking.AfterAttack += AfterAttack;
            Orbwalking.BeforeAttack += BeforeAttack;
            Drawing.OnDraw += Game_OnDraw;
            Obj_AI_Base.OnProcessSpellCast += Obj_AI_Base_OnProcessSpellCast;
            HpBarDamageIndicator.DamageToUnit = ComboDamage;
            Helpers.Jungle.setSmiteSlot();
        }


        private void Obj_AI_Base_OnProcessSpellCast(Obj_AI_Base hero, GameObjectProcessSpellCastEventArgs args)
        {
            if (hero.IsMe && args.SData.Name == "MordekaiserCreepingDeathCast")
            {
                if (!justW)
                {
                    justW = true;
                    Utility.DelayAction.Add(1000, () => justW = false);
                }
            }
        }

        private void Game_OnGameUpdate(EventArgs args)
        {
            if (FpsBalancer.CheckCounter())
            {
                return;
            }
            switch (orbwalker.ActiveMode)
            {
                case Orbwalking.OrbwalkingMode.Combo:
                    Combo();
                    TurnOffW();
                    break;
                case Orbwalking.OrbwalkingMode.Mixed:
                    Harass();
                    TurnOffW();
                    break;
                case Orbwalking.OrbwalkingMode.LaneClear:
                    Clear();
                    TurnOffW();
                    break;
                case Orbwalking.OrbwalkingMode.LastHit:
                    break;
                default:
                    break;
            }
            if (MordeGhost && R.IsReady())
            {
                PetHandler.MovePet(config, orbwalker.ActiveMode);
            }
        }

        private void BeforeAttack(Orbwalking.BeforeAttackEventArgs args)
        {
            if (args.Unit.IsMe && Q.IsReady() && orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.LaneClear &&
                config.Item("useqLC", true).GetValue<bool>() &&
                Environment.Minion.countMinionsInrange(player.Position, 600f) >
                config.Item("qhitLC", true).GetValue<Slider>().Value)
            {
                Q.Cast();
                Orbwalking.ResetAutoAttackTimer();
                //player.IssueOrder(GameObjectOrder.MoveTo, Game.CursorPos);
            }
        }

        private static void AfterAttack(AttackableUnit unit, AttackableUnit target)
        {
            var junglemob = Jungle.GetNearest(player.Position);
            if (unit.IsMe && Q.IsReady() &&
                ((orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.Combo && config.Item("useq", true).GetValue<bool>() &&
                  target.IsEnemy && target.Team != player.Team) ||
                 (config.Item("useqLC", true).GetValue<bool>() && junglemob != null &&
                  junglemob.Distance(player.Position) < player.AttackRange + 30)))
            {
                Q.Cast();
                Orbwalking.ResetAutoAttackTimer();
                //player.IssueOrder(GameObjectOrder.MoveTo, Game.CursorPos);
            }
        }

        private void Combo()
        {
            Obj_AI_Hero target = DrawHelper.GetBetterTarget(W.Range, TargetSelector.DamageType.Magical);
            if (target != null)
            {
                if (config.Item("useItems").GetValue<bool>())
                {
                    ItemHandler.UseItems(target, config, ComboDamage(target));
                }
                bool hasIgnite = player.Spellbook.CanUseSpell(player.GetSpellSlot("SummonerDot")) == SpellState.Ready;
                if (config.Item("usew", true).GetValue<bool>() && W.IsReady())
                {
                    CastW(target);
                }
                if (config.Item("usee", true).GetValue<bool>() && E.CanCast(target) && player.Distance(target) < E.Range)
                {
                    E.CastIfHitchanceEquals(target, HitChance.High);
                }
                var ignitedmg = (float) player.GetSummonerSpellDamage(target, Damage.SummonerSpell.Ignite);
                var canUlt = config.Item("user", true).GetValue<bool>() && !MordeGhost &&
                             !config.Item("ult" + target.SkinName, true).GetValue<bool>() &&
                             (!config.Item("ultDef", true).GetValue<bool>() ||
                              (config.Item("ultDef", true).GetValue<bool>() && !CombatHelper.HasDef(target)));
                if (canUlt &&
                    (((player.Distance(target.Position) <= 400f ||
                       (R.CanCast(target) && target.Health < 250f && target.Position.CountAlliesInRange(600f) >= 1)) &&
                      R.GetDamage(target) * 0.8f > target.Health) ||
                     (player.HealthPercent < 40 && target.HealthPercent < 40 && target.Distance(player) < 300f)))
                {
                    R.CastOnUnit(target);
                }
                if (canUlt && hasIgnite && player.Distance(target) < 600 &&
                    R.GetDamage(target) * 0.8f + ignitedmg > HealthPrediction.GetHealthPrediction(target, 400))
                {
                    IgniteTarget = target;
                    Utility.DelayAction.Add(
                        150, () =>
                        {
                            player.Spellbook.CastSpell(player.GetSpellSlot("SummonerDot"), IgniteTarget);
                            IgniteTarget = null;
                        });
                    R.CastOnUnit(target);
                }
                if (config.Item("useIgnite").GetValue<bool>() && ignitedmg > target.Health && hasIgnite)
                {
                    if (IgniteTarget != null)
                    {
                        player.Spellbook.CastSpell(player.GetSpellSlot("SummonerDot"), IgniteTarget);
                        return;
                    }
                    player.Spellbook.CastSpell(player.GetSpellSlot("SummonerDot"), target);
                }
            }
        }

        private void CastW(Obj_AI_Hero target)
        {
            if (justW)
            {
                return;
            }
            var allyWs =
                ObjectManager.Get<Obj_AI_Base>()
                    .Where(o => o.Distance(player) < 1500 && o.HasBuff("mordekaisercreepingdeath"));
            if (allyWs.Any())
            {
                foreach (var allyW in allyWs)
                {
                    if (allyW is Obj_AI_Hero)
                    {
                        var data = Program.IncDamages.GetAllyData(allyW.NetworkId);
                        if (data != null)
                        {
                            if (data.IsAboutToDie)
                            {
                                W.Cast();
                            }
                        }
                    }
                }
            }
            else
            {
                Obj_AI_Base wTarget = Environment.Hero.mostEnemyAtFriend(player, W.Range, wWidth);
                if (MordeGhost && PetHandler.Pet != null)
                {
                    if (wTarget == null || PetHandler.Pet.CountEnemiesInRange(250f) > wTarget.CountEnemiesInRange(250f))
                    {
                        W.Cast(PetHandler.Pet);
                        return;
                    }
                }
                if (wTarget != null && (wTarget.CountEnemiesInRange(250) > 0 || player.CountEnemiesInRange(250) > 0))
                {
                    W.Cast(wTarget);
                }
            }
        }

        private static bool MordeGhost
        {
            get { return player.Spellbook.GetSpell(SpellSlot.R).Name.ToLower() == "mordekaisercotgguide"; }
        }

        private static bool wActive
        {
            get
            {
                return player.Spellbook.GetSpell(SpellSlot.W).Name.ToLower() == "mordekaisercreepingdeath2" || justW;
            }
        }

        private void Harass()
        {
            Obj_AI_Hero target = DrawHelper.GetBetterTarget(W.Range, TargetSelector.DamageType.Magical);

            if (target != null)
            {
                if (config.Item("useeH", true).GetValue<bool>() && E.CanCast(target))
                {
                    E.Cast(target);
                }
                if (config.Item("usewH", true).GetValue<bool>() && W.IsReady() && !wActive)
                {
                    if (config.Item("usewHHero", true).GetValue<bool>())
                    {
                        var bestHeroes =
                            HeroManager.Allies.Where(h => h.CountEnemiesInRange(wWidth) > 0)
                                .OrderByDescending(h => h.CountEnemiesInRange(wWidth));
                        foreach (var hero in bestHeroes)
                        {
                            W.Cast(hero);
                            return;
                        }
                    }
                    if (config.Item("usewHMini", true).GetValue<bool>())
                    {
                        var minions =
                            MinionManager.GetMinions(W.Range, MinionTypes.All, MinionTeam.Ally)
                                .Where(m => m.CountEnemiesInRange(wWidth) > 0)
                                .OrderByDescending(h => h.CountEnemiesInRange(wWidth));
                        foreach (var minion in minions)
                        {
                            W.Cast(minion);
                            return;
                        }
                    }
                }
            }
            if (config.Item("usewHLH", true).GetValue<bool>())
            {
                LasthitW();
            }
        }

        private void LasthitW()
        {
            if (!W.IsReady())
            {
                return;
            }
            if (!wActive)
            {
                if (!player.IsWindingUp && Orbwalking.CanMove(100))
                {
                    var minions =
                        MinionManager.GetMinions(W.Range)
                            .Where(
                                m => HealthPrediction.GetHealthPrediction(m, 500) - GetWDamage(m) - W.GetDamage(m) < 0)
                            .OrderByDescending(m => m.Distance(player));
                    foreach (var minion in minions)
                    {
                        var allyTarget =
                            MinionManager.GetMinions(minion.Position, wWidth, MinionTypes.All, MinionTeam.Ally)
                                .OrderByDescending(m => m.CountEnemiesInRange(wWidth))
                                .FirstOrDefault();
                        if (allyTarget == null)
                        {
                            allyTarget =
                                HeroManager.Allies.Where(m => m.Distance(minion) < wWidth)
                                    .OrderByDescending(m => m.CountEnemiesInRange(wWidth))
                                    .FirstOrDefault();
                        }
                        if (allyTarget != null)
                        {
                            if (minion.IsInAttackRange() && player.CanAttack &&
                                MinionManager.GetMinions(allyTarget.Position, wWidth).Count == 1)
                            {
                                break;
                            }
                            if (minion.Health - GetWDamage(minion) - W.GetDamage(minion) < 0)
                            {
                                W.Cast(allyTarget);
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                var allyWs =
                    ObjectManager.Get<Obj_AI_Base>()
                        .Where(o => o.Distance(player) < 1500 && o.HasBuff("mordekaisercreepingdeath"));
                foreach (var allyW in allyWs)
                {
                    var minions =
                        MinionManager.GetMinions(allyW.Position, wWidth, MinionTypes.All, MinionTeam.Enemy)
                            .OrderByDescending(m => m.Distance(player))
                            .Where(m => m.Health - W.GetDamage(m) < 0);
                    if (minions.Any() &&
                        (!config.Item("usewHHeroPrior", true).GetValue<bool>() ||
                         allyW.CountEnemiesInRange(allyW.MaxHealth > 749 ? 300 : wWidth) == 0))
                    {
                        W.Cast();
                    }
                }
            }
        }

        private void TurnOffW()
        {
            if (!W.IsReady())
            {
                return;
            }
            if (wActive)
            {
                var targethero = DrawHelper.GetBetterTarget(W.Range, TargetSelector.DamageType.Magical);
                var hit = false;
                if (targethero == null)
                {
                    return;
                }
                var allyWs =
                    ObjectManager.Get<Obj_AI_Base>()
                        .Where(o => o.Distance(player) < 1500 && o.HasBuff("mordekaisercreepingdeath"));
                foreach (var allyW in allyWs)
                {
                    var range = allyW.MaxHealth > 749 ? 300 : wWidth;
                    if (allyW != null &&
                        Prediction.GetPrediction(targethero, 0.1f)
                            .UnitPosition.Distance(
                                allyW is Obj_AI_Hero
                                    ? Prediction.GetPrediction(allyW, 0.1f).UnitPosition
                                    : allyW.Position) > range && allyW.Distance(targethero) < range)
                    {
                        if (hit)
                        {
                            hit = false;
                        }
                        else
                        {
                            hit = true;
                        }
                    }
                }
                if (hit)
                {
                    W.Cast();
                }
            }
        }

        private void Clear()
        {
            MinionManager.FarmLocation bestPosition =
                E.GetCircularFarmLocation(
                    MinionManager.GetMinions(E.Range - 100f, MinionTypes.All, MinionTeam.NotAlly), 200f);
            if (config.Item("useeLC", true).GetValue<bool>() && E.IsReady() &&
                bestPosition.MinionsHit > config.Item("ehitLC", true).GetValue<Slider>().Value)
            {
                E.Cast(bestPosition.Position);
            }
            if (config.Item("usewLCHLH", true).GetValue<bool>() && !wActive)
            {
                LasthitW();
            }
        }

        private void Game_OnDraw(EventArgs args)
        {
            DrawHelper.DrawCircle(config.Item("drawaa", true).GetValue<Circle>(), player.AttackRange);
            DrawHelper.DrawCircle(config.Item("drawww", true).GetValue<Circle>(), W.Range);
            DrawHelper.DrawCircle(config.Item("drawee", true).GetValue<Circle>(), E.Range);
            DrawHelper.DrawCircle(config.Item("drawrr", true).GetValue<Circle>(), R.Range);
            HpBarDamageIndicator.Enabled = config.Item("drawcombo").GetValue<bool>();
        }

        private static double GetWDamage(Obj_AI_Base mini)
        {
            var dmg = new double[] { 50, 85, 120, 155, 190 }[W.Level - 1] + 0.3f * player.FlatMagicDamageMod;
            return Damage.CalcDamage(player, mini, Damage.DamageType.Magical, dmg);
        }

        private float ComboDamage(Obj_AI_Hero hero)
        {
            double damage = 0;
            if (Q.IsReady())
            {
                damage += Damage.GetSpellDamage(player, hero, SpellSlot.Q);
            }
            if (W.IsReady())
            {
                damage += Damage.GetSpellDamage(player, hero, SpellSlot.W);
            }
            if (E.IsReady())
            {
                damage += Damage.GetSpellDamage(player, hero, SpellSlot.E);
            }
            if (R.IsReady())
            {
                damage += Damage.GetSpellDamage(player, hero, SpellSlot.R);
            }

            damage += ItemHandler.GetItemsDamage(hero);

            var ignitedmg = player.GetSummonerSpellDamage(hero, Damage.SummonerSpell.Ignite);
            if (player.Spellbook.CanUseSpell(player.GetSpellSlot("summonerdot")) == SpellState.Ready &&
                hero.Health < damage + ignitedmg)
            {
                damage += ignitedmg;
            }
            return (float) damage;
        }

        private void InitMordekaiser()
        {
            Q = new Spell(SpellSlot.Q, player.AttackRange);
            W = new Spell(SpellSlot.W, 750);
            E = new Spell(SpellSlot.E, 650);
            E.SetSkillshot(0.5f, 45, 2000, false, SkillshotType.SkillshotCone);
            R = new Spell(SpellSlot.R, 850);
        }

        private void InitMenu()
        {
            config = new Menu("Mordekaiser", "Mordekaiser", true);
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
            menuD.AddItem(new MenuItem("drawaa", "Draw AA range", true))
                .SetValue(new Circle(false, Color.FromArgb(180, 109, 111, 126)));
            menuD.AddItem(new MenuItem("drawww", "Draw W range", true))
                .SetValue(new Circle(false, Color.FromArgb(180, 109, 111, 126)));
            menuD.AddItem(new MenuItem("drawee", "Draw E range", true))
                .SetValue(new Circle(false, Color.FromArgb(180, 109, 111, 126)));
            menuD.AddItem(new MenuItem("drawrr", "Draw R range", true))
                .SetValue(new Circle(false, Color.FromArgb(180, 109, 111, 126)));
            menuD.AddItem(new MenuItem("drawcombo", "Draw combo damage")).SetValue(true);
            config.AddSubMenu(menuD);
            // Combo Settings
            Menu menuC = new Menu("Combo ", "csettings");
            menuC.AddItem(new MenuItem("useq", "Use Q", true)).SetValue(true);
            menuC.AddItem(new MenuItem("usew", "Use W", true)).SetValue(true);
            menuC.AddItem(new MenuItem("usee", "Use E", true)).SetValue(true);
            menuC.AddItem(new MenuItem("user", "Use R", true)).SetValue(true);
            menuC.AddItem(new MenuItem("ultDef", "   Don't use on Qss/barrier etc...", true)).SetValue(true);
            menuC.AddItem(new MenuItem("selected", "Focus Selected target", true)).SetValue(true);
            menuC.AddItem(new MenuItem("useIgnite", "Use Ignite")).SetValue(true);
            menuC = ItemHandler.addItemOptons(menuC);
            config.AddSubMenu(menuC);
            // Harass Settings
            Menu menuH = new Menu("Harass ", "Hsettings");
            menuH.AddItem(new MenuItem("useeH", "Use E", true)).SetValue(true);
            menuH.AddItem(new MenuItem("usewH", "Use W", true)).SetValue(true);
            menuH.AddItem(new MenuItem("usewHHero", "   On Hero", true)).SetValue(true);
            menuH.AddItem(new MenuItem("usewHMini", "   On Minion", true)).SetValue(true);
            menuH.AddItem(new MenuItem("usewHLH", "Use W lasthit", true)).SetValue(true);
            menuH.AddItem(new MenuItem("usewHHeroPrior", "   Priorize harass", true)).SetValue(true);
            config.AddSubMenu(menuH);
            // LaneClear Settings
            Menu menuLC = new Menu("LaneClear ", "Lcsettings");
            menuLC.AddItem(new MenuItem("useqLC", "Use Q", true)).SetValue(true);
            menuLC.AddItem(new MenuItem("qhitLC", "   Min hit", true).SetValue(new Slider(2, 1, 3)));
            menuLC.AddItem(new MenuItem("usewLCHLH", "Use W lasthit", true)).SetValue(true);
            menuLC.AddItem(new MenuItem("useeLC", "Use E", true)).SetValue(true);
            menuLC.AddItem(new MenuItem("ehitLC", "   Min hit", true).SetValue(new Slider(2, 1, 5)));
            config.AddSubMenu(menuLC);
            // Misc Settings
            Menu menuM = new Menu("Misc ", "Msettings");
            menuM = DrawHelper.AddMisc(menuM);
            config.AddSubMenu(menuM);
            var sulti = new Menu("Don't ult on ", "dontult");
            foreach (var hero in ObjectManager.Get<Obj_AI_Hero>().Where(hero => hero.IsEnemy))
            {
                sulti.AddItem(new MenuItem("ult" + hero.SkinName, hero.SkinName, true)).SetValue(false);
            }
            config.AddSubMenu(sulti);

            config.AddItem(new MenuItem("UnderratedAIO", "by Soresu v" + Program.version.ToString().Replace(",", ".")));
            config.AddToMainMenu();
        }
    }
}