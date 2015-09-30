using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.Remoting.Messaging;
using Color = System.Drawing.Color;
using LeagueSharp;
using LeagueSharp.Common;
using SharpDX;
using UnderratedAIO.Helpers;
using Environment = UnderratedAIO.Helpers.Environment;
using Orbwalking = UnderratedAIO.Helpers.Orbwalking;

namespace UnderratedAIO.Champions
{
    internal class Garen
    {
        public static Menu config;
        private static Orbwalking.Orbwalker orbwalker;
        public static readonly Obj_AI_Hero player = ObjectManager.Player;
        public static Spell Q, W, E, R;
        public static AutoLeveler autoLeveler;

        public Garen()
        {
            InitGaren();
            InitMenu();
            Game.PrintChat("<font color='#9933FF'>Soresu </font><font color='#FFFFFF'>- Garen</font>");
            Game.OnUpdate += Game_OnGameUpdate;
            Orbwalking.AfterAttack += AfterAttack;
            Drawing.OnDraw += Game_OnDraw;
            Jungle.setSmiteSlot();
            Utility.HpBarDamageIndicator.DamageToUnit = ComboDamage;
        }

        private void Game_OnGameUpdate(EventArgs args)
        {
            switch (orbwalker.ActiveMode)
            {
                case Orbwalking.OrbwalkingMode.Combo:
                    Combo();
                    break;
                case Orbwalking.OrbwalkingMode.Mixed:
                    break;
                case Orbwalking.OrbwalkingMode.LaneClear:
                    Clear();
                    break;
                case Orbwalking.OrbwalkingMode.LastHit:
                    break;
                default:
                    break;
            }
            Jungle.CastSmite(config.Item("useSmite").GetValue<KeyBind>().Active);
        }

        private void Clear()
        {
            if (config.Item("useeLC", true).GetValue<bool>() && E.IsReady() && !GarenE &&
                Environment.Minion.countMinionsInrange(player.Position, E.Range) > 2)
            {
                E.Cast(config.Item("packets").GetValue<bool>());
            }
        }

        private void AfterAttack(AttackableUnit unit, AttackableUnit target)
        {
            if (unit.IsMe && Q.IsReady() && config.Item("useqAAA", true).GetValue<bool>() && !GarenE && target.IsEnemy &&
                target is Obj_AI_Hero)
            {
                Q.Cast(config.Item("packets").GetValue<bool>());
                player.IssueOrder(GameObjectOrder.AutoAttack, target);
            }
        }

        private static bool GarenE
        {
            get { return player.Buffs.Any(buff => buff.Name == "GarenE"); }
        }

        private static bool GarenQ
        {
            get { return player.Buffs.Any(buff => buff.Name == "GarenQ"); }
        }

        private void Combo()
        {
            Obj_AI_Hero target = TargetSelector.GetTarget(700, TargetSelector.DamageType.Physical);
            if (target == null)
            {
                return;
            }
            var combodamage = ComboDamage(target);
            if (config.Item("useItems").GetValue<bool>())
            {
                ItemHandler.UseItems(target, config, combodamage);
            }
            bool hasIgnite = player.Spellbook.CanUseSpell(player.GetSpellSlot("SummonerDot")) == SpellState.Ready;
            var ignitedmg = (float) player.GetSummonerSpellDamage(target, Damage.SummonerSpell.Ignite);
            if (config.Item("useIgnite").GetValue<bool>() && hasIgnite &&
                ((R.IsReady() && ignitedmg + R.GetDamage(target) > target.Health) || ignitedmg > target.Health) &&
                (target.Distance(player) > E.Range || player.HealthPercent < 20))
            {
                player.Spellbook.CastSpell(player.GetSpellSlot("SummonerDot"), target);
            }
            if (config.Item("useq", true).GetValue<bool>() && Q.IsReady() &&
                player.Distance(target) > player.AttackRange && !GarenE && !GarenQ &&
                player.Distance(target) > Orbwalking.GetRealAutoAttackRange(target) &&
                CombatHelper.IsPossibleToReachHim(target, 0.30f, new float[5] { 1.5f, 2f, 2.5f, 3f, 3.5f }[Q.Level - 1]))
            {
                Q.Cast(config.Item("packets").GetValue<bool>());
            }
            if (config.Item("useq", true).GetValue<bool>() && Q.IsReady() && !GarenQ &&
                (!GarenE || (Q.IsReady() && Damage.GetSpellDamage(player, target, SpellSlot.Q) > target.Health)))
            {
                if (GarenE)
                {
                    E.Cast(config.Item("packets").GetValue<bool>());
                }
                Q.Cast(config.Item("packets").GetValue<bool>());
                player.IssueOrder(GameObjectOrder.AutoAttack, target);
            }
            if (config.Item("usee", true).GetValue<bool>() && E.IsReady() && !Q.IsReady() && !GarenQ && !GarenE &&
                player.CountEnemiesInRange(E.Range) > 0)
            {
                E.Cast(config.Item("packets").GetValue<bool>());
            }
            var targHP = target.Health + 20 - CombatHelper.IgniteDamage(target);
            var rLogic = config.Item("user", true).GetValue<bool>() && R.IsReady() && target.IsValidTarget() &&
                         (!config.Item("ult" + target.SkinName, true).GetValue<bool>() ||
                          player.CountEnemiesInRange(1500) == 1) && getRDamage(target) > targHP && targHP > 0;
            if (rLogic && target.Distance(player) < R.Range)
            {
                if (!(GarenE && target.Health < getEDamage(target, true) && target.Distance(player) < E.Range))
                {
                    if (GarenE)
                    {
                        E.Cast(config.Item("packets").GetValue<bool>());
                    }
                    else
                    {
                        R.Cast(target, config.Item("packets").GetValue<bool>());
                    }
                }
            }
            if (config.Item("usew", true).GetValue<bool>() && W.IsReady() && player.CountEnemiesInRange(E.Range) > 0 &&
                target.IsFacing(player))
            {
                W.Cast(config.Item("packets").GetValue<bool>());
            }
            bool hasFlash = player.Spellbook.CanUseSpell(player.GetSpellSlot("SummonerFlash")) == SpellState.Ready;
            if (config.Item("useFlash", true).GetValue<bool>() && hasFlash && rLogic &&
                target.Distance(player) < R.Range + 425 && target.Distance(player) > R.Range + 250 && !Q.IsReady() &&
                !CombatHelper.IsFacing(target, player.Position) && !GarenQ)
            {
                if (target.Distance(player) < R.Range + 300 && player.MoveSpeed > target.MoveSpeed)
                {
                    return;
                }
                if (GarenE)
                {
                    E.Cast(config.Item("packets").GetValue<bool>());
                }
                else if (!player.Position.Extend(target.Position, 425f).IsWall()) {}
                {
                    player.Spellbook.CastSpell(
                        player.GetSpellSlot("SummonerFlash"), player.Position.Extend(target.Position, 425f));
                }
            }
        }

