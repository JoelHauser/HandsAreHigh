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
    // Coordination: an item another of our captors, a prisoner (TargetWeapon) or a squadmate's own
    // BotItemTaker is going for is left alone; a race lost to anyone else is a failed transaction
    // (the game allows one move), logged. Looting Bots, if installed, is asked not to start its own
    // looting on a captor while he guards or fetches (LootingBotsAdapter; only an exact API match).
    internal enum LootStage { Walk, Settle, Pickup }

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
                if ((j.Bot.Position - j.At).sqrMagnitude > 3f * 3f) return false;
                what = $"walking over to his {j.C.Name}";
                return true;
            }
            what = j.Stage == LootStage.Pickup ? $"picking up his {j.C.Name}" : $"bending for his {j.C.Name}";
            return true;
        }

        internal static bool Fetching(Player captor) => TryJob(captor, out LootJob j) && !j.AfterRelease;

        internal static void Clear()
        {
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
            if (!plan.Assessed)
            {
                if (!Perception.TakeHeavy()) return false;   // a few slot/grid searches and a loot scan: a frame with budget
                Assess(cs, s, plan, situation);
            }
            else if (situation != plan.SituationAt)
            {
                // The situation improved (he knelt, turned away, a squadmate covers him): the risk of
                // what he wanted but didn't dare fetch is weighed again (never the appetite).
                plan.SituationAt = situation;
                foreach (LootCandidate c in plan.Items)
                    if (c.Decision == "later") Reweigh(cs, s, c);
            }
            LootCandidate next = null;
            foreach (LootCandidate c in plan.Items)
                if (c.Decision == "take" && (next == null || c.Value > next.Value)) next = c;
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

        private static void Assess(CaptorState cs, SurrenderState s, LootPlan plan, int situation)
        {
            plan.Assessed = true;
            plan.SituationAt = situation;
            var seen = new HashSet<Item>();
            var all = new List<Item>(s.Dropped);
            if (s.DroppedWeapon != null && !all.Contains(s.DroppedWeapon)) all.Insert(0, s.DroppedWeapon);
            float greed = Greed(cs.Bot, out string nature);
            var log = new StringBuilder($"{cs.Name}: weighs what {s.Name} dropped ({nature} x{greed:0.00}, takes at most {Settings.LootMaxItems.Value}):");
            int n = 0;
            foreach (Item it in all)
            {
                if (it == null || !seen.Add(it)) continue;
                var c = new LootCandidate { Item = it, Name = Compliance.Name(it) };
                plan.Items.Add(c);
                LootItem loose = ScenarioOps.FindLoose(it);
                if (loose == null) { c.Decision = "leave"; c.Why = "not on the ground any more"; }
                else if (Claimed.Contains(it) || ClaimedByOther(cs, it, loose, out _)) { ClaimedByOther(cs, it, loose, out string by); c.Decision = "leave"; c.Why = $"{by ?? "another captor"} is going for it"; }
                else
                {
                    c.Value = Value(it);
                    ItemAddress where = AddressFor(cs.Bot, it, out c.Fit, out c.Gap);
                    if (where == null) { c.Decision = "leave"; c.Why = $"no room for it ({c.Fit})"; }
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
                        else { n++; c.Why = $"{valueTxt}: want {c.Want:0.00}, roll {roll:0.00}"; Reweigh(cs, s, c); }
                    }
                }
                log.Append($"\n    {c.Name}: {c.Decision} ({c.Why})");
            }
            if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo(log.ToString());
        }

        // Take now, later or leave, from the risk of walking over to it now.
        private static void Reweigh(CaptorState cs, SurrenderState s, LootCandidate c)
        {
            LootItem loose = ScenarioOps.FindLoose(c.Item);
            if (loose == null) { c.Decision = "leave"; c.Why += "; gone since"; return; }
            float risk = Risk(cs, s, loose.transform.position, out string riskWhy);
            string before = c.Decision;
            c.Decision = risk <= Settings.LootRisk.Value ? "take" : Settings.LootAfterRelease.Value ? "later" : "leave";
            string verdict = c.Decision == "take" ? "takes it now" : c.Decision == "later" ? "too risky now: later" : "too risky: leaves it";
            int cut = c.Why.IndexOf("; risk ");
            if (cut >= 0) c.Why = c.Why.Substring(0, cut);
            c.Why += $"; risk {risk:0.00} ({riskWhy}) vs {Settings.LootRisk.Value:0.00}: {verdict}";
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
            LootItem loose = ScenarioOps.FindLoose(c.Item);
            if (loose == null) { c.Decision = "leave"; c.Why += "; gone before he went"; return false; }
            if (!Perception.PathTo(o.GetPlayer.Position, loose.transform.position, out _, out string pw))
            {
                c.Decision = "leave";
                c.Why += $"; no way to it ({pw})";
                if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo($"{Restraint.Name(o.GetPlayer)}: can't get to {s.Name}'s {c.Name} ({pw}); leaves it");
                return false;
            }
            var job = new LootJob
            {
                Bot = o.GetPlayer, Owner = o, Name = Restraint.Name(o.GetPlayer), C = c, Loose = loose, At = loose.transform.position,
                Prisoner = s, PrisonerFrom = s.Bot != null ? s.Bot.Position : Vector3.zero, AfterRelease = afterRelease,
                Stage = LootStage.Walk, Since = now, StageSince = now, ProgressFrom = o.GetPlayer.Position, ProgressSince = now
            };
            Jobs[job.Bot] = job;
            Claimed.Add(c.Item);
            c.Decision = "fetching";
            LootingBotsAdapter.Hold(o, Settings.LootTimeLimit.Value + 5f);
            if (Settings.LogLoot.Value)
                HandsAreHighPlugin.Log.LogInfo($"{job.Name}: goes to take {s.Name}'s {c.Name} ({Vector3.Distance(job.Bot.Position, job.At):0.0} m{(afterRelease ? ", after letting him go" : $", {Vector3.Distance(job.At, job.PrisonerFrom):0.0} m from him")})");
            return true;
        }

        private static void TickJob(LootJob j, float now, ThreatLevel t)
        {
            string stop = null;
            if (!Perception.Alive(j.Bot) || j.Owner == null || j.Owner.IsDead) stop = "he is down";
            else if (t != ThreatLevel.None) stop = "something else needs his attention";
            else if (!j.AfterRelease && (j.Prisoner == null || j.Prisoner.Dead || j.Prisoner.Phase != Phase.HandsUp)) stop = "the prisoner stopped complying";
            else if (!j.AfterRelease && j.Prisoner.Bot != null && (j.Prisoner.Bot.Position - j.PrisonerFrom).sqrMagnitude > 1.5f * 1.5f) stop = "the prisoner moved";
            // Taken by anyone: its LootItem is destroyed (or recycled for another item).
            else if (!j.Asked && (j.Loose == null || !j.Loose.isActiveAndEnabled || j.Loose.Item != j.C.Item)) stop = "someone else took it";
            else if (now - j.Since > Settings.LootTimeLimit.Value) stop = "took too long";
            else if (j.Stage == LootStage.Walk)
            {
                if ((j.Bot.Position - j.ProgressFrom).sqrMagnitude >= 0.25f) { j.ProgressFrom = j.Bot.Position; j.ProgressSince = now; }
                else if (now - j.ProgressSince > 2.5f) stop = "stuck on the way";
                Vector3 d = j.At - j.Bot.Position; d.y = 0f;
                // Within the game's own reach for a bot pick-up (BotItemTaker.DIST_TO_TAKE 1.5 m).
                if (stop == null && d.magnitude <= 1.45f) { j.Stage = LootStage.Settle; j.StageSince = now; }
            }
            else if (j.Stage == LootStage.Pickup && j.Asked && !j.Answered && now - j.AskedAt > 4f)
            {
                // The animation never called back (he left the idle state): the same transaction
                // without it, once (as Arsenal.PickUp does for a prisoner's own gun).
                Fallback(j);
            }
            if (stop == null) return;
            End(j, stop, failed: true);
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
                    if (!Jobs.TryGetValue(p, out LootJob cur) || cur != j || j.Answered) { try { p.CurrentManagedState?.Pickup(false, null); } catch { } return; }
                    Run(j, move, lastOwner);
                });
            }
            catch (System.Exception e) { HandsAreHighPlugin.Report("captor pick-up", e); Fallback(j); }
        }

        private static void Fallback(LootJob j)
        {
            if (j.Answered) return;
            j.Animated = false;
            Player p = j.Bot;
            InventoryController ic = p.InventoryController;
            try { p.CurrentManagedState?.Pickup(false, null); } catch { }
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
                    try { p.CurrentManagedState?.Pickup(false, null); } catch { }
                    if (Jobs.TryGetValue(p, out LootJob cur) && cur == j) Finish(j, ok, ok ? null : (result?.Error?.ToString() ?? "refused"));
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
            if (Settings.LogLoot.Value)
                HandsAreHighPlugin.Log.LogInfo($"{j.Name}: took {j.Prisoner?.Name}'s {j.C.Name} into {where} ({j.C.Value:0} roubles; {(j.Animated ? "pick-up animation" : "no animation")}; {Time.time - j.Since:0.0} s)");
            End(j, null, failed: false);
        }

        private static void End(LootJob j, string why, bool failed)
        {
            if (Jobs.TryGetValue(j.Bot, out LootJob cur) && cur == j) Jobs.Remove(j.Bot);
            Claimed.Remove(j.C.Item);
            if (failed)
            {
                j.C.Decision = "failed";
                j.C.Why += $"; stopped: {why}";
                try { if (j.Asked && !j.Answered) j.Bot.CurrentManagedState?.Pickup(false, null); } catch { }
                if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo($"{j.Name}: stops going for the {j.C.Name}: {why}");
            }
        }

        // The captor's custody ended (released, fight, his death): his fetch ends with it; what he
        // still wanted for later is kept for after a release only.
        internal static void OnCustodyEnd(CaptorState cs, bool released)
        {
            if (TryJob(cs.Bot, out LootJob j) && !j.AfterRelease) End(j, released ? "let him go first" : "custody over", failed: true);
            if (!released || !Settings.LootAfterRelease.Value || cs.Loot == null || cs.Unsafe || !Perception.Alive(cs.Bot)) return;
            var d = new Deferred { Owner = cs.Owner, Ex = cs.Prisoner, Since = Time.time, NextAt = Time.time + 1f };
            foreach (LootCandidate c in cs.Loot.Items)
                if ((c.Decision == "later" || c.Decision == "take" || (c.Decision == "failed" && c.Why.Contains("let him go first"))) && c.Wanted) { c.Decision = "later"; d.Items.Add(c); }
            if (d.Items.Count == 0) return;
            Later[cs.Bot] = d;
            if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo($"{cs.Name}: will take {d.Items.Count} item(s) of {cs.Prisoner?.Name}'s once he is well away");
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
                    LootCandidate next = null;
                    foreach (LootCandidate c in d.Items) if (c.Decision == "later" && (next == null || c.Value > next.Value)) next = c;
                    if (next == null) { Later.Remove(p); continue; }
                    LootItem li = ScenarioOps.FindLoose(next.Item);
                    if (li == null || (li.transform.position - p.Position).sqrMagnitude > 25f * 25f)
                    {
                        next.Decision = "leave";
                        next.Why += li == null ? "; gone before he could fetch it" : "; he has moved on (over 25 m from it)";
                        if (Settings.LogLoot.Value) HandsAreHighPlugin.Log.LogInfo($"{Restraint.Name(p)}: leaves {d.Ex?.Name}'s {next.Name} ({(li == null ? "gone" : "he has moved on")})");
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
