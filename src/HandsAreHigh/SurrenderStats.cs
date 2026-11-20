using System.Collections.Generic;
using System.Text;
using EFT;
using UnityEngine;

namespace HandsAreHigh
{
    // Where a surrender came from. Only Natural (and Demand, the optional Stop key) are the
    // mod's real gameplay; Recapture is a natural follow-up; ScenarioNatural is the natural
    // decision made by a scenario-spawned bot (its suppression may be synthetic); DebugKey and
    // Scenario are forced.
    internal enum SurrenderSource { Natural, Demand, Recapture, ScenarioNatural, DebugKey, Scenario, Count }

    // Why a natural check ended before a roll: one counter each, never a message.
    internal enum NaturalGate
    {
        Role, Teammate, Cooldown, NotActive, NoTarget, TargetNotLiving, BotTarget, NotPerceived,
        WeaponChange, TooFewReasons, LowPressure, UnderBreakingFire, Count
    }

    // 0.19.0: bounded counters for the natural pipeline, so a normal raid's log says how far bots
    // got (evaluated, stopped where, rolled, gave up, activated, acknowledged), apart from forced
    // or debug surrenders. Fixed arrays and one small set of bot ids (capped): no per-frame cost
    // beyond an increment. A summary goes to the log every 2 minutes while anything changed, and
    // once when the raid ends.
    internal static class SurrenderStats
    {
        private const float ReportEvery = 120f;
        private const int MaxBotsTracked = 512;

        private static readonly int[] Gates = new int[(int)NaturalGate.Count];
        private static readonly HashSet<int> BotsEvaluated = new HashSet<int>();
        private static int _checks, _eligible, _rolls, _gives, _activated, _failed, _scavRolls, _pmcRolls, _scavGives, _pmcGives;
        private static int _hitGrace;
        private static readonly int[] Started = new int[(int)SurrenderSource.Count];
        private static readonly int[] Accepted = new int[(int)SurrenderSource.Count];
        private static readonly int[] Custody = new int[(int)SurrenderSource.Count];
        private static float _nextReport;
        private static bool _dirty;

        internal static void Gate(NaturalGate g) { Gates[(int)g]++; _dirty = true; }

        internal static void Checked(Player bot)
        {
            _checks++;
            if (BotsEvaluated.Count < MaxBotsTracked && bot != null) BotsEvaluated.Add(bot.Id);
            _dirty = true;
        }

        internal static void Rolled(NaturalSurrender.RoleClass rc, bool gives)
        {
            _eligible++;
            _rolls++;
            if (rc == NaturalSurrender.RoleClass.Pmc) _pmcRolls++; else _scavRolls++;
            if (!gives) return;
            _gives++;
            if (rc == NaturalSurrender.RoleClass.Pmc) _pmcGives++; else _scavGives++;
        }

        internal static void Activation(bool ok) { if (ok) _activated++; else _failed++; }

        internal static void HitGrace() { _hitGrace++; _dirty = true; }

        internal static void Start(SurrenderState s)
        {
            if (s == null) return;
            Started[(int)s.Source]++;
            _dirty = true;
        }

        // A human acknowledged it (close, or aimed at him) or a bot took custody: counted once per
        // surrender.
        internal static void Acknowledged(SurrenderState s, bool botCustody)
        {
            if (s == null || s.CountedAccept) return;
            s.CountedAccept = true;
            Accepted[(int)s.Source]++;
            if (botCustody) Custody[(int)s.Source]++;
            _dirty = true;
        }

        internal static void Tick()
        {
            float now = Time.time;
            if (now < _nextReport) return;
            _nextReport = now + ReportEvery;
            if (_dirty) Report("so far this raid");
        }

        // Raid over (or teardown): the last summary, then a clean slate.
        internal static void EndRaid()
        {
            if (_dirty) Report("raid summary");
            Reset();
        }

        private static void Reset()
        {
            System.Array.Clear(Gates, 0, Gates.Length);
            System.Array.Clear(Started, 0, Started.Length);
            System.Array.Clear(Accepted, 0, Accepted.Length);
            System.Array.Clear(Custody, 0, Custody.Length);
            BotsEvaluated.Clear();
            _checks = _eligible = _rolls = _gives = _activated = _failed = _scavRolls = _pmcRolls = _scavGives = _pmcGives = _hitGrace = 0;
            _dirty = false;
            _nextReport = Time.time + ReportEvery;
        }

        private static void Report(string when)
        {
            _dirty = false;
            var sb = new StringBuilder(512);
            sb.Append($"Natural surrender, {when}: {BotsEvaluated.Count} bot(s) checked ({_checks} checks); stopped before a roll:");
            bool any = false;
            for (int i = 0; i < Gates.Length; i++)
            {
                if (Gates[i] == 0) continue;
                sb.Append($" {(NaturalGate)i} {Gates[i]},");
                any = true;
            }
            if (!any) sb.Append(" none,");
            sb.Length--;
            sb.Append($"; rolls {_rolls} (Scav {_scavRolls}, PMC {_pmcRolls}), gave up {_gives} (Scav {_scavGives}, PMC {_pmcGives}), activated {_activated}");
            if (_failed > 0) sb.Append($", ACTIVATION FAILED {_failed}");
            sb.Append($"; hits ignored in the first moments of a surrender {_hitGrace}");
            sb.Append($"; hold-ups broken by incoming fire {FireBreak.Broken}");   // 0.21.0
            sb.Append(". Surrenders started / acknowledged (bot custody):");
            for (int i = 0; i < Started.Length; i++)
                sb.Append($" {(SurrenderSource)i} {Started[i]}/{Accepted[i]} ({Custody[i]}),");
            sb.Length--;
            sb.Append(Started[(int)SurrenderSource.DebugKey] + Started[(int)SurrenderSource.Scenario] + Started[(int)SurrenderSource.ScenarioNatural] > 0
                ? ". Only Natural and Demand are raid evidence (DebugKey and Scenario are forced; ScenarioNatural is a spawned test bot)."
                : ".");
            HandsAreHighPlugin.Log.LogInfo(sb.ToString());
        }
    }
}
