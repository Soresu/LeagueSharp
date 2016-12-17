using System;
using System.Collections.Generic;
using System.Linq;
using Color = System.Drawing.Color;
using LeagueSharp;
using LeagueSharp.Common;
using SPrediction;
using SharpDX;
using SharpDX.Direct3D9;
using UnderratedAIO.Helpers;
using UnderratedAIO.Helpers.SkillShot;
using Environment = UnderratedAIO.Helpers.Environment;
using Geometry = LeagueSharp.Common.Geometry;
using Orbwalking = UnderratedAIO.Helpers.Orbwalking;

namespace UnderratedAIO.Champions
{
    internal class Fizz
    {
        public static Menu config;
        public static Orbwalking.Orbwalker orbwalker;
        public static AutoLeveler autoLeveler;
        public static Spell Q, W, E, R;
        public static readonly Obj_AI_Hero player = ObjectManager.Player;
        public Team lastWtarget = Team.Null;
        public static bool justQ, justE, justR;
        public Vector3 lastE;

        public Fizz()
        {
            InitFizz();
            InitMenu();
            //Game.PrintChat("<font color='#9933FF'>Soresu </font><font color='#FFFFFF'>- Fizz</font>");
            Drawing.OnDraw += Game_OnDraw;
            Game.OnUpdate += Game_OnGameUpdate;
            Helpers.Jungle.setSmiteSlot();
            HpBarDamageIndicator.DamageToUnit = ComboDamage;
            Obj_AI_Base.OnProcessSpellCast += Game_ProcessSpell;
            AntiGapcloser.OnEnemyGapcloser += OnEnemyGapcloser;
            Orbwalking.BeforeAttack += Orbwalking_BeforeAttack;
        }

        private void Orbwalking_BeforeAttack(Orbwalking.BeforeAttackEventArgs args)
        {
            if (W.IsReady() && orbwalker.ActiveMode == Orbwalking.OrbwalkingMode.LaneClear &&
                config.Item("usewLC", true).GetValue<bool>() &&
                args.Target.Health > player.GetAutoAttackDamage((Obj_AI_Base) args.Target, true) * 3)
            {
                W.Cast();
            }
        }

        private void OnEnemyGapcloser(ActiveGapcloser gapcloser)
        {
            if (config.Item("useegc", true).GetValue<bool>() && Q.IsReady() &&
                gapcloser.End.Distance(player.Position) < 200 && !gapcloser.Sender.ChampionName.ToLower().Contains("yi"))
            {
                E.Cast(gapcloser.End);
            }
        }

        private void InitFizz()
        {
            Q = new Spell(SpellSlot.Q, 550);
            W = new Spell(SpellSlot.W);
            E = new Spell(SpellSlot.E, 650);
            E.SetSkillshot(0.5f, 165f, float.MaxValue, true, SkillshotType.SkillshotCircle);
            R = new Spell(SpellSlot.R, 1200);
            R.SetSkillshot(0.25f, 125f, 1300f, true, SkillshotType.SkillshotLine);
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
                    break;
                case Orbwalking.OrbwalkingMode.Mixed:
                    Harass();
                    break;
                case Orbwalking.OrbwalkingMode.LaneClear:
                    Clear();
                    break;
                case Orbwalking.OrbwalkingMode.LastHit:
                    break;
                default:
                    break;
            }
            var data = Program.IncDamages.GetAllyData(player.NetworkId);
            if (config.Item("autoECC", true).GetValue<bool>() && data.AnyCC)
            {
                CastAutoE();
            }
            if (config.Item("autoEdmg2", true).GetValue<Slider>().Value / 100f * player.Health < data.DamageTaken &&
                E.IsReady() && !OnTrident)
            {
                CastAutoE();
            }
            if (config.Item("castR", true).GetValue<KeyBind>().Active && R.IsReady())
            {
                Obj_AI_Hero target = DrawHelper.GetBetterTarget(R.Range, TargetSelector.DamageType.Magical, true);
                if (target != null && R.CanCast(target))
                {
                    CastR(target);
                    Orbwalking.MoveTo(Game.CursorPos);
                }
            }
        }

