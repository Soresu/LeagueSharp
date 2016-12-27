using System;
using System.Collections.Generic;
using System.Linq;
using LeagueSharp;
using LeagueSharp.Common;
using Color = System.Drawing.Color;
using UnderratedAIO.Helpers;
using Environment = UnderratedAIO.Helpers.Environment;
using Orbwalking = UnderratedAIO.Helpers.Orbwalking;

namespace UnderratedAIO.Champions
{
    internal class Poppy
    {
        public static Menu config;
        public static Orbwalking.Orbwalker orbwalker;
        public static readonly Obj_AI_Hero player = ObjectManager.Player;
        public static Spell Q, W, E, R;
        public static double[] eSecond = new double[5] { 75, 125, 175, 225, 275 };
        public static List<string> NotDash = new List<string>() { "Udyr", "Malphite", };
        public static AutoLeveler autoLeveler;

        public Poppy()
        {
            Initpoppy();
            InitMenu();
            //Game.PrintChat("<font color='#9933FF'>Soresu </font><font color='#FFFFFF'>- Poppy</font>");
            Game.OnUpdate += Game_OnGameUpdate;
            Drawing.OnDraw += Game_OnDraw;
            AntiGapcloser.OnEnemyGapcloser += OnEnemyGapcloser;
            Interrupter2.OnInterruptableTarget += OnInterruptableTarget;
            Jungle.setSmiteSlot();
            HpBarDamageIndicator.DamageToUnit = ComboDamage;
        }

        private void OnInterruptableTarget(Obj_AI_Hero sender, Interrupter2.InterruptableTargetEventArgs args)
        {
            if (config.Item("useEint", true).GetValue<bool>() && E.IsReady() && E.CanCast(sender))
            {
                E.CastOnUnit(sender);
            }
        }


        private static void Game_OnGameUpdate(EventArgs args)
        {
            if (FpsBalancer.CheckCounter())
            {
                return;
            }
            Obj_AI_Hero targetf = DrawHelper.GetBetterTarget(1000, TargetSelector.DamageType.Magical);
            if (config.Item("useeflashforced", true).GetValue<KeyBind>().Active)
            {
                if (targetf == null)
                {
                    player.IssueOrder(GameObjectOrder.MoveTo, Game.CursorPos);
                }
                else
                {
                    var bestpos = CombatHelper.bestVectorToPoppyFlash2(targetf);
                    bool hasFlash = player.Spellbook.CanUseSpell(player.GetSpellSlot("SummonerFlash")) ==
                                    SpellState.Ready;
                    if (E.IsReady() && hasFlash && !CheckWalls(player, targetf) && bestpos.IsValid())
                    {
                        player.Spellbook.CastSpell(player.GetSpellSlot("SummonerFlash"), bestpos);
                        player.IssueOrder(GameObjectOrder.MoveTo, Game.CursorPos);
                    }
                    else if (!hasFlash)
                    {
                        Combo();
                        Orbwalking.Orbwalk(targetf, Game.CursorPos, 90, 90);
                    }
                }
            }
            switch (orbwalker.ActiveMode)
            {
                case Orbwalking.OrbwalkingMode.Combo:
                    Combo();
                    KsPassive();
                    break;
                case Orbwalking.OrbwalkingMode.Mixed:
                    Harass();
                    KsPassive();
                    break;
                case Orbwalking.OrbwalkingMode.LaneClear:
                    Clear();
                    KsPassive();
                    break;
                case Orbwalking.OrbwalkingMode.LastHit:
                    KsPassive();
                    break;
                default:
                    break;
            }
            if (!player.IsDead)
            {
                foreach (var dashingEnemy in
                    HeroManager.Enemies.Where(
                        e =>
                            e.IsValidTarget() && e.Distance(player) < 1600 &&
                            config.Item("useAutoW" + e.SkinName, true).GetValue<Slider>().Value > 0)
                        .OrderByDescending(e => config.Item("useAutoW" + e.SkinName, true).GetValue<Slider>().Value)
                        .ThenBy(e => e.Distance(player)))
                {
                    var nextpos = Prediction.GetPrediction(dashingEnemy, 0.1f).UnitPosition;
                    if (dashingEnemy.IsDashing() && !dashingEnemy.HasBuffOfType(BuffType.SpellShield) &&
                        !dashingEnemy.HasBuff("poppyepushenemy") && dashingEnemy.Distance(player) <= W.Range &&
                        (nextpos.Distance(player.Position) > W.Range || (player.Distance(dashingEnemy) < W.Range - 100)) &&
                        dashingEnemy.IsTargetable && !NotDash.Contains(dashingEnemy.ChampionName))
                    {
                        W.Cast();
                    }
                    if (
                        CombatHelper.DashDatas.Any(
                            d => d.ChampionName == dashingEnemy.ChampionName && d.IsReady(dashingEnemy)))
                    {
                        break;
                    }
                }
            }
        }

