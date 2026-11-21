using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // What a bot carries between surrenders in one raid: his nerve never re-rolls.
    internal sealed class BotHistory
    {
        internal float Nerve;
        internal float Reaction;                // s, his own reaction time, rolled once per raid
        internal float WiggleCooldownUntil;     // kept across recaptures
        internal float CooldownUntil;
        internal float NextEval;
        internal int SquadPeak;
        internal int SquadSeen;
        internal float SquadLossTime = -1000f;

        // Incoming fire (Suppression.cs): shots at him, faded by half-life from FireAt.
        internal float Fire;
        internal float FireAt;
        internal float NearMissAt = -1000f;
        internal Vector3 FireFrom;              // roughly where the last near miss came from
        internal float FireFromAt = -1000f;
        internal float ShotAt = -1000f;         // he last fired (heard by those near)

        // Stop demand (StopDemand.cs): who demanded, until when it weighs, and the cooldown.
        internal Player DemandBy;
        internal float DemandUntil;
        internal float DemandCooldownUntil;
        internal float PromptAt = -1000f;     // last time a hit / near miss brought a check forward

        // Cached kit assessment (Arsenal.AssessKit), refreshed every KitRecheck seconds.
        internal float KitAt = -1000f;
        internal float KitValue = -1f;     // handbook roubles; -1 = no handbook, neutral
        internal float Protection;         // best armour class worn / 6

        internal float DefiantCooldownUntil;    // middle finger, kept across recaptures

        // As an observer of other men's surrenders (Restraint.cs): his own disposition, rolled
        // once per raid, and the men he saw go for a weapon after giving up.
        internal float Mercy;
        internal HashSet<string> Distrust;

        // Stable per raid, resolved once instead of on every check.
        internal NaturalSurrender.RoleClass Role = (NaturalSurrender.RoleClass)(-1);
        internal bool PersonalityRead;
        internal string Personality;
        internal object SainComponent;          // SAIN BotComponent (reflection), cached
        internal float SainLookupAt = -1000f;

        // 0.21.0: incoming fire (FireBreak.cs). Until then he can't be taken or give up again (the
        // fire that broke his last hold-up is still on); every new near miss or hit extends it.
        // FireBy: who fired the last near miss or hit he took, when known (Suppression.Record,
        // BotMemory.SetUnderFire's source).
        internal float FireBreakUntil = -1000f;
        // ...and, as the captor in such a hold-up, until then he takes nobody (he may still give up
        // himself: that is a new encounter, decided by his own pressure).
        internal float NoCustodyUntil = -1000f;
        internal Player FireBy;
        internal float FireByAt = -1000f;
    }

    // Who is surrendered, keyed by the bot's Player so the layer (BotOwner) and the arm
    // pose (Player) can both find the state. Everything here is raid-scoped: a new
    // GameWorld clears it, and so does hot-reload teardown.
    internal static class SurrenderRegistry
    {
        private static readonly Dictionary<Player, SurrenderState> States = new Dictionary<Player, SurrenderState>();
        private static readonly List<Player> Finished = new List<Player>();
        private static readonly Dictionary<string, BotHistory> History = new Dictionary<string, BotHistory>();
        // Same entries keyed by the Player object: a reference hash instead of hashing the
        // ProfileId string on every per-frame lookup. Both cleared per raid.
        private static readonly Dictionary<Player, BotHistory> ByPlayer = new Dictionary<Player, BotHistory>();
        private static GameWorld _world;

        internal static bool Any => States.Count > 0;
        internal static int Count => States.Count;
        internal static int HistoryCount => History.Count;
        internal static int TrackedPlayers => ByPlayer.Count;   // live Player keys (pruned every 30 s)

        // The concrete collection: foreach over it uses a struct enumerator (no allocation),
        // unlike IEnumerable<T>.
        internal static Dictionary<Player, SurrenderState>.ValueCollection All => States.Values;

        // Is there a raid at all? Menus have no GameWorld; the hideout has its own world type
        // and no bots to surrender.
        internal static bool InRaid
        {
            get
            {
                GameWorld w = Singleton<GameWorld>.Instance;
                return w != null && !(w is HideoutGameWorld);
            }
        }

        internal static bool TryGet(Player bot, out SurrenderState state) => States.TryGetValue(bot, out state);

        // The layer keeps the bot until he's rearmed and his arms are fully down.
        internal static bool HoldsBot(Player bot) => bot != null && States.ContainsKey(bot);

        // New raid (or back in the menu): forget everything from the last one.
        internal static void EnsureRaid()
        {
            GameWorld world = Singleton<GameWorld>.Instance;
            if (world == _world) return;
            SurrenderStats.EndRaid();   // 0.19.0: the last raid's natural-surrender summary
            if (States.Count > 0 || History.Count > 0)
                HandsAreHighPlugin.Log.LogInfo("New raid: surrender state cleared");
            _world = world;
            Scenarios.ForgetRaid();
            Restraint.Clear();
            Captors.Clear();
            CaptorVoice.Clear();
            Voice.ClearChecks();
            Compliance.Clear();
            Judgement.Clear();
            ScenarioStaging.Clear();
            OrbitAdapter.ReleaseAll();
            Runtime.Menu.Reset();   // nothing bound to the old raid's bots, no key blocked
            FireBreak.Clear();      // 0.21.0: captors' hit hooks, queued fire
            ReleaseWatch.Clear();
            Escape.Clear();         // 0.24.0
            WeaponState.Clear();    // 0.24.0
            UnhookAll();
            States.Clear();
            Finished.Clear();
            History.Clear();
            ByPlayer.Clear();
            Arsenal.ClearCache();
            // First-use costs (assembly scan for SAIN, Unity layer lookups) paid now, at raid
            // start, not in the frame of the first surrender check.
            if (world != null) HandsAreHighPlugin.Try("prewarm", SainAdapter.Prewarm);
            if (world != null) HandsAreHighPlugin.Try("prewarm orbit", OrbitAdapter.Prewarm);
            if (world != null) HandsAreHighPlugin.Try("prewarm orbit objectives", OrbitObjectives.Prewarm);
            if (world != null) HandsAreHighPlugin.Try("prewarm looting bots", () => { bool _ = LootingBotsAdapter.Present; });
        }

        internal static BotHistory HistoryOf(Player bot)
        {
            if (ByPlayer.TryGetValue(bot, out BotHistory h)) return h;
            string id = bot.ProfileId;
            if (!History.TryGetValue(id, out h))
            {
                h = new BotHistory
                {
                    Nerve = Settings.RollNerve(),
                    Reaction = Settings.Between(Settings.ReactionMin, Settings.ReactionMax),
                    NextEval = Time.time + Dice.Value * Settings.EvalInterval.Value
                };
                History[id] = h;
            }
            ByPlayer[bot] = h;
            return h;
        }

        // Every 30 s (NaturalSurrender.Tick): drop the Player keys of bots that died or
        // despawned, so destroyed players aren't kept referenced for the rest of the raid.
        private static readonly List<Player> Gone = new List<Player>();
        internal static void PruneGone()
        {
            Gone.Clear();
            foreach (KeyValuePair<Player, BotHistory> kv in ByPlayer)
                if (kv.Key == null || !Perception.Alive(kv.Key)) Gone.Add(kv.Key);
            for (int i = 0; i < Gone.Count; i++)
            {
                if (States.ContainsKey(Gone[i])) continue;   // Tick handles held bots
                Forget(Gone[i]);
            }
            Gone.Clear();
            Restraint.PruneObservers();
        }

        // Dead or despawned bots: their history stays (bounded per raid, a few hundred small
        // objects at most) but the Player key and the references it holds are dropped.
        internal static void Forget(Player bot)
        {
            // Unity's == null is true for a destroyed Player, but the C# key is still there.
            if ((object)bot == null || !ByPlayer.TryGetValue(bot, out BotHistory h)) return;
            h.DemandBy = null;
            h.SainComponent = null;
            h.FireBy = null;
            ByPlayer.Remove(bot);
        }

        // knownAt: where he knows his threat to be (NaturalSurrender passes the EnemyInfo's last
        // sighting); null when the captor is right there (debug keys, being caught again).
        // decideNow false (debug scenarios, forced custody only): a bot captor's decision is not
        // asked here; the caller gives custody through Captors.ForceAccept straight after.
        internal static void Surrender(Player bot, Player captor, string reason, Vector3? knownAt = null, bool decideNow = true,
            SurrenderSource source = SurrenderSource.Natural)
        {
            if (bot == null || captor == null || !Settings.Enabled.Value) return;
            EnsureRaid();

            // 0.21.0: the fire that broke his last hold-up (or that he or this captor is under) is
            // still on: nobody gives up or is taken in the middle of it. Debug keys and scenario
            // setups are deliberate and pass.
            if ((source == SurrenderSource.Natural || source == SurrenderSource.Demand || source == SurrenderSource.Recapture) && FireBreak.Blocks(bot))
            {
                if (Settings.LogFireBreak.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{Scenarios.Label(bot)}: no surrender to {Restraint.Name(captor)} ({reason}): he is still under the fire that broke his hold-up");
                return;
            }

            // 0.23.0: no recapture loop: not to the bot (or squad) that just let him go or walked away.
            if ((source == SurrenderSource.Natural || source == SurrenderSource.Demand || source == SurrenderSource.Recapture) && Encounter.RecentlyFreedFrom(bot, captor, out string freed))
            {
                if (Settings.LogEncounter.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{Scenarios.Label(bot)}: no surrender to {Restraint.Name(captor)} ({reason}): {freed}");
                return;
            }

            if (States.TryGetValue(bot, out SurrenderState existing))
            {
                // Mid-draw: a hands change on top of the pending draw is the busy-hands glitch.
                // He's armed in a moment; the normal AI has him then.
                if (existing.DrawPending)
                {
                    HandsAreHighPlugin.Log.LogInfo($"{existing.Name}: not surrendering again ({reason}): he's drawing");
                    return;
                }
                // Found again while retreating or hiding: hands go back up. Stopped mid-scramble,
                // he won't try that again straight away (no flip-flopping).
                if (existing.Scrambling)
                    existing.ScrambleCooldownUntil = Time.time + Settings.ScrambleCooldown.Value;
                Captors.EndCustody(existing, "caught again by someone else");
                existing.Captor = captor;
                existing.RaiseArms();
                existing.Order = Order.None;
                existing.TargetWeapon = null;
                existing.CaptorHiddenSince = -1f;
                existing.CaptorLastSeen = knownAt ?? captor.Position;
                existing.WasHit = false;
                existing.AimedBy = null;
                existing.DebugOpeningUntil = 0f;
                existing.FaceSet = false;
                existing.ResetEncounter();
                existing.Source = source;
                existing.CountedAccept = false;
                SurrenderStats.Start(existing);
                AcknowledgeIfClose(existing, captor);
                existing.SetPhase(Phase.HandsUp, reason);
                if (existing.Moving) { bot.AIData?.BotOwner?.StopMove(); existing.Moving = false; }
                StartDrop(existing);
                Voice.Say(existing, VoiceEvent.Surrender);
                Voice.ScheduleChatter(existing);
                Presence.NewEncounter(existing);
                if (captor.IsAI && decideNow) Captors.OnSurrender(existing, captor, reason);
                return;
            }

            BotHistory h = HistoryOf(bot);
            var s = new SurrenderState
            {
                Bot = bot,
                Name = Scenarios.Label(bot),
                Captor = captor,
                Seed = Dice.Value * 100f,
                RolledNerve = h.Nerve,
                CaptorLastSeen = knownAt ?? captor.Position,
                DropPosition = bot.Position,
                LastPosition = bot.Position,
                Reaction = h.Reaction,
                Source = source
            };
            s.Personality = PersonalityOf(bot, h);
            SainAdapter.Leanings(s.Personality, out s.Fight, out s.Flee);
            // Hits only: bleeding lowers health too, and must not count as being shot.
            s.HitHandler = (info, part, absorbed) =>
            {
                s.WasHit = true;
                try { s.LastHitBy = Perception.PlayerOf(info.Player?.iPlayer); s.LastHitAt = Time.time; } catch { }
                // A man shot while escaping from his knees runs: hits are never reconsidered
                // (ReassessEscape would otherwise call off the bolt if his captor covered him).
                s.KneelEscape = false;
                Presence.OnHit(s);   // a hit is not a flinch: the layer's hit response owns him
                try { Restraint.OnPrisonerHit(s, info); } catch (System.Exception e) { HandsAreHighPlugin.Report("prisoner hit", e); }
                // 0.21.0: a hit from outside the hold-up breaks it (queued; FireBreak.Tick acts).
                try { FireBreak.OnHit(s, bot, false, info); } catch (System.Exception e) { HandsAreHighPlugin.Report("prisoner hit (fire break)", e); }
            };
            bot.BeingHitAction += s.HitHandler;
            // Death or despawn (Player.OnDead / unspawn raise it): everything of ours on him
            // stops that moment, before the ragdoll takes over; Tick removes the state.
            s.DeathHandler = p => OnDeadOrGone(s);
            bot.OnPlayerDeadOrUnspawn += s.DeathHandler;
            States[bot] = s;
            SurrenderStats.Start(s);
            s.OfferUntil = Time.time + Settings.Between(Settings.OfferMin, Settings.OfferMax) * (1.2f - 0.4f * s.Nerve);
            AcknowledgeIfClose(s, captor);
            HandsAreHighPlugin.Log.LogInfo(
                $"Surrender: {s.Name} ({bot.Profile?.Info?.Settings?.Role}), nerve {s.Nerve:0.00}, reaction {s.Reaction:0.00} s" +
                $"{(string.IsNullOrEmpty(s.Personality) ? "" : ", " + s.Personality)}, voice {bot.Speaker?.PlayerVoice}, to {captor.Profile?.Nickname} {Vector3.Distance(captor.Position, bot.Position):0} m " +
                $"({(s.Accepted ? "close, acknowledged" : $"unanswered for now, lasts {s.OfferUntil - Time.time:0} s")}): {reason}");
            s.SetPhase(Phase.HandsUp, reason);

            BotOwner owner = bot.AIData?.BotOwner;
            HandsAreHighPlugin.Try("stop shooting", () => owner?.ShootData?.EndShoot());
            HandsAreHighPlugin.Try("stop sprinting", () => owner?.Sprint(false));
            StartDrop(s);
            Voice.Say(s, VoiceEvent.Surrender);
            Voice.ScheduleChatter(s);
            Presence.NewEncounter(s);
            // A captor who gives up loses his own prisoner (no capture loops); then the bot he
            // gave up to decides about him.
            Captors.EndFor(bot, "surrendered himself");
            if (captor.IsAI && decideNow) Captors.OnSurrender(s, captor, reason);
        }

        // A human captor close and in sight has his surrender already (as before 0.9.0); far away,
        // or a bot captor (custody decides), it starts as an offer. Logged in the surrender line.
        private static void AcknowledgeIfClose(SurrenderState s, Player captor)
        {
            if (captor == null || captor.IsAI) return;
            float d = Vector3.Distance(captor.Position, s.Bot.Position);
            if (d <= Settings.AcknowledgeDistance.Value && Perception.LineClear(Perception.Eye(s.Bot), Perception.Eye(captor)))
            {
                s.Accepted = true;
                SurrenderStats.Acknowledged(s, botCustody: false);
            }
        }

        // Personality never changes in a raid: one reflection call per bot, not per check.
        internal static string PersonalityOf(Player bot, BotHistory h)
        {
            if (!h.PersonalityRead)
            {
                h.PersonalityRead = true;
                h.Personality = SainAdapter.Personality(bot.AIData?.BotOwner);
            }
            return h.Personality;
        }

        // The game's death/unspawn event for a held bot. No bone, collider, rigidbody or
        // corpse is touched: the pose stops because the state is Dead (and because the game
        // itself stops calling VisualPass for the dead), the lean and gestures are dropped
        // without writing to him, speech and claims are cleared.
        private static void OnDeadOrGone(SurrenderState s)
        {
            if (s.Dead) return;
            s.Dead = true;
            s.Weight = 0f;
            s.Gesture = GestureKind.None;
            s.GestureArm = 0f;
            s.WigglePendingUntil = -1f;
            s.DefiantPendingUntil = -1f;
            s.PendingVoice = null;
            s.VerifyAt = -1f;
            s.VerifyClip = null;
            s.TargetWeapon = null;
            s.DropQueue.Clear();
            // The kneel stops here too: no stand-up on a corpse, no bone written after death
            // (the pose patch checks Dead), the ragdoll starts from whatever pose he died in.
            s.KneelTestLeft = 0;
            s.KneelTestArmed = false;
        }

        // Empties his hands at once: a gun goes on the ground, a knife goes back in its sheath,
        // anything else (meds, a grenade) is put away. The game's callback (hands empty) is what
        // lets the arms rise, the same frame. Timeline logged when the arms are fully up (Tick).
        internal static void StartDrop(SurrenderState s)
        {
            s.TStart = Time.realtimeSinceStartup;
            s.TDropRequested = s.THandsEmpty = s.TThrown = s.TInterrupt = -1f;
            s.HandsAtStart = Arsenal.DescribeHands(s.Bot);
            s.TimingLogged = false;
            s.DropAttempts = 0;
            s.DropSince = Time.time;
            s.HandsPending = false;
            s.DropPending = !Arsenal.HandsEmpty(s.Bot);
            if (!s.DropPending) { s.THandsEmpty = s.TStart; return; }
            RequestDrop(s);
        }

        // Is this callback still about the request that is current for this bot?
        private static bool Current(SurrenderState s, int token) =>
            !s.Dead && s.Bot != null && States.TryGetValue(s.Bot, out SurrenderState now) && now == s && s.HandsToken == token;

        // One request at a time: never while the previous one is unresolved (CheckDrop decides
        // when an unanswered one is given up). A magazine reload in progress holds the hide
        // until it ends (BaseReloadOperation.HideWeapon only stores the callback), so it is
        // finished instantly with the game's own FastForwardCurrentState, which then runs
        // the stored hide (ReloadExternalMagOperation.SwitchToIdlingState).
        internal static void RequestDrop(SurrenderState s)
        {
            if (s.HandsPending || s.Bot == null) return;
            int token = ++s.HandsToken;
            s.HandsPending = true;
            s.HandsSince = Time.time;
            s.DropAttempts++;
            if (s.TDropRequested < 0f) s.TDropRequested = Time.realtimeSinceStartup;

            HandsAreHighPlugin.Try("drop weapon in hands", () =>
            {
                Item dropped = Arsenal.EmptyHands(s.Bot,
                    onEmpty: () =>
                    {
                        if (!Current(s, token)) return;
                        s.HandsPending = false;
                        if (s.THandsEmpty < 0f) s.THandsEmpty = Time.realtimeSinceStartup;
                        s.DropPending = false;
                    },
                    onFailed: () =>
                    {
                        if (!Current(s, token)) return;
                        s.HandsPending = false;
                        HandsAreHighPlugin.Log.LogInfo($"{s.Name}: the game refused the hands change ({Arsenal.DescribeHands(s.Bot)})");
                    },
                    onThrown: ok =>
                    {
                        if (ok) { s.TThrown = Time.realtimeSinceStartup; if (s.DroppedWeapon != null) Compliance.NoteDropped(s, s.DroppedWeapon); }
                        else HandsAreHighPlugin.Log.LogWarning($"{s.Name}: the game refused to throw his gun; it went back on his body");
                    });
                if (dropped != null)
                {
                    s.DroppedWeapon = dropped;
                    s.DropPosition = s.Bot.Position;
                    s.GunGone = false;
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: drops {dropped.ShortName.Localized()} (hands were: {s.HandsAtStart}, request {s.DropAttempts})");
                }
                CutReload(s);
            });
        }

        // The draw callback (Arsenal.Draw): the new hands controller is ready to operate. Only
        // for the request that is still current for a bot still held.
        internal static void OnDrawn(SurrenderState s, int token, bool ok)
        {
            if (s == null || s.Dead || s.Bot == null || !States.TryGetValue(s.Bot, out SurrenderState now) || now != s) return;
            if (s.DrawToken != token || !s.DrawPending) return;
            s.DrawPending = false;
            s.TDrawReady = Time.realtimeSinceStartup;
            if (!ok) HandsAreHighPlugin.Log.LogInfo($"{s.Name}: the game didn't complete the draw ({Arsenal.DescribeHands(s.Bot)}); the normal AI takes it from here");
        }

        // Rearmed: handed on once his arms are down AND the weapon he went for is ready (or the
        // game answered the draw at all). A draw that never answers is given up on after the
        // hand-off limit; its late callback is then ignored (token).
        private static bool DrawSettled(SurrenderState s)
        {
            if (!s.DrawPending) return true;
            if (Time.time - s.DrawSince < Settings.DrawHandoffTimeout.Value) return false;
            s.DrawPending = false;
            s.DrawToken++;
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: no answer to the draw in {Settings.DrawHandoffTimeout.Value:0.0} s ({Arsenal.DescribeHands(s.Bot)}), handing him to the normal AI anyway");
            return true;
        }

        // 0.21.0: no hands request of ours still open at a hand-back without a draw: an unanswered
        // drop gets its usual limit (twice DropRetrySeconds), then is abandoned (its callback is
        // ignored by token), exactly as CheckDrop gives one up.
        private static bool HandsSettled(SurrenderState s)
        {
            if (!s.HandsPending && !s.DrawPending) return true;
            float now = Time.time;
            if (s.HandsPending && now - s.HandsSince < Mathf.Max(1f, 2f * Settings.DropRetrySeconds.Value)) return false;
            if (s.DrawPending && now - s.DrawSince < Settings.DrawHandoffTimeout.Value) return false;
            if (s.HandsPending)
            {
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: no answer to the hands change ({Arsenal.DescribeHands(s.Bot)}); given up before the hand-back");
                AbandonRequest(s);
            }
            if (s.DrawPending) { s.DrawPending = false; s.DrawToken++; }
            return true;
        }

        private static void LogDrawTimeline(SurrenderState s)
        {
            if (s.TDrawAsked < 0f || s.TDecided < 0f) return;
            float t0 = s.TDecided;
            string Rel(float t) => t < 0f ? "n/a" : $"+{(t - t0) * 1000f:0} ms";
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: draw timeline ({Arsenal.SlotName(s.DrawSlot)}{(s.DrawSlot == EquipmentSlot.Scabbard ? " (knife)" : "")}" +
                $"{(s.DrawQuick ? ", game quickdraw" : "")}): decided +0 ms, draw asked {Rel(s.TDrawAsked)}, " +
                $"arms down {Rel(s.TArmsDown)} (lower {Settings.DrawLowerSeconds.Value:0.00} s, overlaps the draw), " +
                $"weapon ready {Rel(s.TDrawReady)} (game draw animation), handed to the AI {Rel(Time.realtimeSinceStartup)}");
            s.TDecided = -1f;
            s.TDrawAsked = -1f;
        }

        internal static void CutReload(SurrenderState s)
        {
            if (!Settings.FinishReloadInstantly.Value || s.TInterrupt >= 0f) return;
            if (!(s.Bot?.HandsController is Player.FirearmController fc) || !fc.IsInReloadOperation()) return;
            s.TInterrupt = Time.realtimeSinceStartup;
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: mid-reload: the reload is finished instantly so the gun can drop now");
            HandsAreHighPlugin.Try("finish reload", () => s.Bot.HandsController.FastForwardCurrentState());
        }

        // CheckDrop: the outstanding request got no answer in time. Forget it (its callback
        // will be ignored) so a fresh one can be made.
        internal static void AbandonRequest(SurrenderState s)
        {
            s.HandsToken++;
            s.HandsPending = false;
        }

        // Debug keys: one bot only, resolved by the caller from the crosshair.
        internal static string DebugLeave(SurrenderState s)
        {
            if (s.Phase != Phase.HandsUp) return $"not holding his hands up ({s.Phase}), nothing to leave";
            s.ForcedLeave = true;
            return "acts as if you left";
        }

        internal static string DebugOpening(SurrenderState s)
        {
            if (s.Phase != Phase.HandsUp) return $"not holding his hands up ({s.Phase}), no opening to give";
            float secs = Settings.DebugOpeningSeconds.Value;
            s.DebugOpeningUntil = Time.time + secs;
            // Noticed like a clear opening (his reaction time), then his own rolls.
            s.LastCovered = true;
            s.DecideAt = -1f;
            s.ComplyUntil = 0f;
            return $"not covered for {secs:0} s (you can keep aiming, it doesn't count); his own rolls decide";
        }

        internal static string DebugForceRun(SurrenderState s)
        {
            if (s.Phase != Phase.HandsUp) return $"not holding his hands up ({s.Phase})";
            s.ForceRun = true;
            return "runs now (forced, no roll)";
        }

        internal static string DebugForceGrab(SurrenderState s)
        {
            if (s.Phase != Phase.HandsUp) return $"not holding his hands up ({s.Phase})";
            if (s.DroppedWeapon == null) return "didn't drop a gun";
            s.ForceGrab = true;
            return "goes for his gun now (forced, no roll); the log says if he can't";
        }

        // A held bot was shot or killed: others with their hands up who can see it run.
        internal static void Witness(Vector3 at, Player victim, string what)
        {
            if (!Settings.LocalReactions.Value) return;
            float r = Settings.WitnessRadius.Value;
            foreach (SurrenderState s in States.Values)
            {
                if (s.Bot == null || s.Bot == victim || s.Phase != Phase.HandsUp || s.Suspended) continue;
                if ((s.Bot.Position - at).sqrMagnitude > r * r) continue;
                if (!Perception.LineClear(Perception.Eye(s.Bot), at + Vector3.up * 1.2f)) continue;
                float roll = Dice.Value;
                if (roll >= Settings.WitnessEscape.Value) continue;
                s.BeginRetreat($"saw {what}", emergency: true);
            }
        }

        // Called once a frame: sleep/wake, arm blending, deaths and cleanup.
        // Behaviour itself lives in SurrenderLogic.
        internal static void Tick(float dt)
        {
            EnsureRaid();
            // 0.21.0: bots handed back are followed in the log for a while (ReleaseWatch), and
            // queued incoming fire is acted on (FireBreak) before anything reads the phases.
            ReleaseWatch.Tick();
            Escape.Tick();          // 0.24.0: the capture remembered after a release, keep-away legs
            if (States.Count == 0) { FireBreak.Idle(); return; }
            FireBreak.Tick();
            Finished.Clear();
            bool enabled = Settings.Enabled.Value;

            foreach (KeyValuePair<Player, SurrenderState> kv in States)
            {
                Player bot = kv.Key;
                SurrenderState s = kv.Value;
                if (s.Dead || bot == null || bot.HealthController == null || !bot.HealthController.IsAlive)
                {
                    if (s.Phase == Phase.HandsUp)
                    {
                        // 0.16.0: who killed him, and whether that was a decision or a leak.
                        HandsAreHighPlugin.Log.LogInfo($"{s.Name}: killed with his hands up{Judgement.DeathReport(s)}");
                        Witness(s.LastPosition, bot, "a surrendered man killed");
                    }
                    Finished.Add(bot);
                    continue;
                }
                s.LastPosition = bot.Position;

                if (!enabled && s.Phase != Phase.Rearmed)
                {
                    s.LowerArms(Settings.LowerSeconds.Value);
                    s.SetPhase(Phase.Rearmed, "mod disabled");
                }

                UpdateSuspension(bot, s);
                // try/catch inline: a lambda here would allocate a closure per bot per frame.
                try { Voice.Tick(s); } catch (System.Exception e) { HandsAreHighPlugin.Report("voice", e); }
                if (s.ArmedPending != null && (s.Phase == Phase.HandsUp || s.Phase == Phase.Retreating || s.Phase == Phase.Hiding || s.Phase == Phase.Regrouping || s.Phase == Phase.Released)) s.ArmedPending = null;
                if (!s.Dead && s.ArmedPending != null && (Reactions.Shows(s) != null || Time.time - s.ArmedPendingSince > 1.5f))
                {
                    string what = s.ArmedPending;
                    s.ArmedPending = null;
                    if (Settings.LogDecisions.Value || Settings.LogRestraint.Value)
                        HandsAreHighPlugin.Log.LogInfo($"{s.Name}: his resistance shows ({Reactions.Shows(s) ?? "time"}) {Time.time - s.ArmedPendingSince:0.00} s after he decided; the bots around him react to it now");
                    try { Restraint.OnArmed(s, what); } catch (System.Exception e) { HandsAreHighPlugin.Report("restraint armed", e); }
                }
                try { Kneel.Update(s, dt); } catch (System.Exception e) { HandsAreHighPlugin.Report("kneel", e); }
                try { HandsPose.Update(s, dt); Facing.Update(s, dt); } catch (System.Exception e) { HandsAreHighPlugin.Report("hands and facing", e); }
                try { Presence.Update(s, dt); } catch (System.Exception e) { HandsAreHighPlugin.Report("presence", e); }
                try { Gestures.Update(s); } catch (System.Exception e) { HandsAreHighPlugin.Report("gestures", e); }

                if (s.ArmsUp)
                {
                    // Raise only once the hands are empty, so he never poses holding a gun.
                    if (!s.DropPending)
                        s.Progress += dt / Mathf.Max(0.01f, Settings.RaiseSeconds.Value);
                }
                else
                {
                    float lower = s.LowerSeconds > 0f ? s.LowerSeconds : Settings.LowerSeconds.Value;
                    // From behind his head the hands have further to go: the emergency 0.08 s
                    // whipped them past his head in ~5 frames. Kneel.MinLowerSeconds while kneeling.
                    if (Kneel.Active(s)) lower = Mathf.Max(lower, Kneel.MinLowerSeconds);
                    s.Progress -= dt / Mathf.Max(0.01f, lower);
                }
                s.Progress = Mathf.Clamp01(s.Progress);
                s.Weight = Mathf.SmoothStep(0f, 1f, s.Progress);

                if (s.ArmsUp && s.Progress >= 1f && !s.TimingLogged && s.TStart > 0f)
                {
                    s.TimingLogged = true;
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: hands up. Timeline from surrender (hands were: {s.HandsAtStart}): " +
                        $"drop asked {Ms(s, s.TDropRequested)}, reload cut {Ms(s, s.TInterrupt)}, " +
                        $"hands empty {Ms(s, s.THandsEmpty)} (game hide animation), gun on the ground {Ms(s, s.TThrown)}, " +
                        $"arms up {Ms(s, Time.realtimeSinceStartup)} (raise {Settings.RaiseSeconds.Value:0.00} s), requests {s.DropAttempts}");
                }

                // Handed back only once he is off his knees: the AI never gets a kneeling body.
                if (s.Phase == Phase.Rearmed && s.Progress <= 0f && !Kneel.Posing(s))
                {
                    if (s.TArmsDown < 0f && s.TDrawAsked >= 0f) s.TArmsDown = Time.realtimeSinceStartup;
                    if (DrawSettled(s)) Finished.Add(bot);
                }
                // 0.21.0: free without a draw (let go and away, in cover, no gun to get, under fire):
                // the same hand-back once his arms are down, his legs are his own and no hands
                // request of ours is still open (a drop answered, or given up on after its limit), so
                // the AI never inherits a pending hands change (the busy-hands glitch).
                else if (s.Phase == Phase.Released && s.Progress <= 0f && !Kneel.Posing(s) && HandsSettled(s))
                    Finished.Add(bot);
            }

            foreach (Player p in Finished)
            {
                if (!States.TryGetValue(p, out SurrenderState s)) continue;
                bool alive = !s.Dead && p != null && Perception.Alive(p);
                if (alive)
                {
                    if (s.Phase == Phase.Rearmed)
                    {
                        LogDrawTimeline(s);
                        HandsAreHighPlugin.Log.LogInfo($"{s.Name}: back to normal AI");
                    }
                    else if (s.Phase == Phase.Released)
                        HandsAreHighPlugin.Log.LogInfo($"{s.Name}: back to his own AI ({s.HandBackWhy ?? "free"}; {Arsenal.DescribeArmed(p)}" +
                            $"{(s.HeldSet ? $", {Perception.Flat(p.Position, s.HeldAt):0} m from where he was held" : "")}" +
                            $"{(s.LeftHeldAt > 0f ? $", {Time.time - s.LeftHeldAt:0.0} s after leaving captivity" : "")})");
                    HistoryOf(p).CooldownUntil = Time.time + Settings.SurrenderCooldown.Value;
                    // His own AI starts from here, not from our last path (a non-SAIN brain would
                    // otherwise keep walking it: an outdated destination).
                    // 0.25.0: except a man handed back mid-stride (QuickHandBack): he walks on along our
                    // last path (away from his captor) until his AI's first move replaces it.
                    try { BotOwner o = p.AIData?.BotOwner; if (s.Moving && !s.HandBackMoving) o?.StopMove(); if (!s.HandBackMoving) o?.Sprint(false); } catch { }
                    s.Moving = false;
                    s.TLayerOff = Time.time;
                    ReleaseWatch.Start(s, p);
                    Escape.Begin(s, p);     // 0.24.0
                    Release(s, "left surrender");
                }
                else
                {
                    Forget(p);
                }
                Unhook(s);
                Captors.EndCustody(s, alive ? "back to normal AI" : "dead");
                // 0.22.0: a man handed back unarmed (let go, or freed by incoming fire) keeps the
                // hold-fire decisions about him for a while: his ex-captor (and anyone who spared
                // him) doesn't turn on him the moment our layer lets go, only once he takes a weapon
                // in his hands (Restraint.TickDetached). A draw (Rearmed) or death ends them now.
                if (alive && s.Phase == Phase.Released) Restraint.Detach(s, s.HandBackWhy ?? "free");
                else Restraint.ForgetPrisoner(p);
                States.Remove(p);
            }
        }

        // A debug scenario is removing this bot (it is about to be despawned): the same exit as
        // a bot finishing (Tick's Finished loop), at once: gestures and speech dropped, events
        // unhooked, custody ended, restraint forgotten. No phase change, nothing done to him.
        internal static bool Discharge(Player bot, string why)
        {
            if ((object)bot == null || !States.TryGetValue(bot, out SurrenderState s)) return false;
            Release(s, why);
            Unhook(s);
            Captors.EndCustody(s, why);
            Restraint.ForgetPrisoner(bot);
            States.Remove(bot);
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: removed from the surrender registry ({why})");
            return true;
        }

        // Every state that leaves the registry must come through here, or the old
        // handler stays on the Player (and survives a hot reload).
        private static void Unhook(SurrenderState s)
        {
            object bot = s.Bot;   // a destroyed Player is still a live C# object; its events can be cleared
            if (bot == null) { s.HitHandler = null; s.DeathHandler = null; return; }
            if (s.HitHandler != null)
            {
                try { s.Bot.BeingHitAction -= s.HitHandler; } catch (System.Exception e) { HandsAreHighPlugin.Report("unhook hit event", e); }
                s.HitHandler = null;
            }
            if (s.DeathHandler != null)
            {
                try { s.Bot.OnPlayerDeadOrUnspawn -= s.DeathHandler; } catch (System.Exception e) { HandsAreHighPlugin.Report("unhook death event", e); }
                s.DeathHandler = null;
            }
        }

        private static void UnhookAll()
        {
            foreach (SurrenderState s in States.Values) Unhook(s);
        }

        // Pending speech and gestures go with the state; the lean is handed back upright.
        // Nothing stored here can call back later: gestures run off time, speech off Tick,
        // and both only for states still in the registry.
        private static void Release(SurrenderState s, string why)
        {
            HandsAreHighPlugin.Try("release gesture", () => Gestures.Cancel(s, why));
            HandsAreHighPlugin.Try("release turn", () => TurnDriver.StopNow(s));   // 0.17.0: the game's trunk limit back (at once: he is leaving us)
            HandsAreHighPlugin.Try("release fingers", () => Fingers.Restore(s));   // 0.24.0: only if nothing else writes them
            s.PendingVoice = null;
            s.VerifyAt = -1f;
            s.VerifyClip = null;
        }

        private static string Ms(SurrenderState s, float t) =>
            t < 0f ? "n/a" : $"+{(t - s.TStart) * 1000f:0} ms";

        // ORBIT's Ghost Mode (or anything else) can put a bot to sleep. While asleep he
        // can't fetch anything, so the claim on a gun goes; on waking he decides again.
        private static void UpdateSuspension(Player bot, SurrenderState s)
        {
            if (!Settings.PauseWhenAsleep.Value) return;
            BotOwner owner = bot.AIData?.BotOwner;
            bool awake = owner != null && owner.BotState == EBotState.Active && bot.gameObject.activeInHierarchy;

            if (!awake && !s.Suspended)
            {
                s.Suspended = true;
                s.TargetWeapon = null;
                s.DropQueue.Clear();
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: asleep ({s.Phase}), plans dropped");
            }
            else if (awake && s.Suspended)
            {
                s.Suspended = false;
                if (s.Phase != Phase.HandsUp && s.Phase != Phase.Rearmed && s.Phase != Phase.Released)
                {
                    // 0.21.0: decides straight away what he does now (no waiting where he stands).
                    s.SetPhase(Phase.Regrouping, "woke up");
                }
                else
                {
                    HandsAreHighPlugin.Log.LogInfo($"{s.Name}: woke up ({s.Phase})");
                }
            }
        }

        // Hot-reload teardown: release every held bot so the old assembly's layer goes
        // inert. A bot held at that moment stays unarmed and returns to normal AI.
        internal static void Clear()
        {
            if (States.Count > 0)
                HandsAreHighPlugin.Log?.LogInfo($"Teardown: releasing {States.Count} surrendered bot(s)");
            foreach (SurrenderState s in States.Values) Release(s, "teardown");
            SurrenderStats.EndRaid();
            FireBreak.Clear();      // 0.21.0: captors' hit hooks off (an old assembly's handler must not stay on a Player)
            ReleaseWatch.Clear();
            Escape.Clear();         // 0.24.0: keep-away layer inert (BigBrain can't unregister it)
            WeaponState.Clear();    // 0.24.0: both weapon-state patches step aside (unpatched too)
            UnhookAll();
            Restraint.Clear();
            Captors.Clear();
            CaptorVoice.Clear();
            Voice.ClearChecks();
            Compliance.Clear();
            Judgement.Clear();
            ScenarioStaging.Clear();
            LootingBotsAdapter.Reset();
            OrbitAdapter.ReleaseAll();
            States.Clear();
            Finished.Clear();
            History.Clear();
            // 0.20.1: the Player keys too (EnsureRaid clears them on a raid change, but after a
            // hot-reload teardown this assembly's EnsureRaid never runs again).
            ByPlayer.Clear();
            Gone.Clear();
            _world = null;
        }
    }
}