        private void CastR(Obj_AI_Hero target)
        {
            if (Program.IsSPrediction)
            {
                R.SPredictionCast(target, HitChance.High);
            }
            else
            {
                var pred = R.GetPrediction(target);
                if (pred.Hitchance >= HitChance.High)
                {
                    R.Cast(player.Position.Extend(pred.CastPosition, R.Range));
                }
            }
        }

        private void CastAutoE()
        {
            E.Cast(Game.CursorPos);
        }

        private void Harass()
        {
            Obj_AI_Hero target = DrawHelper.GetBetterTarget(Q.Range, TargetSelector.DamageType.Magical, true);
            float perc = config.Item("minmanaH", true).GetValue<Slider>().Value / 100f;
            if (player.Mana < player.MaxMana * perc || target == null)
            {
                return;
            }
            if (config.Item("useqH", true).GetValue<bool>() && Q.CanCast(target) && !player.IsWindingUp)
            {
                Q.CastOnUnit(target);
            }
            if (config.Item("useeH", true).GetValue<bool>() && E.CanCast(target) && !player.IsWindingUp &&
                Program.IncDamages.GetAllyData(player.NetworkId).ProjectileDamageTaken > 60)
            {
                if (OnTrident)
                {
                    E.Cast(target);
                }
                else
                {
                    E.Cast(target);
                }
            }
            if (config.Item("usewH", true).GetValue<bool>() && W.IsReady() && player.IsWindingUp)
            {
                W.Cast();
            }
        }

        private bool OnTrident
        {
            get { return player.Spellbook.GetSpell(SpellSlot.E).Name == "fizzjumptwo"; }
        }


        private void Clear()
        {
            float perc = config.Item("minmana", true).GetValue<Slider>().Value / 100f;
            if (player.Mana < player.MaxMana * perc)
            {
                return;
            }
            MinionManager.FarmLocation bestPosition =
                E.GetLineFarmLocation(
                    MinionManager.GetMinions(
                        ObjectManager.Player.ServerPosition, E.Range, MinionTypes.All, MinionTeam.NotAlly));
            var mini =
                MinionManager.GetMinions(player.Position, Q.Range, MinionTypes.All, MinionTeam.NotAlly)
                    .OrderByDescending(m => Q.GetDamage(m) > m.Health)
                    .ThenByDescending(m => m.MaxHealth)
                    .FirstOrDefault();
            if (mini != null && Q.IsReady() && config.Item("useqLC", true).GetValue<bool>())
            {
                Q.CastOnUnit(mini);
            }
            if (E.IsReady() && config.Item("useeLC", true).GetValue<bool>() &&
                bestPosition.MinionsHit >= config.Item("eMinHit", true).GetValue<Slider>().Value &&
                Program.IncDamages.GetAllyData(player.NetworkId).ProjectileDamageTaken > 15)
            {
                if (OnTrident)
                {
                    E.Cast(bestPosition.Position);
                }
                else
                {
                    E.Cast(bestPosition.Position);
                }
            }
        }

