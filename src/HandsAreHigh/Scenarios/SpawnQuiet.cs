using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace HandsAreHigh
{
    // Scenario-spawned bots only (0.14.0, Joel: "remove the ammo check when I spawn in bots, not
    // overall"): no magazine check in the first moments after a scenario bot arrives.
    //
    // Where the spawn-time check comes from (both verified in the decompiles; both fire for every
    // bot in a raid, which is why ordinary raid spawns are left exactly as they are):
    //   - Vanilla: BotMagazineChecker._nextPosibleCheckMagazine starts at 0, so the first
    //     ManualUpdate (PatrolMoveSimple, the patrol layer) calls ShootController.CheckAmmo() at
    //     once, then every GreateRandom(Patrol.CHECK_MAGAZIN_PERIOD).
    //   - SAIN 4.5.1: Firemode.TryCheckWeapon (from BotWeaponInfoClass.ManualUpdate, no enemy)
    //     has CheckMagTimer and NextCheckTimer at 0, so it plays FirearmsAnimator.CheckAmmo() on
    //     its first update, then every 360 s x 0.5-1.5 (a chamber check 30 s apart from it).
    // Neither is blocked. Each is SCHEDULED as if a check had just been done, before its first
    // update can run: vanilla's timer to now + its own random period, SAIN's to now + its own
    // period (NextCheckTimer 30 s, so the next check of either kind is a normal later one).
    // Nothing touches the hands controller, the weapon, a running operation or a busy flag;
    // later checks, reloads and readiness are the game's and SAIN's as always.
    // SAIN makes its BotComponent around activation: the scenario bot is polled for up to 6 s;
    // if SAIN's first check already ran by then (its timer is no longer 0) that is logged.
    internal static class SpawnQuiet
    {
        private sealed class Entry
        {
            internal Player P;
            internal BotOwner O;
            internal string Name;
            internal float Since;
            internal bool VanillaDone, SainDone;
            internal string VanillaNote, SainNote;
        }

        private static readonly List<Entry> Pending = new List<Entry>(4);
        private static PropertyInfo _info, _weaponInfo, _firemode;
        private static FieldInfo _magTimer, _nextTimer;
        private static bool _sainResolved, _sainOk;

        internal static void ResolveSain(Assembly sain)
        {
            if (_sainResolved || sain == null) return;
            _sainResolved = true;
            try
            {
                Type comp = sain.GetType("SAIN.Components.BotComponent");
                Type infoT = sain.GetType("SAIN.SAINComponent.Classes.Info.SAINBotInfoClass");
                Type wiT = sain.GetType("SAIN.SAINComponent.Classes.Info.BotWeaponInfoClass");
                Type fmT = sain.GetType("SAIN.SAINComponent.Classes.WeaponFunction.Firemode");
                _info = comp != null ? AccessTools.Property(comp, "Info") : null;
                _weaponInfo = infoT != null ? AccessTools.Property(infoT, "WeaponInfo") : null;
                _firemode = wiT != null ? AccessTools.Property(wiT, "Firemode") : null;
                _magTimer = fmT != null ? AccessTools.Field(fmT, "CheckMagTimer") : null;
                _nextTimer = fmT != null ? AccessTools.Field(fmT, "NextCheckTimer") : null;
                _sainOk = _info != null && _weaponInfo != null && _firemode != null && _magTimer != null && _nextTimer != null;
                if (!_sainOk) HandsAreHighPlugin.Log.LogWarning("Scenario spawn: SAIN's magazine-check timers not found (different SAIN version?); SAIN's spawn check is left alone");
            }
            catch (Exception e) { _sainOk = false; HandsAreHighPlugin.Log.LogWarning($"Scenario spawn: SAIN timer lookup failed: {e.Message}"); }
        }

        // ScenarioSpawner.Spawned: the bot exists and was handed over by the game.
        internal static void Add(BotOwner bot, string name)
        {
            if (bot == null || !Settings.ScenarioNoSpawnMagCheck.Value) return;
            var e = new Entry { O = bot, P = bot.GetPlayer, Name = name, Since = Time.time };
            Vanilla(e);
            Pending.Add(e);
        }

        private static void Vanilla(Entry e)
        {
            BotMagazineChecker mc = e.O.MagazineChecker;
            if (mc == null) { e.VanillaNote = "vanilla: no magazine checker"; e.VanillaDone = true; return; }
            if (mc._nextPosibleCheckMagazine > Time.time)
            {
                e.VanillaNote = $"vanilla: already scheduled in {mc._nextPosibleCheckMagazine - Time.time:0} s";
            }
            else if (mc._nextPosibleCheckMagazine > 0f)
            {
                e.VanillaNote = "vanilla: its first check had already run";
            }
            else
            {
                float period = e.O.Settings?.FileSettings?.Patrol != null ? e.O.Settings.FileSettings.Patrol.CHECK_MAGAZIN_PERIOD : 30f;
                mc._nextPosibleCheckMagazine = Time.time + MyExtensions.GreateRandom(period);
                e.VanillaNote = $"vanilla: first check in {mc._nextPosibleCheckMagazine - Time.time:0} s";
            }
            e.VanillaDone = true;
        }

        // Every frame while any is pending (Scenarios.Tick): a few bots, a few reflection reads.
        internal static void Tick()
        {
            if (Pending.Count == 0) return;
            float now = Time.time;
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                Entry e = Pending[i];
                bool gone = e.O == null || e.O.IsDead || !Perception.Alive(e.P);
                if (!gone && !e.SainDone)
                {
                    if (!_sainOk) { e.SainDone = true; e.SainNote = _sainResolved ? "SAIN: timers unknown, left alone" : null; }
                    else TrySain(e, now);
                }
                if (gone || e.SainDone || now - e.Since > 6f)
                {
                    if (!gone && !e.SainDone) e.SainNote = "SAIN: no component within 6 s, left alone";
                    if (!gone)
                        HandsAreHighPlugin.Log.LogInfo($"Scenario spawn: {e.Name}: no magazine check at spawn ({e.VanillaNote}{(e.SainNote != null ? "; " + e.SainNote : "")})");
                    Pending.RemoveAt(i);
                }
            }
        }

        private static void TrySain(Entry e, float now)
        {
            try
            {
                object comp = SainAdapter.Component(e.O);
                object info = comp != null ? _info.GetValue(comp, null) : null;
                object wi = info != null ? _weaponInfo.GetValue(info, null) : null;
                object fm = wi != null ? _firemode.GetValue(wi, null) : null;
                if (fm == null) return;   // not made yet: next frame
                float mag = (float)_magTimer.GetValue(fm);
                if (mag > 0f)
                {
                    e.SainNote = mag > now ? $"SAIN: already scheduled in {mag - now:0} s" : "SAIN: its first check had already run before the bot reached us";
                }
                else
                {
                    float next = now + 360f * UnityEngine.Random.Range(0.5f, 1.5f);
                    _magTimer.SetValue(fm, next);
                    // Its own spacing after a magazine check, so a chamber check doesn't take the
                    // magazine check's place at spawn.
                    float gap = now + 30f;
                    if ((float)_nextTimer.GetValue(fm) < gap) _nextTimer.SetValue(fm, gap);
                    e.SainNote = $"SAIN: first magazine check in {next - now:0} s, nothing for 30 s";
                }
                e.SainDone = true;
            }
            catch (Exception ex) { e.SainDone = true; e.SainNote = $"SAIN: {ex.GetType().Name}, left alone"; }
        }

        internal static void Clear() => Pending.Clear();
    }
}