        private static void KsPassive()
        {
            var target =
                HeroManager.Enemies.Where(
                    e =>
                        e.IsInAttackRange() &&
                        HealthPrediction.GetHealthPrediction(e, 500) < player.GetAutoAttackDamage(e, true) &&
                        e.IsValidTarget()).OrderByDescending(e => TargetSelector.GetPriority(e)).FirstOrDefault();
            if (target != null)
            {
                orbwalker.ForceTarget(target);
            }
        }

        private static void Combo()
        {
            Obj_AI_Hero target = DrawHelper.GetBetterTarget(1000, TargetSelector.DamageType.Magical);
            if (target == null)
            {
                return;
            }
            var cmbdmg = ComboDamage(target);
            if (config.Item("useItems").GetValue<bool>())
            {
                ItemHandler.UseItems(target, config, ComboDamage(target));
            }
            bool hasFlash = player.Spellbook.CanUseSpell(player.GetSpellSlot("SummonerFlash")) == SpellState.Ready;
            if (config.Item("usee", true).GetValue<bool>() && E.IsReady() &&
                (config.Item("useetower", true).GetValue<bool>() || !target.UnderTurret(true)))
            {
                if (config.Item("useewall", true).GetValue<bool>())
                {
                    var bestpos = CombatHelper.bestVectorToPoppyFlash2(target);
                    float damage =
                        (float)
                            (ComboDamage(target) +
                             Damage.CalcDamage(
                                 player, target, Damage.DamageType.Magical,
                                 (eSecond[E.Level - 1] + 0.8f * player.FlatMagicDamageMod)) +
                             (player.GetAutoAttackDamage(target) * 4));
                    float damageno = (float) (ComboDamage(target) + (player.GetAutoAttackDamage(target) * 4));
                    if (config.Item("useeflash", true).GetValue<bool>() && hasFlash && !CheckWalls(player, target) &&
                        damage > target.Health && target.Health > damageno &&
                        CombatHelper.bestVectorToPoppyFlash(target).IsValid())
                    {
                        player.Spellbook.CastSpell(player.GetSpellSlot("SummonerFlash"), bestpos);
                        Utility.DelayAction.Add(100, () => E.CastOnUnit(target));
                    }
                    if (E.CanCast(target) &&
                        (CheckWalls(player, target) ||
                         target.Health < E.GetDamage(target) + player.GetAutoAttackDamage(target, true)))
                    {
                        E.CastOnUnit(target);
                    }
                    if (E.CanCast(target) && Q.IsReady() && Q.Instance.ManaCost + E.Instance.ManaCost > player.Mana &&
                        target.Health <
                        E.GetDamage(target) + Q.GetDamage(target) + player.GetAutoAttackDamage(target, true))
                    {
                        E.CastOnUnit(target);
                    }
                }
                else
                {
                    if (E.CanCast(target))
                    {
                        E.CastOnUnit(target);
                    }
                }
            }
            if (config.Item("useq", true).GetValue<bool>() && Q.IsReady() && Q.CanCast(target) &&
                Orbwalking.CanMove(100) && target.Distance(player) < Q.Range &&
                (player.Distance(target) > Orbwalking.GetRealAutoAttackRange(target) || !Orbwalking.CanAttack()))
            {
                Q.CastIfHitchanceEquals(target, HitChance.High);
            }

            bool hasIgnite = player.Spellbook.CanUseSpell(player.GetSpellSlot("SummonerDot")) == SpellState.Ready &&
                             config.Item("useIgnite").GetValue<bool>();
            var ignitedmg = hasIgnite ? (float) player.GetSummonerSpellDamage(target, Damage.SummonerSpell.Ignite) : 0f;
            if (ignitedmg > target.Health && hasIgnite && !E.CanCast(target) && !Q.CanCast(target) &&
                (player.Distance(target) > Q.Range || player.HealthPercent < 30))
            {
                player.Spellbook.CastSpell(player.GetSpellSlot("SummonerDot"), target);
            }
            if (config.Item("userindanger", true).GetValue<bool>() && R.IsReady() &&
                ((player.CountEnemiesInRange(800) >= 2 &&
                  player.CountEnemiesInRange(800) > player.CountAlliesInRange(1500) + 1 && player.HealthPercent < 60) ||
                 (player.Health < target.Health && player.HealthPercent < 40 &&
                  player.CountAlliesInRange(1000) + 1 < player.CountEnemiesInRange(1000))))
            {
                var targ =
                    HeroManager.Enemies.Where(
                        e =>
                            e.IsValidTarget() && R.CanCast(e) &&
                            (player.HealthPercent < 60 || e.CountEnemiesInRange(300) > 2) &&
                            HeroManager.Enemies.Count(h => h.Distance(e) < 400 && e.HealthPercent < 35) == 0 &&
                            R.GetPrediction(e).CastPosition.Distance(player.Position) < R.ChargedMaxRange)
                        .OrderByDescending(e => R.GetPrediction(e).CastPosition.CountEnemiesInRange(400))
                        .ThenByDescending(e => e.Distance(target))
                        .FirstOrDefault();
                if (R.Range > 1300 && targ == null)
                {
                    targ =
                        HeroManager.Enemies.Where(
                            e =>
                                e.IsValidTarget() && R.CanCast(e) &&
                                R.GetPrediction(e).CastPosition.Distance(player.Position) < R.ChargedMaxRange)
                            .OrderByDescending(e => R.GetPrediction(e).CastPosition.CountEnemiesInRange(400))
                            .ThenByDescending(e => e.Distance(target))
                            .FirstOrDefault();
                }
                if (!R.IsCharging && targ != null)
                {
                    R.StartCharging();
                }
                if (R.IsCharging && targ != null && R.CanCast(targ) && R.Range > 1000 && R.Range > targ.Distance(player))
                {
                    R.CastIfHitchanceEquals(targ, HitChance.Medium);
                }
                if (R.IsCharging && targ != null && R.Range < 1000)
                {
                    return;
                }
            }
            if (config.Item("user", true).GetValue<bool>() && R.IsReady() && player.Distance(target) < 1400 &&
                !target.UnderTurret(true))
            {
                var cond = ((Rdmg(target) < target.Health && ignitedmg + Rdmg(target) > target.Health &&
                             player.Distance(target) < 600) ||
                            (target.Distance(player) > E.Range && Rdmg(target) > target.Health &&
                             target.Distance(player) < 1100));
                if (!R.IsCharging && cond && !Q.IsReady() && player.HealthPercent < 40)
                {
                    R.StartCharging();
                    if (hasIgnite && cmbdmg > target.Health && cmbdmg - Rdmg(target) < target.Health)
                    {
                        if (!target.HasBuff("summonerdot"))
                        {
                            player.Spellbook.CastSpell(player.GetSpellSlot("SummonerDot"), target);
                        }
                    }
                }
                if (R.IsCharging && R.CanCast(target) && R.Range > target.Distance(player) && cond)
                {
                    R.CastIfHitchanceEquals(target, HitChance.High);
                }
            }
        }

