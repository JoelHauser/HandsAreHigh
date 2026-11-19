using System.Collections.Generic;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    internal enum ScenarioEvent
    {
        Opening, Reload, Glance, ThirdParty, Suppress,
        DamageLeg, DamageChest, Bleed, RemoveGun, Kill,
        OrderStay, OrderDrop, OrderFollow, OrderKneel, OrderLeave,
        CaptorOrderStay, CaptorOrderDrop,
        ForceRun, ForceGrab, PretendLeft, Wiggle, Finger,
        SurrenderToYou, SurrenderToEnemy
    }

    // Controlled events against ONE selected participant. Each says what it is:
    //   real       a game action is requested (reload, kill, spawn, damage, loot removal, an
    //              order through the normal order path); the game may still refuse it
    //   simulated  a synthetic input to the decision logic (an opening, a suppression event):
    //              tests what he decides, never whether the input is detected
    //   forced     skips a decision (force run / grab / surrender / gesture start window)
    // All of them go through the mod's existing paths, with their busy-hands and range checks.
    internal static class ScenarioEvents
    {
        internal static readonly ScenarioEvent[] All = (ScenarioEvent[])System.Enum.GetValues(typeof(ScenarioEvent));

        internal static string Label(ScenarioEvent e)
        {
            switch (e)
            {
                case ScenarioEvent.Opening: return "Opening for the prisoner (SIMULATED)";
                case ScenarioEvent.Reload: return "Captor reloads (real request)";
                case ScenarioEvent.Glance: return "Captor glances away (real glance, forced start)";
                case ScenarioEvent.ThirdParty: return "Hostile bot appears 40 m out (real spawn)";
                case ScenarioEvent.Suppress: return "Suppression +3 (SIMULATED)";
                case ScenarioEvent.DamageLeg: return "Damage left leg 30 (real health change)";
                case ScenarioEvent.DamageChest: return "Damage thorax 25, non-lethal (real health change)";
                case ScenarioEvent.Bleed: return "Light bleed (real effect)";
                case ScenarioEvent.RemoveGun: return "Remove his dropped gun (real loot removal)";
                case ScenarioEvent.Kill: return "Kill (real death)";
                case ScenarioEvent.OrderStay: return "Your order: Stay";
                case ScenarioEvent.OrderDrop: return "Your order: Drop weapons";
                case ScenarioEvent.OrderFollow: return "Your order: Follow";
                case ScenarioEvent.OrderKneel: return "Your order: Kneel";
                case ScenarioEvent.OrderLeave: return "Your order: Leave";
                case ScenarioEvent.CaptorOrderStay: return "His bot captor orders: Stay";
                case ScenarioEvent.CaptorOrderDrop: return "His bot captor orders: Drop weapons";
                case ScenarioEvent.ForceRun: return "Run now (FORCED, no roll)";
                case ScenarioEvent.ForceGrab: return "Grab his gun now (FORCED, no roll)";
                case ScenarioEvent.PretendLeft: return "Act as if his captor left (SIMULATED)";
                case ScenarioEvent.Wiggle: return "Wiggle window (FORCED start, real conditions)";
                case ScenarioEvent.Finger: return "Middle finger window (FORCED start, real conditions)";
                case ScenarioEvent.SurrenderToYou: return "Surrender to you (FORCED)";
                default: return "Surrender to the bot he fights (FORCED)";
            }
        }

        private static TL Nature(ScenarioEvent e)
        {
            switch (e)
            {
                case ScenarioEvent.Opening: case ScenarioEvent.Suppress: case ScenarioEvent.PretendLeft: return TL.Simulated;
                case ScenarioEvent.Glance: case ScenarioEvent.ForceRun: case ScenarioEvent.ForceGrab: case ScenarioEvent.Wiggle:
                case ScenarioEvent.Finger: case ScenarioEvent.SurrenderToYou: case ScenarioEvent.SurrenderToEnemy: return TL.Forced;
                default: return TL.Real;
            }
        }

        // Why this event can't target him now (null: it can).
        internal static string Unavailable(Scenario sc, ScenarioEvent e, Participant t)
        {
            if (t == null) return "no target";
            if (t.Player == null || !t.Spawned) return "not spawned";
            if (t.Dead || !Perception.Alive(t.Player)) return "dead";
            SurrenderState s = ScenarioOps.StateOf(t);
            bool handsUp = s != null && s.Phase == Phase.HandsUp;
            Captors.TryGet(t.Player, out CaptorState cs);
            switch (e)
            {
                case ScenarioEvent.Opening: case ScenarioEvent.ForceRun: case ScenarioEvent.PretendLeft:
                case ScenarioEvent.Wiggle: case ScenarioEvent.Finger:
                case ScenarioEvent.OrderStay: case ScenarioEvent.OrderDrop: case ScenarioEvent.OrderFollow:
                case ScenarioEvent.OrderKneel: case ScenarioEvent.OrderLeave:
                    return handsUp ? null : "isn't holding his hands up";
                case ScenarioEvent.ForceGrab:
                    if (!handsUp) return "isn't holding his hands up";
                    return s.DroppedWeapon == null ? "dropped no gun" : null;
                case ScenarioEvent.CaptorOrderStay: case ScenarioEvent.CaptorOrderDrop:
                    if (!handsUp) return "isn't holding his hands up";
                    return s.Custody == null ? "no bot has custody of him" : null;
                case ScenarioEvent.Reload:
                    if (s != null) return "is held himself";
                    return t.Player.HandsController is Player.FirearmController ? null : "no gun in his hands";
                case ScenarioEvent.Glance:
                    return cs == null ? "isn't guarding anyone" : null;
                case ScenarioEvent.RemoveGun:
                    if (s?.DroppedWeapon == null) return "dropped no gun";
                    return ScenarioOps.FindLoose(s.DroppedWeapon) == null ? "his gun isn't lying loose (someone has it)" : null;
                case ScenarioEvent.SurrenderToYou:
                    return handsUp ? "already has his hands up" : t.Existing != null ? "an existing raid bot (your teammate)" : null;
                case ScenarioEvent.SurrenderToEnemy:
                    if (handsUp) return "already has his hands up";
                    Player foe = t.Owner2?.Memory?.GoalEnemy?.Person as Player;
                    return foe == null || !foe.IsAI || !Perception.Alive(foe) ? "isn't fighting a bot right now" : null;
                case ScenarioEvent.ThirdParty:
                    return sc.Stage == Stage.Running ? null : "only once the scenario runs";
                default:
                    return null;
            }
        }

        // Run it against exactly this participant; the outcome goes to the timeline and back.
        internal static string Run(Scenario sc, ScenarioEvent e, Participant t)
        {
            string no = Unavailable(sc, e, t);
            if (no != null)
            {
                string m = $"{Label(e)} on {t?.Tag ?? "nobody"}: not done ({no})";
                sc.Note(TL.ExecFail, m);
                return m;
            }
            string outcome;
            try { outcome = Do(sc, e, t); }
            catch (System.Exception ex)
            {
                HandsAreHighPlugin.Report("scenario event", ex);
                outcome = $"failed ({ex.GetType().Name}: {ex.Message})";
            }
            string msg = $"{Label(e)} on {t.Tag}: {outcome}";
            sc.Note(outcome.StartsWith("failed") || outcome.StartsWith("refused") ? TL.ExecFail : Nature(e), msg);
            return msg;
        }

        private static string Do(Scenario sc, ScenarioEvent e, Participant t)
        {
            SurrenderState s = ScenarioOps.StateOf(t);
            Player me = sc.Observer;
            switch (e)
            {
                case ScenarioEvent.Opening: return SurrenderRegistry.DebugOpening(s);
                case ScenarioEvent.ForceRun: return SurrenderRegistry.DebugForceRun(s);
                case ScenarioEvent.ForceGrab: return SurrenderRegistry.DebugForceGrab(s);
                case ScenarioEvent.PretendLeft: return SurrenderRegistry.DebugLeave(s);
                case ScenarioEvent.Reload:
                {
                    bool ok = t.Owner2.WeaponManager?.Reload != null && t.Owner2.WeaponManager.Reload.TryReload();
                    return ok ? "reload started (BotReload.TryReload)" : "refused by the game (full magazine, nothing to reload with, or busy)";
                }
                case ScenarioEvent.Glance:
                    return Captors.ForceGlance(t.Player, out string why) ? "his glance code runs now (where and how long: his own)" : $"refused: {why}";
                case ScenarioEvent.ThirdParty: return Scenarios.SpawnThirdParty(sc, t);
                case ScenarioEvent.Suppress:
                    Suppression.Synthetic(t.Player, 1f); Suppression.Synthetic(t.Player, 1f); Suppression.Synthetic(t.Player, 1f);
                    return $"3 synthetic fire events, now {Suppression.Decayed(SurrenderRegistry.HistoryOf(t.Player), Time.time):0.0} shots' worth (no shot fired, no near miss)";
                case ScenarioEvent.DamageLeg:
                    return $"{ScenarioOps.Hurt(t.Player.ActiveHealthController, EBodyPart.LeftLeg, 30f):0} damage, health {Arsenal.HealthFraction(t.Player):P0} (no hit event)";
                case ScenarioEvent.DamageChest:
                    return $"{ScenarioOps.Hurt(t.Player.ActiveHealthController, EBodyPart.Chest, 25f):0} damage, health {Arsenal.HealthFraction(t.Player):P0} (no hit event)";
                case ScenarioEvent.Bleed:
                    t.Player.ActiveHealthController.DoBleed(false, EBodyPart.LeftLeg);
                    return "light bleeding on the left leg";
                case ScenarioEvent.RemoveGun:
                    return ScenarioOps.CleanItem(sc, s.DroppedWeapon, remove: true);
                case ScenarioEvent.Kill:
                    t.Player.ActiveHealthController.Kill(EDamageType.Bullet);
                    return "killed (ActiveHealthController.Kill)";
                case ScenarioEvent.OrderStay: return Order(s, OrderKind.Stay, me, true);
                case ScenarioEvent.OrderDrop: return Order(s, OrderKind.DropWeapons, me, true);
                case ScenarioEvent.OrderFollow: return Order(s, OrderKind.Follow, me, true);
                case ScenarioEvent.OrderKneel: return Order(s, OrderKind.Kneel, me, true);
                case ScenarioEvent.OrderLeave: return Order(s, OrderKind.Leave, me, true);
                case ScenarioEvent.CaptorOrderStay: return Order(s, OrderKind.Stay, s.Custody.Bot, false);
                case ScenarioEvent.CaptorOrderDrop: return Order(s, OrderKind.DropWeapons, s.Custody.Bot, false);
                case ScenarioEvent.Wiggle:
                    if (!Settings.Wiggle.Value) return "refused: Wiggle is off in the config";
                    s.WigglePendingUntil = Time.time + Settings.WiggleWindow.Value;
                    return $"wiggle may start within {Settings.WiggleWindow.Value:0} s if its own conditions hold (room, you watching)";
                case ScenarioEvent.Finger:
                    if (!Settings.Defiant.Value) return "refused: the middle finger is off in the config";
                    s.DefiantPendingUntil = Time.time + Settings.DefiantWindow.Value;
                    return $"middle finger may start within {Settings.DefiantWindow.Value:0} s if its own conditions hold";
                case ScenarioEvent.SurrenderToYou:
                    return ScenarioOps.Surrender(sc, t, me, true, "event") ?? "surrenders to you";
                default:
                {
                    Player foe = t.Owner2.Memory.GoalEnemy.Person as Player;
                    return ScenarioOps.Surrender(sc, t, foe, true, "event: to the bot he fights") ?? $"surrenders to {Restraint.Name(foe)}; that bot decides";
                }
            }
        }

        private static string Order(SurrenderState s, OrderKind k, Player by, bool human) =>
            Orders.Give(s, k, by, human, out string why) ? $"given by {(human ? "you" : Restraint.Name(by))}" : $"refused: {why}";
    }
}
