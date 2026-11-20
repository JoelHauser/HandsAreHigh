using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // How a held man carries himself (0.13.0): demeanor, where he looks, small movement while he
    // kneels, flinching at near misses. Presentation only: nothing here feeds a decision (openings,
    // escapes, draws, compliance are all SurrenderLogic's, with their own rolls), so a defiant look
    // never means he will break and a frightened one never means he won't.
    //
    // Engine facts (0.16.9 decompile):
    //   - Player.VisualPass -> PlayerBones.RotateHead adds the free-look offset
    //     (ProceduralWeaponAnimation.GetHeadRotation = (yaw, 0, -pitch), half on the neck, half on
    //     the head, in their local space). 0.18.0: bots DO use it: BotOwner.UpdateManual ->
    //     BotHeadData.ManualUpdate turns the head (Player.Look, withReturn false, clamp +-40 deg)
    //     towards BestObservedEnemy (EnemyVisibility) or by up to +-25 deg on patrol glances, and
    //     holds it there. The body animator sets the neck and head every frame; ArmPosePatch
    //     (postfix on VisualPass) is the last writer, so a rotation applied there lasts one frame
    //     and nothing accumulates (the animator rewrites the bones next frame).
    //   - Head bone axes (0.18.0, read from the installed skeleton.bundle rest pose with UnityPy:
    //     Root_Joint +Z forward; Base HumanHead local +Y = world forward (the face), local X = world
    //     down, local Z = world left; SAIN's PlayerTransformClass also takes head.up as the look).
    //     0.13-0.17 measured a "forward" once per bot from one animated frame (HeadFwdLocal = the
    //     body's forward in head space). That frame's head was wherever the game's free-look had it
    //     (up to 40 deg towards whoever he watched): the error was kept for the raid (the sideways
    //     gaze), and the pitch was applied about a "right" axis off the real one, which rolled the
    //     head (the cocked head). Now the head is set to a full target rotation built from the rig's
    //     face and up axes, upright (zero roll), and the game's free-look is centred while held
    //     (HeadFreeLookPatch).
    //   - Breathing: the body animator's idle already moves the spine; the kneel only re-aims the
    //     pelvis (Kneel.ApplyBody), so that breathing still shows. No second breathing is added.
    //
    // Order in ArmPosePatch: Kneel.ApplyBody (uses IdleShift) -> ApplyUpper (torso turn towards
    // a look, flinch hunch) -> ArmPose.Apply -> ApplyHead (neck + head; HeadDelta stored) ->
    // Kneel.ApplyHands (hands follow HeadDelta round the skull) -> Kneel.ApplyBrace.
    public enum DemeanorKind { Resigned, Frightened, Cautious, Defiant }
    internal enum GlanceKind { Captor, Weapon, Threat, Squad, Escape, Down }

    internal sealed class PresenceState
    {
        internal DemeanorKind Demeanor, Base;
        internal bool Chosen;
        internal float ChosenAt, CheckAt;
        internal float AtInjury, AtFire;       // circumstances when it was last set
        internal float WorseAt = -1000f;
        internal bool WarningShot;              // a warning shot since the last check

        // Attention: smoothed look (yaw relative to his body, pitch below the horizon), degrees.
        internal float Yaw, Pitch, YawVel, PitchVel;
        internal float W;                       // 0..1, blends the whole head/torso look in and out
        internal GlanceKind Glance;
        internal Vector3 GlancePoint;
        internal float GlanceUntil, NextGlanceAt;
        internal Quaternion HeadDelta = Quaternion.identity;
        internal Quaternion HeadFinal = Quaternion.identity;   // 0.18.0: the head's rotation as we left it
        internal int HeadFrame = -1;
        internal bool RigChecked;               // the rig's face/up axes checked against this bot once
        internal bool RigOk = true;
        internal int RigBad;
        internal float DiagAt;
        internal float LastApply;
        internal float Assist;                  // torso yaw this frame (deg)
        // 0.15.0: his captor is behind him, out of what his eyes can reach (kneeling): he doesn't
        // twist after him. Now and then a glance over that shoulder, as far as a neck goes.
        internal bool Lost;
        internal float LostSide;                // -1 left, +1 right: where he last had him
        internal float BehindUntil, NextBehindAt;

        // Flinch.
        internal float FlinchStart = -1000f, FlinchAmp, FlinchSide, FlinchHab = 1f, FlinchGate = 1f;
        internal float FlinchLogAt = -1000f;

        // Kneeling idle.
        internal float IdleW;
        internal float ShiftFrom, ShiftTo, ShiftStart, ShiftDur = 1f, NextShiftAt;
        internal float ElbowFrom, ElbowTo, ElbowStart, NextElbowAt;
    }

    // One demeanor's presentation. Multipliers are 1 = neutral; Demeanor strength scales them.
    internal sealed class DemeanorProfile
    {
        internal string Name;
        internal float GlanceEvery, Weapon, Threat, Squad, Escape, Down, Commit, Pitch, Speed, Idle, Flinch, Tremble;
        internal float Steady, Scared, OneKnee, DownTime, RiseTime, Brace, Plead;
    }

    internal static class Presence
    {
        private static readonly DemeanorProfile[] Profiles =
        {
            // Resigned: head a little low, looks about now and then, does as told.
            new DemeanorProfile { Name = "resigned", GlanceEvery = 1f, Weapon = 0.6f, Threat = 1f, Squad = 0.6f, Escape = 0.4f, Down = 1f, Commit = 1f,
                Pitch = 4f, Speed = 1f, Idle = 1f, Flinch = 1f, Tremble = 1f, Steady = 1.2f, Scared = 1f, OneKnee = 1f, DownTime = 1f, RiseTime = 1f, Brace = 1f, Plead = 1f },
            // Frightened: drops quickly, head bowed, short darting glances (fire, his friends),
            // fidgets, flinches hard, pleads.
            new DemeanorProfile { Name = "frightened", GlanceEvery = 0.7f, Weapon = 0.3f, Threat = 1.6f, Squad = 0.8f, Escape = 0.7f, Down = 1.6f, Commit = 0.7f,
                Pitch = 9f, Speed = 1.25f, Idle = 1.5f, Flinch = 1.4f, Tremble = 1.6f, Steady = 0.6f, Scared = 2.4f, OneKnee = 0.8f, DownTime = 0.95f, RiseTime = 0.85f, Brace = 1.2f, Plead = 1.7f },
            // Cautious: deliberate, measures the room (his gun, the way out, his squad), slow head.
            new DemeanorProfile { Name = "cautious", GlanceEvery = 1f, Weapon = 1.3f, Threat = 1.1f, Squad = 1f, Escape = 1.1f, Down = 0.4f, Commit = 1.25f,
                Pitch = 2f, Speed = 0.85f, Idle = 0.8f, Flinch = 0.9f, Tremble = 0.9f, Steady = 1f, Scared = 0.6f, OneKnee = 1.6f, DownTime = 1.08f, RiseTime = 1.1f, Brace = 1.25f, Plead = 0.8f },
            // Defiant: holds your eyes, chin up, few glances, slow to comply, barely flinches.
            new DemeanorProfile { Name = "defiant", GlanceEvery = 2.2f, Weapon = 0.7f, Threat = 0.7f, Squad = 0.7f, Escape = 0.5f, Down = 0.1f, Commit = 1.3f,
                Pitch = -3f, Speed = 0.7f, Idle = 0.5f, Flinch = 0.6f, Tremble = 0.5f, Steady = 1.6f, Scared = 0.25f, OneKnee = 1f, DownTime = 1.12f, RiseTime = 1.2f, Brace = 0.7f, Plead = 0.4f },
        };

        private const float CheckEvery = 2f;
        private const float MinChange = 12f;      // s between two changes of demeanor
        private const float CalmBack = 25f;       // s of calm after a worsening before stepping back
        private const float FlinchAttack = 0.07f, FlinchRelease = 0.5f;

        internal static bool On => Settings.Enabled.Value;

        // 0.18.0: the Head neutral test (14. Debug), or a scenario switching it on for its run;
        // TestDiag: pose diagnostics for a scenario's run. Both cleared with the scenario.
        internal static bool TestNeutral, TestDiag;
        internal static bool Neutral => TestNeutral || Settings.HeadNeutralTest.Value;

        private static float K => Settings.DemeanorEnabled.Value ? Settings.DemeanorStrength.Value : 0f;

        // A multiplier from the profile, scaled towards 1 by Demeanor strength (0 = all neutral).
        private static float M(float v) => 1f + (v - 1f) * K;

        internal static DemeanorProfile Of(SurrenderState s) => Profiles[(int)s.Look.Demeanor];

        // Callers that tune motion by demeanor (Kneel, ArmPose, Voice). Plain fields, no lambdas.
        internal static float DescentSteady(SurrenderState s) => M(Of(s).Steady);
        internal static float DescentScared(SurrenderState s) => M(Of(s).Scared);
        internal static float DescentOneKnee(SurrenderState s) => M(Of(s).OneKnee);
        internal static float DownTime(SurrenderState s) => M(Of(s).DownTime);
        internal static float RiseTime(SurrenderState s) => M(Of(s).RiseTime);
        internal static float Brace(SurrenderState s) => M(Of(s).Brace);
        internal static float Tremble(SurrenderState s) => M(Of(s).Tremble);
        internal static float Plead(SurrenderState s) => M(Of(s).Plead);
        internal static bool Defiant(SurrenderState s) => K > 0f && s.Look.Demeanor == DemeanorKind.Defiant;

        internal static string Describe(SurrenderState s)
        {
            if (!s.Look.Chosen || !Settings.DemeanorEnabled.Value) return null;
            string g = s.Look.Glance != GlanceKind.Captor ? $", glances: {s.Look.Glance}" : "";
            return $"{Of(s).Name}{g}";
        }

        private static void Log(SurrenderState s, string msg)
        {
            if (Settings.LogAttention.Value) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: {msg}");
        }

        // ---- demeanor ---------------------------------------------------------------------

        // Once per encounter (a new surrender or a recapture), from who he is and what is
        // happening: nerve, SAIN personality, wounds, incoming fire, squadmates near, faction as a
        // mild tendency only. Weighted pick, logged with the weights.
        internal static void NewEncounter(SurrenderState s)
        {
            PresenceState p = s.Look;
            p.Glance = GlanceKind.Captor;
            p.GlanceUntil = 0f;
            p.NextGlanceAt = Time.time + Dice.Range(1.5f, 3.5f);
            p.WarningShot = false;
            p.FlinchAmp = 0f;
            if (s.Bot == null) return;
            if (!Settings.DemeanorEnabled.Value) { p.Demeanor = p.Base = DemeanorKind.Resigned; p.Chosen = true; return; }

            float nerve = s.Nerve, hurt = s.Injury, fire = Fire(s);
            float timid = Timid(s.Personality);
            bool scav = s.Bot.Profile?.Info?.Side == EPlayerSide.Savage;
            int allies = Reactions.FreeAlliesNear(s.Bot.AIData?.BotOwner, s.Bot);
            float fr = (1.35f - nerve) * timid * (1f + 0.8f * fire) * (1f + 0.6f * hurt) * (scav ? 1.15f : 1f);
            float ca = 0.9f * (0.5f + nerve) * (s.Personality == "Rat" || s.Personality == "SnappingTurtle" ? 1.4f : 1f) * (allies > 0 ? 1.3f : 1f);
            float de = Mathf.Max(0f, nerve - 0.3f) * 2.2f / timid * s.Fight * (1f - 0.7f * hurt) * (1f - 0.6f * fire) * (scav ? 1f : 1.15f);
            float re = 0.8f * (1f + 0.5f * hurt);
            float total = fr + ca + de + re;
            float roll = Dice.Value * Mathf.Max(0.0001f, total);
            DemeanorKind d = roll < re ? DemeanorKind.Resigned : roll < re + fr ? DemeanorKind.Frightened : roll < re + fr + ca ? DemeanorKind.Cautious : DemeanorKind.Defiant;
            Set(s, d, now: Time.time, hurt, fire);
            p.Base = d;
            if (Settings.LogAttention.Value || Settings.LogDecisions.Value)
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: demeanor {Profiles[(int)d].Name} (resigned {re:0.00} / frightened {fr:0.00} / cautious {ca:0.00} / defiant {de:0.00}, roll {roll:0.00}; " +
                    $"nerve {nerve:0.00}, hurt {hurt:0.00}, fire {fire:0.00}{(string.IsNullOrEmpty(s.Personality) ? "" : ", " + s.Personality)}, {allies} squadmate(s) near, {(scav ? "Scav" : "PMC/other")}). Presentation only");
        }

        private static void Set(SurrenderState s, DemeanorKind d, float now, float hurt, float fire)
        {
            PresenceState p = s.Look;
            p.Demeanor = d;
            p.Chosen = true;
            p.ChosenAt = now;
            p.AtInjury = hurt;
            p.AtFire = fire;
            p.CheckAt = now + CheckEvery;
        }

        internal static float Timid(string personality)
        {
            switch (personality)
            {
                case "Coward": case "Timmy": return 1.6f;
                case "Rat": return 1.2f;
                case "Chad": case "GigaChad": case "Wreckless": case "SnappingTurtle": return 0.6f;
                default: return 1f;
            }
        }

        // His own count of shots at him (Suppression), 0..1. No SAIN call per check.
        private static float Fire(SurrenderState s)
        {
            BotHistory h = SurrenderRegistry.HistoryOf(s.Bot);
            return Mathf.Clamp01(Suppression.Decayed(h, Time.time) / Mathf.Max(1f, Settings.SustainedFireEvents.Value));
        }

        // Things got materially worse (a fresh wound, heavy fire, a warning shot): one step more
        // fearful, at most every MinChange. Calm for a while after that: one step back towards how
        // he started. Never a re-roll.
        private static void Reconsider(SurrenderState s, float now)
        {
            PresenceState p = s.Look;
            if (now < p.CheckAt) return;
            p.CheckAt = now + CheckEvery;
            if (!Settings.DemeanorEnabled.Value) return;
            float hurt = s.Injury, fire = Fire(s);
            bool shot = p.WarningShot;
            p.WarningShot = false;
            string worse = hurt - p.AtInjury >= 0.25f ? $"hurt {p.AtInjury:0.00} -> {hurt:0.00}"
                         : fire >= 0.6f && p.AtFire < 0.3f ? $"under fire ({fire:0.00})"
                         : shot ? "a warning shot" : null;
            if (worse != null && now - p.ChosenAt >= MinChange)
            {
                DemeanorKind d = Worse(p.Demeanor, s.Nerve);
                if (d != p.Demeanor)
                {
                    if (Settings.LogAttention.Value) Log(s, $"demeanor {Profiles[(int)p.Demeanor].Name} -> {Profiles[(int)d].Name} ({worse})");
                    Set(s, d, now, hurt, fire);
                    p.WorseAt = now;
                }
                return;
            }
            if (p.Demeanor != p.Base && now - p.WorseAt >= CalmBack && now - p.ChosenAt >= MinChange && fire < 0.15f && hurt <= p.AtInjury + 0.05f)
            {
                DemeanorKind d = p.Demeanor == DemeanorKind.Frightened && p.Base == DemeanorKind.Defiant ? DemeanorKind.Cautious : p.Base;
                if (Settings.LogAttention.Value) Log(s, $"demeanor {Profiles[(int)p.Demeanor].Name} -> {Profiles[(int)d].Name} (calmer: no fire, no new wound for {now - p.WorseAt:0} s)");
                Set(s, d, now, hurt, fire);
            }
        }

        private static DemeanorKind Worse(DemeanorKind d, float nerve)
        {
            switch (d)
            {
                case DemeanorKind.Defiant: return DemeanorKind.Cautious;
                case DemeanorKind.Cautious: return nerve < 0.5f ? DemeanorKind.Frightened : DemeanorKind.Resigned;
                case DemeanorKind.Resigned: return DemeanorKind.Frightened;
            }
            return d;
        }

        internal static void OnWarningShot(SurrenderState s) => s.Look.WarningShot = true;

        // ---- per frame (SurrenderRegistry.Tick, after Kneel.Update) ------------------------

        internal static void Update(SurrenderState s, float dt)
        {
            if (s.Dead || s.Bot == null) return;
            PresenceState p = s.Look;
            float now = Time.time;
            if (s.Phase == Phase.HandsUp && !s.Suspended) Reconsider(s, now);

            // Head and torso look: on while he holds his hands up (fades out in 0.2 s when he acts:
            // the AI or the game's own animation has the head then).
            bool attend = On && Settings.HeadMovement.Value && s.Phase == Phase.HandsUp && !s.Suspended && (s.Weight > 0.3f || Kneel.Active(s)) &&
                          !(Kneel.Posing(s) && Kneel.Off(KneelPoseIsolate.Head));
            p.W = Mathf.MoveTowards(p.W, attend ? 1f : 0f, dt / (attend ? 0.5f : 0.2f));

            // Kneeling idle: only while settled on his knees, nothing else going on.
            bool idle = On && Settings.KneelIdle.Value && !Neutral && s.Kneel == KneelPhase.Down && s.Phase == Phase.HandsUp && !s.Suspended && !s.WasHit;
            p.IdleW = Mathf.MoveTowards(p.IdleW, idle ? 1f : 0f, dt / (idle ? 1.2f : 0.25f));
            if (p.IdleW > 0f) IdleSchedule(s, now);

            // Flinch: the gate closes fast once he acts or is hit (the real reaction owns him).
            bool flinchOk = s.Phase == Phase.HandsUp && !s.WasHit && !s.Suspended;
            p.FlinchGate = Mathf.MoveTowards(p.FlinchGate, flinchOk ? 1f : 0f, dt / (flinchOk ? 0.3f : 0.1f));
            p.FlinchHab = Mathf.Min(1f, p.FlinchHab + dt * 0.2f);

            if (p.W <= 0f)
            {
                // Look off: the next one starts from straight ahead (the animated head), not from
                // the last encounter's turn (a stale yaw turned the torso before the head axis
                // was measured, and skewed it for good).
                p.Glance = GlanceKind.Captor;
                p.Yaw = p.Pitch = p.YawVel = p.PitchVel = p.Assist = 0f;
                p.Lost = false;
                return;
            }
            Glances(s, now);
        }

        // ---- glances ----------------------------------------------------------------------

        // Back on the captor when a glance's time is up; a new glance only when he is settled
        // (standing still or down on his knees, no gesture, no flinch, not moving on an order).
        private static void Glances(SurrenderState s, float now)
        {
            PresenceState p = s.Look;
            DemeanorProfile d = Of(s);
            if (p.Glance != GlanceKind.Captor && now >= p.GlanceUntil)
            {
                p.Glance = GlanceKind.Captor;
                p.NextGlanceAt = Mathf.Max(p.NextGlanceAt, now + Dice.Range(0.8f, 1.6f));
            }
            bool settled = s.Gesture == GestureKind.None && !s.Moving && s.Order != Order.Follow &&
                           (s.Kneel == KneelPhase.Standing || s.Kneel == KneelPhase.Down) && Flinch(s) < 0.05f &&
                           s.ArmsUp && s.Progress >= 1f && !s.DropPending;
            if (!settled)
            {
                if (p.Glance != GlanceKind.Captor && s.Gesture != GestureKind.None) p.Glance = GlanceKind.Captor;
                return;
            }
            if (p.Glance != GlanceKind.Captor || now < p.NextGlanceAt) return;
            if (Neutral) return;   // 14. Debug: only his captor
            float lo = Settings.GlanceMin.Value, hi = Mathf.Max(lo, Settings.GlanceMax.Value);
            p.NextGlanceAt = now + Dice.Range(lo, hi) * M(d.GlanceEvery);
            Pick(s, now, d);
        }

        private static void Pick(SurrenderState s, float now, DemeanorProfile d)
        {
            PresenceState p = s.Look;
            Player bot = s.Bot;
            Vector3 eye = Perception.Eye(bot);
            Vector3 captor = CaptorPoint(s, out bool known);

            // Candidates, each only from what he knows. At most one line check (a squadmate).
            float wW = 0f, wT = 0f, wS = 0f, wE = 0f, wD = 0f;
            Vector3 pW = default, pT = default, pS = default, pE = default, pD = default;
            if (s.DroppedWeapon != null && !s.GunGone && (s.DropPosition - bot.Position).sqrMagnitude < 25f * 25f)
            {
                // He believes it is where he dropped it (NoticeGunGone). An opening makes him look
                // at it more, and nothing he does depends on that look.
                wW = M(d.Weapon) * (s.LastCovered ? 1f : 1.6f);
                pW = s.DropPosition + Vector3.up * 0.1f;
            }
            BotHistory h = SurrenderRegistry.HistoryOf(bot);
            if (now - h.FireFromAt < 8f)
            {
                wT = 2f * M(d.Threat);
                pT = h.FireFrom + Vector3.up * 1.3f;
            }
            if (FindSquadmate(s, eye, out Vector3 mate)) { wS = M(d.Squad); pS = mate; }
            if (known)
            {
                // 0.17.0: a way out to his side, 40-70 deg off the way his body faces. 0.16 aimed
                // 35-110 deg off "away from the captor", i.e. mostly behind him: every such glance
                // ran the neck and the upper back to their limits (the wound-up torso in Joel's
                // screenshots).
                Vector3 bf = BodyFwd(bot, s);
                if (bf.sqrMagnitude < 1e-4f) { bf = bot.Transform.Original.forward; bf.y = 0f; bf.Normalize(); }
                float ang = Dice.Range(40f, 70f) * (Dice.Value < 0.5f ? -1f : 1f);
                pE = eye + Quaternion.AngleAxis(ang, Vector3.up) * bf * 6f;
                wE = M(d.Escape);
            }
            if (s.Kneel == KneelPhase.Down || s.Look.Demeanor == DemeanorKind.Frightened)
            {
                // 0.16.0: eyes down, not the head bowed onto the chest: a point on the ground 2.5 m
                // ahead (0.15.0 used 1.1 m, ~44 deg below his eyes on his knees: the head hung
                // forward between his arms, as if his hands pulled it down). With his hands on his
                // head it is rarer still; the pitch limit (Smooth) bounds it either way.
                Vector3 fwd = BodyFwd(bot, s);
                if (fwd.sqrMagnitude < 1e-4f) { fwd = bot.Transform.Original.forward; fwd.y = 0f; fwd.Normalize(); }
                pD = bot.Position + fwd * 2.5f;
                wD = M(d.Down) * (s.Kneel == KneelPhase.Down ? 1.2f : 0.7f) * (1f - 0.6f * Kneel.HandsWeight(s));
            }
            float total = wW + wT + wS + wE + wD;
            if (total <= 0f) return;
            float r = Dice.Value * total;
            GlanceKind g; Vector3 at;
            if ((r -= wW) < 0f) { g = GlanceKind.Weapon; at = pW; }
            else if ((r -= wT) < 0f) { g = GlanceKind.Threat; at = pT; }
            else if ((r -= wS) < 0f) { g = GlanceKind.Squad; at = pS; }
            else if ((r -= wE) < 0f) { g = GlanceKind.Escape; at = pE; }
            else { g = GlanceKind.Down; at = pD; }
            float hold = g == GlanceKind.Down ? Dice.Range(1.2f, 2.6f) : Dice.Range(0.5f, 1.2f);
            hold *= d.Name == "frightened" ? 0.75f : d.Name == "cautious" ? 1.2f : 1f;
            p.Glance = g;
            p.GlancePoint = at;
            p.GlanceUntil = now + hold;
            if (Settings.LogAttention.Value) Log(s, $"glances: {g} for {hold:0.0} s ({Of(s).Name}; weights gun {wW:0.0} fire {wT:0.0} squad {wS:0.0} way out {wE:0.0} down {wD:0.0})");
        }

        // A squadmate he can see, close, not held: three members at most, one line check each.
        private static bool FindSquadmate(SurrenderState s, Vector3 eye, out Vector3 at)
        {
            at = default;
            BotOwner owner = s.Bot.AIData?.BotOwner;
            BotsGroup g = owner?.BotsGroup;
            if (g == null) return false;
            int tries = 0;
            for (int i = 0; i < g.MembersCount && tries < 3; i++)
            {
                BotOwner m = g.Member(i);
                if (m == null || m == owner || m.IsDead) continue;
                Player mp = m.GetPlayer;
                if (mp == null || !Perception.Alive(mp) || SurrenderRegistry.HoldsBot(mp)) continue;
                if ((mp.Position - s.Bot.Position).sqrMagnitude > 40f * 40f) continue;
                tries++;
                Vector3 e = Perception.Eye(mp);
                if (!Perception.LineClear(eye, e)) continue;
                at = e;
                return true;
            }
            return false;
        }

        // Where he looks by default: his captor's face if he sees him, else where he last saw him.
        private static Vector3 CaptorPoint(SurrenderState s, out bool known)
        {
            Player c = s.Captor;
            known = true;
            if (s.CaptorSeen && s.CaptorInView && Perception.Alive(c)) return Perception.Eye(c) - Vector3.up * 0.08f;
            if (s.CaptorLastSeen != Vector3.zero) return s.CaptorLastSeen + Vector3.up * 1.5f;
            known = false;
            return Vector3.zero;
        }

        // ---- flinch -----------------------------------------------------------------------

        // Every shot by anyone while someone is held (Suppression.OnShot): a shot that passes
        // within the near-miss radius of a held man's head, in front of the muzzle, with nothing
        // in between. A hit is not a flinch: Player.BeingHitAction sets WasHit, the gate closes
        // and the layer's own hit response (a bolt) takes over.
        internal static void OnShot(Player shooter, Vector3 from, Vector3 dir)
        {
            if (!Settings.Flinch.Value || Neutral) return;
            float radius = Settings.NearMissRadius.Value;
            float range = Settings.NearMissRange.Value;
            foreach (SurrenderState s in SurrenderRegistry.All)
            {
                if (s.Bot == null || s.Dead || s.Suspended || s.Phase != Phase.HandsUp || s.Bot == shooter || s.WasHit) continue;
                Vector3 head = Perception.Eye(s.Bot) - Vector3.up * 0.2f;
                float along = Vector3.Dot(head - from, dir);
                if (along <= 0f || along > range) continue;
                Vector3 closest = from + dir * along;
                float miss = (head - closest).magnitude;
                if (miss > radius) continue;
                if (Physics.Linecast(from, closest, LayersMaskController.HighPolyWithTerrainMask)) continue;
                Trigger(s, closest, miss / radius, shooter);
            }
        }

        // Cooldown and habituation keep automatic fire from restarting it: inside the cooldown a
        // closer round can only lift the running flinch (never restart it), and each flinch
        // leaves the next one weaker for a few seconds.
        private static void Trigger(SurrenderState s, Vector3 passed, float missFrac, Player shooter)
        {
            PresenceState p = s.Look;
            float now = Time.time;
            float amp = Mathf.Clamp01(1f - 0.6f * missFrac) * p.FlinchHab * M(Of(s).Flinch) * Settings.FlinchStrength.Value;
            if (now - p.FlinchStart < Settings.FlinchCooldown.Value)
            {
                if (now - p.FlinchStart < FlinchAttack + 0.1f) p.FlinchAmp = Mathf.Max(p.FlinchAmp, Mathf.Min(1.2f, amp));
                return;
            }
            p.FlinchStart = now;
            p.FlinchAmp = Mathf.Min(1.2f, amp);
            Vector3 right = s.Bot.Transform.Original.right;
            p.FlinchSide = Vector3.Dot(passed - Perception.Eye(s.Bot), right) >= 0f ? 1f : -1f;
            p.FlinchHab = Mathf.Max(0.35f, p.FlinchHab * 0.7f);
            if (Settings.LogGestures.Value && now - p.FlinchLogAt > 3f)
            {
                p.FlinchLogAt = now;
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: flinches (a shot from {Restraint.Name(shooter)} passed {missFrac * Settings.NearMissRadius.Value:0.0} m away, {(p.FlinchSide > 0 ? "right" : "left")}; intensity {p.FlinchAmp:0.00})");
            }
        }

        // Cancelled by a hit (the hit is not cosmetic; the layer's bolt owns him).
        internal static void OnHit(SurrenderState s)
        {
            s.Look.FlinchAmp = 0f;
            s.Look.FlinchGate = 0f;
        }

        internal static float Flinch(SurrenderState s)
        {
            PresenceState p = s.Look;
            float t = Time.time - p.FlinchStart;
            if (p.FlinchAmp <= 0f || t >= FlinchAttack + FlinchRelease) return 0f;
            float c = t < FlinchAttack ? Mathf.SmoothStep(0f, 1f, t / FlinchAttack) : 1f - Mathf.SmoothStep(0f, 1f, (t - FlinchAttack) / FlinchRelease);
            return c * p.FlinchAmp * p.FlinchGate;
        }

        // ---- kneeling idle ----------------------------------------------------------------

        // Events on per-bot random timers (no shared sine loop, so bots never move in step): a
        // weight shift of the hips to one side, now and then the elbows settle a little. Each is a
        // fresh offset from the animator's pose every frame: nothing accumulates.
        private static void IdleSchedule(SurrenderState s, float now)
        {
            PresenceState p = s.Look;
            float k = Settings.KneelIdleStrength.Value * M(Of(s).Idle);
            if (now >= p.NextShiftAt)
            {
                p.ShiftFrom = ShiftNow(p, now);
                float side = p.ShiftTo > 0f ? -1f : p.ShiftTo < 0f ? 1f : (Dice.Value < 0.5f ? -1f : 1f);
                p.ShiftTo = Dice.Value < 0.25f ? 0f : side * Dice.Range(0.006f, 0.016f) * k;
                p.ShiftStart = now;
                p.ShiftDur = Dice.Range(1.1f, 2.2f);
                p.NextShiftAt = now + Dice.Range(5f, 12f) / Mathf.Max(0.3f, k);
            }
            if (now >= p.NextElbowAt)
            {
                p.ElbowFrom = ElbowNow(p, now);
                p.ElbowTo = Dice.Range(-0.12f, 0.08f) * k;
                p.ElbowStart = now;
                p.NextElbowAt = now + Dice.Range(7f, 18f) / Mathf.Max(0.3f, k);
            }
        }

        private static float ShiftNow(PresenceState p, float now) =>
            Mathf.Lerp(p.ShiftFrom, p.ShiftTo, Mathf.SmoothStep(0f, 1f, (now - p.ShiftStart) / Mathf.Max(0.1f, p.ShiftDur)));

        private static float ElbowNow(PresenceState p, float now) =>
            Mathf.Lerp(p.ElbowFrom, p.ElbowTo, Mathf.SmoothStep(0f, 1f, (now - p.ElbowStart) / 0.6f));

        // Hips to the side (m, + = his right) for Kneel.ApplyBody: the knees stay planted (the
        // thigh is re-aimed at the same knee point; a 1.6 cm shift changes the hip-knee distance
        // by 0.3 mm).
        internal static float IdleShift(SurrenderState s) => s.Look.IdleW > 0f ? ShiftNow(s.Look, Time.time) * s.Look.IdleW : 0f;

        // Elbows-out offset for Kneel.ApplyHands.
        internal static float IdleElbow(SurrenderState s) => s.Look.IdleW > 0f ? ElbowNow(s.Look, Time.time) * s.Look.IdleW : 0f;

        // ---- applying (ArmPosePatch) ------------------------------------------------------

        internal static bool Posing(SurrenderState s) => s.Look.W > 0f || Flinch(s) > 0f;

        // Torso: a share of a wide look turns the upper spine (restrained: never the hips or the
        // kneeling body); a flinch hunches the shoulders and bends the upper back a little.
        internal static void ApplyUpper(Player pl, SurrenderState s)
        {
            PresenceState p = s.Look;
            PlayerBones b = pl.PlayerBones;
            if (b == null) return;
            float fl = Flinch(s);
            Smooth(pl, s);
            Transform spine = b.Spine3?.Original;
            if (spine == null) return;
            Vector3 up = Vector3.up;
            // 0.14.0: with his hands clasped behind his head the head can't turn far without
            // dragging the hands round the skull (or through a helmet), so the upper back carries
            // most of a wide look: earlier (dead zone 25 -> 8 deg), a bigger share (0.4 -> 0.75)
            // and up to twice the torso limit. The arms are solved after this (ArmPose.Apply,
            // Kneel.ApplyHands), from the turned shoulders, so they turn with the chest instead
            // of twisting. Never the hips or the legs: the kneeling body stays planted.
            // 0.15.0: kneeling, the torso's share is small and bounded (Kneeling torso turn max,
            // 12 deg) and starts past what the neck does easily: the hips and knees are planted,
            // and the chest under it is square to them (Kneel.SquareTorso). 0.14.0 let it carry up
            // to 35 deg from 8 deg on, on top of the animator's own trunk twist: the contortion.
            float maxA = TorsoMax(s);
            // 0.18.0: the chest joins in early and gradually (a turn of the head carries the upper
            // back with it), instead of a neck twisting alone up to 25-30 deg and the chest
            // starting late: from 8 deg standing (15 kneeling), 35% of the look, eased in over
            // 10 deg, up to the torso limit (kneeling 12: what planted knees allow).
            bool kneelT = Kneel.Active(s);
            float dead = kneelT ? 15f : 8f, share = 0.35f;
            float over = Mathf.Max(0f, Mathf.Abs(p.Yaw) - dead);
            float ease = Mathf.SmoothStep(0f, 1f, over / 10f);
            float a = Mathf.Sign(p.Yaw) * Mathf.Min(over * share * ease, maxA) * p.W;
            p.Assist = a;
            if (Mathf.Abs(a) > 0.01f)
            {
                // About the spine's own long axis (a kneeling man leans forward: a turn about world
                // up would swing the lean sideways), split over the lower and upper spine.
                Transform low = b.Spine1;
                Transform neckT = b.Neck;
                Vector3 axis = up;
                if (low != null && neckT != null)
                {
                    Vector3 v = neckT.position - low.position;
                    if (v.sqrMagnitude > 1e-4f && Vector3.Angle(v, up) < 50f) axis = v.normalized;
                }
                if (low != null && low != spine && spine.IsChildOf(low))
                {
                    low.rotation = Quaternion.AngleAxis(a * 0.4f, axis) * low.rotation;
                    spine.rotation = Quaternion.AngleAxis(a * 0.6f, axis) * spine.rotation;
                }
                else spine.rotation = Quaternion.AngleAxis(a, axis) * spine.rotation;
            }
            if (fl > 0.001f)
            {
                Vector3 right = pl.Transform.Original.right;
                spine.rotation = Quaternion.AngleAxis(5f * fl, right) * spine.rotation;
                // Shoulders up: each collarbone (the upper arm's parent) tips its arm root upwards.
                if (b.Upperarms != null)
                    for (int i = 0; i < b.Upperarms.Length; i++)
                    {
                        Transform ua = b.Upperarms[i];
                        Transform clav = ua != null ? ua.parent : null;
                        if (clav == null || clav.name.IndexOf("Collarbone", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                        Vector3 d = ua.position - clav.position;
                        if (d.sqrMagnitude < 1e-6f) continue;
                        Vector3 want = Vector3.RotateTowards(d, up, 9f * fl * Mathf.Deg2Rad, 0f);
                        ArmPose.Aim(clav, ua, want, 1f);
                    }
            }
        }

        // Target and smoothing, once per posed frame (frame-rate independent: SmoothDamp with the
        // time since the last posed frame).
        private static void Smooth(Player pl, SurrenderState s)
        {
            PresenceState p = s.Look;
            float now = Time.time;
            float dt = Mathf.Clamp(now - p.LastApply, 0.001f, 0.1f);
            p.LastApply = now;
            Vector3 bodyF = BodyFwd(pl, s);
            if (bodyF.sqrMagnitude < 1e-4f) return;
            Vector3 eye = Perception.Eye(pl);
            bool onCaptor = p.Glance == GlanceKind.Captor;
            Vector3 at = onCaptor ? CaptorPoint(s, out _) : p.GlancePoint;
            float yaw = 0f, pitch = 0f;
            bool have = !onCaptor || s.Captor != null;
            if (have && at != Vector3.zero)
            {
                Vector3 d = at - eye;
                Vector3 flat = d; flat.y = 0f;
                if (flat.sqrMagnitude > 0.01f) yaw = Vector3.SignedAngle(bodyF, flat, Vector3.up);
                if (d.sqrMagnitude > 0.01f) pitch = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            }
            DemeanorProfile prof = Of(s);
            // 0.16.0: hands clasped on his head carry a bowed head with them: the demeanor's tilt is
            // halved there (a frightened man still lowers his eyes, not his whole head).
            float hands = Kneel.HandsWeight(s);
            if (!Neutral) pitch += prof.Pitch * K * (onCaptor ? 1f : 0.3f) * (1f - 0.5f * hands);
            // 0.15.0 limits, measured from his planted body (BodyFwd): the torso's share
            // (TorsoMax) plus the head's turn from the chest (HeadShare) is all he can turn.
            // 0.16.0: with his hands on his head (standing or kneeling) the neck bows and lifts only
            // within Head down/up max with hands on head: the root cause of the head hanging
            // forward was a 35 deg limit (and a 44 deg Down glance) applied as if his hands were up.
            float reach = Reach(s);
            float maxUp = Mathf.Lerp(Settings.HeadMaxUp.Value, Settings.HandsOnHeadUp.Value, hands);
            float maxDown = Mathf.Lerp(Settings.HeadMaxDown.Value, Settings.HandsOnHeadDown.Value, hands);
            now = Time.time;
            if (onCaptor && Planted(s))
            {
                // His captor behind him (out of view: the layer's CaptorInView, or the last place
                // he had him now beyond his reach): he loses him. No stare from a neck at its
                // limit, no torso twisting after him: he faces where he knelt, head a little down
                // (listening), and now and then glances over that shoulder as far as it goes.
                bool lost = !s.CaptorInView || Mathf.Abs(yaw) > reach + Peripheral;
                if (lost && !p.Lost)
                {
                    p.Lost = true;
                    p.LostSide = Mathf.Abs(yaw) > 1f ? Mathf.Sign(yaw) : (Dice.Value < 0.5f ? -1f : 1f);
                    p.NextBehindAt = now + Dice.Range(1.5f, 4f);
                    p.BehindUntil = 0f;
                    if (Settings.LogAttention.Value) Log(s, $"loses sight of his captor (about {Mathf.Abs(yaw):0} deg round, past what he can turn to: {reach:0} + {Peripheral:0} from the corner of his eye); stops tracking him");
                }
                else if (!lost && p.Lost)
                {
                    p.Lost = false;
                    if (Settings.LogAttention.Value) Log(s, $"has his captor in view again ({yaw:+0;-0} deg)");
                }
                if (p.Lost)
                {
                    if (now >= p.NextBehindAt && p.BehindUntil < now && s.Gesture == GestureKind.None)
                    {
                        p.BehindUntil = now + Dice.Range(0.6f, 1.1f);
                        p.NextBehindAt = p.BehindUntil + Dice.Range(4f, 9f) * M(prof.GlanceEvery);
                    }
                    // Over the shoulder: as far as is comfortable (0.8 of his reach), never the limit.
                    yaw = now < p.BehindUntil ? p.LostSide * reach * 0.8f : p.LostSide * Mathf.Min(10f, reach * 0.2f);
                    pitch = Mathf.Max(pitch, 0f) * 0.5f + 8f * (1f - 0.5f * hands);
                }
                else if (Mathf.Abs(yaw) > reach)
                {
                    // In the corner of his eye: held a little short of his limit.
                    yaw = Mathf.Sign(yaw) * reach * 0.92f;
                }
            }
            else
            {
                p.Lost = false;
                if (Mathf.Abs(yaw) > reach + 12f) { yaw = Mathf.Sign(yaw) * reach * 0.85f; pitch += 6f; }
            }
            // 0.17.0: a glance (anything but his captor) stays within a comfortable 0.75 of his reach:
            // a look further round is a turn of the body (TurnDriver) or nothing, never neck and spine
            // wound to their stops. Following his captor may use the whole reach (kneeling, until he
            // loses him; standing, his body turns to the man).
            float lim = onCaptor ? reach : reach * 0.75f;
            yaw = Mathf.Clamp(yaw, -lim, lim);
            pitch = Mathf.Clamp(pitch, -maxUp, maxDown);
            float speed = Settings.HeadSpeed.Value * M(prof.Speed);
            float smooth = p.Glance == GlanceKind.Captor ? 0.22f : 0.12f;
            p.Yaw = Mathf.SmoothDamp(p.Yaw, yaw, ref p.YawVel, smooth / M(prof.Speed), speed, dt);
            p.Pitch = Mathf.SmoothDamp(p.Pitch, pitch, ref p.PitchVel, smooth / M(prof.Speed), speed, dt);
        }

        // 0.15.0 limits. The head's turn from the chest: Head turn max standing, Kneeling head turn
        // max (55) kneeling; the hands behind the head go round with it (Kneel.ApplyHands follows
        // HeadDelta). The torso's: Torso turn max standing, Kneeling torso turn max (12) kneeling.
        private static float HeadShare(SurrenderState s) =>
            Kneel.Active(s) ? Mathf.Min(Settings.KneelHeadYaw.Value, Settings.HeadMaxYaw.Value)
            : Mathf.Lerp(Settings.HeadMaxYaw.Value, Mathf.Min(Settings.HandsOnHeadYaw.Value, Settings.HeadMaxYaw.Value), Kneel.HandsWeight(s));

        // 0.16.0: his body doesn't turn after his captor: kneeling (planted knees), or turned away
        // on an order. Then a captor beyond what he can turn his head to is out of view.
        internal static bool Planted(SurrenderState s) => Kneel.Active(s) || Facing.Away(s);

        private static float TorsoMax(SurrenderState s) =>
            Kneel.Active(s) ? Settings.KneelTorsoYaw.Value : Settings.TorsoAssist.Value;

        internal static float Reach(SurrenderState s) =>
            Kneel.Off(KneelPoseIsolate.Head) ? 0f : HeadShare(s) + TorsoMax(s);

        // Beyond his turn, what the corner of his eye still takes in (deg).
        internal const float Peripheral = 40f;

        // Is this point within what he can see from where his body faces? Standing, he turns to
        // face his captor (the layer's Face), so always; kneeling, his body is planted: reach plus
        // the corner of his eye, measured from the way his knees point.
        internal static bool InView(SurrenderState s, Vector3 point)
        {
            if (s?.Bot == null || !Planted(s)) return true;
            Vector3 bodyF = BodyFwd(s.Bot, s);
            Vector3 d = point - Perception.Eye(s.Bot); d.y = 0f;
            if (bodyF.sqrMagnitude < 1e-4f || d.sqrMagnitude < 0.04f) return true;
            return Vector3.Angle(bodyF, d) <= Reach(s) + Peripheral;
        }

        // His body's facing for the look: while any part of a kneel is on, the frame his legs are
        // planted in (Kneel.KneelAnchorFwd), not the root's, which steering may still move; so the
        // limits are always relative to the body that can't turn.
        private static Vector3 BodyFwd(Player pl, SurrenderState s)
        {
            Vector3 f = pl.Transform.Original.forward;
            if (Kneel.Posing(s))
            {
                Vector3 a = s.KneelAnchorFwd;
                a.y = 0f;
                if (a.sqrMagnitude > 1e-4f) f = a;
            }
            f.y = 0f;
            return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.zero;
        }

        // The head bone's face and up axes in its own space (see the header: read from the rig,
        // not assumed and not taken from an animated frame).
        internal static readonly Vector3 HeadFaceLocal = Vector3.up;
        internal static readonly Vector3 HeadUpLocal = Vector3.left;
        private static readonly Quaternion HeadRigInv = Quaternion.Inverse(Quaternion.LookRotation(HeadFaceLocal, HeadUpLocal));

        // The head's world rotation that faces `face` with its crown along `up` (no roll).
        private static Quaternion HeadRotationFor(Vector3 face, Vector3 up) => Quaternion.LookRotation(face, up) * HeadRigInv;

        // Yaw (from bodyF, + = his right), pitch (+ = down) and roll (+ = crown to his right) of a
        // head rotation, read through the rig's axes.
        internal static void HeadAngles(Quaternion rot, Vector3 bodyF, out float yaw, out float pitch, out float roll)
        {
            Vector3 f = rot * HeadFaceLocal, u = rot * HeadUpLocal;
            Vector3 flat = f; flat.y = 0f;
            yaw = flat.sqrMagnitude > 1e-6f && bodyF.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(bodyF, flat, Vector3.up) : 0f;
            pitch = -Mathf.Asin(Mathf.Clamp(f.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 upright = Vector3.ProjectOnPlane(Vector3.up, f);
            Vector3 crown = Vector3.ProjectOnPlane(u, f);
            roll = upright.sqrMagnitude > 1e-6f && crown.sqrMagnitude > 1e-6f ? -Vector3.SignedAngle(upright, crown, f) : 0f;
        }

        // Once per bot: the rig's axes against his animated head. Every EFT body shares the one
        // skeleton, so this should always pass; if it doesn't (a modded body with another rig), the
        // look is switched off for him rather than turning a head about the wrong axes.
        private static bool RigCheck(Player pl, SurrenderState s, Transform head)
        {
            PresenceState p = s.Look;
            if (p.RigChecked) return p.RigOk;
            Vector3 bf = pl.Transform.Original.forward; bf.y = 0f;
            if (bf.sqrMagnitude < 1e-4f) return false;
            bf.Normalize();
            HeadAngles(head.rotation, bf, out float y, out float pt, out float r);
            Vector3 u = head.rotation * HeadUpLocal;
            bool ok = Mathf.Abs(y) < 75f && Mathf.Abs(pt) < 60f && u.y > 0.3f;
            if (!ok && ++p.RigBad < 30) return false;   // one odd frame (a flinch, a stumble) is not the rig
            p.RigChecked = true;
            p.RigOk = ok;
            float fl = pl.HeadRotation.y;
            HandsAreHighPlugin.Log.LogInfo(ok
                ? $"{s.Name}: head rig checked: face = bone +Y, crown = bone -X; animated head {y:+0;-0;0} deg yaw, {pt:+0;-0;0} pitch, {r:+0;-0;0} roll from his body (game free-look {fl:+0;-0;0} deg); the look sets the head upright from here"
                : $"{s.Name}: head rig doesn't match (animated head {y:+0;-0;0} yaw, {pt:+0;-0;0} pitch, crown up {u.y:0.00}); head look OFF for him (no guessing the axes)");
            return ok;
        }

        // Neck and head (0.18.0): the head is set to an absolute target (the smoothed yaw/pitch from
        // his planted body, crown upright: no roll), blended in by W; the turn from the animated
        // head is shared 40% neck, 60% head. Whatever the animator or the game's free-look left in
        // the head (a turn, a tilt) is replaced, not added to. A flinch ducks the head and turns it
        // away from the side the round passed. HeadDelta (the turn applied) is what the hands
        // behind the head follow.
        internal static void ApplyHead(Player pl, SurrenderState s)
        {
            PresenceState p = s.Look;
            PlayerBones b = pl.PlayerBones;
            Transform head = b?.Head?.Original;
            if (head == null) return;
            float fl = Flinch(s);
            if (p.W <= 0f && fl <= 0f) return;
            if (!RigCheck(pl, s, head)) return;
            Vector3 bodyF = BodyFwd(pl, s);
            if (bodyF.sqrMagnitude < 1e-4f) return;
            Quaternion anim = head.rotation;
            Vector3 face = Quaternion.AngleAxis(p.Yaw, Vector3.up) * bodyF;
            Vector3 side = Vector3.Cross(Vector3.up, face);
            face = Quaternion.AngleAxis(p.Pitch, side) * face;
            Quaternion want = Quaternion.Slerp(anim, HeadRotationFor(face, Vector3.up), p.W);
            if (fl > 0.001f)
            {
                Vector3 wf = want * HeadFaceLocal; wf.y = 0f;
                Vector3 wr = wf.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, wf.normalized) : side;
                want = Quaternion.AngleAxis(-fl * 10f * p.FlinchSide, Vector3.up) * Quaternion.AngleAxis(fl * 16f, wr) * want;
            }
            Quaternion q = want * Quaternion.Inverse(anim);
            q.ToAngleAxis(out float ang, out Vector3 ax);
            if (ang > 180f) ang -= 360f;
            if (Mathf.Abs(ang) > 110f) q = Quaternion.AngleAxis(Mathf.Sign(ang) * 110f, ax);   // safety net only
            Transform neck = b.Neck;
            if (neck != null && head.IsChildOf(neck) && neck != head)
            {
                Quaternion qn = Quaternion.Slerp(Quaternion.identity, q, 0.4f);
                neck.rotation = qn * neck.rotation;
            }
            head.rotation = q * anim;
            p.HeadDelta = q;
            p.HeadFinal = head.rotation;
            p.HeadFrame = Time.frameCount;
            if (Settings.LogPose.Value || TestDiag) GazeDiag(pl, s, anim, bodyF);
        }

        // 14. Debug "Log pose diagnostics": once a second, standing or kneeling: where the animated
        // head pointed before us, the game's free-look, where we left it (yaw/pitch/ROLL), the chest
        // against the hips, and his captor's bearing. Head roll after us should read ~0.
        private static void GazeDiag(Player pl, SurrenderState s, Quaternion anim, Vector3 bodyF)
        {
            PresenceState p = s.Look;
            float now = Time.time;
            if (now < p.DiagAt) return;
            p.DiagAt = now + 1f;
            Transform head = pl.PlayerBones?.Head?.Original;
            if (head == null) return;
            HeadAngles(anim, bodyF, out float ay, out float ap, out float ar);
            HeadAngles(head.rotation, bodyF, out float hy, out float hp, out float hr);
            float chest = 0f;
            if (ArmPose.TorsoFrame(pl, pl.PlayerBones, out _, out _, out Vector3 tf)) { tf.y = 0f; if (tf.sqrMagnitude > 1e-4f) chest = Vector3.SignedAngle(bodyF, tf, Vector3.up); }
            string captor = "none";
            if (s.Captor != null)
            {
                Vector3 d = s.Captor.Position - pl.Position; d.y = 0f;
                if (d.sqrMagnitude > 0.01f) captor = $"{Vector3.SignedAngle(bodyF, d, Vector3.up):+0;-0;0} deg, {d.magnitude:0.0} m";
            }
            MovementContext mc = pl.MovementContext;
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: gaze: {(Kneel.Active(s) ? "kneeling" : "standing")}{(s.HandsOnHead ? ", hands on head" : "")}; captor at {captor}; " +
                $"animated head yaw {ay:+0;-0;0} pitch {ap:+0;-0;0} roll {ar:+0;-0;0} (game free-look {pl.HeadRotation.y:+0;-0;0}, look-to-body {(mc != null ? mc.HandsToBodyAngle : 0f):+0;-0;0}); " +
                $"after: head yaw {hy:+0;-0;0} pitch {hp:+0;-0;0} roll {hr:+0;-0;0}, target yaw {p.Yaw:+0;-0;0} pitch {p.Pitch:+0;-0;0}; chest {chest:+0;-0;0} from body (our torso turn {p.Assist:+0;-0;0}); attention {p.Glance}{(Neutral ? " [neutral test]" : "")}");
        }

        // 0.18.0: the skull's own frame as the look left it this frame (false: not posed this frame),
        // for the hands on the head: they sit on the head wherever it points, never the reverse.
        internal static bool SkullFrame(SurrenderState s, out Vector3 fwd, out Vector3 up, out Vector3 right)
        {
            fwd = up = right = Vector3.zero;
            if (s.Look.HeadFrame != Time.frameCount) return false;
            Quaternion r = s.Look.HeadFinal;
            fwd = r * HeadFaceLocal;
            up = r * HeadUpLocal;
            right = Vector3.Cross(up, fwd).normalized;
            return right.sqrMagnitude > 0.5f;
        }

        // This frame's head turn, for the kneel's hands (identity if the head wasn't turned).
        internal static Quaternion HeadDelta(SurrenderState s) =>
            s.Look.HeadFrame == Time.frameCount ? s.Look.HeadDelta : Quaternion.identity;
    }
}