        private static void Clear()
        {
            var mob = Jungle.GetNearest(player.Position);
            float perc = config.Item("minmana", true).GetValue<Slider>().Value / 100f;
            if (player.Mana < player.MaxMana * perc)
            {
                return;
            }
            if (config.Item("useeLC", true).GetValue<bool>() && E.CanCast(mob) && CheckWalls(player, mob))
            {
                E.CastOnUnit(mob);
            }
            MinionManager.FarmLocation bestPositionQ =
                Q.GetLineFarmLocation(MinionManager.GetMinions(Q.Range, MinionTypes.All, MinionTeam.NotAlly));
            if (bestPositionQ.MinionsHit >= config.Item("qMinHit", true).GetValue<Slider>().Value &&
                config.Item("useqLC", true).GetValue<bool>())
            {
                Q.Cast(bestPositionQ.Position);
            }
        }

        private static void Harass()
        {
            Obj_AI_Hero target = DrawHelper.GetBetterTarget(Q.Range, TargetSelector.DamageType.Magical);
            float perc = config.Item("minmanaH", true).GetValue<Slider>().Value / 100f;
            if (player.Mana < player.MaxMana * perc || target == null)
            {
                return;
            }
            if (config.Item("useqH", true).GetValue<bool>() && Q.IsReady() && Q.CanCast(target) &&
                Orbwalking.CanMove(100) && target.Distance(player) < Q.Range &&
                (player.Distance(target) > Orbwalking.GetRealAutoAttackRange(target) || !Orbwalking.CanAttack()))
            {
                Q.CastIfHitchanceEquals(target, HitChance.High);
            }
        }

