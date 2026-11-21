using System.Collections.Generic;
using System.Text;
using Comfort.Common;
using EFT;
using EFT.HandBook;
using EFT.Interactive;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // 0.16.0: a bot captor weighs what his prisoner dropped and may go and take it.
    //
    // Assessed once the prisoner's drop is done and he is under control, item by item:
    //   worth   handbook value (Singleton<Handbook>, as Arsenal.AssessKit), and whether it fills an
    //           empty slot of his (a gun for an empty weapon slot, a helmet with none on)
    //   room    the game's own answer: InventoryController.FindSlotToPickUp (an empty equipment
    //           slot that takes it), else FindGridToPickUp for anything but a gun (as the game's
    //           BotItemTaker.GetItemAddress); no answer = no room. Nothing of his is ever moved
    //           or dropped to make room.
    //   risk    walking over costs control: a prisoner standing near the item, a weapon he can see
    //           still on the man, nobody else covering, being hurt; less with the man on his knees,
    //           turned away or his hands on his head, or a squadmate covering him.
    //   nature  appetite by personality (Rats most, cautious men least), one roll per item.
    // Outcomes per item: take now / later (after a turn-around order, once the man kneels or turns,
    // or after letting him go) / leave. Logged with every input.
    //
    // Taking it is the game's own bot pick-up: walk to it (BotMover.GoToByWay), stand still, look
    // down at it, MovementState.Pickup (the pick-up animation, IdlePlayerState only), and in its
    // callback the same transaction BotItemTaker.PickupAction runs (QuickFindAppropriatePlace
    // simulated, then RunNetworkTransaction). The result is read back from his equipment.
    //
    // A real distraction: walking over his eyes go to the item near the end, and while he bends
    // for it they are on the ground: the prisoner's Reactions.Covered sees an opening (and weighs
    // the other guards he can see). His reaction to the prisoner moving is slower meanwhile
    // (Captors.Check). Any threat, the prisoner moving or the item gone ends it.
    //
    // 0.18.0: the weapons he made the man drop (the gun at the surrender, guns and knife on Drop
    // your weapons) are "demanded": always wanted (no appetite roll, no value threshold), fetched
    // first, with their own risk tolerance (Risk he accepts for those). Items dropped AFTER the first
    // weighing (every Drop your weapons answer comes later) are weighed when they land: 0.16-0.17
    // weighed once, so a weapon dropped on his order was never considered (0.17.0 log, #13: the
    // knife). A fetch interrupted by a threat or the man moving is weighed again once the situation
    // changes (at most twice), not dropped for good and not retried at once. After a kill decision
    // is carried out the killer fetches what he still wanted (AfterKill).
    //
    // Coordination: an item another of our captors, a prisoner (TargetWeapon) or a squadmate's own
    // BotItemTaker is going for is left alone; a race lost to anyone else is a failed transaction
    // (the game allows one move), logged. Looting Bots, if installed, is asked not to start its own
    // looting on a captor while he guards or fetches (LootingBotsAdapter; only an exact API match).
    //
    // 0.19.0, where things are: each item is TRACKED (its real LootItem, sampled every 0.4 s), and
    // nothing is decided while it is still moving (thrown, rolling). The captor only KNOWS where it
    // lies if he saw it there (Notices: in front of him with a clear line, or at the feet of the man
    // he is watching); otherwise he looks for it (LootSearchSeconds) and then forgets it. He walks to
    // where he believes it is and re-acquires it on the way by sight; it isn't there: he stops.
    // Close items (walking distance <= LootNearDistance) are weighed as before (Reweigh, risk of the
    // few steps), and so are items at the man's feet (walking to them is walking up to him, eyes on
    // him). Distant ones (a walk away from him) get a deliberate decision (WeighDistant): benefit (denying the man a
    // gun near him, a gap it fills, its value; appetite for non-demanded items) against cost (the
    // walk, the time the man is unwatched unless a squadmate covers him, made worse by a weapon he
    // still has, better by kneeling / turned away / hands on head; by caution and experience).
    // Outcomes: take now; postpone (another enemy about, under fire, or the man would be left
    // unwatched) and weigh again after LootReweighSeconds or when the situation changes, at most
    // LootMaxPostpone times; then after the release, or leave it. A distant walk is a real walk: he
    // looks where he goes, with a look back every few seconds, so the man gets a genuine opening; he
    // only stops for the man moving when he sees it (Captors.Check notices resistance by sight).
    //
    // 0.19.0, the pick-up itself (why it snapped):
    //   - Pickup(false) on a man who is no longer in PickUpState STARTS a new pick-up: the game's
    //     IdlePlayerState.Pickup ignores its flag (OverrideState(PickUpState)), and that state holds
    //     him for up to 5 s with his rotation clamped. Every exit here now goes through
    //     LeavePickupState, which (as the player's own pick-up, InteractionContextHelper) only acts
    //     on a man actually in PickUpState.
    //   - He looked at the item from his FEET (as BotItemTaker.LookAndTakeItem does), so his pitch
    //     stayed ~17 deg and, stopped 1.45 m away, the reach went into the air while the item jumped
    //     into his slot; PickUpState only lowers the body (PoseLevel / 3, smoothed by the game) when
    //     the look is over 60 deg down. He now walks to ~0.7 m, eases his eyes down to the item from
    //     his EYES (a 150 deg/s turn), and starts the pick-up only once the look has arrived (6 deg)
    //     and his hands are idle (the game's SetInteractInHands plays the reach only then; aiming is
    //     lowered first), within 1.5 s.
    //   - After the transaction he straightens up (Recover, 0.6 s: no walk, the look eased back to
    //     the man at 140 deg/s) before guarding resumes, instead of the look snapping back at once.
    internal enum LootStage { Walk, Settle, Pickup, Recover }

    internal sealed class LootCandidate
    {
        internal Item Item;
        internal string Name;
        internal float Value;
        internal string Fit;            // where it would go
        internal bool Gap;              // fills an empty slot of his
        internal string Decision;       // take / later / leave / taken / failed
        internal string Why;
        internal float Want;            // the appetite roll's chance (rolled once)
        internal bool Wanted;
        internal bool Demanded;         // 0.18.0: a weapon he made him drop
        internal bool Needed;           // 0.23.0: gear he told him to drop because he lacks it (GearNeeds)
        internal bool Melee;            // 0.24.0: a knife / melee item (EFT.InventoryLogic.Knife): optional, never postponed
        internal bool StepAsked;        // 0.23.0: he asked the man to step off it once
        internal int Tries;             // fetches started
        // 0.19.0: tracking and what he knows (see the header).
        internal LootItem Loose;
        internal Vector3 Sample;
        internal float StillSince = -1f;
        internal bool Landed;
        internal bool Seen;
        internal Vector3 KnownAt;
        internal float NextTrack;
        internal float FirstAt;
        internal float PathLen = -1f;
        internal bool Distant;
        internal int Postponed;
        internal float NextWeigh;
    }

    internal sealed class LootJob
    {
        internal Player Bot;
        internal BotOwner Owner;
        internal string Name;
        internal LootCandidate C;
        internal LootItem Loose;
        internal Vector3 At;
        internal SurrenderState Prisoner;     // may be already let go (after release)
        internal Vector3 PrisonerFrom;
        internal bool AfterRelease;
        internal LootStage Stage;
        internal float Since, StageSince;
        internal bool PathIssued;
        internal Vector3 ProgressFrom;
        internal float ProgressSince;
        internal bool Asked, Answered, Animated;
        internal float AskedAt;
        // 0.19.0
        internal bool Distant;
        internal int Repaths;
        internal float NextSeek;
        internal float NextGlanceBack, GlanceBackUntil;
        // 0.20.0: he started it with his squadmate covering him (Overwatch); a loss he can tell is judged once.
        internal bool CoverRelied, CoverJudged;
        // 0.23.0: where he stands to pick it up (StandPoint: beside it, clear of the prisoner and the
        // walls), chosen once, checked again twice a second only against the prisoner moving into it.
        internal bool HasStand, Close;
        internal Vector3 Stand, StandFor;
        internal int Reselects;
        internal float NextStandCheck;
    }

    internal sealed class LootPlan
    {
        internal readonly List<LootCandidate> Items = new List<LootCandidate>(4);
        internal bool Assessed;
        internal bool TurnAsked;
        internal int SituationAt;       // what the prisoner was doing when risk was last weighed
    }

    internal static class CaptorLoot
    {
        private static readonly Dictionary<Player, LootJob> Jobs = new Dictionary<Player, LootJob>();
        private static readonly Dictionary<Player, Deferred> Later = new Dictionary<Player, Deferred>();
        private static readonly HashSet<Item> Claimed = new HashSet<Item>();
        private static readonly List<Player> Scratch = new List<Player>(4);

        private sealed class Deferred
        {
            internal BotOwner Owner;
            internal SurrenderState Ex;
            internal readonly List<LootCandidate> Items = new List<LootCandidate>(3);
            internal float Since, NextAt;
        }

        internal static int Count => Jobs.Count + Later.Count;

        // Scenario forcing: this captor wants everything worth anything (the appetite roll only;
        // room, risk and the fetch itself stay real). Cleared with the scenario.
        private static readonly HashSet<Player> ForcedWant = new HashSet<Player>();
        internal static void ForceWant(Player p, bool on) { if (p == null) return; if (on) ForcedWant.Add(p); else ForcedWant.Remove(p); }

        internal static bool TryJob(Player bot, out LootJob job)
        {
            job = null;
            return bot != null && Jobs.Count > 0 && Jobs.TryGetValue(bot, out job);
        }

        // The layer keeps a bot fetching after release (custody already over).
        internal static bool WantsControl(Player bot) => bot != null && Jobs.Count > 0 && Jobs.TryGetValue(bot, out LootJob j) && j.AfterRelease;

        // What the prisoner can see him doing (Reactions.Covered): bent over an item, or walking
        // to it with his eyes on it.
        internal static bool Busy(Player captor, out string what)
        {
            what = null;
            if (!TryJob(captor, out LootJob j) || j.AfterRelease) return false;
            if (j.Stage == LootStage.Walk)
            {
                // 0.19.0: walking off to a distant item is busy all the way (he looks where he goes).
                if (j.Distant) { what = $"walking off to his {j.C.Name}"; return true; }
                if ((j.Bot.Position - j.At).sqrMagnitude > 3f * 3f) return false;
                what = $"walking over to his {j.C.Name}";
                return true;
            }
            what = j.Stage == LootStage.Pickup ? $"picking up his {j.C.Name}"
                 : j.Stage == LootStage.Recover ? $"straightening up with his {j.C.Name}"
                 : $"bending for his {j.C.Name}";
            return true;
        }

        internal static bool Fetching(Player captor) => TryJob(captor, out LootJob j) && !j.AfterRelease;

        // 0.25.0 (SurrenderLogic.Abandonment): the captor is seeing to gear lying close to the
        // prisoner (his dropped gun, kit at his feet), during custody or just after it: from the
        // prisoner's side that is a man stepping aside for a moment, not a man leaving.
        internal static bool GatheringNear(Player captor, Vector3 prisoner, float radius, out string what)
        {
            what = null;
            if (!TryJob(captor, out LootJob j)) return false;
            if (Perception.Flat(j.At, prisoner) > radius || Perception.Flat(captor.Position, prisoner) > radius + 3f) return false;
            what = j.Stage == LootStage.Walk ? $"walking over to the {j.C.Name}" : $"seeing to the {j.C.Name}";
            return true;
        }

        internal static void Clear()
        {
            // Raid teardown / hot reload: nobody is left frozen in a pick-up.
            foreach (LootJob j in Jobs.Values) if (j.Asked && !j.Answered) LeavePickupState(j.Bot);
            Jobs.Clear();
            Later.Clear();
            Claimed.Clear();
            ForcedWant.Clear();
        }

        // ---- during custody (Captors.Check, 4x a second, guarding, no threat) -------------------

        // True while it has the captor (a fetch under way, or one starting now).
        internal static bool Step(CaptorState cs, SurrenderState s, float now, ThreatLevel t)
        {
            if (TryJob(cs.Bot, out LootJob job)) { TickJob(job, now, t); return Jobs.ContainsKey(cs.Bot); }
            if (!Settings.CaptorLooting.Value || cs.Unsafe || t != ThreatLevel.None) return false;
            if (s.Weight < 0.95f || s.DropPending || s.HandsPending || s.DropQueue.Count > 0 || s.ReportAt > 0f || !Arsenal.HandsEmpty(s.Bot)) return false;
            if (s.Dropped.Count == 0 && s.DroppedWeapon == null) return false;
            LootPlan plan = cs.Loot ?? (cs.Loot = new LootPlan());
            int situation = Situation(cs, s);
            if (!plan.Assessed || HasNew(s, plan))
            {
                if (!Perception.TakeHeavy()) return false;   // a few slot/grid searches and a loot scan: a frame with budget
                Assess(cs, s, plan, situation, now);
            }
            else
            {
                // 0.19.0: items still in the air (or not seen land) are tracked until they can be
                // weighed; postponed ones are weighed again when the situation changes (he knelt,
                // turned away, a squadmate covers him) or after LootReweighSeconds, a bounded number
                // of times. Never the appetite again.
                bool changed = situation != plan.SituationAt;
                plan.SituationAt = situation;
                foreach (LootCandidate c in plan.Items)
                {
                    if (c.Decision == "pending" || c.Decision == "unseen")
                    {
                        Track(cs.Bot, s, c, now);
                        if (c.Landed && Perception.TakeHeavy()) Weigh(cs, s, c, now);
                    }
                    else if (c.Decision == "later" && (changed || (c.Distant && now >= c.NextWeigh)) && Perception.TakeHeavy())
                    {
                        c.Postponed++;
                        Track(cs.Bot, s, c, now);
                        Weigh(cs, s, c, now);
                    }
                }
            }
            LootCandidate next = Pick(plan.Items, "take");
            // 0.20.0: his squadmate's cover first (a bounded wait, then his own risk). Waiting spends
            // this check (true), so no other order is given meanwhile.
            if (next != null && Overwatch.HoldForCover(cs, s, now, $"{s.Name}'s {next.Name}")) return true;
            if (next != null) return Start(cs.Owner, s, next, afterRelease: false, now);
            // Wanted but too risky: a turn-around order first (once), then weigh again.
            if (!plan.TurnAsked && Settings.TurnOrder.Value && !s.TurnedAway && !s.Turning && !Kneel.Active(s) && Any(plan, "later") && !cs.Plan.Contains(OrderKind.TurnAround))
            {
                plan.TurnAsked = true;
                cs.Plan.Insert(cs.Step, OrderKind.TurnAround);
                if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: wants {s.Name}'s gear but not with him watching: orders him to turn around first");
            }
            return false;
        }

        private static bool Any(LootPlan p, string decision)
        {
            foreach (LootCandidate c in p.Items) if (c.Decision == decision) return true;
            return false;
        }

        // 0 standing facing him; +1 kneeling; +2 turned away; +4 hands on head; +8 covered by a squadmate.
        private static int Situation(CaptorState cs, SurrenderState s) =>
            (s.Kneel == KneelPhase.Down ? 1 : 0) | (s.TurnedAway ? 2 : 0) | (s.HeadHands >= 1f ? 4 : 0) | (SquadCover(cs, s, out _) ? 8 : 0);

        // Demanded weapons first, then the most valuable.
        private static LootCandidate Pick(List<LootCandidate> items, string decision)
        {
            LootCandidate next = null;
            foreach (LootCandidate c in items)
            {
                if (c.Decision != decision) continue;
                int rc = c.Demanded ? 2 : c.Needed ? 1 : 0, rn = next == null ? -1 : next.Demanded ? 2 : next.Needed ? 1 : 0;
                if (next == null || rc > rn || (rc == rn && c.Value > next.Value)) next = c;
            }
            return next;
        }

        // Something on the ground he hasn't weighed yet (a Drop your weapons answered since).
        private static bool HasNew(SurrenderState s, LootPlan plan)
        {
            if (s.DroppedWeapon != null && !Has(plan, s.DroppedWeapon)) return true;
            for (int i = 0; i < s.Dropped.Count; i++) if (s.Dropped[i] != null && !Has(plan, s.Dropped[i])) return true;
            return false;
        }

        private static bool Has(LootPlan plan, Item it)
        {
            foreach (LootCandidate c in plan.Items) if (c.Item == it) return true;
            return false;
        }

        // A weapon the man dropped because a captor told him to (or at the surrender).
        // 0.24.0: guns only. A knife from his Scabbard slot is EFT.InventoryLogic.Knife : Item (with a
        // KnifeComponent, not a Weapon): weighed as optional (MeleeWant), not always fetched.
        private static bool IsDemanded(Item it) => Settings.CollectDemanded.Value && it is Weapon;

        // Every item on the ground not weighed yet (all of them the first time).
        private static void Assess(CaptorState cs, SurrenderState s, LootPlan plan, int situation, float now)
        {
            bool first = !plan.Assessed;
            plan.Assessed = true;
            plan.SituationAt = situation;
            var all = new List<Item>(s.Dropped);
            if (s.DroppedWeapon != null && !all.Contains(s.DroppedWeapon)) all.Insert(0, s.DroppedWeapon);
            float greed = Greed(cs.Bot, out string nature);
            var log = new StringBuilder($"{cs.Name}: weighs {(first ? "what" : "what else")} {s.Name} dropped ({nature} x{greed:0.00}, takes at most {Settings.LootMaxItems.Value} besides the weapons he made him drop):");
            int n = 0;
            foreach (LootCandidate c in plan.Items) if (c.Wanted && !c.Demanded) n++;
            int added = 0;
            foreach (Item it in all)
            {
                if (it == null || Has(plan, it)) continue;
                added++;
                var c = new LootCandidate { Item = it, Name = Compliance.Name(it), Demanded = IsDemanded(it), FirstAt = now };
                plan.Items.Add(c);
                LootItem loose = ScenarioOps.FindLoose(it);
                c.Loose = loose;
                if (loose != null) Track(cs.Bot, s, c, now);
                if (loose == null) { c.Decision = "leave"; c.Why = $"not on the ground any more{WhoHas(it)}"; }
                else if (Claimed.Contains(it) || ClaimedByOther(cs, it, loose, out _)) { ClaimedByOther(cs, it, loose, out string by); c.Decision = "leave"; c.Why = $"{by ?? "another captor"} is going for it"; }
                else
                {
                    c.Value = Value(it);
                    ItemAddress where = AddressFor(cs.Bot, it, out c.Fit, out c.Gap);
                    if (where == null) { c.Decision = "leave"; c.Why = $"{(c.Demanded ? "a weapon he made him drop, but " : "")}no room for it ({c.Fit})"; }
                    else if (Arsenal.Kind(it) == Arsenal.GearKind.Melee)
                    {
                        // 0.24.0: optional; leaving it is a finished decision (never postponed).
                        c.Melee = true;
                        MeleeWant(cs, s, c, greed, out string mw);
                        if (!c.Wanted) { c.Decision = "leave"; c.Why = $"{mw}: leaves it"; }
                        else { c.Why = mw; Weigh(cs, s, c, now); }
                    }
                    else if (c.Demanded)
                    {
                        c.Want = 1f;
                        c.Wanted = true;
                        c.Why = $"the weapon he made him drop ({c.Value:0} roubles, into his {c.Fit})";
                        Weigh(cs, s, c, now);
                    }
                    else if (GearNeeds.IsNeeded(cs, it))
                    {
                        // 0.23.0: gear he told him to drop because he has none (GearNeeds): wanted.
                        c.Needed = true;
                        c.Want = 1f;
                        c.Wanted = true;
                        c.Why = $"the {c.Fit.Replace(" slot", "")} he lacks and told him to drop ({c.Value:0} roubles)";
                        Weigh(cs, s, c, now);
                    }
                    else if (n >= Settings.LootMaxItems.Value) { c.Decision = "leave"; c.Why = "has taken enough"; }
                    else
                    {
                        float worth = c.Gap ? 1.4f : c.Value >= Settings.LootMinValue.Value ? Mathf.Lerp(0.7f, 1.3f, Mathf.Clamp01(c.Value / 100000f)) : 0f;
                        c.Want = Mathf.Clamp01(Settings.LootChance.Value * greed * worth);
                        float roll = Dice.Value;
                        c.Wanted = roll < c.Want;
                        if (ForcedWant.Contains(cs.Bot) && worth > 0f) { c.Wanted = true; c.Want = 1f; }
                        string valueTxt = $"{c.Value:0} roubles{(c.Gap ? $", fills his empty {c.Fit}" : $", into his {c.Fit}")}";
                        if (worth <= 0f) { c.Decision = "leave"; c.Why = $"{valueTxt}: not worth the walk"; }
                        else if (!c.Wanted) { c.Decision = "leave"; c.Why = $"{valueTxt}: want {c.Want:0.00}, roll {roll:0.00}: doesn't bother"; }
                        else { n++; c.Why = $"{valueTxt}: want {c.Want:0.00}, roll {roll:0.00}"; Weigh(cs, s, c, now); }
                    }
                }
                log.Append($"\n    {c.Name}: {c.Decision} ({c.Why})");
            }
            if (added == 0) log.Append(" nothing on the ground he can identify as the man's");
            if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo(log.ToString());
        }

        // Who holds an item that left the ground (for the log): "" when unknown.
        private static string WhoHas(Item it)
        {
            try
            {
                Item root = it?.Owner?.RootItem;
                var world = Singleton<GameWorld>.Instance;
                if (root == null || world?.AllAlivePlayersList == null) return "";
                foreach (Player p in world.AllAlivePlayersList)
                    if (p != null && p.Equipment == root) return $" ({Restraint.Name(p)} has it)";
            }
            catch { }
            return "";
        }

        // ---- 0.19.0: tracking, knowledge, distance ---------------------------------------------

        private const float TrackGap = 0.4f;

        // The item's LootItem, re-found only when the cached one no longer holds it.
        private static LootItem LooseOf(LootCandidate c)
        {
            LootItem li = c.Loose;
            if (li != null && li.isActiveAndEnabled && li.Item == c.Item) return li;
            c.Loose = ScenarioOps.FindLoose(c.Item);
            return c.Loose;
        }

        // Where it is (moving or landed) and whether he sees it there.
        private static void Track(Player captor, SurrenderState s, LootCandidate c, float now)
        {
            if (now < c.NextTrack) return;
            c.NextTrack = now + TrackGap;
            LootItem li = LooseOf(c);
            if (li == null) return;
            Vector3 pos = li.transform.position;
            if (c.StillSince < 0f || (pos - c.Sample).sqrMagnitude > 0.06f * 0.06f) { c.Sample = pos; c.StillSince = now; c.Landed = false; }
            else if (now - c.StillSince >= 0.6f) c.Landed = true;
            if (captor != null && Notices(captor, s, pos)) { c.Seen = true; c.KnownAt = pos; }
        }

        // He sees where it lies: within sight range, in front of him (65 deg of his look) with a
        // clear line to it; or at the feet of the man he is looking at.
        internal static bool Notices(Player captor, SurrenderState s, Vector3 pos)
        {
            Vector3 eye = Perception.Eye(captor);
            float range = Settings.LootSightRange.Value;
            if ((pos - eye).sqrMagnitude > range * range) return false;
            if (s?.Bot != null && (pos - s.Bot.Position).sqrMagnitude < 2.5f * 2.5f &&
                Vector3.Angle(captor.LookDirection, Perception.Chest(s.Bot) - eye) < 65f && Perception.LineClear(eye, Perception.Chest(s.Bot)))
                return true;
            Vector3 at = pos + Vector3.up * 0.15f;
            return Vector3.Angle(captor.LookDirection, at - eye) < 65f && Perception.LineClear(eye, at);
        }

        // 0.24.0: a dropped knife (or axe, shovel: any KnifeComponent item). The captor used to fetch
        // it like a gun (always wanted, "the weapon he made him drop": 0.23.0 log, #29/#30/#32 walked
        // over and bent down by the man for a 3,560-rouble MPL-50 and a 6Kh5 he had no use for).
        // Wanted only if it is worth it to him: "Captors pick up a dropped knife" x his appetite
        // (greed) x use (none of his own 1.4, has one 0.35) x value (0.6 at nothing .. 1.5 at 40k
        // roubles) x a little denial if it lies within the man's reach (1.15). Then the same exposure
        // weighing as any item with a lower tolerance; too risky = left (never "later" or "after the
        // release": a knife keeps nobody waiting). Logged with what the item is.
        private static void MeleeWant(CaptorState cs, SurrenderState s, LootCandidate c, float greed, out string why)
        {
            Item own = cs.Bot?.Equipment?.GetSlot(EquipmentSlot.Scabbard)?.ContainedItem;
            float use = own == null ? 1.4f : 0.35f;
            float worth = Mathf.Lerp(0.6f, 1.5f, Mathf.Clamp01(c.Value / 40000f));
            LootItem li = c.Loose;
            float toMan = li != null && s?.Bot != null ? Vector3.Distance(li.transform.position, s.Bot.Position) : 99f;
            float denial = toMan <= Settings.ScrambleRange.Value ? 1.15f : 1f;
            c.Want = Mathf.Clamp01(Settings.MeleePickupChance.Value * greed * use * worth * denial);
            float roll = Dice.Value;
            c.Wanted = roll < c.Want;
            bool forced = ForcedWant.Contains(cs.Bot);
            if (forced) { c.Wanted = true; c.Want = 1f; }
            why = $"{Arsenal.KindText(c.Item)}, {c.Value:0} roubles, into his {c.Fit}; {(own == null ? "he has no knife of his own" : $"he has his own {own.ShortName.Localized()}")}; " +
                  $"want {c.Want:0.00} (base {Settings.MeleePickupChance.Value:0.00} x greed {greed:0.00} x use {use:0.00} x value {worth:0.00}{(denial > 1f ? $" x by his reach {denial:0.00}" : "")}), " +
                  $"{(forced ? "FORCED appetite" : $"roll {roll:0.00}")}";
        }

        // What to do about it now, from what he knows (see the header).
        private static void Weigh(CaptorState cs, SurrenderState s, LootCandidate c, float now)
        {
            if (LooseOf(c) == null) { Decide(cs, c, "leave", "gone since"); return; }
            if (!c.Landed) { Decide(cs, c, "pending", "still moving: waits for it to come to rest"); return; }
            if (!c.Seen)
            {
                if (now - c.FirstAt > Settings.LootSearchSeconds.Value) Decide(cs, c, "leave", "never saw where it went");
                else Decide(cs, c, "unseen", "didn't see where it came to rest: keeps an eye out for it");
                return;
            }
            if (!Perception.PathTo(cs.Bot.Position, c.KnownAt, out Vector3[] corners, out string pw)) { Decide(cs, c, "leave", $"can't get to it ({pw})"); return; }
            c.PathLen = Captors.PathLength(corners);
            if (c.PathLen > Settings.LootMaxDistance.Value) { Decide(cs, c, "leave", $"a {c.PathLen:0} m walk: too far"); return; }
            // Distant = a walk AWAY from the man: far to walk, and not by his feet (walking to a gun at
            // his feet is walking up to him, eyes on him: the close-range risk rule covers that).
            c.Distant = c.PathLen > Settings.LootNearDistance.Value && (s?.Bot == null || (c.KnownAt - s.Bot.Position).sqrMagnitude > 3f * 3f);
            if (!c.Distant) Reweigh(cs, s, c);
            else WeighDistant(cs, s, c, now);
            if (c.Decision == "later")
            {
                c.NextWeigh = now + Settings.LootReweighSeconds.Value;
                if (c.Melee)
                    Decide(cs, c, "leave", "a knife: not worth waiting for");
                else if (c.Postponed >= Settings.LootMaxPostpone.Value)
                    Decide(cs, c, Settings.LootAfterRelease.Value ? "after release" : "leave",
                        $"postponed {c.Postponed} time(s): keeps his hands on the man{(Settings.LootAfterRelease.Value ? ", fetches it after letting him go" : "")}");
            }
        }

        private static void Decide(CaptorState cs, LootCandidate c, string decision, string why)
        {
            string before = c.Decision;
            c.Decision = decision;
            int cut = c.Why?.IndexOf("; now: ") ?? -1;
            if (cut >= 0) c.Why = c.Why.Substring(0, cut);
            c.Why += $"; now: {why}";
            if (before != null && before != decision && Settings.LogLoot.Value)
                HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: {c.Name}: {decision} ({why})");
        }

        // A distant item: worth the walk and the time away from the man?
        private static void WeighDistant(CaptorState cs, SurrenderState s, LootCandidate c, float now)
        {
            string wait = null;
            if (Restraint.OtherThreat(cs.Owner, s.Bot, out string threat)) wait = $"{threat} about";
            else if (cs.Owner.Memory != null && cs.Owner.Memory.IsUnderFire) wait = "under fire";

            float near = Settings.LootNearDistance.Value, far = Settings.LootMaxDistance.Value;
            float walk = Settings.LootDistanceWeight.Value * Mathf.InverseLerp(near, far, c.PathLen);
            bool covered = SquadCover(cs, s, out string who);
            float away = 2f * c.PathLen / 1.4f + 2f;   // there and back at a walk, plus the pick-up
            float custody;
            var cw = new StringBuilder();
            if (covered) { custody = 0.1f; cw.Append($"{who} covers him"); }
            else
            {
                custody = Mathf.Clamp01(away / 25f);
                cw.Append($"{away:0} s with nobody on him");
                if (cs.SawKept != null) { custody *= 1.8f; cw.Append($", a {Compliance.Name(cs.SawKept)} still on him"); }
                if (s.Kneel == KneelPhase.Down) { custody *= 0.6f; cw.Append(", kneeling"); }
                if (s.TurnedAway) { custody *= 0.6f; cw.Append(", turned away"); }
                if (s.HeadHands >= 1f) { custody *= 0.85f; cw.Append(", hands on his head"); }
            }
            float caution = Caution(cs.Bot) * (1f + 0.2f * NaturalSurrender.Experience(cs.Bot, cs.Owner));
            float cost = (walk + custody) * caution;

            float toMan = Vector3.Distance(c.KnownAt, s.Bot.Position);
            float denial = c.Demanded ? (toMan <= Settings.ScrambleRange.Value ? 0.6f : 0.25f) : c.Melee && toMan <= Settings.ScrambleRange.Value ? 0.15f : 0f;
            float use = c.Gap ? 0.5f : Mathf.Lerp(0f, 0.45f, Mathf.Clamp01(c.Value / 100000f));
            float appetite = c.Demanded ? 1f : Greed(cs.Bot, out _);
            float benefit = (denial + use) * appetite;
            string why = $"{c.PathLen:0} m walk, {toMan:0} m from him: benefit {benefit:0.00} (denial {denial:0.00}, use {use:0.00}, appetite x{appetite:0.00}) " +
                         $"vs cost {cost:0.00} (walk {walk:0.00}, custody {custody:0.00}: {cw}; caution x{caution:0.00})";
            if (c.Melee && (wait != null || benefit < cost)) Decide(cs, c, "leave", $"{why}{(wait != null ? $"; {wait}" : "")}: a knife isn't worth leaving his post for");
            else if (wait != null) Decide(cs, c, "later", $"{why}; {wait}: not now");
            else if (benefit >= cost) Decide(cs, c, "take", $"{why}: goes for it");
            else if (!covered && custody > walk) Decide(cs, c, "later", $"{why}: would leave him unwatched; waits");
            else Decide(cs, c, Settings.LootAfterRelease.Value ? "after release" : "leave", $"{why}: not worth leaving his post for");
        }

        // Take now, later or leave, from the risk of walking over to it now.
        private static void Reweigh(CaptorState cs, SurrenderState s, LootCandidate c)
        {
            LootItem loose = LooseOf(c);
            if (loose == null) { c.Decision = "leave"; c.Why += "; gone since"; return; }
            float risk = Risk(cs, s, c.Seen ? c.KnownAt : loose.transform.position, out string riskWhy);
            float accepts = c.Demanded || c.Needed ? Mathf.Max(Settings.LootRisk.Value, Settings.DemandedRisk.Value) : Settings.LootRisk.Value;
            if (c.Melee) accepts = Settings.LootRisk.Value * 0.8f;   // 0.24.0: not worth much exposure
            string before = c.Decision;
            c.Decision = risk <= accepts ? "take" : !c.Melee && (Settings.LootAfterRelease.Value || c.Demanded || c.Needed) ? "later" : "leave";
            string verdict = c.Decision == "take" ? "takes it now" : c.Decision == "later" ? "too risky now: later" : "too risky: leaves it";
            int cut = c.Why.IndexOf("; risk ");
            if (cut >= 0) c.Why = c.Why.Substring(0, cut);
            c.Why += $"; risk {risk:0.00} ({riskWhy}) vs {accepts:0.00}: {verdict}";
            if (before != null && before != c.Decision && Settings.LogLoot.Value)
                HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: {c.Name}: weighed again ({riskWhy}): risk {risk:0.00} -> {verdict}");
        }

        private static float Risk(CaptorState cs, SurrenderState s, Vector3 item, out string why)
        {
            var sb = new StringBuilder();
            float r = 0.5f;
            float near = Vector3.Distance(item, s.Bot.Position);
            if (near < 2.5f) { r *= 1.6f; sb.Append($"by his feet ({near:0.0} m)"); } else sb.Append($"{near:0.0} m from him");
            if (cs.SawKept != null) { r *= 2.2f; sb.Append($", a {Compliance.Name(cs.SawKept)} still on him"); }
            if (s.Kneel == KneelPhase.Down) { r *= 0.55f; sb.Append(", kneeling"); }
            if (s.TurnedAway) { r *= 0.5f; sb.Append(", turned away"); }
            if (s.HeadHands >= 1f) { r *= 0.8f; sb.Append(", hands on his head"); }
            if (SquadCover(cs, s, out string who)) { r *= 0.35f; sb.Append($", {who} covers him"); }
            else if (Overwatch.On && cs.Guard != null) sb.Append($", {cs.Guard.Name} not in position yet");
            else if (Overwatch.On && cs.AloneWhy != null) { r *= 1.15f; sb.Append(", nobody can cover him"); }
            float hurt = cs.Bot.HealthController != null ? NaturalSurrender.Injury(cs.Bot, out _) : 0f;
            if (hurt > 0.2f) { r *= 1f + hurt; sb.Append($", hurt {hurt:0.00}"); }
            float caution = Caution(cs.Bot);
            r *= caution;
            sb.Append($", nature x{caution:0.00}");
            float exp = NaturalSurrender.Experience(cs.Bot, cs.Owner);
            r *= 1f + 0.2f * exp;   // the experienced see the danger in it
            why = sb.ToString();
            return r;
        }

        // A squadmate who holds fire at him, alive, close, with a line to him and his gun on him.
        private static bool SquadCover(CaptorState cs, SurrenderState s, out string who)
        {
            // 0.20.0: with squad cover on, the one squadmate actually given the role, in position and
            // not fighting (what he counts on when weighing; the start itself waits for real cover).
            if (Overwatch.On) return Overwatch.InPosition(cs, out who);
            who = null;
            var map = Restraint.EntriesFor(s.Bot);
            if (map == null) return false;
            int tries = 0;
            foreach (RestraintEntry e in map.Values)
            {
                if (e.Observer == cs.Bot || !e.Squad || !e.Holds || !Perception.Alive(e.Observer)) continue;
                if ((e.Observer.Position - s.Bot.Position).sqrMagnitude > 15f * 15f) continue;
                if (Fetching(e.Observer)) continue;
                if (tries++ >= 2) break;
                if (!Perception.LineClear(Perception.Eye(e.Observer), Perception.Chest(s.Bot))) continue;
                who = Restraint.Name(e.Observer);
                return true;
            }
            return false;
        }

        // Appetite and caution by personality (no SAIN: Scavs a little greedier). Tuning, not data.
        private static float Greed(Player p, out string nature)
        {
            string pers = SurrenderRegistry.PersonalityOf(p, SurrenderRegistry.HistoryOf(p));
            nature = string.IsNullOrEmpty(pers) ? (NaturalSurrender.ClassOf(p) == NaturalSurrender.RoleClass.Scav ? "a Scav" : "no personality") : pers;
            switch (pers)
            {
                case "Rat": return 1.5f;
                case "Wreckless": return 1.2f;
                case "Timmy": return 1.1f;
                case "GigaChad": return 0.8f;
                case "Coward": return 0.7f;
                case "SnappingTurtle": return 0.9f;
                case "": case null: return NaturalSurrender.ClassOf(p) == NaturalSurrender.RoleClass.Scav ? 1.2f : 1f;
                default: return 1f;
            }
        }

        // 0.20.0: his cover was there when he started; now he can tell it isn't (his guard's gunfire,
        // a reload callout, the guard down or gone; not a glance or a blocked view, which he can't
        // see bent over the item). Judged once: pause (the fetch ends, weighed again later) or carry
        // on. Once bending he mostly finishes. Null: carry on (or nothing he can tell).
        private static string CoverLossJudgement(LootJob j)
        {
            if (!Captors.TryGet(j.Bot, out CaptorState cs)) return null;
            string lost = Overwatch.LoudLoss(cs, j.CoverRelied);
            if (lost == null) return null;
            j.CoverJudged = true;
            SurrenderState s = j.Prisoner;
            float k = 0.55f * Caution(j.Bot);
            var why = new StringBuilder($"{lost}; caution x{Caution(j.Bot):0.00}");
            if (cs.SawKept != null) { k *= 1.5f; why.Append($", a {Compliance.Name(cs.SawKept)} still on him"); }
            if (s != null && s.Kneel == KneelPhase.Down) { k *= 0.7f; why.Append(", kneeling"); }
            if (s != null && s.TurnedAway) { k *= 0.7f; why.Append(", turned away"); }
            if (j.Stage == LootStage.Settle) { k *= 0.4f; why.Append(", already bending for it"); }
            float chance = Mathf.Clamp(k, 0.05f, 0.95f), roll = Dice.Value;
            bool pause = roll < chance;
            if (Settings.LogLoot.Value || Settings.LogSquadCover.Value)
                HandsAreHighPlugin.Log.LogInfo($"{j.Name}: loses his cover while going for the {j.C.Name} ({why}): pause? chance {chance:0.00}, roll {roll:0.00} -> {(pause ? "stops" : "carries on, accepts the risk")}");
            return pause ? $"his cover is gone ({lost})" : null;
        }

        private static float Caution(Player p)
        {
            switch (SurrenderRegistry.PersonalityOf(p, SurrenderRegistry.HistoryOf(p)))
            {
                case "Coward": return 1.4f;
                case "Timmy": return 1.3f;
                case "Rat": return 1.1f;
                case "Chad": return 0.85f;
                case "GigaChad": return 0.8f;
                case "Wreckless": return 0.6f;
                default: return 1f;
            }
        }

        // The game's own answer to "where would it go" (BotItemTaker.GetItemAddress).
        internal static ItemAddress AddressFor(Player bot, Item it, out string fit, out bool gap)
        {
            fit = "nothing";
            gap = false;
            InventoryController ic = bot?.InventoryController;
            if (ic == null) return null;
            ItemAddress slot = ic.FindSlotToPickUp(it);
            if (slot != null)
            {
                gap = true;
                fit = slot.Container is Slot sl ? $"{sl.ID} slot" : "slot";
                return slot;
            }
            if (it is Weapon) { fit = "no empty weapon slot (the game's bots never pack guns away)"; return null; }
            ItemAddress grid = ic.FindGridToPickUp(it);
            if (grid != null) { fit = $"{grid.Container?.ParentItem?.ShortName?.Localized() ?? "bag"}"; return grid; }
            fit = "no slot and no space in his bags";
            return null;
        }

        // Handbook value of the item and everything in it.
        internal static float Value(Item it)
        {
            Handbook hb = Singleton<Handbook>.Instance;
            if (hb?.StructuredItems == null || it == null) return 0f;
            double total = 0;
            foreach (Item x in it.GetAllItems())
            {
                HandbookNode node = hb.StructuredItems[x.TemplateId.ToString()];
                if (node?.Data != null) total += node.Data.Price * Mathf.Max(1, x.StackObjectsCount);
            }
            return (float)total;
        }

        private static bool ClaimedByOther(CaptorState cs, Item it, LootItem loose, out string by)
        {
            by = null;
            foreach (SurrenderState o in SurrenderRegistry.All)
                if (o.TargetWeapon == it) { by = $"{o.Name} (his own gun)"; return true; }
            BotsGroup g = cs.Owner?.BotsGroup;
            if (g != null)
                for (int i = 0; i < g.MembersCount; i++)
                {
                    BotOwner m = g.Member(i);
                    if (m == null || m == cs.Owner || m.IsDead) continue;
                    if (m.ItemTaker != null && m.ItemTaker._itemToTake == loose) { by = $"{Restraint.Name(m.GetPlayer)} (the game's own item taker)"; return true; }
                }
            return false;
        }

        // ---- a fetch --------------------------------------------------------------------------

        private static bool Start(BotOwner o, SurrenderState s, LootCandidate c, bool afterRelease, float now)
        {
            LootItem loose = LooseOf(c);
            if (loose == null) { c.Decision = "leave"; c.Why += "; gone before he went"; return false; }
            // 0.19.0: to where he believes it is (he saw it there), not where it really is.
            Vector3 at = c.Seen ? c.KnownAt : loose.transform.position;
            // 0.23.0: the man he asked to step off it is still stepping: wait for him (this check).
            if (!afterRelease && s?.Order == Order.StepBack) return false;
            // 0.23.0: a spot beside it to stand on (not the item itself: at a man's feet that walked
            // the captor into his body).
            bool hasStand = false;
            Vector3 stand = at;
            if (Settings.LootStandBeside.Value)
            {
                Player man = s?.Bot != null && !s.Dead && Perception.Alive(s.Bot) ? s.Bot : null;
                if (!Perception.TakeHeavy()) return false;   // a few NavMesh samples and up to 3 path solves: a frame with budget
                if (!StandPoint(o.GetPlayer, at, man, out stand, out string standWhy))
                {
                    NoStand(o, s, c, at, man, standWhy, afterRelease);
                    return false;
                }
                hasStand = true;
            }
            if (!Perception.PathTo(o.GetPlayer.Position, hasStand ? stand : at, out _, out string pw))
            {
                c.Decision = "leave";
                c.Why += $"; no way to it ({pw})";
                if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo($"{Restraint.Name(o.GetPlayer)}: can't get to {s.Name}'s {c.Name} ({pw}); leaves it");
                return false;
            }
            var job = new LootJob
            {
                Bot = o.GetPlayer, Owner = o, Name = Restraint.Name(o.GetPlayer), C = c, Loose = loose, At = at, Distant = c.Distant,
                Prisoner = s, PrisonerFrom = s.Bot != null ? s.Bot.Position : Vector3.zero, AfterRelease = afterRelease,
                Stage = LootStage.Walk, Since = now, StageSince = now, ProgressFrom = o.GetPlayer.Position, ProgressSince = now
            };
            if (!afterRelease && Captors.TryGet(job.Bot, out CaptorState jcs)) job.CoverRelied = Overwatch.Covered(jcs);
            if (hasStand)
            {
                job.HasStand = true;
                job.Stand = stand;
                job.StandFor = s?.Bot != null ? s.Bot.Position : Vector3.zero;
                if (Settings.LogEncounter.Value || Settings.LogLoot.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{job.Name}: stands to pick up the {c.Name} {Perception.Flat(stand, at):0.00} m from it" +
                        $"{(s?.Bot != null && !s.Dead ? $", {Perception.Flat(stand, s.Bot.Position):0.00} m from {s.Name}" : "")} ({Perception.Flat(job.Bot.Position, stand):0.0} m walk)");
            }
            Jobs[job.Bot] = job;
            Claimed.Add(c.Item);
            c.Decision = "fetching";
            c.Tries++;
            LootingBotsAdapter.Hold(o, Settings.LootTimeLimit.Value + 5f);
            if (Settings.LogLoot.Value)
                HandsAreHighPlugin.Log.LogInfo($"{job.Name}: goes to take {s.Name}'s {c.Name}{(c.Demanded ? " (the weapon he made him drop)" : "")}{(c.Distant ? $" (distant: {c.PathLen:0} m walk)" : "")} ({Vector3.Distance(job.Bot.Position, job.At):0.0} m{(afterRelease ? (s.Dead ? ", after the kill" : ", after letting him go") : $", {Vector3.Distance(job.At, job.PrisonerFrom):0.0} m from him")}{(c.Tries > 1 ? $", try {c.Tries}" : "")})");
            return true;
        }

        // ---- 0.23.0: where to stand for the pick-up ------------------------------------------------
        //
        // 0.22 walked the path to the item itself (shortened by 0.7 m along it). An item at the
        // prisoner's feet put the end of that path inside his capsule: the captor walked into his
        // body, the two capsules pushed, the mover re-pathed and the stop-and-settle test (distance
        // to the item, his speed) flipped between walking and settling: the jitter and the walking
        // into him. Now he picks ONE spot beside the item:
        //   in reach      StandMin..StandMax (0.5-0.85 m) from it on the flat: the look from his eyes
        //                 is then over 60 deg down (PickUpState lowers the body) and the reach lands;
        //   clear of him  at least StandClear (0.8 m: two capsules of ~0.35 m and a margin) from the
        //                 prisoner's feet, so neither body overlaps the other or the pick-up animation;
        //   clear of walls his capsule fits (Physics.CheckCapsule against the player collision mask,
        //                 hip to head), on the NavMesh within 0.35 m, on the item's floor;
        //   sees it       a clear line from his eyes there to the item;
        //   reachable     a complete path that ends on the spot and isn't a long way round (<= 3
        //                 path solves per choice).
        // Candidates: his own side of the item first, then round it (10 directions x 2 distances,
        // cheapest checks first). Chosen once per fetch; checked again twice a second only for the
        // prisoner moving into it (Restand, twice at most). No spot: the man is asked once to step
        // back off it (if he is standing right on it), else it waits for after the release (or is
        // left). Never collision changes, teleports or moving the item.
        private const float StandMin = 0.5f, StandMax = 0.85f, StandClear = 0.8f;
        private static readonly float[] StandAngles = { 0f, 35f, -35f, 70f, -70f, 105f, -105f, 140f, -140f, 180f };
        private static readonly float[] StandRadii = { 0.65f, 0.8f };
        private static readonly float[] StepTurns = { 0f, 45f, -45f, 90f, -90f };

        internal static bool StandPoint(Player me, Vector3 item, Player man, out Vector3 stand, out string why)
        {
            stand = item;
            why = "no candidate";
            Vector3 toMe = me.Position - item; toMe.y = 0f;
            if (toMe.sqrMagnitude < 0.01f) { toMe = man != null ? item - man.Position : me.Transform.forward; toMe.y = 0f; }
            if (toMe.sqrMagnitude < 1e-4f) toMe = Vector3.forward;
            toMe.Normalize();
            int mask = LayersMaskController.PlayerStaticCollisionsMask;
            int solves = 0;
            for (int ai = 0; ai < StandAngles.Length; ai++)
                for (int ri = 0; ri < StandRadii.Length; ri++)
                {
                    Vector3 c = item + Quaternion.AngleAxis(StandAngles[ai], Vector3.up) * toMe * StandRadii[ri];
                    if (!UnityEngine.AI.NavMesh.SamplePosition(c, out UnityEngine.AI.NavMeshHit hit, 0.35f, UnityEngine.AI.NavMesh.AllAreas)) { why = "off the NavMesh"; continue; }
                    Vector3 p = hit.position;
                    if (Mathf.Abs(p.y - item.y) > 0.5f) { why = "another level"; continue; }
                    float r = Perception.Flat(p, item);
                    if (r < StandMin || r > StandMax) { why = "out of reach"; continue; }
                    if (man != null && Perception.Flat(p, man.Position) < StandClear) { why = "too close to the prisoner's body"; continue; }
                    if (Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.55f, 0.3f, mask, QueryTriggerInteraction.Ignore)) { why = "no room to stand (a wall or a prop)"; continue; }
                    if (!Perception.LineClear(p + Vector3.up * 1.5f, item + Vector3.up * 0.12f)) { why = "can't see it from there"; continue; }
                    if (solves >= 3) { why = $"no reachable spot among the nearest ({why})"; return false; }
                    solves++;
                    if (!Perception.PathTo(me.Position, p, out Vector3[] corners, out string pw)) { why = pw; continue; }
                    Vector3 end = corners[corners.Length - 1];
                    if (Perception.Flat(end, p) > 0.3f) { why = "the path doesn't end on it"; continue; }
                    if (Captors.PathLength(corners) > 2f * Perception.Flat(me.Position, p) + 3f) { why = "the way round is too long"; continue; }
                    stand = p;
                    return true;
                }
            return false;
        }

        // The prisoner moved into the chosen spot (or the item moved): one new choice.
        private static bool Restand(LootJob j, float now, string what)
        {
            if (!Perception.TakeHeavy()) { j.NextStandCheck = now + 0.1f; return true; }   // next frame with budget
            Player man = !j.AfterRelease && j.Prisoner?.Bot != null && !j.Prisoner.Dead ? j.Prisoner.Bot : null;
            if (!StandPoint(j.Bot, j.At, man, out Vector3 p, out string why))
            {
                if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo($"{j.Name}: {what}; no clean spot by the {j.C.Name} any more ({why})");
                return false;
            }
            j.Reselects++;
            if (Perception.Flat(p, j.Stand) > 0.25f) { j.Stand = p; j.PathIssued = false; }
            if (man != null) j.StandFor = man.Position;
            if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo($"{j.Name}: {what}: picks another spot by the {j.C.Name} ({Perception.Flat(p, j.At):0.00} m from it, choice {j.Reselects + 1})");
            return true;
        }

        // No spot beside it: ask the man to step back off it (once, only if he stands right on it and
        // can walk), else wait for after the release (or leave it).
        private static void NoStand(BotOwner o, SurrenderState s, LootCandidate c, Vector3 at, Player man, string why, bool afterRelease)
        {
            string name = Restraint.Name(o.GetPlayer);
            if (!afterRelease && man != null && s.Phase == Phase.HandsUp && !c.StepAsked && s.Kneel == KneelPhase.Standing && s.Order != Order.Kneel &&
                Perception.Flat(man.Position, at) < StandClear + 0.2f && Captors.TryGet(o.GetPlayer, out CaptorState cs) && cs.Prisoner == s &&
                StepBackSpot(s, at, o.GetPlayer, out Vector3 to))
            {
                c.StepAsked = true;
                s.StepBackTo = to;
                bool given = Orders.Give(s, OrderKind.StepBack, o.GetPlayer, human: false, out string not);
                if (Settings.LogLoot.Value)
                    HandsAreHighPlugin.Log.LogInfo($"{name}: no clean spot by {s.Name}'s {c.Name} ({why}); " +
                        (given ? $"tells him to step back off it ({Perception.Flat(man.Position, to):0.0} m)" : $"couldn't tell him to step back ({not})"));
                if (given) return;   // still "take": fetched once he has moved (Start waits for the step)
            }
            c.Decision = afterRelease ? "leave" : Settings.LootAfterRelease.Value ? "after release" : "leave";
            c.Why += $"; no clean spot to pick it up from ({why})";
            if (Settings.LogLoot.Value)
                HandsAreHighPlugin.Log.LogInfo($"{name}: no clean spot by {s?.Name}'s {c.Name} ({why}): {(c.Decision == "after release" ? "takes it after letting him go" : "leaves it")}");
        }

        // A short step back for the man, away from the item (and from his captor's side), on the
        // NavMesh, his capsule fits, a short complete path.
        private static bool StepBackSpot(SurrenderState s, Vector3 item, Player captor, out Vector3 to)
        {
            to = s.Bot.Position;
            Vector3 away = s.Bot.Position - item; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) { away = s.Bot.Position - captor.Position; away.y = 0f; }
            if (away.sqrMagnitude < 1e-4f) return false;
            away.Normalize();
            int mask = LayersMaskController.PlayerStaticCollisionsMask;
            foreach (float a in StepTurns)
            {
                Vector3 c = s.Bot.Position + Quaternion.AngleAxis(a, Vector3.up) * away * 1.3f;
                if (!UnityEngine.AI.NavMesh.SamplePosition(c, out UnityEngine.AI.NavMeshHit hit, 0.4f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                Vector3 p = hit.position;
                if (Mathf.Abs(p.y - s.Bot.Position.y) > 0.4f || Perception.Flat(p, item) < StandClear + 0.3f) continue;
                if (Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.55f, 0.3f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (!Perception.PathTo(s.Bot.Position, p, out Vector3[] corners, out _) || Captors.PathLength(corners) > 3f) continue;
                to = p;
                return true;
            }
            return false;
        }

        private static void TickJob(LootJob j, float now, ThreatLevel t)
        {
            // 0.19.0: the item is his; straightening up ends early on any interruption.
            if (j.Stage == LootStage.Recover)
            {
                if (now - j.StageSince >= RecoverSeconds || t != ThreatLevel.None || !Perception.Alive(j.Bot)) End(j, null, failed: false);
                return;
            }
            string stop = null;
            if (!Perception.Alive(j.Bot) || j.Owner == null || j.Owner.IsDead) stop = "he is down";
            // 0.23.0: a near pick-up is committed: only fire or an enemy in sight stops it (Alert, an
            // enemy seen a few seconds ago, stops a distant walk only). 0.22 stopped on any threat
            // level and restarted later: the walk-stop-walk the 0.22.0 raid showed as jitter.
            else if (t >= ThreatLevel.Fire || (t == ThreatLevel.Alert && j.Distant && j.Stage == LootStage.Walk)) stop = "something else needs his attention";
            else if (!j.AfterRelease && (j.Prisoner == null || j.Prisoner.Dead || j.Prisoner.Phase != Phase.HandsUp)) stop = "the prisoner stopped complying";
            // 0.19.0: only when he sees it (a man walking off while the captor looks the other way
            // is the prisoner's opening; Captors.Check notices resistance by sight in its own time).
            else if (!j.AfterRelease && j.Prisoner.Bot != null && (j.Prisoner.Bot.Position - j.PrisonerFrom).sqrMagnitude > 1.5f * 1.5f &&
                     Perception.Covering(j.Bot, j.Prisoner.Bot, 70f)) stop = "the prisoner moved";
            // Taken by anyone: its LootItem is destroyed (or recycled for another item).
            else if (!j.Asked && (j.Loose == null || !j.Loose.isActiveAndEnabled || j.Loose.Item != j.C.Item)) stop = $"someone else took it{WhoHas(j.C.Item)}";
            // 0.20.0: his cover gone in a way he can tell (his guard fighting, reloading, down): once.
            else if (!j.AfterRelease && j.CoverRelied && !j.CoverJudged && (j.Stage == LootStage.Walk || j.Stage == LootStage.Settle) &&
                     CoverLossJudgement(j) is string lost) stop = lost;
            // A distant walk gets its walking time on top (1 s per metre of path).
            else if (now - j.Since > Settings.LootTimeLimit.Value + (j.Distant ? Mathf.Max(0f, j.C.PathLen) : 0f)) stop = "took too long";
            else if (j.Stage == LootStage.Walk)
            {
                if ((j.Bot.Position - j.ProgressFrom).sqrMagnitude >= 0.25f) { j.ProgressFrom = j.Bot.Position; j.ProgressSince = now; }
                else if (now - j.ProgressSince > 2.5f) stop = "stuck on the way";
                // 0.19.0: re-acquired by sight on the way (it was kicked, or he only had a glimpse):
                // the new spot replaces the old, a few times at most.
                LootItem li = j.Loose != null && j.Loose.isActiveAndEnabled && j.Loose.Item == j.C.Item ? j.Loose : null;
                if (stop == null && li != null && now >= j.NextSeek)
                {
                    j.NextSeek = now + 0.5f;
                    Vector3 real = li.transform.position;
                    if ((real - j.At).sqrMagnitude > 1f && Notices(j.Bot, j.Prisoner, real))
                    {
                        if (++j.Repaths > 3) stop = "it keeps moving";
                        else
                        {
                            j.At = real; j.C.KnownAt = real; j.PathIssued = false;
                            if (j.HasStand && !Restand(j, now, "it moved")) stop = "no clean spot by it any more";
                        }
                    }
                }
                // 0.23.0: the prisoner stepped into his spot (or moved off his line to it): one new spot,
                // twice at most, checked twice a second (not every frame: no re-path jitter).
                if (stop == null && j.HasStand && !j.AfterRelease && now >= j.NextStandCheck && j.Prisoner?.Bot != null && !j.Prisoner.Dead)
                {
                    j.NextStandCheck = now + 0.5f;
                    Vector3 man = j.Prisoner.Bot.Position;
                    if (Perception.Flat(man, j.Stand) < StandClear - 0.05f && Perception.Flat(man, j.StandFor) > 0.3f)
                    {
                        if (j.Reselects >= 2) stop = "the prisoner keeps getting in the way";
                        else if (!Restand(j, now, "the prisoner moved into his spot")) stop = "no clean spot by it any more";
                    }
                }
                Vector3 d = j.At - j.Bot.Position; d.y = 0f;
                float dist = d.magnitude;
                // 0.19.0: he walks to ~0.7 m (in reach for a look over 60 deg down: the game's crouch
                // to the floor); a path that ends short of that settles once he has come to a stop.
                // 0.23.0: with a stand spot, arriving is reaching that spot (and the item is in reach from it).
                bool slow = j.Bot.Velocity.sqrMagnitude < 0.2f * 0.2f && now - j.StageSince > 0.8f;
                bool there = j.HasStand
                    ? Perception.Flat(j.Bot.Position, j.Stand) <= 0.25f || (Perception.Flat(j.Bot.Position, j.Stand) <= 0.6f && dist <= StandMax + 0.2f && slow)
                    : dist <= 0.95f || (dist <= 1.45f && slow && now - j.StageSince > 1f);
                if (stop == null && there)
                {
                    if (li == null || (li.transform.position - j.At).sqrMagnitude > 1.6f * 1.6f) stop = "it isn't where he saw it";
                    else { j.Stage = LootStage.Settle; j.StageSince = now; }
                }
            }
            else if (j.Stage == LootStage.Pickup && j.Asked && !j.Answered && now - j.AskedAt > 4f)
            {
                // The animation never called back (he left the idle state): the same transaction
                // without it, once (as Arsenal.PickUp does for a prisoner's own gun).
                Fallback(j);
            }
            if (stop == null) return;
            // 0.18.0: a threat or the man moving interrupts it; the item is weighed again when the
            // situation changes (twice at most), never retried at once. Anything else (gone, stuck,
            // too long, no room) ends it for good.
            bool interrupted = stop == "something else needs his attention" || stop == "the prisoner moved" || stop == "the prisoner stopped complying" || stop.StartsWith("his cover is gone");
            if (stop == "it isn't where he saw it") j.C.Seen = false;   // what he knew is wrong now
            End(j, stop, failed: true);   // always the full cleanup (a pick-up animation cut short)
            if (interrupted && j.C.Tries < 2)
            {
                j.C.Decision = "later";
                j.C.Why += "; weighed again later";
                if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo($"{j.Name}: will weigh the {j.C.Name} again when things change (try {j.C.Tries} of 2)");
            }
        }

        // CaptorLogic, standing still and settled over it: the game's pick-up.
        internal static void Settled(LootJob j, float now)
        {
            if (j.Stage != LootStage.Settle || now - j.StageSince < 0.3f) return;
            if (!(j.Bot.CurrentManagedState is IdlePlayerState))
            {
                if (now - j.StageSince > 1.5f) { j.Stage = LootStage.Pickup; j.Asked = true; j.AskedAt = now - 5f; }   // never idle: the fallback next check
                return;
            }
            // 0.19.0: the look has arrived on the item and his hands are free for the reach (the
            // game's SetInteractInHands skips the hands' TakeItem unless FirearmsAnimator.IsIdling);
            // aiming is lowered first. At most 1.5 s of waiting, then he picks it up as he is.
            Player bot = j.Bot;
            if (bot.HandsController is Player.FirearmController fc && fc.IsAiming) { try { fc.SetAim(false); } catch { } }
            bool late = now - j.StageSince > 1.5f;
            if (!late)
            {
                Vector3 want = PickupLook(bot, j.At);
                if (Vector3.Angle(bot.LookDirection, want) > 6f) return;
                FirearmsAnimator fa = bot.HandsController?.FirearmsAnimator;
                if (fa != null && !fa.IsIdling()) return;
            }
            // Still lying there (taken in the last quarter second: TickJob's check runs 4x a second).
            if (j.Loose == null || !j.Loose.isActiveAndEnabled || j.Loose.Item != j.C.Item) { End(j, $"someone else took it{WhoHas(j.C.Item)}", failed: true); return; }
            j.Stage = LootStage.Pickup;
            j.StageSince = now;
            Player p = j.Bot;
            InventoryController ic = p.InventoryController;
            if (AddressFor(p, j.C.Item, out string fit, out _) == null) { End(j, $"no room any more ({fit})", failed: true); return; }
            var op = ItemManipulator.QuickFindAppropriatePlace(j.C.Item, ic, new CompoundItem[] { ic.Inventory.Equipment }, ItemManipulator.EMoveItemOrder.PickUp, simulate: true);
            if (op.Failed) { End(j, $"the game found no place for it ({op.Error})", failed: true); return; }
            IOperationResult move = op.Value;
            IPlayer lastOwner = j.Loose != null ? j.Loose.LastOwner : null;
            j.Asked = true;
            j.Animated = true;
            j.AskedAt = now;
            try
            {
                p.CurrentManagedState.Pickup(true, () =>
                {
                    if (!Jobs.TryGetValue(p, out LootJob cur) || cur != j || j.Answered) { LeavePickupState(p); return; }
                    Run(j, move, lastOwner);
                });
            }
            catch (System.Exception e) { HandsAreHighPlugin.Report("captor pick-up", e); Fallback(j); }
        }

        // Where his eyes go for the pick-up: from his eyes to the item (not from his feet).
        internal static Vector3 PickupLook(Player bot, Vector3 item)
        {
            Vector3 v = item + Vector3.up * 0.05f - Perception.Eye(bot);
            return v.sqrMagnitude > 1e-4f ? v.normalized : bot.LookDirection;
        }

        internal const float RecoverSeconds = 0.6f;

        // The game's own rule (InteractionContextHelper, the player's pick-up): only a man still in
        // the pick-up state is taken out of it. IdlePlayerState.Pickup ignores its flag and STARTS a
        // pick-up, so Pickup(false) on anyone else replayed the animation and held him up to 5 s.
        internal static void LeavePickupState(Player p)
        {
            try { if (p != null && p.CurrentManagedState is PickUpState ps) ps.Pickup(false, null); } catch { }
        }

        private static void Fallback(LootJob j)
        {
            if (j.Answered) return;
            j.Animated = false;
            Player p = j.Bot;
            InventoryController ic = p.InventoryController;
            LeavePickupState(p);
            var op = ItemManipulator.QuickFindAppropriatePlace(j.C.Item, ic, new CompoundItem[] { ic.Inventory.Equipment }, ItemManipulator.EMoveItemOrder.PickUp, simulate: true);
            if (op.Failed) { End(j, $"no place for it ({op.Error})", failed: true); return; }
            if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo($"{j.Name}: no pick-up animation (state {p.CurrentManagedState?.GetType().Name}); takes the {j.C.Name} directly");
            Run(j, op.Value, j.Loose != null ? j.Loose.LastOwner : null);
        }

        // BotItemTaker.PickupAction's own sequence.
        private static void Run(LootJob j, IOperationResult move, IPlayer lastOwner)
        {
            Player p = j.Bot;
            j.Answered = true;
            // 0.19.0: as the player's own pick-up (InteractionContextHelper): the move was planned
            // 0.6 s ago; if the item has left that place since (someone took it), it is refused.
            if (move is IItemOperationResult planned && planned.From != null && !planned.From.Equals(j.C.Item.CurrentAddress))
            {
                LeavePickupState(p);
                End(j, "someone else took it during the pick-up", failed: true);
                return;
            }
            try
            {
                if (move is MoveResult && lastOwner != null && lastOwner.ProfileId != p.ProfileId)
                {
                    if (j.C.Item is Magazine mag) p.InventoryController.StrictCheckMagazine(mag, false);
                    if (j.C.Item is ContainerCollection col)
                        foreach (Item it in col.GetAllItemsFromCollection())
                            if (it is Magazine m2) p.InventoryController.StrictCheckMagazine(m2, false);
                }
                p.InventoryController.RunNetworkTransaction(move, result =>
                {
                    bool ok = result != null && result.Succeed;
                    try { if (ok) p.UpdateInteractionCast(); } catch { }
                    LeavePickupState(p);
                    if (Jobs.TryGetValue(p, out LootJob cur) && cur == j) Finish(j, ok, ok ? null : (result?.Error?.ToString() ?? "refused"));
                    else if (Settings.LogLoot.Value)
                        HandsAreHighPlugin.Log.LogInfo($"{j.Name}: the {j.C.Name} transaction finished after the fetch had ended ({(ok ? "it is his now" : "refused")})");
                });
            }
            catch (System.Exception e)
            {
                HandsAreHighPlugin.Report("captor pick-up transaction", e);
                End(j, $"the transaction threw {e.GetType().Name}", failed: true);
            }
        }

        private static void Finish(LootJob j, bool ok, string error)
        {
            if (!ok) { End(j, $"the game refused the move ({error}): someone else may have taken it", failed: true); return; }
            // Where it really is now (read back from his equipment, by identity).
            string where = "?";
            try
            {
                Item root = j.C.Item.Owner?.RootItem;
                if (root != j.Bot.Equipment) { End(j, "reported done but the item isn't in his equipment", failed: true); return; }
                where = j.C.Item.Parent?.Container is Slot sl ? $"his {sl.ID} slot" : $"his {j.C.Item.Parent?.Container?.ParentItem?.ShortName?.Localized() ?? "kit"}";
            }
            catch { }
            j.C.Decision = "taken";
            Claimed.Remove(j.C.Item);
            if (Settings.LogLoot.Value)
                HandsAreHighPlugin.Log.LogInfo($"{j.Name}: took {j.Prisoner?.Name}'s {j.C.Name} into {where} ({j.C.Value:0} roubles; {(j.Animated ? "pick-up animation" : "no animation")}; {Time.time - j.Since:0.0} s); straightens up");
            // 0.19.0: a moment to straighten up and look back before he guards (or moves on) again.
            j.Stage = LootStage.Recover;
            j.StageSince = Time.time;
        }

        private static void End(LootJob j, string why, bool failed)
        {
            if (j.Stage == LootStage.Recover) failed = false;   // already his: nothing failed
            if (Jobs.TryGetValue(j.Bot, out LootJob cur) && cur == j) Jobs.Remove(j.Bot);
            Claimed.Remove(j.C.Item);
            if (failed)
            {
                j.C.Decision = "failed";
                j.C.Why += $"; stopped: {why}";
                if (j.Asked && !j.Answered) LeavePickupState(j.Bot);
                if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo($"{j.Name}: stops going for the {j.C.Name}: {why}");
            }
        }

        // The captor's custody ended (released, fight, his death): his fetch ends with it; what he
        // still wanted for later is kept for after a release only.
        internal static void OnCustodyEnd(CaptorState cs, bool released)
        {
            if (TryJob(cs.Bot, out LootJob j) && !j.AfterRelease) End(j, released ? "let him go first" : "custody over", failed: true);
            if (!released || !Settings.LootAfterRelease.Value || cs.Loot == null || cs.Unsafe || !Perception.Alive(cs.Bot)) return;
            Defer(cs.Owner, cs.Prisoner, cs.Loot, "once he is well away");
        }

        // 0.18.0: a kill decision carried out (Judgement.TickKills): what he still wanted of the
        // man's, the weapons he made him drop first, is fetched now (the same fetch as after a
        // release; the dead man is "well away").
        internal static void AfterKill(BotOwner o, SurrenderState s, LootPlan plan)
        {
            if (o == null || plan == null || !Settings.CaptorLooting.Value || !Perception.Alive(o.GetPlayer)) return;
            Defer(o, s, plan, "now that he is dead");
        }

        private static void Defer(BotOwner o, SurrenderState ex, LootPlan plan, string when)
        {
            Player bot = o?.GetPlayer;
            if (bot == null) return;
            var d = new Deferred { Owner = o, Ex = ex, Since = Time.time, NextAt = Time.time + 1f };
            foreach (LootCandidate c in plan.Items)
                if (c.Wanted && (c.Decision == "later" || c.Decision == "take" || c.Decision == "after release" ||
                                 (c.Decision == "failed" && (c.Why.Contains("let him go first") || c.Why.Contains("custody over")))))
                { c.Decision = "later"; d.Items.Add(c); }
            if (d.Items.Count == 0) return;
            Later[bot] = d;
            if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo($"{Restraint.Name(bot)}: will take {d.Items.Count} item(s) of {ex?.Name}'s {when}");
        }

        // ---- after a release (Captors.Tick, 4x a second) -----------------------------------------

        internal static void TickLater(float now)
        {
            if (Jobs.Count > 0)
            {
                Scratch.Clear();
                foreach (LootJob j in Jobs.Values) if (j.AfterRelease) Scratch.Add(j.Bot);
                foreach (Player p in Scratch)
                    if (Jobs.TryGetValue(p, out LootJob j)) TickJob(j, now, ThreatOf(j.Owner, j.Prisoner?.Bot));
            }
            if (Later.Count == 0) return;
            Scratch.Clear();
            foreach (KeyValuePair<Player, Deferred> kv in Later) Scratch.Add(kv.Key);
            foreach (Player p in Scratch)
            {
                Deferred d = Later[p];
                if (now < d.NextAt || Jobs.ContainsKey(p)) continue;
                d.NextAt = now + 0.5f;
                string stop = null;
                if (d.Owner == null || d.Owner.IsDead || !Perception.Alive(p)) stop = "he is down";
                else if (Captors.TryGet(p, out _)) stop = "guarding someone again";
                else if (SurrenderRegistry.HoldsBot(p)) stop = "surrendered himself";
                else if (now - d.Since > Settings.LootTimeLimit.Value * (d.Items.Count + 1)) stop = "took too long to be safe";
                else if (ThreatOf(d.Owner, d.Ex?.Bot) != ThreatLevel.None) stop = "something else needs his attention";
                if (stop == null)
                {
                    Player ex = d.Ex?.Bot;
                    bool away = ex == null || d.Ex.Dead || !Perception.Alive(ex) || Vector3.Distance(ex.Position, p.Position) > 15f ||
                                !Perception.LineClear(Perception.Eye(p), Perception.Eye(ex));
                    if (!away) continue;   // waits for the man to be well away
                    LootCandidate next = Pick(d.Items, "later");
                    if (next == null) { Later.Remove(p); continue; }
                    LootItem li = LooseOf(next);
                    if (li != null && !next.Seen) Track(p, null, next, now);   // what he can see of it from here
                    float far = Settings.LootMaxDistance.Value;
                    if (li == null || !next.Seen || (next.KnownAt - p.Position).sqrMagnitude > far * far)
                    {
                        next.Decision = "leave";
                        next.Why += li == null ? "; gone before he could fetch it" : !next.Seen ? "; he doesn't know where it is" : $"; he has moved on (over {far:0} m from it)";
                        if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo($"{Restraint.Name(p)}: leaves {d.Ex?.Name}'s {next.Name} ({(li == null ? "gone" : !next.Seen ? "doesn't know where it is" : "he has moved on")})");
                        continue;
                    }
                    Start(d.Owner, d.Ex, next, afterRelease: true, now);
                    continue;
                }
                Later.Remove(p);
                if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo($"{Restraint.Name(p)}: leaves {d.Ex?.Name}'s gear after all ({stop})");
            }
            Scratch.Clear();
        }

        // Without custody: another enemy in sight, or fire on him (Restraint.OtherThreat).
        private static ThreatLevel ThreatOf(BotOwner o, Player ex)
        {
            if (o == null) return ThreatLevel.Contact;
            if (Restraint.OtherThreat(o, ex, out _)) return ThreatLevel.Contact;
            if (o.Memory != null && o.Memory.IsUnderFire) return ThreatLevel.Fire;
            return ThreatLevel.None;
        }
    }
}