        private void Game_OnDraw(EventArgs args)
        {
            DrawHelper.DrawCircle(config.Item("drawaa", true).GetValue<Circle>(), player.AttackRange);
            DrawHelper.DrawCircle(config.Item("drawee", true).GetValue<Circle>(), E.Range);
            DrawHelper.DrawCircle(config.Item("drawrr", true).GetValue<Circle>(), R.Range);
            Jungle.ShowSmiteStatus(
                config.Item("useSmite").GetValue<KeyBind>().Active, config.Item("smiteStatus").GetValue<bool>());
            Utility.HpBarDamageIndicator.Enabled = config.Item("drawcombo").GetValue<bool>();
        }

        private float ComboDamage(Obj_AI_Hero hero)
        {
            double damage = 0;
            if (R.IsReady())
            {
                damage += getRDamage(hero);
            }
            damage += ItemHandler.GetItemsDamage(hero);

            if ((Items.HasItem(ItemHandler.Bft.Id) && Items.CanUseItem(ItemHandler.Bft.Id)) ||
                (Items.HasItem(ItemHandler.Dfg.Id) && Items.CanUseItem(ItemHandler.Dfg.Id)))
            {
                damage = (float) (damage * 1.2);
            }
            if (Q.IsReady() && !GarenQ)
            {
                damage += Damage.GetSpellDamage(player, hero, SpellSlot.Q);
            }
            if (E.IsReady() && !GarenE)
            {
                damage += getEDamage(hero);
            }
            else if (GarenE)
            {
                damage += getEDamage(hero, true);
            }
            var ignitedmg = player.GetSummonerSpellDamage(hero, Damage.SummonerSpell.Ignite);
            if (player.Spellbook.CanUseSpell(player.GetSpellSlot("summonerdot")) == SpellState.Ready &&
                hero.Health < damage + ignitedmg)
            {
                damage += ignitedmg;
            }
            return (float) damage;
        }

        private double getRDamage(Obj_AI_Hero hero)
        {
            var dmg = new double[] { 175, 350, 525 }[R.Level - 1] +
                      new[] { 28.57, 33.33, 40 }[R.Level - 1] / 100 * (hero.MaxHealth - hero.Health);
            if (hero.HasBuff("garenpassiveenemytarget"))
            {
                return Damage.CalcDamage(player, hero, Damage.DamageType.True, dmg);
            }
            else
            {
                return Damage.CalcDamage(player, hero, Damage.DamageType.Magical, dmg);
            }
        }

        public static int[] spins = new int[] { 5, 6, 7, 8, 9, 10 };
        public static double[] baseEDamage = new double[] { 15, 18.8, 22.5, 26.3, 30 };
        public static double[] bonusEDamage = new double[] { 34.5, 35.3, 36, 36.8, 37.5 };