        private void Combo()
        {
            Obj_AI_Hero target = DrawHelper.GetBetterTarget(R.Range, TargetSelector.DamageType.Magical, true);
            if (target == null || target.IsInvulnerable || target.MagicImmune)
            {
                return;
            }
            if (config.Item("useItems").GetValue<bool>())
            {
                ItemHandler.UseItems(target, config, ComboDamage(target));
            }
            var data = Program.IncDamages.GetAllyData(player.NetworkId);
            var ignitedmg = (float) player.GetSummonerSpellDamage(target, Damage.SummonerSpell.Ignite);
            bool hasIgnite = player.Spellbook.CanUseSpell(player.GetSpellSlot("SummonerDot")) == SpellState.Ready;
            if (config.Item("useIgnite", true).GetValue<bool>() &&
                ignitedmg > HealthPrediction.GetHealthPrediction(target, 700) && hasIgnite &&
                !CombatHelper.CheckCriticalBuffs(target) &&
                target.Distance(player) > Orbwalking.GetRealAutoAttackRange(target) + 25 && !justQ)
            {
                player.Spellbook.CastSpell(player.GetSpellSlot("SummonerDot"), target);
            }
            if (Q.CanCast(target) && config.Item("useq", true).GetValue<bool>() && !player.IsWindingUp &&
                !data.IncSkillShot)
            {
                Q.CastOnUnit(target);
            }
            var cmbdmg = ComboDamage(target) + ItemHandler.GetItemsDamage(target);

            if (E.IsReady() && config.Item("usee", true).GetValue<bool>() && !player.IsWindingUp)
            {
                var enemyPred = E.GetPrediction(target);
                if (!OnTrident)
                {
                    if (config.Item("useedmg", true).GetValue<bool>() &&
                        data.DamageTaken > target.GetAutoAttackDamage(player, true) + 10)
                    {
                        E.Cast(enemyPred.CastPosition);
                    }
                    if (config.Item("useehighdmg", true).GetValue<bool>() && data.DamageTaken > player.Health * 0.4f)
                    {
                        E.Cast(enemyPred.CastPosition);
                    }
                    if (config.Item("useeaa", true).GetValue<bool>() &&
                        data.AADamageTaken < target.GetAutoAttackDamage(player, true) + 10 &&
                        !SpellDatabase.AnyReadyCC(player.Position, 700, true))
                    {
                        E.Cast(enemyPred.CastPosition);
                    }
                    if (config.Item("useecq", true).GetValue<bool>() &&
                        cmbdmg > HeroManager.Enemies.Where(e => target.Distance(e) < 1500).Sum(e => e.Health) &&
                        !target.UnderTurret(true))
                    {
                        E.Cast(enemyPred.CastPosition);
                    }
                }
                else
                {
                    if (data.DamageTaken < 10 && enemyPred.Hitchance >= HitChance.High)
                    {
                        E.Cast(enemyPred.CastPosition);
                    }
                }
            }
            if (config.Item("usew", true).GetValue<bool>() && W.IsReady() && player.IsWindingUp)
            {
                W.Cast();
            }
            if (config.Item("user", true).GetValue<bool>() && R.IsReady() && player.IsWindingUp &&
                cmbdmg * 1.6 + player.GetAutoAttackDamage(target, true) * 5 > target.Health &&
                (target.Health > R.GetDamage(target) * 1.4f || player.HealthPercent < 40))
            {
                CastR(target);
            }
        }


        private void Game_OnDraw(EventArgs args)
        {
            DrawHelper.DrawCircle(config.Item("drawqq", true).GetValue<Circle>(), Q.Range);
            DrawHelper.DrawCircle(config.Item("drawee", true).GetValue<Circle>(), E.Range);
            DrawHelper.DrawCircle(config.Item("drawrr", true).GetValue<Circle>(), R.Range);
            HpBarDamageIndicator.Enabled = config.Item("drawcombo", true).GetValue<bool>();
        }