        private static void Game_OnDraw(EventArgs args)
        {
            DrawHelper.DrawCircle(config.Item("drawqq", true).GetValue<Circle>(), Q.Range);
            DrawHelper.DrawCircle(config.Item("drawww", true).GetValue<Circle>(), W.Range);
            DrawHelper.DrawCircle(config.Item("drawee", true).GetValue<Circle>(), E.Range);
            DrawHelper.DrawCircle(config.Item("drawrr", true).GetValue<Circle>(), R.Range);
            HpBarDamageIndicator.Enabled = config.Item("drawcombo").GetValue<bool>();
        }

        private static void OnEnemyGapcloser(ActiveGapcloser gapcloser)
        {
            if (config.Item("useEgap", true).GetValue<bool>() && E.IsReady() && E.CanCast(gapcloser.Sender) &&
                CheckWalls(player, gapcloser.Sender))
            {
                E.CastOnUnit(gapcloser.Sender);
            }
        }

        public static bool CheckWalls(Obj_AI_Base player, Obj_AI_Base enemy)
        {
            var distance = player.Position.Distance(enemy.Position);
            for (int i = 1; i < 6; i++)
            {
                if (player.Position.Extend(enemy.Position, distance + 60 * i).IsWall())
                {
                    return true;
                }
            }
            return false;
        }

        public static float ComboDamage(Obj_AI_Hero hero)
        {
            double damage = 0;
            if (Q.IsReady())
            {
                damage += Damage.GetSpellDamage(player, hero, SpellSlot.Q);
            }
            if (E.IsReady())
            {
                damage += (float) Damage.GetSpellDamage(player, hero, SpellSlot.E);
            }
            if ((Items.HasItem(ItemHandler.Bft.Id) && Items.CanUseItem(ItemHandler.Bft.Id)) ||
                (Items.HasItem(ItemHandler.Dfg.Id) && Items.CanUseItem(ItemHandler.Dfg.Id)))
            {
                damage = (float) (damage * 1.2);
            }
            if (player.Spellbook.CanUseSpell(player.GetSpellSlot("summonerdot")) == SpellState.Ready &&
                player.Distance(hero) < 500)
            {
                damage += (float) player.GetSummonerSpellDamage(hero, Damage.SummonerSpell.Ignite);
            }
            if (R.IsReady() || R.IsCharging)
            {
                damage += (float) Rdmg(hero);
            }
            damage += ItemHandler.GetItemsDamage(hero);
            return (float) damage;
        }

        public static double Rdmg(Obj_AI_Base target)
        {
            return Damage.CalcDamage(
                player, target, Damage.DamageType.Physical,
                (new double[] { 200, 300, 400 }[R.Level - 1] +
                 0.9f * (player.BaseAttackDamage + player.FlatPhysicalDamageMod)));
        }

        private static void Initpoppy()
        {
            Q = new Spell(SpellSlot.Q, 400);
            Q.SetSkillshot(0.55f, 90f, float.MaxValue, false, SkillshotType.SkillshotLine);
            W = new Spell(SpellSlot.W, 400);
            E = new Spell(SpellSlot.E, 500);
            R = new Spell(SpellSlot.R);
            R.SetSkillshot(0.5f, 90f, 1400, true, SkillshotType.SkillshotLine);
            R.SetCharged("PoppyR", "PoppyR", 425, 1400, 1.0f);
        }