        private double getEDamage(Obj_AI_Hero target, bool bufftime = false)
        {
            var spins = 0d;
            if (bufftime)
            {
                spins = CombatHelper.GetBuffTime(player.GetBuff("GarenE")) * GetSpins() / 3;
            }
            else
            {
                spins = GetSpins();
            }
            var dmg = (baseEDamage[E.Level - 1] + bonusEDamage[E.Level - 1] / 100 * player.TotalAttackDamage) * spins;
            var bonus = target.HasBuff("garenpassiveenemytarget") ? target.MaxHealth / 100f * spins : 0;
            if (ObjectManager.Get<Obj_AI_Base>().Count(o => o.IsValidTarget() && o.Distance(target) < 650) == 0)
            {
                return Damage.CalcDamage(player, target, Damage.DamageType.Physical, dmg) * 1.33 + bonus;
            }
            else
            {
                return Damage.CalcDamage(player, target, Damage.DamageType.Physical, dmg) + bonus;
            }
        }

        private static double GetSpins()
        {
            if (player.Level < 4)
            {
                return 5;
            }
            if (player.Level < 7)
            {
                return 6;
            }
            if (player.Level < 10)
            {
                return 7;
            }
            if (player.Level < 13)
            {
                return 8;
            }
            if (player.Level < 16)
            {
                return 9;
            }
            if (player.Level < 18)
            {
                return 10;
            }
            return 5;
        }


        private void InitGaren()
        {
            Q = new Spell(SpellSlot.Q, player.AttackRange);
            W = new Spell(SpellSlot.W);
            E = new Spell(SpellSlot.E, 325);
            R = new Spell(SpellSlot.R, 400);
        }

        private void InitMenu()
        {
            config = new Menu("Garen", "Garen", true);
            // Target Selector
            Menu menuTS = new Menu("Target Selector", "tselect");
            TargetSelector.AddToMenu(menuTS);
            config.AddSubMenu(menuTS);

            // Orbwalker
            Menu menuOrb = new Menu("Orbwalker", "orbwalker");
            orbwalker = new Orbwalking.Orbwalker(menuOrb);
            config.AddSubMenu(menuOrb);

            // Draw settings
            Menu menuD = new Menu("Drawings ", "dsettings");
            menuD.AddItem(new MenuItem("drawaa", "Draw AA Range", true))
                .SetValue(new Circle(false, Color.FromArgb(180, 109, 111, 126)));
            menuD.AddItem(new MenuItem("drawee", "Draw E Range", true))
                .SetValue(new Circle(false, Color.FromArgb(180, 109, 111, 126)));
            menuD.AddItem(new MenuItem("drawrr", "Draw R Range", true))
                .SetValue(new Circle(false, Color.FromArgb(180, 109, 111, 126)));
            menuD.AddItem(new MenuItem("drawcombo", "Draw Combo-damage")).SetValue(true);
            config.AddSubMenu(menuD);
            // Combo Settings
            Menu menuC = new Menu("Combo ", "csettings");
            menuC.AddItem(new MenuItem("useq", "Use Q", true)).SetValue(true);
            menuC.AddItem(new MenuItem("usew", "Use W", true)).SetValue(true);
            menuC.AddItem(new MenuItem("usee", "Use E", true)).SetValue(true);
            menuC.AddItem(new MenuItem("user", "Use R", true)).SetValue(true);
            menuC.AddItem(new MenuItem("useFlash", "-- Use Flash to R", true)).SetValue(true);
            menuC.AddItem(new MenuItem("useIgnite", "Use Ignite")).SetValue(true);
            menuC = ItemHandler.addItemOptons(menuC);
            config.AddSubMenu(menuC);
            // LaneClear Settings
            Menu menuLC = new Menu("Lane Clear ", "Lcsettings");
            menuLC.AddItem(new MenuItem("useeLC", "Use E", true)).SetValue(true);
            config.AddSubMenu(menuLC);
            // Misc Settings
            Menu menuM = new Menu("Misc ", "Msettings");
            menuM.AddItem(new MenuItem("useqAAA", "Use Q after AA", true)).SetValue(true);
            menuM = Jungle.addJungleOptions(menuM);


            Menu autolvlM = new Menu("Auto-Level", "AutoLevel");
            autoLeveler = new AutoLeveler(autolvlM);
            menuM.AddSubMenu(autolvlM);

            config.AddSubMenu(menuM);
            var sulti = new Menu("Block R on X in Teamfights", "dontult");
            foreach (var hero in ObjectManager.Get<Obj_AI_Hero>().Where(hero => hero.IsEnemy))
            {
                sulti.AddItem(new MenuItem("ult" + hero.SkinName, hero.SkinName, true)).SetValue(false);
            }
            config.AddSubMenu(sulti);
            config.AddItem(new MenuItem("packets", "Use Packets")).SetValue(false);
            config.AddItem(new MenuItem("UnderratedAIO", "by Soresu v" + Program.version.ToString().Replace(",", ".")));
            config.AddToMainMenu();
        }
    }
}
