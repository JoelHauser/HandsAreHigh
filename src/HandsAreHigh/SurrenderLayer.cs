using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // While a bot is in the registry this layer outranks SAIN's, ORBIT's and the vanilla
    // ones, so nothing else moves, aims or shoots with him until it hands him back.
    internal sealed class SurrenderLayer : CustomLayer
    {
        public SurrenderLayer(BotOwner botOwner, int priority) : base(botOwner, priority) { }

        public override string GetName() => "HandsAreHigh_Surrender";

        public override bool IsActive() => SurrenderRegistry.HoldsBot(BotOwner?.GetPlayer);

        public override Action GetNextAction() => new Action(typeof(SurrenderLogic), "Surrendered");

        public override bool IsCurrentActionEnding() => !IsActive();
    }

    internal sealed class SurrenderLogic : CustomLogic
    {
        private const float CheckInterval = 0.25f;
        private const float MoveOrderInterval = 1f;
        private const float ReachDist = 0.5f;
        private const float ScrambleGrace = 0.75f;
        private const int MaxDropAttempts = 3;

        public SurrenderLogic(BotOwner botOwner) : base(botOwner) { }

        private Player Me => BotOwner.GetPlayer;

        public override void Start()
        {
            BotOwner.ShootData?.EndShoot();
            BotOwner.Sprint(false);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (!SurrenderRegistry.TryGet(Me, out SurrenderState s) || s.Suspended) return;
            HandsAreHighPlugin.Try($"phase {s.Phase}", () => Run(s));
        }

        private void Run(SurrenderState s)
        {
            bool check = Time.time >= s.NextCheck;
            if (check) s.NextCheck = Time.time + CheckInterval;

            switch (s.Phase)
            {
                case Phase.HandsUp: HandsUp(s, check); break;
                case Phase.Retreating: Retreating(s, check); break;
                case Phase.Regrouping: Regrouping(s, check); break;
                case Phase.Recovering: Recovering(s, check); break;
                case Phase.PickingUp: PickingUp(s); break;
                case Phase.Hiding: Hiding(s, check); break;
                case Phase.Rearmed: Hold(1f); break;
            }
        }

        // ---- hands up ------------------------------------------------------------------

        private void HandsUp(SurrenderState s, bool check)
        {
            Player captor = s.Captor;
            bool captorAlive = Perception.Alive(captor);

            switch (s.Order)
            {
                case Order.Kneel: Hold(0f); break;   // no kneel animation exists: lowest crouch, hands up
                case Order.Follow when captorAlive: Follow(s, captor); break;
                default: Hold(Settings.Stance.Value); break;
            }
            if (Settings.FaceCaptor.Value && captorAlive)
                BotOwner.Steering.LookToPoint(Perception.Eye(captor));

            ProcessDropQueue(s);
            if (!check) return;

            if (s.DropPending && !CheckDrop(s)) return;

            // Shot while his hands are up, by anyone: he runs. (A hit, not health: a bleeding
            // man loses health every second without anyone firing.)
            bool hit = s.WasHit;
            s.WasHit = false;
            if (hit && Settings.BoltWhenShot.Value)
            {
                Voice.Say(s, EPhraseTrigger.OnBeingHurt);
                SurrenderRegistry.Witness(Me.Position, Me, "a surrendered man shot");
                s.BeginRetreat("shot with his hands up", emergency: true);
                return;
            }

            if (!captorAlive)
            {
                s.BeginRetreat("captor is dead", emergency: false);
                return;
            }

            float dist = Vector3.Distance(captor.Position, Me.Position);

            if (s.ForcedLeave)
            {
                s.ForcedLeave = false;
                s.BeginRetreat("debug: captor left", emergency: false);
                return;
            }

            // What he can see of the captor. Out of sight, he only knows where he last saw him.
            bool seen = Perception.LineClear(Perception.Eye(Me), Perception.Eye(captor));
            if (seen)
            {
                s.CaptorLastSeen = captor.Position;
                s.CaptorHiddenSince = -1f;
                if (dist > Settings.LeaveDistance.Value)
                {
                    s.BeginRetreat($"captor {dist:0} m away", emergency: false);
                    return;
                }
            }
            else
            {
                if (s.CaptorHiddenSince < 0f) s.CaptorHiddenSince = Time.time;
                if (Time.time - s.CaptorHiddenSince > Settings.LeaveSeconds.Value)
                {
                    s.BeginRetreat($"captor out of sight {Settings.LeaveSeconds.Value:0} s", emergency: false);
                    return;
                }
            }

            // Captor not covering him (no line of sight counts too, and so does the debug
            // opening for this one bot): an opening.
            bool debugOpening = Time.time < s.DebugOpeningUntil;
            if (!debugOpening && seen && Perception.Covering(captor, Me, Settings.WatchAngle.Value))
            {
                s.UnwatchedSince = -1f;
                return;
            }
            if (s.UnwatchedSince < 0f) s.UnwatchedSince = Time.time;
            if (Time.time - s.UnwatchedSince < Settings.OpeningDelay.Value) return;
            if (Time.time < s.NextBreakRoll) return;
            s.NextBreakRoll = Time.time + Settings.BreakInterval.Value;

            // His own gun close, reachable and away from you makes an opening much more tempting.
            float scramble = ScrambleQuality(s, captor, out string scrambleWhy);
            float nerveScale = Mathf.Lerp(0.3f, 1f, s.Nerve);
            float chance = Mathf.Clamp01(Settings.BreakChance.Value * nerveScale * (1f + Settings.OpportunityBoost.Value * scramble));
            float roll = Random.value;
            if (Settings.VerboseLogs.Value || debugOpening)
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: opening{(debugOpening ? " (debug)" : "")}, nerve {s.Nerve:0.00}, " +
                    $"gun nearby {scramble:0.00} ({scrambleWhy}), chance {chance:0.00}, roll {roll:0.00}{(roll < chance ? " -> takes it" : "")}");
            if (roll >= chance) return;

            TakeOpening(s, captor, dist, scramble, scrambleWhy);
        }

        // The game's callback normally clears DropPending within the holster time. If it
        // hasn't come, ask again; after a few tries give up surrendering rather than pose
        // holding a gun.
        private bool CheckDrop(SurrenderState s)
        {
            if (Arsenal.HandsFree(Me))
            {
                s.DropPending = false;
                return true;
            }
            if (Time.time - s.DropSince < Settings.DropRetrySeconds.Value) return false;

            if (s.DropAttempts >= MaxDropAttempts)
            {
                HandsAreHighPlugin.Log.LogWarning($"{s.Name}: couldn't empty his hands after {s.DropAttempts} tries, fights instead");
                s.DropPending = false;
                s.LowerArms(Settings.EmergencyLowerSeconds.Value);
                s.SetPhase(Phase.Rearmed, "drop failed");
                return false;
            }
            s.DropAttempts++;
            s.DropSince = Time.time;
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: weapon still in hands after {Settings.DropRetrySeconds.Value:0.0} s, drop try {s.DropAttempts}");
            SurrenderRegistry.RequestDrop(s);
            return false;
        }

        // ---- taking an opening -----------------------------------------------------------

        // 0..1: how good a grab for his own dropped gun would be right now. Closer, reachable,
        // and the further you are from it (and the less you can see it), the better.
        private float ScrambleQuality(SurrenderState s, Player captor, out string why)
        {
            Item gun = s.DroppedWeapon;
            if (gun == null) { why = "he didn't drop a gun"; return 0f; }
            if (Time.time < s.ScrambleCooldownUntil) { why = "stopped once, not again yet"; return 0f; }
            if (s.Skip.Contains(gun)) { why = "failed to get it before"; return 0f; }
            float d = Vector3.Distance(Me.Position, s.DropPosition);
            float range = Settings.ScrambleRange.Value;
            if (d > range) { why = $"{d:0} m away, over {range:0} m"; return 0f; }
            if (ClaimedByOther(s, gun)) { why = "another held bot is going for it"; return 0f; }
            if (!Arsenal.StillThere(gun, s.DropPosition)) { why = "it's gone"; return 0f; }
            if (!Perception.Reachable(Me.Position, s.DropPosition)) { why = "no path to it"; return 0f; }

            float fromCaptor = Vector3.Distance(captor.Position, s.DropPosition);
            float exposure = 1f - Mathf.Clamp01(fromCaptor / Settings.ExposureDistance.Value);
            if (!Perception.LineClear(Perception.Eye(captor), s.DropPosition + Vector3.up * 0.3f)) exposure *= 0.5f;
            float proximity = 1f - d / range;
            why = $"{d:0.0} m from him, you {fromCaptor:0.0} m from it";
            return Mathf.Clamp01(Mathf.Sqrt(proximity) * (1f - 0.7f * exposure));
        }

        private static bool ClaimedByOther(SurrenderState s, Item gun)
        {
            foreach (SurrenderState other in SurrenderRegistry.All)
                if (other != s && other.TargetWeapon == gun) return true;
            return false;
        }

        private struct Choice
        {
            internal string Name;
            internal float Score;
        }

        // He takes his chance: every option he has is weighed, logged, and one is picked at
        // random in proportion to its weight. A gun he still carries beats a knife (slung gun
        // first, then sidearm); the knife is only for a man with no gun on him.
        private void TakeOpening(SurrenderState s, Player captor, float dist, float scramble, string scrambleWhy)
        {
            string personality = SainAdapter.Personality(BotOwner);
            SainAdapter.Leanings(personality, out float fight, out float flee);
            float nerve = s.Nerve;
            EquipmentSlot? gun = Arsenal.GunOnBody(Me);

            var options = new List<Choice>(4);
            var log = new StringBuilder();
            log.Append($"{s.Name} takes an opening at {dist:0.0} m (nerve {nerve:0.00}{(string.IsNullOrEmpty(personality) ? "" : ", " + personality)}): ");

            if (gun == null) log.Append("draw: no gun on him; ");
            else if (dist > Settings.SidearmRange.Value) log.Append($"draw: you're beyond {Settings.SidearmRange.Value:0} m; ");
            else Add(options, log, "draw " + Arsenal.SlotName(gun), Settings.DrawWeight.Value * (0.4f + nerve) * fight);

            if (gun != null) log.Append("knife: has a gun to draw; ");
            else if (!Arsenal.HasMelee(Me)) log.Append("knife: none; ");
            else if (dist > Settings.KnifeRange.Value) log.Append($"knife: you're beyond {Settings.KnifeRange.Value:0} m; ");
            else Add(options, log, "knife", Settings.KnifeWeight.Value * (0.2f + nerve) * fight);

            if (scramble <= 0f) log.Append($"scramble: {scrambleWhy}; ");
            else Add(options, log, "scramble", Settings.ScrambleWeight.Value * scramble * (0.5f + nerve) * fight * (gun != null ? 0.5f : 1f));

            if (dist < Settings.EscapeMinDistance.Value) log.Append($"run: you're within {Settings.EscapeMinDistance.Value:0} m; ");
            else Add(options, log, "run", Settings.EscapeWeight.Value * Mathf.Max(0.1f, 1.3f - nerve) * flee);

            if (options.Count == 0)
            {
                HandsAreHighPlugin.Log.LogInfo(log.Append("-> nothing he can do, keeps his hands up").ToString());
                return;
            }

            float total = 0f;
            foreach (Choice c in options) total += c.Score;
            float pick = Random.value * total;
            Choice chosen = options[options.Count - 1];
            float acc = 0f;
            foreach (Choice c in options)
            {
                acc += c.Score;
                if (pick <= acc) { chosen = c; break; }
            }
            HandsAreHighPlugin.Log.LogInfo(log.Append($"-> {chosen.Name} (roll {pick:0.00} of {total:0.00})").ToString());

            if (chosen.Name == "knife")
            {
                if (Arsenal.DrawMelee(BotOwner)) Rearm(s, $"goes for his knife at {dist:0.0} m");
            }
            else if (chosen.Name == "scramble")
            {
                StartScramble(s, captor, scrambleWhy);
            }
            else if (chosen.Name == "run")
            {
                s.BeginRetreat("bolts during an opening", emergency: true);
            }
            else if (Arsenal.DrawGun(BotOwner))
            {
                Rearm(s, $"draws his {Arsenal.SlotName(gun)} at {dist:0.0} m");
            }
            else
            {
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: tried to draw, the game refused the weapon change");
            }
        }

        private static void Add(List<Choice> options, StringBuilder log, string name, float score)
        {
            score = Mathf.Max(0f, score);
            log.Append($"{name} {score:0.00}; ");
            if (score > 0f) options.Add(new Choice { Name = name, Score = score });
        }

        // Arms down at once and a sprint for his own gun; the pick-up is the real transaction.
        private void StartScramble(SurrenderState s, Player captor, string why)
        {
            s.LowerArms(Settings.EmergencyLowerSeconds.Value);
            s.Order = Order.None;
            s.DropQueue.Clear();
            s.TargetWeapon = s.DroppedWeapon;
            s.TargetPoint = s.DropPosition;
            s.GraceUntil = Time.time + ScrambleGrace;
            s.CaptorLastSeen = captor.Position;
            Voice.Say(s, EPhraseTrigger.OnFight);
            s.SetPhase(Phase.Recovering, $"scrambles for his {s.DroppedWeapon.ShortName.Localized()} ({why})");
            s.Scrambling = true;
        }

        // The scramble didn't work: he won't try it again straight away. With a gun still on
        // him and you close, he draws; otherwise he runs.
        private void FailScramble(SurrenderState s, string why)
        {
            if (s.TargetWeapon != null) s.Skip.Add(s.TargetWeapon);
            s.TargetWeapon = null;
            s.Scrambling = false;
            s.ScrambleCooldownUntil = Time.time + Settings.ScrambleCooldown.Value;
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: scramble failed: {why}");
            if (Arsenal.GunOnBody(Me) != null && Arsenal.DrawGun(BotOwner))
            {
                Rearm(s, "scramble failed, draws what he has");
                return;
            }
            s.BeginRetreat($"scramble failed ({why})", emergency: true);
        }

        private void Rearm(SurrenderState s, string reason)
        {
            s.LowerArms(Settings.EmergencyLowerSeconds.Value);
            s.Order = Order.None;
            s.DropQueue.Clear();
            s.TargetWeapon = null;
            Voice.Say(s, EPhraseTrigger.OnFight);
            s.SetPhase(Phase.Rearmed, reason);
        }

        private void Follow(SurrenderState s, Player captor)
        {
            Vector3 to = captor.Position - Me.Position;
            to.y = 0f;
            float d = to.magnitude;
            float want = Settings.FollowDistance.Value;
            if (d <= want + Settings.FollowTolerance.Value)
            {
                Hold(Settings.Stance.Value);
                return;
            }
            Vector3 point = captor.Position - to.normalized * want;
            MoveTo(s, point, sprint: false);
        }

        // One item at a time, in order; a report once the last one should have landed.
        private void ProcessDropQueue(SurrenderState s)
        {
            if (s.DropQueue.Count > 0 && Time.time >= s.NextDropAt)
            {
                var next = s.DropQueue.Dequeue();
                s.NextDropAt = Time.time + Settings.DropItemDelay.Value;
                if (Arsenal.StillWorn(Me, next.Key, next.Value))
                    HandsAreHighPlugin.Try($"drop {next.Key}", () => Arsenal.Drop(Me, next.Value));
                if (s.DropQueue.Count == 0) s.ReportAt = Time.time + Settings.DropItemDelay.Value + 0.5f;
            }

            if (s.ReportAt > 0f && Time.time >= s.ReportAt)
            {
                s.ReportAt = -1f;
                int kept = 0;
                foreach (var kv in s.Ordered)
                    if (Arsenal.StillWorn(Me, kv.Key, kv.Value)) kept++;
                int done = s.Ordered.Count - kept;
                string msg = kept == 0
                    ? $"{s.Name} did it: {done} item(s) dropped"
                    : $"{s.Name} dropped {done} of {s.Ordered.Count}; {kept} wouldn't come off";
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: order '{s.OrderName}': {msg}");
                OrderMenu.Notify(msg);
                if (kept > 0) Voice.Say(s, EPhraseTrigger.Negative);
            }
        }

        // ---- after the captor leaves ---------------------------------------------------

        private void Retreating(SurrenderState s, bool check)
        {
            if (s.NextMoveOrder == 0f && !PickRetreat(s)) return;

            if (!MoveTo(s, s.TargetPoint, sprint: true))
            {
                Retry(s, "no path");
                return;
            }
            if (!check) return;
            if (SpottedAgain(s)) return;

            if (Stalled(s))
            {
                Retry(s, "stuck");
                return;
            }
            if (Arrived(s.TargetPoint, Settings.ArrivalTolerance.Value))
            {
                Regroup(s, "got away");
                return;
            }
            if (Time.time - s.PhaseSince > Settings.RetreatTimeout.Value)
                Regroup(s, "ran out of time running");
        }

        private bool PickRetreat(SurrenderState s)
        {
            if (Perception.RetreatPoint(Me, s.CaptorLastSeen, Settings.RetreatDistance.Value, false, out s.TargetPoint))
                return true;
            GoHide(s, "no way out");
            return false;
        }

        // Stuck or blocked: a few fresh routes, then hide where he is.
        private void Retry(SurrenderState s, string why)
        {
            s.Retries++;
            if (s.Retries > Settings.PathRetries.Value)
            {
                GoHide(s, $"{why}, giving up running");
                return;
            }
            if (Settings.VerboseLogs.Value)
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {why}, new route ({s.Retries})");
            s.NextMoveOrder = 0f;   // Retreating picks a fresh route next frame
            ResetProgress(s);
        }

        private void Regroup(SurrenderState s, string why)
        {
            BotOwner.StopMove();
            s.RegroupUntil = Time.time + Settings.RegroupSeconds();
            s.SetPhase(Phase.Regrouping, why);
        }

        private void Regrouping(SurrenderState s, bool check)
        {
            Hold(0.6f);
            BotOwner.Steering.LookToPoint(s.CaptorLastSeen + Vector3.up * 1.5f);
            if (!check) return;
            if (SpottedAgain(s)) return;
            if (Time.time < s.RegroupUntil) return;

            Decide(s);
        }

        // How to get back in the fight, best option first.
        private void Decide(SurrenderState s)
        {
            EquipmentSlot? kept = Arsenal.GunOnBody(Me);
            if (kept != null && Arsenal.DrawGun(BotOwner))
            {
                Rearm(s, $"draws the {Arsenal.SlotName(kept)} he kept");
                return;
            }

            if (Settings.RecoverOwnWeapon.Value && s.DroppedWeapon != null && !s.Skip.Contains(s.DroppedWeapon) &&
                Vector3.Distance(s.DropPosition, s.CaptorLastSeen) > Settings.AvoidRadius.Value &&
                Arsenal.StillThere(s.DroppedWeapon, s.DropPosition) &&
                Perception.Reachable(Me.Position, s.DropPosition))
            {
                s.TargetWeapon = s.DroppedWeapon;
                s.TargetPoint = s.DropPosition;
                s.SetPhase(Phase.Recovering, "going back for his own gun");
                return;
            }

            if (Settings.ScavengeWeapons.Value)
            {
                Item found = Arsenal.FindWeapon(Me, s.CaptorLastSeen, Settings.ScavengeAvoid.Value,
                    Settings.ScavengeRadius.Value, s.Skip, out Vector3 at);
                if (found != null)
                {
                    if (Perception.Reachable(Me.Position, at))
                    {
                        s.TargetWeapon = found;
                        s.TargetPoint = at;
                        s.SetPhase(Phase.Recovering, $"going for {found.ShortName.Localized()} {Vector3.Distance(at, Me.Position):0} m away");
                        return;
                    }
                    s.Skip.Add(found);
                }
            }

            if (Settings.HighNerveKnife.Value && s.Nerve >= Settings.HighNerve.Value && Arsenal.HasMelee(Me) && Arsenal.DrawMelee(BotOwner))
            {
                Rearm(s, $"no gun to find, comes back with his knife (nerve {s.Nerve:0.00})");
                return;
            }

            s.TargetWeapon = null;
            GoHide(s, "nothing to fight with", quiet: s.Phase == Phase.Regrouping && s.HidCount++ > 0);
        }

        private void Recovering(SurrenderState s, bool check)
        {
            if (!MoveTo(s, s.TargetPoint, sprint: s.Scrambling))
            {
                if (s.Scrambling) FailScramble(s, "no path to it");
                else GiveUpOn(s, "no path to it");
                return;
            }
            if (!check) return;
            if (SpottedAgain(s)) return;

            // Someone else got there first.
            if (Time.time - s.PhaseSince > 0.5f && !Arsenal.StillThere(s.TargetWeapon, s.TargetPoint))
            {
                if (s.Scrambling) FailScramble(s, "it's gone");
                else GiveUpOn(s, "it's gone");
                return;
            }

            if (Arrived(s.TargetPoint, Settings.PickupRange.Value))
            {
                BotOwner.StopMove();
                Item weapon = s.TargetWeapon;
                bool scrambling = s.Scrambling;
                s.SetPhase(Phase.PickingUp, $"picking up {weapon.ShortName.Localized()}");
                s.Scrambling = scrambling;
                Arsenal.PickUp(Me, weapon, ok => HandsAreHighPlugin.Try("after pick-up", () => AfterPickUp(weapon, ok)));
                return;
            }
            if (Stalled(s))
            {
                if (s.Scrambling) FailScramble(s, "stuck on the way");
                else GiveUpOn(s, "stuck on the way");
                return;
            }
            float limit = s.Scrambling ? Settings.ScrambleTimeout.Value : Settings.RecoveryTimeout.Value;
            if (Time.time - s.PhaseSince > limit)
            {
                if (s.Scrambling) FailScramble(s, "took too long");
                else GiveUpOn(s, "took too long");
            }
        }

        private void AfterPickUp(Item weapon, bool ok)
        {
            if (!SurrenderRegistry.TryGet(Me, out SurrenderState s) || s.Phase != Phase.PickingUp || s.TargetWeapon != weapon) return;
            if (!ok)
            {
                if (s.Scrambling) FailScramble(s, "pick-up failed (no room, or taken)");
                else GiveUpOn(s, "pick-up failed (no room, or taken)");
                return;
            }
            if (!Arsenal.Carries(Me, weapon))
            {
                GiveUpOn(s, "pick-up reported success but the gun isn't on him");
                return;
            }
            if (Arsenal.DrawGun(BotOwner))
                Rearm(s, $"rearmed with {weapon.ShortName.Localized()}{(s.Scrambling ? " after a scramble" : "")}");
            else
                GiveUpOn(s, "has the gun but couldn't draw it");
        }

        private void PickingUp(SurrenderState s)
        {
            Hold(Settings.Stance.Value);
            if (Time.time - s.PhaseSince > Settings.PickupTimeout.Value)
            {
                if (s.Scrambling) FailScramble(s, "pick-up never finished");
                else GiveUpOn(s, "pick-up never finished");
            }
        }

        private void GiveUpOn(SurrenderState s, string why)
        {
            if (s.TargetWeapon != null) s.Skip.Add(s.TargetWeapon);
            s.TargetWeapon = null;
            BotOwner.StopMove();
            s.RegroupUntil = Time.time + 1f;
            s.SetPhase(Phase.Regrouping, $"gave up on the gun: {why}");
        }

        // Nothing to fight with: gets out of sight of where he last saw you, stays low,
        // and looks again every few seconds.
        private void GoHide(SurrenderState s, string why, bool quiet = false)
        {
            BotOwner.StopMove();
            s.SetPhase(Phase.Hiding, why, quiet);
            if (Perception.CanSee(Me, s.CaptorLastSeen) &&
                Perception.RetreatPoint(Me, s.CaptorLastSeen, Settings.CoverRadius.Value, true, out s.TargetPoint))
            {
                s.HideMoving = true;
            }
        }

        private void Hiding(SurrenderState s, bool check)
        {
            if (s.HideMoving)
            {
                if (!MoveTo(s, s.TargetPoint, sprint: false)) s.HideMoving = false;
            }
            else
            {
                Hold(0f);
                BotOwner.Steering.LookToPoint(s.CaptorLastSeen + Vector3.up * 1.5f);
            }
            if (!check) return;
            if (SpottedAgain(s)) return;

            if (s.HideMoving && (Arrived(s.TargetPoint, Settings.ArrivalTolerance.Value) || Stalled(s)))
            {
                s.HideMoving = false;
                BotOwner.StopMove();
            }
            if (Time.time - s.PhaseSince > Settings.HideRecheck.Value)
            {
                s.RegroupUntil = Time.time;
                // Decide comes back to Hiding if there's still nothing; logged once, not every 8 s.
                s.SetPhase(Phase.Regrouping, "looks again", quiet: true);
            }
        }

        // A human player close by aims straight at him for a moment: hands go up again.
        // With a gun still on him, a bold bot may draw instead.
        private bool SpottedAgain(SurrenderState s)
        {
            if (Time.time < s.GraceUntil) return false;
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == null) return false;

            float range = Settings.ResurrenderDistance.Value;
            Player aiming = null;
            foreach (Player p in world.AllAlivePlayersList)
            {
                if (p == null || p.IsAI) continue;
                if ((p.Position - Me.Position).sqrMagnitude > range * range) continue;
                if (!Perception.Covering(p, Me, Settings.AimTolerance.Value)) continue;
                aiming = p;
                break;
            }

            if (aiming == null) { s.AimedBy = null; return false; }
            if (s.AimedBy != aiming) { s.AimedBy = aiming; s.AimedSince = Time.time; }
            if (Time.time - s.AimedSince < Settings.AimConfirm.Value) return false;

            s.AimedBy = null;
            if (Arsenal.GunOnBody(Me) != null)
            {
                float chance = Mathf.Clamp01(Settings.RecaptureResistance.Value * s.Nerve);
                float roll = Random.value;
                if (roll < chance && Arsenal.DrawGun(BotOwner))
                {
                    Rearm(s, $"caught again but draws (chance {chance:0.00}, roll {roll:0.00})");
                    return true;
                }
            }
            BotOwner.StopMove();
            s.HidCount = 0;
            SurrenderRegistry.Surrender(Me, aiming, s.Scrambling ? "caught mid-scramble" : "caught again");
            return true;
        }

        // ---- movement ------------------------------------------------------------------

        private void Hold(float pose)
        {
            BotOwner.StopMove();
            BotOwner.SetTargetMoveSpeed(0f);
            BotOwner.SetPose(pose);
        }

        // Follows a NavMesh path we computed (BotMover.GoToByWay), re-issued every second;
        // false if there's no complete way there. Never BotOwner.GoToPoint (see Perception.PathTo).
        private bool MoveTo(SurrenderState s, Vector3 point, bool sprint)
        {
            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            if (Time.time < s.NextMoveOrder) return true;
            bool first = s.NextMoveOrder == 0f;
            s.NextMoveOrder = Time.time + MoveOrderInterval;
            if (first) ResetProgress(s);

            if (!Perception.PathTo(Me.Position, point, out Vector3[] corners)) return false;
            BotOwner.MovementResume();
            BotOwner.Mover.GoToByWay(corners, ReachDist);
            BotOwner.Sprint(sprint);
            return true;
        }

        private void ResetProgress(SurrenderState s)
        {
            s.ProgressFrom = Me.Position;
            s.ProgressSince = Time.time;
        }

        // No real progress for a while: blocked by a door, a wall or another body.
        private bool Stalled(SurrenderState s)
        {
            float moved = (Me.Position - s.ProgressFrom).magnitude;
            if (moved >= Settings.ProgressMeters.Value)
            {
                ResetProgress(s);
                return false;
            }
            return Time.time - s.ProgressSince > Settings.StallSeconds.Value;
        }

        private bool Arrived(Vector3 point, float within)
        {
            Vector3 d = point - Me.Position;
            d.y = 0f;
            return d.magnitude <= within;
        }

        public override void BuildDebugText(StringBuilder sb)
        {
            if (SurrenderRegistry.TryGet(Me, out SurrenderState s))
                sb.AppendLine($"HandsAreHigh: {s.Phase}, order {s.Order}, arms {s.Weight:0.00}, nerve {s.Nerve:0.00}");
        }
    }
}