        private static void InitMenu()
        {
            config = new Menu("Poppy", "Poppy", true);
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
            menuD.AddItem(new MenuItem("drawqq", "Draw Q range", true)).SetValue(new Circle(false, Color.DarkCyan));
            menuD.AddItem(new MenuItem("drawww", "Draw W range", true)).SetValue(new Circle(false, Color.DarkCyan));
            menuD.AddItem(new MenuItem("drawee", "Draw E range", true)).SetValue(new Circle(false, Color.DarkCyan));
            menuD.AddItem(new MenuItem("drawrr", "Draw R range", true)).SetValue(new Circle(false, Color.DarkCyan));
            menuD.AddItem(new MenuItem("drawcombo", "Draw combo damage")).SetValue(true);
            config.AddSubMenu(menuD);
            // Combo Settings
            Menu menuC = new Menu("Combo ", "csettings");
            menuC.AddItem(new MenuItem("useq", "Use Q", true)).SetValue(true);
            menuC.AddItem(new MenuItem("usew", "Use W", true)).SetValue(true);
            menuC.AddItem(new MenuItem("usee", "Use E", true)).SetValue(true);
            menuC.AddItem(new MenuItem("useewall", "Use E only near walls", true)).SetValue(true);
            menuC.AddItem(new MenuItem("useetower", "Use E under turret", true)).SetValue(true);
            menuC.AddItem(new MenuItem("useeflash", "Use flash to positioning", true)).SetValue(false);
            menuC.AddItem(new MenuItem("useeflashforced", "Forced flash+E if possible", true))
                .SetValue(new KeyBind("T".ToCharArray()[0], KeyBindType.Press))
                .SetFontStyle(System.Drawing.FontStyle.Bold, SharpDX.Color.Orange);
            menuC.AddItem(new MenuItem("user", "Use R to maximize dmg", true)).SetValue(true);
            menuC.AddItem(new MenuItem("userindanger", "Use R in teamfight", true)).SetValue(true);
            menuC.AddItem(new MenuItem("useIgnite", "Use Ignite")).SetValue(true);
            menuC = ItemHandler.addItemOptons(menuC);
            config.AddSubMenu(menuC);
            // Harass Settings
            Menu menuH = new Menu("Harass ", "Hsettings");
            menuH.AddItem(new MenuItem("useqH", "Use Q", true)).SetValue(true);
            menuH.AddItem(new MenuItem("minmanaH", "Keep X% mana", true)).SetValue(new Slider(1, 1, 100));
            config.AddSubMenu(menuH);
            // LaneClear Settings
            Menu menuLC = new Menu("Clear ", "Lcsettings");
            menuLC.AddItem(new MenuItem("useqLC", "Use Q", true)).SetValue(true);
            menuLC.AddItem(new MenuItem("qMinHit", "   Q min hit", true)).SetValue(new Slider(3, 1, 6));
            menuLC.AddItem(new MenuItem("useeLC", "Use E", true)).SetValue(true);
            menuLC.AddItem(new MenuItem("minmana", "Keep X% mana", true)).SetValue(new Slider(50, 1, 100));
            config.AddSubMenu(menuLC);
            // Misc Settings
            Menu menuM = new Menu("Misc", "Msettings");
            Menu menuMW = new Menu("Auto W", "MWsettings");
            Menu menuME = new Menu("Auto E", "MEsettings");
            menuME.AddItem(new MenuItem("useEint", "Use E interrupt", true)).SetValue(true);
            menuME.AddItem(new MenuItem("useEgap", "Use E on gapcloser near walls", true)).SetValue(true);
            foreach (var hero in ObjectManager.Get<Obj_AI_Hero>().Where(hero => hero.IsEnemy))
            {
                menuMW.AddItem(new MenuItem("useAutoW" + hero.SkinName, hero.SkinName, true))
                    .SetValue(
                        new Slider(CombatHelper.DashDatas.Any(d => d.ChampionName == hero.ChampionName) ? 5 : 0, 0, 5));
            }
            menuMW.AddItem(new MenuItem("infoPAW", "0 is off"));
            menuM.AddSubMenu(menuMW);
            menuM.AddSubMenu(menuME);
            menuM = DrawHelper.AddMisc(menuM);
            config.AddSubMenu(menuM);

            config.AddItem(new MenuItem("UnderratedAIO", "by Soresu v" + Program.version.ToString().Replace(",", ".")));
            config.AddToMainMenu();
        }
    }
}