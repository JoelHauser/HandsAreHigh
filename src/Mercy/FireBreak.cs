using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace Mercy
{
    // 0.21.0: incoming fire breaks a hold-up.
    //
    // Whenever the prisoner or his captor (a bot with custody, or a human whose surrender he
    // acknowledged) comes under credible fire, both drop the hold-up and act to survive:
    //   - the captor's custody ends at once (Captors.Interrupt): no orders, no gear fetch, no
    //     squad cover, no "fetch it after the release"; his layer lets go and the combat AI
    //     (SAIN / vanilla) has him with its own perception;
    //   - the prisoner gets out of it (SurrenderLogic.UnderFire): to cover away from the fire,
    //     a draw of a weapon he still carries, or a dash for his own dropped gun when it is close,
    //     reachable and not out towards the shooter; then his own AI has him (Phase.Released).
    // A human captor is never touched: only the prisoner's restrictions end.
    //
    // Credible fire, measured, never assumed:
    //   - a hit on either of them (Player.BeingHitAction: the prisoner's own hook in
    //     SurrenderRegistry, one hook per captor here, as the game's boss layers subscribe);
    //   - a near miss: a shot whose line passes within "Near miss counts within" of either chest,
    //     in front of the muzzle, with no wall in between (the same geometry as Suppression);
    //   - for a bot captor, his own threat sense at Fire (BotMemory.IsUnderFire, our near misses,
    //     SAIN's suppression), read by Captors.Check 4x a second.
    // Not credible: shots by the prisoner, by the captor himself (a warning shot, a decided
    // execution), or by anyone on the captor's side (his squad / a bot not hostile to him; your
    // own teammates for a human captor). Gunfire elsewhere on the map never comes near either
    // chest, so it never counts.
    //
    // Detection only queues the event (no state change inside another player's shot or hit
    // code); Tick acts on it a moment later (a hit at once, a near miss after a short reaction).
    // While the same fire lasts neither man can be taken (or give up) again: FireBreakUntil in
    // each man's raid history, extended by every new near miss or hit on him.
    internal static class FireBreak
    {
        private struct Pending
        {
            internal SurrenderState S;
            internal Player Victim;
            internal bool OnCaptor;
            internal Player Shooter;
            internal Vector3 From;
            internal bool FromKnown;
            internal bool Hit;
            internal float Miss;
            internal float At;
        }

        private static readonly List<Pending> Queue = new List<Pending>(4);
        private static readonly Dictionary<Player, System.Action<EFT.Ballistics.DamageInfo, EBodyPart, float>> Hooks =
            new Dictionary<Player, System.Action<EFT.Ballistics.DamageInfo, EBodyPart, float>>();
        private static readonly List<Player> Want = new List<Player>(8);
        private static readonly List<Player> Drop = new List<Player>(8);
        private static float _nextSync;

        internal static int Broken;   // hold-ups broken this raid (stats line)

        // ---- who is in a hold-up --------------------------------------------------------

        // A man holding his hands up (an answered surrender or not): his captivity can break.
        internal static bool InCaptivity(SurrenderState s) =>
            s != null && !s.Dead && !s.Suspended && s.Bot != null && s.Phase == Phase.HandsUp;

        // The other side of it: the bot with custody, or a human whose surrender he acknowledged.
        internal static Player CaptorOf(SurrenderState s)
        {
            if (s.Custody != null) return s.Custody.Bot;
            Player c = s.Captor;
            return c != null && !c.IsAI && s.Accepted && Perception.Alive(c) ? c : null;
        }

        // ---- credibility ------------------------------------------------------------------

        // Fire from this shooter breaks this hold-up (null shooter: an unseen source, from a hit).
        internal static bool Credible(SurrenderState s, Player shooter, bool hit = false)
        {
            if (shooter == null) return true;
            if (shooter == s.Bot) return false;
            if (shooter == s.Captor) return false;   // his warning shot, or a decided execution
            if (SurrenderRegistry.TryGet(shooter, out SurrenderState held) && held.Phase == Phase.HandsUp) return false;
            Player captor = CaptorOf(s) ?? s.Captor;
            return captor == null || !SameSide(shooter, captor, hit);
        }

        // On the captor's side: his own squad or an ally of it, or (human captor) one of your
        // teammates or your own group.
        // 0.22.0: no longer "any shooter the captor isn't hostile to". BotsGroup.IsEnemy only knows
        // players already in the group's Enemies (0.16.9 BotsGroup.IsEnemy walks that list) and an
        // EnemyInfo exists only once he has noticed them, so a shooter he hadn't seen yet (the very
        // case of being shot from somewhere) read as "his side": the 0.21.0 raid's PMC captor was hit
        // by you and neither the hit hook nor his own under-fire sense broke the hold-up. Now only a
        // BOT shooter he isn't hostile to, and only for a near miss (a stray round from a friendly
        // Scav's fight passing him), is left out; anyone else who shoots at him or hits him counts.
        private static bool SameSide(Player shooter, Player captor, bool hit)
        {
            try
            {
                if (captor.IsAI)
                {
                    BotOwner co = captor.AIData?.BotOwner;
                    if (co == null) return false;
                    BotOwner so = shooter.IsAI ? shooter.AIData?.BotOwner : null;
                    if (so != null && so.BotsGroup != null && so.BotsGroup == co.BotsGroup) return true;
                    if (co.BotsGroup != null && co.BotsGroup.IsAlly(shooter)) return true;
                    return so != null && !hit && !Suppression.IsHostile(co, shooter);
                }
                if (shooter.IsAI) return Teammates.IsPlayerTeammate(shooter.AIData?.BotOwner);
                string a = captor.Profile?.Info?.GroupId, b = shooter.Profile?.Info?.GroupId;
                return !string.IsNullOrEmpty(a) && a == b;
            }
            catch { return false; }
        }

        // ---- detection --------------------------------------------------------------------

        // Every shot by anyone while someone is held (ShotPatch): a near miss on a prisoner or his
        // captor from a credible shooter. Cheapest tests first (a few multiplies per encounter).
        internal static void OnShot(Player shooter, Vector3 from, Vector3 dir)
        {
            if (!Settings.FireBreaks.Value) return;
            float radius = Settings.FireBreakNearMiss.Value;
            float range = Settings.NearMissRange.Value;
            foreach (SurrenderState s in SurrenderRegistry.All)
            {
                if (s.Dead || s.Suspended || s.Bot == null || s.FirePending) continue;
                bool held = s.Phase == Phase.HandsUp;
                // After he got away from the hold-up: fire at him keeps the no-recapture window open
                // and lets his escape react (UnderFire), but only while he is still ours.
                if (s.Phase == Phase.Rearmed || s.Phase == Phase.Released) continue;
                if (!Credible(s, shooter)) continue;
                if (NearMiss(s.Bot, from, dir, radius, range, out float miss))
                {
                    Enqueue(s, s.Bot, false, shooter, from, true, false, miss);
                    continue;
                }
                if (!held) continue;
                Player captor = CaptorOf(s);
                if (captor != null && captor != shooter && NearMiss(captor, from, dir, radius, range, out miss))
                    Enqueue(s, captor, true, shooter, from, true, false, miss);
            }
        }

        private static bool NearMiss(Player p, Vector3 from, Vector3 dir, float radius, float range, out float miss)
        {
            miss = 0f;
            Vector3 chest = Perception.Chest(p);
            float along = Vector3.Dot(chest - from, dir);
            if (along <= 0f || along > range) return false;
            Vector3 closest = from + dir * along;
            float m2 = (chest - closest).sqrMagnitude;
            if (m2 > radius * radius) return false;
            if (Physics.Linecast(from, closest, LayersMaskController.HighPolyWithTerrainMask)) return false;
            miss = Mathf.Sqrt(m2);
            return true;
        }

        // A hit on the prisoner (his own hook, SurrenderRegistry) or on a captor (Hooks below).
        internal static void OnHit(SurrenderState s, Player victim, bool onCaptor, EFT.Ballistics.DamageInfo info)
        {
            if (!Settings.FireBreaks.Value || s == null || s.Dead || s.FirePending) return;
            if (s.Phase == Phase.Rearmed || s.Phase == Phase.Released) return;
            if (onCaptor && s.Phase != Phase.HandsUp) return;
            Player shooter = null;
            try { shooter = Perception.PlayerOf(info.Player?.iPlayer); } catch { }
            // 0.22.0: a debug scenario's observer who shoots a participant is no longer an observer
            // to that participant's squad (they may fight him; ScenarioShield).
            if (shooter != null && ScenarioShield.Active) ScenarioShield.ObserverAttacked(victim, shooter);
            if (!Credible(s, shooter, hit: true)) return;
            Vector3 from;
            bool known;
            if (shooter != null) { from = shooter.Position; known = true; }
            else
            {
                Vector3 d = info.Direction;
                known = d.sqrMagnitude > 1e-4f;
                from = known ? victim.Position - d.normalized * 25f : victim.Position;
            }
            Enqueue(s, victim, onCaptor, shooter, from, known, true, 0f);
        }

        // A bot captor's threat sense says "under fire" (Captors.Check): the same break, no shooter.
        internal static void OnCaptorFire(CaptorState cs, string why, Vector3 at, bool atKnown)
        {
            SurrenderState s = cs?.Prisoner;
            if (!Settings.FireBreaks.Value || s == null || s.FirePending || !InCaptivity(s)) return;
            Enqueue(s, cs.Bot, true, null, at, atKnown, false, -1f, why);
        }

        private static void Enqueue(SurrenderState s, Player victim, bool onCaptor, Player shooter, Vector3 from, bool known, bool hit, float miss, string sense = null)
        {
            for (int i = 0; i < Queue.Count; i++)
                if (Queue[i].S == s) { if (hit && !Queue[i].Hit) { Pending q = Queue[i]; q.Hit = true; q.At = Time.time; Queue[i] = q; } return; }
            // A hit is felt at once; the crack of a near miss (or the captor's sense of it) after a
            // short reaction of the man it came near.
            float react = hit ? 0f : onCaptor ? Dice.Range(0.12f, 0.3f) : Mathf.Min(0.35f, 0.5f * s.NoticeDelay());
            Queue.Add(new Pending { S = s, Victim = victim, OnCaptor = onCaptor, Shooter = shooter, From = from, FromKnown = known, Hit = hit, Miss = miss, At = Time.time + react });
            s.FireSense = sense;
        }

        // Debug scenarios: a near miss or a hit on this man (a prisoner, or the captor of one) from
        // `dist` m off to one side, put through the same queue a real one takes. SIMULATED: no shot,
        // no damage; it tests the reactions, never the detection (a real third party tests that).
        internal static string Simulate(Player target, bool hit, float dist = 30f)
        {
            if (target == null) return "no target";
            foreach (SurrenderState s in SurrenderRegistry.All)
            {
                if (s.Dead || s.Bot == null || s.Phase == Phase.Rearmed || s.Phase == Phase.Released) continue;
                bool onCaptor = s.Bot != target;
                if (onCaptor && (!InCaptivity(s) || CaptorOf(s) != target)) continue;
                Vector3 side = Quaternion.Euler(0f, Dice.Range(60f, 120f) * (Dice.Value < 0.5f ? -1f : 1f), 0f) * target.LookDirection;
                side.y = 0f;
                if (side.sqrMagnitude < 1e-4f) side = Vector3.forward;
                Vector3 from = target.Position + side.normalized * dist + Vector3.up * 1.5f;
                Enqueue(s, target, onCaptor, null, from, true, hit, hit ? 0f : 0.6f);
                return $"{(hit ? "a hit" : "a near miss (0.6 m)")} on {(onCaptor ? "the captor of " + s.Name : s.Name)} from {dist:0} m to his side, queued as incoming fire";
            }
            return "refused: he is neither held nor holding anyone";
        }

        // ---- acting on it (SurrenderRegistry.Tick, every frame) ----------------------------------

        internal static void Tick()
        {
            float now = Time.time;
            if (now >= _nextSync) { _nextSync = now + 0.25f; SyncHooks(); }
            if (Queue.Count == 0) return;
            for (int i = Queue.Count - 1; i >= 0; i--)
            {
                Pending e = Queue[i];
                if (now < e.At) continue;
                Queue.RemoveAt(i);
                try { Act(e, now); }
                catch (System.Exception ex) { MercyPlugin.Report("incoming fire", ex); }
            }
        }

        private static void Act(Pending e, float now)
        {
            SurrenderState s = e.S;
            if (s.Dead || s.Bot == null || !SurrenderRegistry.TryGet(s.Bot, out SurrenderState cur) || cur != s) return;
            if (s.Phase == Phase.Rearmed || s.Phase == Phase.Released) return;
            bool held = s.Phase == Phase.HandsUp;
            Player captor = held ? CaptorOf(s) : null;
            Mark(s.Bot, now, prisoner: true);
            if (captor != null) Mark(captor, now, prisoner: false);
            if (e.Shooter != null) s.FireBy = e.Shooter;

            string what = e.Hit ? "hit" : e.Miss >= 0f ? $"a round {e.Miss:0.0} m past him" : (s.FireSense ?? "under fire");
            string by = e.Shooter != null
                ? $"by {Restraint.Name(e.Shooter)} ({Vector3.Distance(e.Shooter.Position, e.Victim.Position):0} m)"
                : e.FromKnown ? $"from about {Vector3.Distance(e.From, e.Victim.Position):0} m away" : "from somewhere he can't place";
            string victim = e.OnCaptor ? $"{Restraint.Name(e.Victim)} (the captor)" : $"{s.Name} (the prisoner)";

            if (held)
            {
                Broken++;
                if (Settings.LogFireBreak.Value)
                    MercyPlugin.Log.LogInfo($"INCOMING FIRE: {victim} {what} {by}: the hold-up breaks" +
                        $"{(captor != null ? $"; {Restraint.Name(captor)} {(captor.IsAI ? "goes to his own combat AI" : "(you) keeps full control")}" : "")}; {s.Name} gets to safety" +
                        $"{(s.Kneel != KneelPhase.Standing ? " (from his knees: urgent rise)" : "")}{(s.DropPending || s.HandsPending ? " (his hands are still emptying: no draw until they are)" : "")}");
            }
            else if (Settings.LogFireBreak.Value)
                MercyPlugin.Log.LogInfo($"{s.Name}: {what} {by} while {s.Phase}: reacts to it");

            // The prisoner's side first: once he is no longer HandsUp nothing of the captor's
            // custody upkeep reads him as resisting (the captor is told separately, below).
            s.FirePending = true;
            s.FireFrom = e.From;
            s.FireFromKnown = e.FromKnown;
            s.FireHit = e.Hit;
            s.FireTime = now;
            s.WasHit = false;   // the hit is handled here (the layer's "shot with his hands up" is for his own side's rounds)
            if (held && s.Custody != null) Captors.Interrupt(s.Custody, $"{(e.OnCaptor ? "he is" : $"{s.Name} is")} {(e.Hit ? "hit" : "under fire")} {by}");
            else if (held) Captors.EndCustody(s, "under fire");   // an unanswered offer to a bot: dropped
        }

        // While this fire lasts the prisoner gives up to nobody (no hands-up-again loop) and the
        // captor takes nobody. A captor may still give up himself (a new encounter).
        private static void Mark(Player p, float now, bool prisoner)
        {
            BotHistory h = SurrenderRegistry.HistoryOf(p);
            float until = now + Settings.FireBreakQuiet.Value;
            if (prisoner) h.FireBreakUntil = Mathf.Max(h.FireBreakUntil, until);
            else h.NoCustodyUntil = Mathf.Max(h.NoCustodyUntil, until);
        }

        // Every near miss or hit recorded for a man already marked keeps his window open.
        internal static void Extend(BotHistory h, float now)
        {
            if (h == null) return;
            float until = now + Settings.FireBreakQuiet.Value;
            if (h.FireBreakUntil > now) h.FireBreakUntil = Mathf.Max(h.FireBreakUntil, until);
            if (h.NoCustodyUntil > now) h.NoCustodyUntil = Mathf.Max(h.NoCustodyUntil, until);
        }

        // He gives up to nobody (an ex-prisoner still under the fire that freed him).
        internal static bool Blocks(Player p) =>
            p != null && Settings.FireBreaks.Value && SurrenderRegistry.HistoryOf(p).FireBreakUntil > Time.time;

        // He takes nobody (an ex-captor still under that fire), and nobody takes a man under it.
        internal static bool BlocksCustody(Player p) =>
            p != null && Settings.FireBreaks.Value && SurrenderRegistry.HistoryOf(p).NoCustodyUntil > Time.time;

        // ---- captors' hit hooks -----------------------------------------------------------------

        // One BeingHitAction handler per captor of a man in captivity (bot custody or an
        // acknowledged human), added when he becomes one, removed when he no longer is.
        private static void SyncHooks()
        {
            Want.Clear();
            if (Settings.FireBreaks.Value)
                foreach (SurrenderState s in SurrenderRegistry.All)
                {
                    if (!InCaptivity(s)) continue;
                    Player c = CaptorOf(s);
                    if (c != null && Perception.Alive(c) && !Want.Contains(c)) Want.Add(c);
                }
            Drop.Clear();
            foreach (Player p in Hooks.Keys) if (!Want.Contains(p)) Drop.Add(p);
            for (int i = 0; i < Drop.Count; i++) Unhook(Drop[i]);
            for (int i = 0; i < Want.Count; i++)
            {
                Player c = Want[i];
                if (Hooks.ContainsKey(c)) continue;
                System.Action<EFT.Ballistics.DamageInfo, EBodyPart, float> h = (info, part, absorbed) =>
                {
                    try
                    {
                        foreach (SurrenderState s in SurrenderRegistry.All)
                            if (InCaptivity(s) && CaptorOf(s) == c) OnHit(s, c, true, info);
                    }
                    catch (System.Exception ex) { MercyPlugin.Report("captor hit", ex); }
                };
                c.BeingHitAction += h;
                Hooks[c] = h;
            }
            Want.Clear();
            Drop.Clear();
        }

        private static void Unhook(Player p)
        {
            if (!Hooks.TryGetValue(p, out var h)) return;
            Hooks.Remove(p);
            try { if ((object)p != null) p.BeingHitAction -= h; } catch { }
        }

        // Nobody held any more: hooks off, nothing queued (one count test when already idle).
        internal static void Idle()
        {
            if (Hooks.Count == 0 && Queue.Count == 0) return;
            Drop.Clear();
            foreach (Player p in Hooks.Keys) Drop.Add(p);
            for (int i = 0; i < Drop.Count; i++) Unhook(Drop[i]);
            Drop.Clear();
            Queue.Clear();
        }

        // Raid change, mod disabled, hot-reload teardown: every hook off, nothing queued.
        internal static void Clear()
        {
            Drop.Clear();
            foreach (Player p in Hooks.Keys) Drop.Add(p);
            for (int i = 0; i < Drop.Count; i++) Unhook(Drop[i]);
            Drop.Clear();
            Want.Clear();
            Hooks.Clear();
            Queue.Clear();
            Broken = 0;
        }

        internal static int HookCount => Hooks.Count;
    }
}
