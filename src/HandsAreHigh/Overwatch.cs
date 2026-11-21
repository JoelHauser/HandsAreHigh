using System.Collections.Generic;
using System.Text;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace HandsAreHigh
{
    // 0.20.0: coordinated squad coverage during captivity.
    //
    // When a bot of a squad takes custody of a prisoner, the captor stays the captor (custody,
    // orders, gear, judgement: Captors.cs, unchanged ownership) and one squadmate is given the
    // supporting role: he covers the prisoner and the captor. No second ownership: the guard is
    // an attachment of the captor's CaptorState (cs.Guard) and ends with it.
    //
    // Squad membership: the game's own BotsGroup (cs.Group = the captor's BotOwner.BotsGroup at
    // custody; Member(i), MembersCount), the same group the restraint system already uses.
    //
    // Who covers (Select, every 2 s while nobody does): a living, active member of that group who
    // is not the captor, not held, not guarding a prisoner of his own, not covering another captor,
    // not fetching gear, not a player ally (your group or a Pit Fireteam follower: their movement
    // stays with Pit Fireteam and your orders), who holds fire at this prisoner (the squad's
    // restraint), is not in a fight (another enemy in sight, or under fire: the game's own
    // EnemyInfo.IsVisible and BotMemory.IsUnderFire, plus our near misses), is not out of ammo,
    // is within 40 m and can walk to the prisoner. Scored by distance, sight of the prisoner
    // (EnemyInfo + line), a gun up and not reloading, and his injuries. The best one gets the
    // role and keeps it until it becomes invalid (no re-scoring every update). Nobody fits: the
    // captor knows he holds the man alone (cs.AloneWhy), logged once, and weighs risks without a
    // cover discount (CaptorLoot.Risk, x1.15).
    //
    // Where he stands (UpdatePosition / PickSpot): a reachable NavMesh spot about "Guard distance"
    // from the prisoner, 50-125 deg round the prisoner from the captor (never stacked on him, never
    // behind the prisoner from the captor's side), with a clear line from eye height to the
    // prisoner's chest, his own line of fire (extended 8 m past the prisoner) clear of the captor
    // and other squadmates, and out of the captor's line of fire; scored by that angle, distance,
    // how far he must walk, solid geometry beside or behind it (cover), hidden from a recent
    // enemy sighting. The spot he already stands on is kept while it stays valid. He moves (a
    // complete NavMesh path, BotMover.GoToByWay at a walk, gun on the prisoner) only when his spot
    // stops working for 1.2 s (view blocked, captor or squadmate in his line, too close/far) or the
    // prisoner moved 3 m; at most 8 moves per role, 3 s apart; stuck -> covers from where he is.
    //
    // Attention (UpdateScan): mostly the prisoner; every 5-10 s a 0.9-1.6 s look outward (behind
    // the captor, or towards an enemy he saw lately). Never while the captor is fetching or walking
    // up, or while the prisoner moves or lowers his hands.
    //
    // Coverage is measured, not assumed (Evaluate, 4x a second): he covers only while he is not
    // fighting someone else, not glancing out, has a firearm up, isn't reloading, switching or
    // running, looks within "Covered within" of the prisoner with a clear line, and the captor is
    // not in his line of fire. Every change is logged with its reason; the totals at the end.
    //
    // Captor actions (HoldForCover): before an optional action that takes his eyes and gun off the
    // man (fetching dropped gear, closing in) the captor waits for his guard's coverage up to
    // "Captor waits for cover"; then he goes ahead and accepts the risk (logged). During a fetch,
    // a loss he can tell (the guard's gunfire, a reload callout, the guard going down; not a glance
    // or an obstructed view he can't see) is judged once: pause (the fetch ends, weighed again
    // later) or carry on (CaptorLoot.CoverLossJudgement).
    //
    // Resistance (Watch, 10x a second): only what shows (Visible: a weapon in his hands that isn't
    // being dropped, a draw, walking towards the gun on the ground, moving off when not ordered,
    // starting up from his knees, lowering his hands). Compliance moves (ordered follow, a kneel or
    // its spot walk, a turn) are never a threat. Noticed only if the guard sees him (the game's
    // sight + a line, within 100 deg of where he looks), after his reaction time ("Guard reaction
    // time" x 0.75-1.35, + peripheral 0.25-0.55 s, + 0.15 s walking, + 0.3-0.7 s fighting someone
    // else). A weapon: his restraint on that man becomes Reject (squad), he remembers it, and the
    // man is made his target (BotMemory.GoalEnemy; SAIN picks among visible enemies itself): the
    // combat AI fires. Friendly fire is the game's own: vanilla Shoot ends a burst whose ray hits a
    // same-side player (ShootData.CheckFriendlyFire), SAIN sphere-casts its own (SAINFriendlyFireClass);
    // the reaction line logs whether the captor stood in that line. Anything else: he tightens
    // (eyes on him, no glances) and holds fire. Restraint.OnArmed leaves a guard's entry to this
    // (his own perception); one who never sees it gets his squad's word after 2.5 s.
    //
    // Endings (Release): captor ends custody (let go, killed him, lost him, gave up), captor down
    // (CaptorDown hands custody to the guard first if he is practical, then any other member),
    // guard invalid (down, asleep, held, took a prisoner, left the group, a player ally now, no
    // longer holding fire, too far, out of ammo, fighting longer than "Guard leaves the role
    // after fighting"): replaced when another member fits. A guard released while the man is
    // resisting stays a responder for 3 s (his reaction still runs). Raid end / disable / teardown:
    // Clear (Captors.Clear). ORBIT: a guard is kept out of sleep while he covers (unblocked only if
    // he isn't a captor or held himself by then).
    internal enum GuardStage { Moving, Holding }

    internal sealed class CoverGuard
    {
        internal Player Bot;
        internal BotOwner Owner;
        internal string Name;
        internal CaptorState For;
        internal SurrenderState Prisoner;
        internal float Since;

        // Position.
        internal GuardStage Stage = GuardStage.Holding;
        internal bool HasSpot;
        internal Vector3 Spot, SpotFor;
        internal bool PathIssued;
        internal float NextPathAt;
        internal Vector3 ProgressFrom;
        internal float ProgressSince, MoveSince;
        internal int Moves;
        internal float NextMoveAt;
        internal float BlockedSince = -1f;
        internal string BlockedWhy, NoSpotLogged;
        internal bool MovesSpentLogged;
        internal float InPositionAt = -1f;

        // Attention.
        internal float NextScanAt, ScanUntil;
        internal Vector3 ScanPoint;
        internal Vector3 AlertAt;
        internal float AlertSeenAt = -1000f;
        internal bool Tight;                // something showed: eyes on him, no glances

        // Other threats.
        internal bool Distracted;
        internal float DistractedSince = -1f, QuietSince = -1f;
        internal string DistractedBy;

        // Coverage.
        internal bool Effective;
        internal string CoverWhy = "not checked yet";
        internal float EvalAt = -1f, CoveredTime, LoggedAt = -10f;
        internal readonly Dictionary<string, int> Losses = new Dictionary<string, int>();

        // Resistance.
        internal float ReactAt = -1f, NoticedAt = -1f;
        internal string Saw;
        internal bool SawArmed, Reacted, MissLogged, TightLogged;
        internal float Deadline = -1f;      // a responder after custody ended
    }

    internal static class Overwatch
    {
        private static readonly Dictionary<Player, CoverGuard> ByGuard = new Dictionary<Player, CoverGuard>();
        private static readonly List<CoverGuard> Responders = new List<CoverGuard>(2);
        private static readonly List<CoverGuard> Scratch = new List<CoverGuard>(4);
        private static readonly StringBuilder Sb = new StringBuilder(256);
        private static float _nextFast;

        private const float MaxRange = 40f;
        private const int MaxMoves = 8;
        private const float GiveSquadWord = 2.5f;

        internal static int Count => ByGuard.Count + Responders.Count;
        internal static bool On => Settings.SquadCover.Value && Settings.Enabled.Value;

        internal static bool TryGet(Player bot, out CoverGuard g)
        {
            g = null;
            return bot != null && ByGuard.Count > 0 && ByGuard.TryGetValue(bot, out g);
        }

        internal static bool IsGuard(Player bot) => bot != null && ByGuard.Count > 0 && ByGuard.ContainsKey(bot);

        // Restraint.OnArmed: this observer reacts by his own perception (Watch), not at once.
        internal static bool ReactsHimself(SurrenderState s, Player observer)
        {
            if (observer == null) return false;
            if (ByGuard.TryGetValue(observer, out CoverGuard g) && g.Prisoner == s && !g.Reacted) return true;
            for (int i = 0; i < Responders.Count; i++)
                if (Responders[i].Bot == observer && Responders[i].Prisoner == s && !Responders[i].Reacted) return true;
            return false;
        }

        // ---- the captor's view --------------------------------------------------------------

        // His guard is in position and not fighting: what the captor counts on when he weighs a
        // risk (not the instantaneous coverage, which flickers with every glance).
        internal static bool InPosition(CaptorState cs, out string who)
        {
            who = null;
            CoverGuard g = cs?.Guard;
            if (g == null || g.Distracted || g.Stage != GuardStage.Holding || !g.HasSpot) return false;
            who = g.Name;
            return true;
        }

        internal static bool Covered(CaptorState cs) => cs?.Guard != null && cs.Guard.Effective;

        // A loss of coverage the captor can tell without looking (gunfire, a reload callout, his
        // guard down or gone): null when he can't tell (a glance, an obstructed view).
        internal static string LoudLoss(CaptorState cs, bool relied)
        {
            CoverGuard g = cs?.Guard;
            if (g == null) return relied ? "his guard is gone" : null;
            if (g.Distracted) return $"{g.Name} is fighting {g.DistractedBy}";
            if (g.Bot.HandsController is Player.FirearmController fc && fc.IsInReloadOperation()) return $"{g.Name} is reloading";
            if (g.Bot.IsSprintEnabled) return $"{g.Name} is running";
            return null;
        }

        // Before an optional action that takes his eyes and gun off the man. True: wait (this check
        // is spent waiting). Never waits forever: after "Captor waits for cover" he goes ahead.
        internal static bool HoldForCover(CaptorState cs, SurrenderState s, float now, string what)
        {
            if (!On || cs == null) return false;
            float limit = Settings.CoverWait.Value;
            CoverGuard g = cs.Guard;
            if (g == null || limit <= 0f)
            {
                if (cs.CoverNotedFor != what)
                {
                    cs.CoverNotedFor = what;
                    if (cs.AloneWhy != null && LogOn)
                        Log($"{cs.Name}: {what}, nobody covering him ({cs.AloneWhy}): his own risk");
                }
                cs.CoverWaitSince = -1f;
                return false;
            }
            if (g.Effective)
            {
                if (cs.CoverNotedFor != what && LogOn)
                    Log($"{cs.Name}: {what}, {g.Name} covering him{(cs.CoverWaitSince >= 0f ? $" (waited {now - cs.CoverWaitSince:0.0} s)" : "")}");
                cs.CoverNotedFor = what;
                cs.CoverWaitSince = -1f;
                cs.CoverAcceptedFor = null;
                return false;
            }
            if (cs.CoverAcceptedFor == what) return false;
            if (cs.CoverWaitSince < 0f || cs.CoverWaitFor != what)
            {
                cs.CoverWaitSince = now;
                cs.CoverWaitFor = what;
                if (LogOn) Log($"{cs.Name}: waits for {g.Name} to cover him before {what} ({g.Name}: {g.CoverWhy})");
                return true;
            }
            if (now - cs.CoverWaitSince < limit) return true;
            if (LogOn) Log($"{cs.Name}: {what} without cover after waiting {now - cs.CoverWaitSince:0.0} s ({g.Name}: {g.CoverWhy}): accepts the risk");
            cs.CoverAcceptedFor = what;
            cs.CoverNotedFor = what;
            cs.CoverWaitSince = -1f;
            return false;
        }

        // ---- per captor (Captors.Check, 4x a second) ------------------------------------------

        internal static void Update(CaptorState cs, SurrenderState s, float now)
        {
            if (!On)
            {
                if (cs.Guard != null) Release(cs, "squad cover turned off", resisted: false);
                return;
            }
            CoverGuard g = cs.Guard;
            if (g != null)
            {
                string bad = Invalid(g, cs, s);
                if (bad != null) { Drop(cs, bad); g = null; }
            }
            if (g == null)
            {
                if (s.Phase == Phase.HandsUp && !s.Suspended && cs.Owner.BotState == EBotState.Active) Select(cs, s, now);
                return;
            }
            // Not gated on the prisoner's phase: a decision he hasn't shown yet changes nothing the
            // guard does (Watch reacts to what shows). Asleep: nothing moves.
            if (s.Suspended) return;

            ThreatLevel t = Captors.Threat(g.Owner, g.Bot, s.Bot, now, out string why, out Vector3 at, out bool atKnown);
            bool fighting = t >= ThreatLevel.Fire;
            if (fighting)
            {
                g.QuietSince = -1f;
                if (!g.Distracted)
                {
                    g.Distracted = true;
                    g.DistractedSince = now;
                    g.DistractedBy = why;
                    g.Stage = GuardStage.Holding;
                    g.PathIssued = false;
                    if (LogOn) Log($"{g.Name}: stops covering {s.Name} to fight ({why}); the combat AI has him");
                }
                g.DistractedBy = why;
                if (now - g.DistractedSince > Settings.GuardGiveUp.Value) { Drop(cs, $"fighting {why} for {Settings.GuardGiveUp.Value:0} s"); return; }
            }
            else if (g.Distracted)
            {
                // Back only after a real lull (as a captor: QuietBeforeGuarding with no contact or
                // fire); the give-up clock keeps running through short pauses in the fight.
                if (g.QuietSince < 0f) g.QuietSince = now;
                if (now - g.QuietSince >= Mathf.Max(2f, Settings.QuietBeforeGuarding.Value))
                {
                    g.Distracted = false;
                    g.QuietSince = -1f;
                    g.BlockedSince = -1f;
                    if (LogOn) Log($"{g.Name}: quiet again after {now - g.DistractedSince:0.0} s; back to covering {s.Name}");
                }
                else if (now - g.DistractedSince > Settings.GuardGiveUp.Value) { Drop(cs, $"fighting {g.DistractedBy} for {Settings.GuardGiveUp.Value:0} s"); return; }
            }
            if (t == ThreatLevel.Alert && atKnown) { g.AlertAt = at; g.AlertSeenAt = now; }
            if (!g.Distracted)
            {
                UpdatePosition(g, cs, s, now);
                UpdateScan(g, cs, s, now);
            }
            Evaluate(g, cs, s, now);
            LootingBotsAdapter.Hold(g.Owner, 10f);
        }

        // 10x a second (Captors.Tick): resistance seen by guards and responders.
        internal static void Tick(float now)
        {
            if (ByGuard.Count == 0 && Responders.Count == 0) return;
            if (now < _nextFast) return;
            _nextFast = now + 0.1f;
            Scratch.Clear();
            foreach (CoverGuard g in ByGuard.Values) Scratch.Add(g);
            for (int i = 0; i < Scratch.Count; i++)
            {
                CoverGuard g = Scratch[i];
                try { Watch(g, now); } catch (System.Exception e) { HandsAreHighPlugin.Report("guard watch", e); }
            }
            Scratch.Clear();
            for (int i = Responders.Count - 1; i >= 0; i--)
            {
                CoverGuard g = Responders[i];
                bool done = g.Reacted || now >= g.Deadline || !Perception.Alive(g.Bot) || g.Prisoner == null || g.Prisoner.Dead;
                if (!done)
                {
                    try { Watch(g, now); } catch (System.Exception e) { HandsAreHighPlugin.Report("guard watch", e); done = true; }
                    done |= g.Reacted;
                }
                if (!done) continue;
                Responders.RemoveAt(i);
                if (!g.Reacted && Perception.Alive(g.Bot) && g.Prisoner?.Bot != null && !g.Prisoner.Dead &&
                    Restraint.TryGet(g.Prisoner.Bot, g.Bot, out RestraintEntry entry) && entry.Holds && g.Prisoner.ArmedShownAt >= 0f)
                {
                    Restraint.Set(g.Prisoner, g.Owner, RestraintKind.Reject, $"{g.Prisoner.Name} resisted (his squad's word: he didn't see it himself)", squad: true);
                    if (LogOn) Log($"{g.Name}: never saw {g.Prisoner.Name} resist ({(g.MissLogged ? g.Saw ?? "looking elsewhere" : "nothing showed to him")}); takes his squad's word for it");
                }
                Unblock(g, "stopped covering");
            }
        }

        // ---- selection --------------------------------------------------------------------------

        private static void Select(CaptorState cs, SurrenderState s, float now)
        {
            if (now < cs.NextGuardPick) return;
            cs.NextGuardPick = now + 2f;
            BotsGroup grp = cs.Group;
            if (grp == null || grp.MembersCount <= 1) { Alone(cs, s, "no squad"); return; }
            BotOwner best = null, second = null;
            float bestScore = float.MinValue, secondScore = float.MinValue;
            string bestDesc = null, secondDesc = null;
            Sb.Clear();
            int members = 0;
            for (int i = 0; i < grp.MembersCount; i++)
            {
                BotOwner m = grp.Member(i);
                if (m == null || m == cs.Owner) continue;
                members++;
                string no = Ineligible(m, cs, s, out float score, out string desc);
                if (no != null)
                {
                    if (Sb.Length > 0) Sb.Append("; ");
                    Sb.Append(Restraint.Name(m.GetPlayer)).Append(": ").Append(no);
                    continue;
                }
                if (score > bestScore) { second = best; secondScore = bestScore; secondDesc = bestDesc; best = m; bestScore = score; bestDesc = desc; }
                else if (score > secondScore) { second = m; secondScore = score; secondDesc = desc; }
            }
            if (members == 0) { Alone(cs, s, "no squadmate left"); return; }
            if (best == null) { Alone(cs, s, Sb.ToString()); return; }
            // One complete path to the prisoner (a frame with budget; else next check).
            if (!Perception.TakeHeavy()) { cs.NextGuardPick = now; return; }
            if (!Perception.Reachable(best.GetPlayer.Position, s.Bot.Position))
            {
                if (Sb.Length > 0) Sb.Append("; ");
                Sb.Append(Restraint.Name(best.GetPlayer)).Append(": no way to walk to him");
                best = second; bestDesc = secondDesc;
                if (best == null || !Perception.Reachable(best.GetPlayer.Position, s.Bot.Position))
                {
                    if (best != null) { Sb.Append("; ").Append(Restraint.Name(best.GetPlayer)).Append(": no way to walk to him"); }
                    Alone(cs, s, Sb.ToString());
                    return;
                }
            }
            string others = Sb.ToString();
            Assign(cs, s, best, bestDesc, others, now);
        }

        private static string Ineligible(BotOwner m, CaptorState cs, SurrenderState s, out float score, out string desc)
        {
            score = 0f; desc = null;
            Player mp = m.GetPlayer;
            if (m.IsDead || mp == null || !Perception.Alive(mp)) return "dead";
            if (m.BotState != EBotState.Active) return "not active";
            if (Teammates.IsPlayerTeammate(m)) return "under the player's orders";
            if (SurrenderRegistry.HoldsBot(mp)) return "held himself";
            if (Captors.TryGet(mp, out _)) return "guarding a prisoner of his own";
            if (ByGuard.TryGetValue(mp, out CoverGuard other)) return $"covering {other.For?.Name}";
            if (CaptorLoot.TryJob(mp, out _)) return "busy with gear";
            if (!Restraint.TryGet(s.Bot, mp, out RestraintEntry e)) return "hasn't decided about him";
            if (!e.Holds) return "doesn't hold fire at him";
            float d = Vector3.Distance(mp.Position, s.Bot.Position);
            if (d > MaxRange) return $"too far ({d:0} m)";
            if (Restraint.OtherThreat(m, s.Bot, out string threat)) return $"in a fight ({threat})";
            if (m.Memory != null && m.Memory.IsUnderFire) return "under fire";
            if (Arsenal.OutOfAmmo(m)) return "out of ammo";
            bool sees = Perception.Recognises(m, s.Bot);
            bool gun = mp.HandsController is Player.FirearmController fc && !fc.IsInReloadOperation();
            float hurt = mp.HealthController != null ? NaturalSurrender.Injury(mp, out _) : 0f;
            score = 30f - 0.8f * d + (sees ? 15f : 0f) + (gun ? 10f : 0f) - 20f * hurt;
            desc = $"{d:0} m from him, {(sees ? "sees him" : "no sight of him yet")}, {(gun ? "gun up" : "no gun ready")}{(hurt > 0.05f ? $", hurt {hurt:0.00}" : "")}, score {score:0}";
            return null;
        }

        private static void Alone(CaptorState cs, SurrenderState s, string why)
        {
            // Compared without numbers (distances change every pick; the reasons rarely do).
            string key = NoDigits(why);
            bool first = cs.AloneWhy == null;
            cs.AloneWhy = why;
            if (key == cs.AloneKey) return;
            cs.AloneKey = key;
            if (LogOn) Log($"{cs.Name}: nobody can cover him while he holds {s.Name} ({why}){(first ? "; he holds him alone" : "")}");
        }

        private static void Assign(CaptorState cs, SurrenderState s, BotOwner m, string desc, string others, float now)
        {
            Player mp = m.GetPlayer;
            var g = new CoverGuard
            {
                Bot = mp, Owner = m, Name = Restraint.Name(mp), For = cs, Prisoner = s, Since = now,
                EvalAt = now, NextScanAt = now + Dice.Range(4f, 8f), NextMoveAt = now
            };
            cs.Guard = g;
            cs.AloneWhy = null;
            cs.AloneKey = null;
            ByGuard[mp] = g;
            OrbitAdapter.KeepAwake(m, true, "covers a captor");
            LootingBotsAdapter.Hold(m, 10f);
            CaptorVoice.Say(mp, CaptorLine.Covering, Dice.Range(0.6f, 1.4f));
            if (LogOn)
                Log($"{cs.Name}'s squad: {g.Name} covers him while he holds {s.Name} ({desc}){(string.IsNullOrEmpty(others) ? "" : $"; not chosen: {others}")}");
        }

        private static string NoDigits(string t)
        {
            var b = new StringBuilder(t.Length);
            foreach (char ch in t) if (!char.IsDigit(ch)) b.Append(ch);
            return b.ToString();
        }

        // ---- validity --------------------------------------------------------------------------

        private static string Invalid(CoverGuard g, CaptorState cs, SurrenderState s)
        {
            if (!Perception.Alive(g.Bot) || g.Owner == null || g.Owner.IsDead) return "he is down";
            if (g.Owner.BotState != EBotState.Active) return "inactive";
            if (SurrenderRegistry.HoldsBot(g.Bot)) return "he gave up himself";
            if (Captors.TryGet(g.Bot, out _)) return "took a prisoner of his own";
            if (g.Owner.BotsGroup != cs.Group) return "left the squad";
            if (Teammates.IsPlayerTeammate(g.Owner)) return "under the player's orders";
            if (!Restraint.TryGet(s.Bot, g.Bot, out RestraintEntry e) || !e.Holds) return "no longer holds fire at him";
            if ((g.Bot.Position - s.Bot.Position).sqrMagnitude > MaxRange * MaxRange * 1.5f) return "too far from him";
            if (Arsenal.OutOfAmmo(g.Owner)) return "out of ammo";
            return null;
        }

        // ---- position ---------------------------------------------------------------------------

        private static void UpdatePosition(CoverGuard g, CaptorState cs, SurrenderState s, float now)
        {
            Player me = g.Bot;
            if (g.Stage == GuardStage.Moving)
            {
                string stop = null;
                Vector3 to = g.Spot - me.Position; to.y = 0f;
                if (to.magnitude <= 0.7f) stop = "in position";
                else if ((s.Bot.Position - g.SpotFor).sqrMagnitude > 3f * 3f) stop = "the prisoner moved";
                else if (now - g.MoveSince > 15f) stop = "took too long";
                else if ((me.Position - g.ProgressFrom).sqrMagnitude >= 0.25f) { g.ProgressFrom = me.Position; g.ProgressSince = now; }
                else if (now - g.ProgressSince > 2.5f) stop = "stuck";
                if (stop == null) return;
                g.Stage = GuardStage.Holding;
                g.PathIssued = false;
                g.BlockedSince = -1f;
                g.NextMoveAt = now + (stop == "in position" ? 3f : 5f);
                g.HasSpot = true;
                if (stop == "in position") g.InPositionAt = now;
                if (LogOn) Log($"{g.Name}: {(stop == "in position" ? "in position" : $"stops ({stop}), covers from here")}: {Describe(me.Position, cs, s)}");
                return;
            }
            string bad = Problem(me.Position, Perception.Eye(me), me, cs, s);
            if (!g.HasSpot && bad == null)
            {
                g.HasSpot = true;
                g.InPositionAt = now;
                if (LogOn) Log($"{g.Name}: covers {s.Name} from where he stands: {Describe(me.Position, cs, s)}");
                return;
            }
            if (bad == null) { g.BlockedSince = -1f; return; }
            if (g.BlockedSince < 0f || g.BlockedWhy != bad) { g.BlockedSince = now; g.BlockedWhy = bad; }
            if (g.HasSpot && now - g.BlockedSince < 1.2f) return;
            if (now < g.NextMoveAt) return;
            if (g.Moves >= MaxMoves)
            {
                if (!g.MovesSpentLogged && LogOn) Log($"{g.Name}: {bad}, but has moved {g.Moves} times; covers from where he is");
                g.MovesSpentLogged = true;
                g.HasSpot = true;
                return;
            }
            if (!Perception.TakeHeavy()) return;
            if (!PickSpot(g, cs, s, out Vector3 spot, out string why))
            {
                g.NextMoveAt = now + 4f;
                g.HasSpot = true;   // the best he has
                if (g.NoSpotLogged != why && LogOn) Log($"{g.Name}: {bad}; no better spot ({why}), covers from where he is");
                g.NoSpotLogged = why;
                return;
            }
            g.Moves++;
            g.Stage = GuardStage.Moving;
            g.Spot = spot;
            g.SpotFor = s.Bot.Position;
            g.PathIssued = false;
            g.MoveSince = now;
            g.ProgressFrom = me.Position;
            g.ProgressSince = now;
            g.ScanUntil = 0f;
            if (LogOn) Log($"{g.Name}: {bad}; moves to cover {s.Name} from {Vector3.Distance(me.Position, spot):0.0} m away ({Describe(spot, cs, s)}, move {g.Moves}/{MaxMoves})");
        }

        // Null when a guard standing at `feet` (eye at `eye`) covers well; else why not.
        private static string Problem(Vector3 feet, Vector3 eye, Player self, CaptorState cs, SurrenderState s)
        {
            Vector3 pr = s.Bot.Position;
            float d = Perception.Flat(feet, pr);
            float want = Settings.GuardDistance.Value;
            if (d < 2.2f) return "too close to him";
            if (d > Mathf.Max(15f, want * 2.5f)) return $"too far from him ({d:0} m)";
            if (Mathf.Abs(feet.y - pr.y) > 2.5f) return "on another level";
            Vector3 chest = Perception.Chest(s.Bot);
            if (!Perception.LineClear(eye, chest)) return "his view of him is blocked";
            Player cap = cs.Bot;
            if (Perception.Flat(feet, cap.Position) < 1.8f) return "on top of his captor";
            if (Near(eye, chest, 8f, Perception.Chest(cap), 0.9f)) return $"{cs.Name} is in his line of fire";
            if (Near(Perception.Eye(cap), chest, 10f, feet + Vector3.up * 1.2f, 1f)) return $"in {cs.Name}'s line of fire";
            BotsGroup grp = cs.Group;
            if (grp != null)
            {
                int n = 0;
                for (int i = 0; i < grp.MembersCount && n < 4; i++)
                {
                    BotOwner m = grp.Member(i);
                    Player mp = m?.GetPlayer;
                    if (mp == null || mp == self || mp == cap || m.IsDead || !Perception.Alive(mp)) continue;
                    if ((mp.Position - pr).sqrMagnitude > 20f * 20f) continue;
                    n++;
                    if (Near(eye, chest, 8f, Perception.Chest(mp), 0.7f)) return $"{Restraint.Name(mp)} is in his line of fire";
                }
            }
            // Two men on one bearing watch one side: bunched up is not cover.
            Vector3 gv = feet - pr, cv = cap.Position - pr; gv.y = 0f; cv.y = 0f;
            if (Perception.Flat(feet, cap.Position) < 4f && gv.sqrMagnitude > 0.01f && cv.sqrMagnitude > 0.01f && Vector3.Angle(gv, cv) < 20f)
                return $"bunched up with {cs.Name}";
            return null;
        }

        // Does the line from a through b (and `extend` m beyond b) pass within r of p (not at b)?
        private static bool Near(Vector3 a, Vector3 b, float extend, Vector3 p, float r)
        {
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < 0.01f) return false;
            Vector3 dir = ab / len;
            float t = Vector3.Dot(p - a, dir);
            if (t < 0.3f || t > len + extend) return false;
            return (a + dir * t - p).sqrMagnitude < r * r;
        }

        private static string Describe(Vector3 at, CaptorState cs, SurrenderState s)
        {
            Vector3 pr = s.Bot.Position;
            Vector3 c = cs.Bot.Position - pr; c.y = 0f;
            Vector3 g = at - pr; g.y = 0f;
            float ang = c.sqrMagnitude > 0.01f && g.sqrMagnitude > 0.01f ? Vector3.Angle(c, g) : 0f;
            return $"{Perception.Flat(at, pr):0.0} m from {s.Name}, {Perception.Flat(at, cs.Bot.Position):0.0} m from {cs.Name}, {ang:0} deg off his line";
        }

        private static readonly float[] Angles = { 70f, 50f, 100f, 125f };
        private static readonly float[] Radii = { 1f, 0.7f, 1.35f };

        private static bool PickSpot(CoverGuard g, CaptorState cs, SurrenderState s, out Vector3 best, out string why)
        {
            best = g.Bot.Position;
            why = "no candidate";
            Vector3 pr = s.Bot.Position;
            Vector3 toCap = cs.Bot.Position - pr; toCap.y = 0f;
            if (toCap.sqrMagnitude < 0.01f) toCap = s.Bot.Transform.forward;
            toCap.Normalize();
            Vector3 toMe = g.Bot.Position - pr; toMe.y = 0f;
            float side = Vector3.Cross(toCap, toMe).y >= 0f ? 1f : -1f;   // his own side of the line first
            float want = Settings.GuardDistance.Value;
            bool alert = Time.time - g.AlertSeenAt < 10f;
            Vector3 alertEye = g.AlertAt + Vector3.up * 1.5f;
            float bestScore = float.MinValue, secondScore = float.MinValue;
            Vector3 second = best;
            int tried = 0, offMesh = 0, rejected = 0;
            string lastWhy = null;
            for (int ai = 0; ai < Angles.Length && tried < 12; ai++)
                for (int sgn = 0; sgn < 2 && tried < 12; sgn++)
                    for (int ri = 0; ri < Radii.Length && tried < 12; ri++)
                    {
                        float ang = Angles[ai] * (sgn == 0 ? side : -side);
                        float r = want * Radii[ri];
                        Vector3 c = pr + Quaternion.AngleAxis(ang, Vector3.up) * toCap * r;
                        if (!NavMesh.SamplePosition(c, out NavMeshHit hit, 1f, NavMesh.AllAreas)) { offMesh++; continue; }
                        tried++;
                        Vector3 spot = hit.position;
                        if (Mathf.Abs(spot.y - pr.y) > 1f) { rejected++; lastWhy = "another level"; continue; }
                        string bad = Problem(spot, spot + Vector3.up * 1.55f, g.Bot, cs, s);
                        if (bad != null) { rejected++; lastWhy = bad; continue; }
                        float score = 20f - 0.15f * Mathf.Abs(Angles[ai] - 80f) - 1.5f * Mathf.Abs(Perception.Flat(spot, pr) - want)
                                      - 0.6f * Perception.Flat(spot, g.Bot.Position) + 5f * CoverAround(spot, pr)
                                      - (sgn == 0 ? 0f : 4f);
                        if (alert && !Perception.LineClear(alertEye, spot + Vector3.up * 1.2f)) score += 8f;
                        if (score > bestScore) { second = best; secondScore = bestScore; best = spot; bestScore = score; }
                        else if (score > secondScore) { second = spot; secondScore = score; }
                    }
            if (bestScore == float.MinValue)
            {
                why = tried == 0 ? $"no NavMesh round him ({offMesh} off it)" : $"{rejected} spots checked, none works (last: {lastWhy})";
                return false;
            }
            if (Walkable(g.Bot.Position, best)) return true;
            if (secondScore > float.MinValue && Walkable(g.Bot.Position, second)) { best = second; return true; }
            why = "no reasonable path to the good spots";
            return false;
        }

        private static bool Walkable(Vector3 from, Vector3 to)
        {
            if (!Perception.PathTo(from, to, out Vector3[] corners, out _)) return false;
            float len = Captors.PathLength(corners);
            return len <= 30f && len <= 2.2f * Vector3.Distance(from, to) + 4f;
        }

        // Solid geometry within 1.6 m beside or behind the spot at waist height (0-3): some
        // protection from whatever isn't the prisoner.
        private static int CoverAround(Vector3 spot, Vector3 prisoner)
        {
            Vector3 f = prisoner - spot; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) return 0;
            f.Normalize();
            Vector3 o = spot + Vector3.up * 0.9f;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            int n = 0;
            if (Physics.Raycast(o, right, 1.6f, LayersMaskController.HighPolyWithTerrainMask)) n++;
            if (Physics.Raycast(o, -right, 1.6f, LayersMaskController.HighPolyWithTerrainMask)) n++;
            if (Physics.Raycast(o, -f, 1.6f, LayersMaskController.HighPolyWithTerrainMask)) n++;
            return n;
        }

        // ---- attention --------------------------------------------------------------------------

        private static void UpdateScan(CoverGuard g, CaptorState cs, SurrenderState s, float now)
        {
            bool vulnerable = CaptorLoot.Fetching(cs.Bot) || cs.Approaching;
            bool restless = s.Weight < 0.95f || s.Bot.Velocity.sqrMagnitude > 0.25f || g.NoticedAt >= 0f;
            g.Tight = restless;
            if (now < g.ScanUntil)
            {
                if (vulnerable || restless || g.Stage == GuardStage.Moving)
                {
                    g.ScanUntil = 0f;
                    g.NextScanAt = now + Dice.Range(3f, 6f);
                    if (Settings.VerboseLogs.Value) Log($"{g.Name}: cuts his look around short ({(vulnerable ? $"{cs.Name} is busy" : "the prisoner moves")})");
                }
                return;
            }
            if (!Settings.GuardScans.Value || g.Stage == GuardStage.Moving || now < g.NextScanAt) return;
            if (vulnerable || restless) { g.NextScanAt = now + 1f; return; }
            bool alert = now - g.AlertSeenAt < 8f;
            if (alert) g.ScanPoint = g.AlertAt + Vector3.up * 1.4f;
            else
            {
                // Behind the captor: the side he can't watch.
                Vector3 back = cs.Bot.Position - s.Bot.Position; back.y = 0f;
                if (back.sqrMagnitude < 0.01f) back = -g.Bot.Transform.forward;
                back = Quaternion.Euler(0f, Dice.Range(-50f, 50f), 0f) * back.normalized;
                g.ScanPoint = cs.Bot.Position + back * 15f + Vector3.up * 1.5f;
            }
            g.ScanUntil = now + Dice.Range(0.9f, 1.6f);
            g.NextScanAt = g.ScanUntil + (alert ? Dice.Range(2.5f, 4f) : Dice.Range(5f, 10f));
            if (Settings.VerboseLogs.Value) Log($"{g.Name}: looks {(alert ? "towards where he last saw an enemy" : $"out behind {cs.Name}")} for {g.ScanUntil - now:0.0} s");
        }

        // ---- coverage (measured) ---------------------------------------------------------------

        private static void Evaluate(CoverGuard g, CaptorState cs, SurrenderState s, float now)
        {
            string why = NotCovering(g, cs, s, now);
            bool eff = why == null;
            if (g.Effective && g.EvalAt >= 0f) g.CoveredTime += now - g.EvalAt;
            g.EvalAt = now;
            string key = Key(why);
            string oldKey = Key(g.Effective ? null : g.CoverWhy);
            g.CoverWhy = why ?? "covering";   // the current reason, numbers and all (HoldForCover's lines)
            if (key == oldKey) return;
            g.Effective = eff;
            if (!eff)
            {
                g.Losses.TryGetValue(key, out int n);
                g.Losses[key] = n + 1;
            }
            if (!LogOn) return;
            // The routine glance (and coming back from it) is only worth a line in verbose logs;
            // other changes always, at most one every 1.5 s (fighting always).
            if ((key == "glancing out" || oldKey == "glancing out") && !Settings.VerboseLogs.Value) return;
            if (now - g.LoggedAt < 1.5f && key != "fighting") return;
            g.LoggedAt = now;
            Log(eff ? $"{g.Name}: covering {s.Name} ({Perception.Flat(g.Bot.Position, s.Bot.Position):0.0} m, gun on him)"
                    : $"{g.Name}: not covering {s.Name}: {why}");
        }

        // A reason's category (no numbers, no names): what a change of coverage is compared by.
        private static string Key(string why)
        {
            if (why == null) return "covering";
            // Ordinal (0.20.1): twice per guard evaluation, 4x a second; the culture-aware default
            // compare was the costliest part of it.
            if (why.StartsWith("looking", System.StringComparison.Ordinal)) return "looking away";
            if (why.StartsWith("fighting", System.StringComparison.Ordinal)) return "fighting";
            if (why.EndsWith("in his line of fire", System.StringComparison.Ordinal)) return "captor in his line";
            return why;
        }

        private static string NotCovering(CoverGuard g, CaptorState cs, SurrenderState s, float now)
        {
            if (g.Distracted) return $"fighting {g.DistractedBy}";
            if (now < g.ScanUntil) return "glancing out";
            Player me = g.Bot;
            if (!(me.HandsController is Player.FirearmController fc)) return "no gun up";
            if (fc.IsInReloadOperation()) return "reloading";
            if (g.Owner.WeaponManager?.Selector?.IsChanging == true) return "switching weapons";
            if (me.IsSprintEnabled) return "running";
            Vector3 eye = Perception.Eye(me), chest = Perception.Chest(s.Bot);
            float off = Vector3.Angle(me.LookDirection, chest - eye);
            if (off > Settings.WatchAngle.Value) return $"looking {off:0} deg away";
            if (!Perception.LineClear(eye, chest)) return "view blocked";
            if (Near(eye, chest, 8f, Perception.Chest(cs.Bot), 0.9f)) return $"{cs.Name} is in his line of fire";
            return null;
        }

        // What the prisoner can see of a squadmate (Reactions): null when that man visibly holds
        // him at gunpoint. Only what shows: the gun, a reload, running, a weapon change, firing
        // elsewhere, where he looks, a clear line. Never the role or the attention state behind it.
        internal static string Visibly(Player obs, Player prisoner, float watchAngle)
        {
            if (!(obs.HandsController is Player.FirearmController fc)) return "has no gun up";
            if (fc.IsInReloadOperation()) return "is reloading";
            if (obs.IsSprintEnabled) return "is running";
            if (obs.AIData?.BotOwner?.WeaponManager?.Selector?.IsChanging == true) return "is switching weapons";
            Vector3 eye = Perception.Eye(obs), chest = Perception.Chest(prisoner);
            float off = Vector3.Angle(obs.LookDirection, chest - eye);
            if (Time.time - SurrenderRegistry.HistoryOf(obs).ShotAt < 1.5f && off > watchAngle * 0.5f) return "is shooting at someone else";
            if (off > watchAngle) return $"looks {off:0} deg away";
            if (!Perception.LineClear(eye, chest)) return "can't see him from there";
            return null;
        }

        // ---- resistance --------------------------------------------------------------------------

        // What anyone could see a prisoner do that threatens (armed) or that isn't compliance.
        // Null: nothing (standing, kneeling, turning, dropping, following an order).
        internal static string Visible(SurrenderState s, out bool armed)
        {
            armed = false;
            Player b = s?.Bot;
            if (b == null || s.Dead) return null;
            bool dropping = s.DropPending || s.HandsPending || s.DropQueue.Count > 0;
            if (s.DrawPending) { armed = true; return "drawing a weapon"; }
            if (!Arsenal.HandsEmpty(b) && !dropping) { armed = true; return "with a weapon in his hands"; }
            bool ordered = s.Order == Order.Follow || s.Order == Order.StepBack || s.KneelSpotMoving || s.Turning || (Kneel.Active(s) && !s.KneelUrgent);
            Vector3 v = b.Velocity; v.y = 0f;
            if (!ordered && v.sqrMagnitude > 0.6f * 0.6f)
            {
                if (s.DroppedWeapon != null && !s.GunGone)
                {
                    Vector3 to = s.DropPosition - b.Position; to.y = 0f;
                    if (to.magnitude < 10f && to.magnitude > 0.3f && Vector3.Angle(v, to) < 35f) { armed = true; return "going for the gun on the ground"; }
                }
                return "moving off";
            }
            if (s.Kneel == KneelPhase.GettingUp && s.KneelUrgent && s.KneelT < 0.8f) return "starting to get up";
            if (s.Weight < 0.75f && !s.HandsOnHead && !ordered) return "lowering his hands";
            return null;
        }

        private static void Watch(CoverGuard g, float now)
        {
            SurrenderState s = g.Prisoner;
            if (g.Reacted || s?.Bot == null || s.Dead || !Perception.Alive(g.Bot)) return;
            string shown = Visible(s, out bool armed);
            if (shown == null && g.NoticedAt < 0f) { g.MissLogged = false; return; }
            if (g.NoticedAt < 0f)
            {
                Vector3 eye = Perception.Eye(g.Bot);
                float off = Vector3.Angle(g.Bot.LookDirection, Perception.Chest(s.Bot) - eye);
                bool sees = off <= 100f && Perception.Recognises(g.Owner, s.Bot);
                if (!sees)
                {
                    if (!g.MissLogged && LogOn)
                        Log($"{g.Name}: doesn't see {s.Name} {shown} ({(off > 100f ? $"looking {off:0} deg away" : "no line to him")})");
                    g.MissLogged = true;
                    g.Saw = off > 100f ? $"looking {off:0} deg away" : "no line to him";
                    return;
                }
                float rt = Settings.GuardReaction.Value * Dice.Range(0.75f, 1.35f);
                if (off > Settings.WatchAngle.Value) rt += Dice.Range(0.25f, 0.55f);
                if (g.Stage == GuardStage.Moving) rt += 0.15f;
                if (g.Distracted) rt += Dice.Range(0.3f, 0.7f);
                g.NoticedAt = now;
                g.ReactAt = now + rt;
                g.Saw = shown;
                g.SawArmed = armed;
                g.ScanUntil = 0f;
                if (LogOn) Log($"{g.Name}: sees {s.Name} {shown} ({off:0} deg from where he looks{(g.Distracted ? ", while fighting" : "")}); reacts in {rt:0.00} s");
                return;
            }
            if (armed && !g.SawArmed) { g.SawArmed = true; g.Saw = shown; }   // it got worse while he took it in
            if (now < g.ReactAt) return;
            if (!g.SawArmed)
            {
                if (shown == null)
                {
                    // It stopped (he complied again): the guard relaxes, his restraint stands.
                    if (LogOn) Log($"{g.Name}: {s.Name} stopped ({g.Saw}); keeps him covered, holds fire");
                    g.NoticedAt = -1f; g.TightLogged = false;
                    return;
                }
                if (!g.TightLogged && LogOn) Log($"{g.Name}: {s.Name} is {shown}, unarmed: eyes on him, holds fire (his captor decides)");
                g.TightLogged = true;
                return;
            }
            React(g, s, now);
        }

        private static void React(CoverGuard g, SurrenderState s, float now)
        {
            g.Reacted = true;
            BotOwner o = g.Owner;
            Restraint.Set(s, o, RestraintKind.Reject, $"saw {s.Name} {g.Saw}", squad: true);
            Restraint.Remember(g.Bot, s.Bot);
            EnemyInfo info = null;
            try
            {
                info = Perception.InfoAbout(o, s.Bot);
                if (info != null && o.Memory != null) { o.Memory.IsPeace = false; o.Memory.GoalEnemy = info; }
            }
            catch (System.Exception e) { HandsAreHighPlugin.Report("guard target", e); }
            CaptorVoice.Interrupt(g.Bot);
            CaptorVoice.Say(g.Bot, CaptorLine.Resist);
            Player cap = g.For?.Bot;
            string line = "nobody in his line";
            if (Perception.Alive(cap) && Near(Perception.Eye(g.Bot), Perception.Chest(s.Bot), 8f, Perception.Chest(cap), 0.9f))
                line = $"{Restraint.Name(cap)} IN HIS LINE: the game's friendly-fire check holds his shot until it clears";
            Log($"GUARD REACTS: {g.Name} engages {s.Name} ({g.Saw}), {now - g.NoticedAt:0.00} s after seeing it; " +
                $"target {(o.Memory?.GoalEnemy != null && Perception.Same(o.Memory.GoalEnemy.Person, s.Bot) ? "set" : info == null ? "not set (no EnemyInfo yet: his combat AI must find him)" : "not taken")}; {line}");
            // The combat AI has him now: the role ends (the captor reacts to what he sees himself).
            CaptorState cs = g.For;
            if (cs != null && cs.Guard == g) { cs.Guard = null; ByGuard.Remove(g.Bot); cs.NextGuardPick = now + 1f; }
            Unblock(g, "engages the prisoner");
        }

        // ---- endings --------------------------------------------------------------------------

        // The role ends; another member may take it at the next check.
        private static void Drop(CaptorState cs, string why)
        {
            CoverGuard g = cs.Guard;
            if (g == null) return;
            Summary(g, why);
            cs.Guard = null;
            ByGuard.Remove(g.Bot);
            cs.NextGuardPick = Time.time;
            cs.CoverWaitSince = -1f;
            Unblock(g, why);
        }

        // Custody ended (Captors.End / CaptorDown). resisted: the man is resisting: the guard keeps
        // watching for a few seconds so his own reaction still happens.
        internal static void Release(CaptorState cs, string why, bool resisted)
        {
            CoverGuard g = cs?.Guard;
            if (g == null) return;
            cs.Guard = null;
            ByGuard.Remove(g.Bot);
            Summary(g, why);
            if (resisted && !g.Reacted && Perception.Alive(g.Bot) && Responders.Count < 8)
            {
                g.Deadline = Time.time + 3f;
                Responders.Add(g);
                return;
            }
            Unblock(g, why);
        }

        private static void Summary(CoverGuard g, string why)
        {
            if (!LogOn) return;
            float held = Mathf.Max(0.01f, Time.time - g.Since);
            Sb.Clear();
            foreach (KeyValuePair<string, int> kv in g.Losses)
            {
                if (Sb.Length > 0) Sb.Append(", ");
                Sb.Append(kv.Key).Append(' ').Append(kv.Value).Append('x');
            }
            Log($"{g.Name}: stops covering {g.For?.Name} ({why}) after {held:0} s: covering {100f * g.CoveredTime / held:0}% of it, " +
                $"{(g.InPositionAt >= 0f ? $"in position after {g.InPositionAt - g.Since:0.0} s" : "never in position")}, {g.Moves} move(s){(Sb.Length > 0 ? $"; gaps: {Sb}" : "")}");
        }

        private static void Unblock(CoverGuard g, string why)
        {
            if (g.Bot == null || Captors.TryGet(g.Bot, out _) || SurrenderRegistry.HoldsBot(g.Bot)) return;
            OrbitAdapter.KeepAwake(g.Owner, false, why);
        }

        internal static void Clear()
        {
            ByGuard.Clear();
            Responders.Clear();
            Scratch.Clear();
            _nextFast = 0f;
        }

        // ---- the layer -------------------------------------------------------------------------

        // A guard is driven by the captor layer while he covers (not while he fights: the combat
        // AI has him then; not once he has reacted to resistance).
        // Not gated on the prisoner's phase either: until something SHOWS (Watch) the guard does
        // what he did. A responder (custody ended with the man resisting) keeps his eyes on him
        // until he has reacted or his few seconds are up.
        internal static bool WantsControl(Player p, out SurrenderState prisoner)
        {
            prisoner = null;
            if (!TryDrive(p, out CoverGuard g) || g.Distracted || g.Reacted) return false;
            SurrenderState s = g.Prisoner;
            if (s?.Bot == null || s.Dead || s.Suspended) return false;
            prisoner = s;
            return true;
        }

        // The guard object the layer drives: a current guard, or a responder.
        internal static bool TryDrive(Player p, out CoverGuard g)
        {
            if (TryGet(p, out g)) return true;
            for (int i = 0; i < Responders.Count; i++)
                if (Responders[i].Bot == p) { g = Responders[i]; return Time.time < g.Deadline; }
            g = null;
            return false;
        }

        private static bool LogOn => Settings.LogSquadCover.Value;
        private static void Log(string m) => HandsAreHighPlugin.Log.LogInfo(m);
    }
}