        private static float ComboDamage(Obj_AI_Hero hero)
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
            //damage += ItemHandler.GetItemsDamage(target);
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
            //Fizzw
            //Fizzwcasttimeandanimation
            if (sender.IsMe)
            {
                if (args.Slot == SpellSlot.Q)
                {
                    justQ = true;
                    Utility.DelayAction.Add(500, () => { justQ = false; });
                }
                if (args.Slot == SpellSlot.E)
                {
                    justE = true;
                    lastE = args.End;
                    Utility.DelayAction.Add(500, () => { justE = false; });
                }
                if (args.Slot == SpellSlot.R)
                {
                    justR = true;
                    Utility.DelayAction.Add(500, () => { justR = false; });
                }
            }
        }

        private void InitMenu()
        {
            config = new Menu("Fizz ", "Fizz", true);
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
                .SetValue(new Circle(false, Color.FromArgb(180, 98, 242, 255)));
            menuD.AddItem(new MenuItem("drawee", "Draw E range", true))
                .SetValue(new Circle(false, Color.FromArgb(180, 98, 242, 255)));
            menuD.AddItem(new MenuItem("drawrr", "Draw R range", true))
                .SetValue(new Circle(false, Color.FromArgb(180, 98, 242, 255)));
            menuD.AddItem(new MenuItem("drawcombo", "Draw combo damage", true)).SetValue(true);
            config.AddSubMenu(menuD);
            // Combo Settings 
            Menu menuC = new Menu("Combo ", "csettings");
            menuC.AddItem(new MenuItem("useq", "Use Q", true)).SetValue(true);
            menuC.AddItem(new MenuItem("usew", "Use W", true)).SetValue(true);
            menuC.AddItem(new MenuItem("usee", "Use E", true)).SetValue(true);
            menuC.AddItem(new MenuItem("useehighdmg", "   On high damage", true)).SetValue(true);
            menuC.AddItem(new MenuItem("useedmg", "   On spell damage", true)).SetValue(true);
            menuC.AddItem(new MenuItem("useeaa", "   On AA damage", true)).SetValue(false);
            menuC.AddItem(new MenuItem("useecq", "   CloseGap 1v1", true)).SetValue(false);
            menuC.AddItem(new MenuItem("user", "Use R", true)).SetValue(true);
            menuC.AddItem(new MenuItem("castR", "Cast R", true))
                .SetValue(new KeyBind("T".ToCharArray()[0], KeyBindType.Press))
                .SetFontStyle(System.Drawing.FontStyle.Bold, SharpDX.Color.Orange);
            menuC.AddItem(new MenuItem("useIgnite", "Use Ignite", true)).SetValue(true);
            menuC = ItemHandler.addItemOptons(menuC);
            config.AddSubMenu(menuC);
            // Harass Settings
            Menu menuH = new Menu("Harass ", "Hsettings");
            menuH.AddItem(new MenuItem("useqH", "Use Q", true)).SetValue(false);
            menuH.AddItem(new MenuItem("usewH", "Use W", true)).SetValue(true);
            menuH.AddItem(new MenuItem("useeH", "Use E if block any damage", true)).SetValue(false);
            menuH.AddItem(new MenuItem("minmanaH", "Keep X% mana", true)).SetValue(new Slider(1, 1, 100));
            config.AddSubMenu(menuH);
            // LaneClear Settings
            Menu menuLC = new Menu("LaneClear ", "Lcsettings");
            menuLC.AddItem(new MenuItem("useqLC", "Use Q", true)).SetValue(true);
            menuLC.AddItem(new MenuItem("usewLC", "Use w", true)).SetValue(true);
            menuLC.AddItem(new MenuItem("useeLC", "Use e", true)).SetValue(false);
            menuLC.AddItem(new MenuItem("eMinHit", "   Min hit", true)).SetValue(new Slider(3, 1, 6));
            menuLC.AddItem(new MenuItem("minmana", "Keep X% mana", true)).SetValue(new Slider(20, 1, 100));
            config.AddSubMenu(menuLC);

            Menu menuM = new Menu("Misc ", "Msettings");
            menuM.AddItem(new MenuItem("useegc", "Use E gapclosers", true)).SetValue(false);
            Menu menuE = new Menu("Auto E ", "Esettings");
            menuE.AddItem(new MenuItem("autoECC", "Before CC", true)).SetValue(true);
            menuE.AddItem(new MenuItem("autoEdmg2", "Before Damage in % Health", true)).SetValue(new Slider(20, 1, 100));
            menuM.AddSubMenu(menuE);
            menuM = DrawHelper.AddMisc(menuM);
            config.AddSubMenu(menuM);


            config.AddItem(new MenuItem("UnderratedAIO", "by Soresu v" + Program.version.ToString().Replace(",", ".")));
            config.AddToMainMenu();
        }
    }
}