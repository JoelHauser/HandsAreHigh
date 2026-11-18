using System.Text;
using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // While a bot is in the registry this layer outranks SAIN's and the vanilla ones,
    // so nothing else moves, aims or shoots with it until it hands him back.
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
        private const float MoveOrderInterval = 2f;

        public SurrenderLogic(BotOwner botOwner) : base(botOwner) { }

        private Player Me => BotOwner.GetPlayer;

        public override void Start()
        {
            BotOwner.ShootData?.EndShoot();
            BotOwner.Sprint(false);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (!SurrenderRegistry.TryGet(Me, out SurrenderState s)) return;
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
                case Phase.PickingUp: Hold(Settings.Stance.Value); break;
                case Phase.Hiding: Hiding(s, check); break;
                case Phase.Rearmed: Hold(1f); break;
            }
        }

        // ---- hands up ------------------------------------------------------------------

        private void HandsUp(SurrenderState s, bool check)
        {
            Hold(Settings.Stance.Value);
            if (Settings.FaceCaptor.Value && s.Captor != null)
                BotOwner.Steering.LookToPoint(Perception.Eye(s.Captor));
            if (!check) return;

            // Shot while his hands are up: he runs.
            float health = Arsenal.Health(Me);
            bool hurt = health < s.LastHealth - 1f;
            s.LastHealth = health;
            if (hurt && Settings.BoltWhenShot.Value)
            {
                s.CaptorLastSeen = s.Captor != null ? s.Captor.Position : Me.Position - Me.Transform.forward * 10f;
                s.LowerArms(0.15f);
                s.SetPhase(Phase.Retreating);
                return;
            }

            Player captor = s.Captor;
            bool captorAlive = captor != null && captor.HealthController != null && captor.HealthController.IsAlive;
            float dist = captorAlive ? Vector3.Distance(captor.Position, Me.Position) : float.MaxValue;
            bool seen = captorAlive && Perception.LineClear(Perception.Eye(Me), Perception.Eye(captor));

            if (s.ForcedLeave)
            {
                s.ForcedLeave = false;
                s.CaptorLastSeen = captorAlive ? captor.Position : Me.Position;
                s.LowerArms(Settings.LowerSeconds.Value);
                s.SetPhase(Phase.Retreating);
                return;
            }
            if (s.ForcedOpening)
            {
                s.ForcedOpening = false;
                if (!TryBreak(s, dist))
                    HandsAreHighPlugin.Log.LogInfo($"{Me.Profile?.Nickname}: nothing he can use at {dist:0.0} m, keeps his hands up");
                return;
            }

            // Captor gone: walked off, out of sight, or dead.
            if (!captorAlive || dist > Settings.LeaveDistance.Value || !seen)
            {
                if (s.CaptorGoneSince < 0f) s.CaptorGoneSince = Time.time;
                if (Time.time - s.CaptorGoneSince > Settings.LeaveSeconds.Value)
                {
                    s.LowerArms(Settings.LowerSeconds.Value);
                    s.SetPhase(Phase.Retreating);
                }
                return;
            }
            s.CaptorGoneSince = -1f;
            s.CaptorLastSeen = captor.Position;

            // Captor not covering him: an opening.
            if (Perception.Covering(captor, Me))
            {
                s.UnwatchedSince = -1f;
                return;
            }
            if (s.UnwatchedSince < 0f) s.UnwatchedSince = Time.time;
            if (Time.time - s.UnwatchedSince < Settings.OpeningDelay.Value) return;

            // Chance per second, scaled by this bot's nerve.
            float perSecond = Mathf.Clamp01(Settings.BreakChance.Value * s.Nerve);
            float perCheck = 1f - Mathf.Pow(1f - perSecond, CheckInterval);
            if (Random.value >= perCheck) return;

            TryBreak(s, dist);
        }

        // Go for whatever he still has, picked by range.
        private bool TryBreak(SurrenderState s, float dist)
        {
            if (dist <= Settings.KnifeRange.Value && Arsenal.HasMelee(Me))
            {
                s.LowerArms(0.12f);
                Arsenal.DrawMelee(BotOwner);
                HandsAreHighPlugin.Log.LogInfo($"{Me.Profile?.Nickname}: goes for his knife at {dist:0.0} m");
                s.SetPhase(Phase.Rearmed);
                return true;
            }
            if (dist <= Settings.SidearmRange.Value && Arsenal.GunOnBody(Me) != null)
            {
                s.LowerArms(0.18f);
                Arsenal.DrawGun(BotOwner);
                HandsAreHighPlugin.Log.LogInfo($"{Me.Profile?.Nickname}: draws at {dist:0.0} m");
                s.SetPhase(Phase.Rearmed);
                return true;
            }
            // Nothing that suits the range: he keeps his hands up and waits.
            return false;
        }

        // ---- after the captor leaves ---------------------------------------------------

        private void Retreating(SurrenderState s, bool check)
        {
            if (s.NextMoveOrder == 0f &&
                !Perception.RetreatPoint(Me, s.CaptorLastSeen, Settings.RetreatDistance.Value, out s.TargetPoint))
            {
                s.SetPhase(Phase.Hiding);
                return;
            }

            if (!MoveTo(s, s.TargetPoint, sprint: true))
            {
                s.SetPhase(Phase.Hiding);
                return;
            }

            // He commits to the run for a few seconds before he can be caught again.
            if (check && Time.time - s.PhaseSince > 4f && SpottedAgain(s)) return;

            if (Arrived(s.TargetPoint, 2f) || Time.time - s.PhaseSince > 25f)
            {
                s.RegroupUntil = Time.time + Random.Range(Settings.RegroupMin.Value, Settings.RegroupMax.Value);
                s.SetPhase(Phase.Regrouping);
            }
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
            if (Arsenal.GunOnBody(Me) != null)
            {
                Arsenal.DrawGun(BotOwner);
                s.SetPhase(Phase.Rearmed);
                return;
            }

            float avoid = Settings.AvoidRadius.Value;
            if (Settings.RecoverOwnWeapon.Value && s.DroppedWeapon != null && !s.Skip.Contains(s.DroppedWeapon) &&
                Vector3.Distance(s.DropPosition, s.CaptorLastSeen) > avoid &&
                Arsenal.StillThere(s.DroppedWeapon, s.DropPosition))
            {
                s.TargetWeapon = s.DroppedWeapon;
                s.TargetPoint = s.DropPosition;
                HandsAreHighPlugin.Log.LogInfo($"{Me.Profile?.Nickname}: going back for his own gun");
                s.SetPhase(Phase.Recovering);
                return;
            }

            if (Settings.ScavengeWeapons.Value)
            {
                Item found = Arsenal.FindWeapon(Me, s.CaptorLastSeen, avoid, Settings.ScavengeRadius.Value, s.Skip, out Vector3 at);
                if (found != null)
                {
                    s.TargetWeapon = found;
                    s.TargetPoint = at;
                    HandsAreHighPlugin.Log.LogInfo($"{Me.Profile?.Nickname}: going for {found.ShortName} {Vector3.Distance(at, Me.Position):0} m away");
                    s.SetPhase(Phase.Recovering);
                    return;
                }
            }

            if (Arsenal.HasMelee(Me) && s.Nerve > 0.75f)
            {
                Arsenal.DrawMelee(BotOwner);
                s.SetPhase(Phase.Rearmed);
                return;
            }

            s.SetPhase(Phase.Hiding);
        }

        private void Recovering(SurrenderState s, bool check)
        {
            if (!MoveTo(s, s.TargetPoint, sprint: false))
            {
                GiveUpOn(s);
                return;
            }
            if (!check) return;
            if (SpottedAgain(s)) return;

            // Someone else got there first.
            if (Time.time - s.PhaseSince > 1f && !Arsenal.StillThere(s.TargetWeapon, s.TargetPoint))
            {
                GiveUpOn(s);
                return;
            }

            if (Arrived(s.TargetPoint, 1.6f))
            {
                BotOwner.StopMove();
                s.SetPhase(Phase.PickingUp);
                Item weapon = s.TargetWeapon;
                Arsenal.PickUp(Me, weapon, ok => HandsAreHighPlugin.Try("after pick-up", () =>
                {
                    if (!SurrenderRegistry.TryGet(Me, out SurrenderState now) || now.Phase != Phase.PickingUp) return;
                    if (ok && Arsenal.DrawGun(BotOwner))
                    {
                        HandsAreHighPlugin.Log.LogInfo($"{Me.Profile?.Nickname}: rearmed with {weapon.ShortName}");
                        now.SetPhase(Phase.Rearmed);
                    }
                    else
                    {
                        GiveUpOn(now);
                    }
                }));
            }
            else if (Time.time - s.PhaseSince > 40f)
            {
                GiveUpOn(s);
            }
        }

        private void GiveUpOn(SurrenderState s)
        {
            if (s.TargetWeapon != null) s.Skip.Add(s.TargetWeapon);
            s.TargetWeapon = null;
            s.RegroupUntil = Time.time + 1f;
            s.SetPhase(Phase.Regrouping);
        }

        // Nothing to fight with: stays low in cover. Keeps an eye out for a gun.
        private void Hiding(SurrenderState s, bool check)
        {
            Hold(0f);
            BotOwner.Steering.LookToPoint(s.CaptorLastSeen + Vector3.up * 1.5f);
            if (!check) return;
            if (SpottedAgain(s)) return;
            if (Time.time - s.PhaseSince > 8f)
            {
                s.RegroupUntil = Time.time;
                s.SetPhase(Phase.Regrouping);   // look again; Decide comes back here if there's still nothing
            }
        }

        // A human player closes in and covers him while he's unarmed: hands go up again.
        // With a gun still on him, a bold bot draws instead.
        private bool SpottedAgain(SurrenderState s)
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == null) return false;
            foreach (Player p in world.AllAlivePlayersList)
            {
                if (p == null || p.IsAI) continue;
                if (Vector3.Distance(p.Position, Me.Position) > Settings.ResurrenderDistance.Value) continue;
                if (!Perception.Covering(p, Me)) continue;

                if (Arsenal.GunOnBody(Me) != null && Random.value < s.Nerve)
                {
                    Arsenal.DrawGun(BotOwner);
                    s.SetPhase(Phase.Rearmed);
                }
                else
                {
                    BotOwner.StopMove();
                    SurrenderRegistry.Surrender(Me, p);
                }
                return true;
            }
            return false;
        }

        // ---- movement ------------------------------------------------------------------

        private void Hold(float pose)
        {
            BotOwner.StopMove();
            BotOwner.SetTargetMoveSpeed(0f);
            BotOwner.SetPose(pose);
        }

        // Re-issues the path every couple of seconds; false if there's no way there.
        private bool MoveTo(SurrenderState s, Vector3 point, bool sprint)
        {
            BotOwner.SetPose(1f);
            BotOwner.SetTargetMoveSpeed(1f);
            if (Time.time < s.NextMoveOrder) return true;
            s.NextMoveOrder = Time.time + MoveOrderInterval;

            var status = BotOwner.GoToPoint(point, false, 1f);
            BotOwner.Sprint(sprint);
            return status != UnityEngine.AI.NavMeshPathStatus.PathInvalid;
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
                sb.AppendLine($"HandsAreHigh: {s.Phase}, arms {s.Weight:0.00}, nerve {s.Nerve:0.00}");
        }
    }
}
