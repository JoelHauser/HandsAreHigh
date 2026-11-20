using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // Natural: the scenario only creates starting conditions (who, where, kit, health, nerve);
    //          surrender, custody and everything after are the mod's real decisions.
    // Forced:  the scenario starts the encounter at a later point (a surrender already made,
    //          custody already taken) through the same transitions the real code uses
    //          (SurrenderRegistry.Surrender, Captors.ForceAccept -> Accept), then every
    //          participant is left to his own decisions. Never evidence that natural surrender works.
    internal enum ScenarioMode { Natural, Forced }

    internal enum Stage { Placing, Spawning, Setup, Running, Ended }

    // What kind of thing a timeline line records (the overlay and the report show it).
    internal enum TL
    {
        Info,        // the scenario itself
        Forced,      // a condition the scenario forced
        Simulated,   // a synthetic input (opening, suppression): tests the decision, not detection
        Real,        // a real game action requested by the scenario (reload, spawn, kill, damage)
        Event,       // something a participant did (phase, drop, order, custody)
        Choice,      // a decision to act
        NoAct,       // a valid choice not to act
        Gate,        // not eligible / not considered (with the reason)
        ExecFail     // decided, but the game refused or he couldn't (execution failure)
    }

    internal sealed class TimelineEntry
    {
        internal float At;      // seconds since launch
        internal TL Kind;
        internal string Text;
        internal int Repeats;
    }

    internal sealed class Participant
    {
        internal Scenario Owner;
        internal int Index;
        internal string Tag;            // "Scav", "PMC-1", "Third party"
        internal string Squad;          // participants of one squad share one BotsGroup
        internal WildSpawnType Role;
        internal EPlayerSide Side;
        internal Vector3 Offset;        // (right, 0, forward) from the anchor, in the scenario frame
        internal string FaceTag;        // looks at this participant once spawned ("" = the anchor)
        internal Vector3 Spot;          // validated spawn point
        internal float? Nerve;          // null: his own roll
        internal string Personality;    // null: SAIN's own (mod-side override only)
        internal string Health = "Full";
        internal string Ammo = "As generated";
        internal string Kit = "As generated";
        internal Player Existing;       // a bot already in the raid (Pit Fireteam teammate): never spawned or despawned by us

        internal Player Player;
        internal BotOwner Owner2;       // BotOwner (Owner is the scenario)
        internal bool Requested, Spawned, Failed, Dead, Removed;
        internal int Raid;              // Scenarios.RaidSerial when requested: a later raid ignores the answer
        internal BotOwner Created;      // made by the game's creator, not yet handed to us (partial-failure cleanup)
        internal string FailWhy;
        internal float SpawnedAt = -1f;
        internal float RequestedAt;
        internal string ProfileId() => Player != null ? Player.ProfileId : "-";
        internal readonly HashSet<Item> Items = new HashSet<Item>();   // tracked by identity
        internal OnPlayerDeadOrUnspawn DeathHandler;

        // Change detection for the timeline (compared every 0.25 s).
        internal CaptorMode? SeenMode;
        internal bool SeenCaptor;
        internal Item SeenTarget;
        internal string LastCheck;      // the latest surrender check line (natural)
        internal float LastCheckAt = -1f;

        internal bool Active => Player != null && Owner2 != null && !Dead && Owner2.BotState == EBotState.Active;
        internal string Label => $"S{Owner.Id}-{Tag}";
    }

    internal sealed class ScheduledEvent
    {
        internal float At;                  // Time.time it may run from
        internal Func<bool> When;           // optional condition (polled after At)
        internal float Until;               // condition given up after
        internal string Label;
        internal Action Run;
        internal bool Done;
    }

    // One setup step: Run returns null when done, "" to wait (re-run next tick), or a failure.
    internal sealed class SetupStep
    {
        internal string Label;
        internal Func<string> Run;
        internal float Timeout = 8f;
        internal float StartedAt = -1f;
        internal bool Fatal;                // failing it means the scenario didn't reach its start state
    }

    internal sealed class Scenario
    {
        internal int Id;
        internal ScenarioPreset Preset;
        internal ScenarioMode Mode;
        internal ParamValues Values;
        internal int Seed;
        internal Stage Stage = Stage.Placing;
        internal float LaunchedAt;
        internal float StageSince;
        internal Vector3 Anchor;
        internal Vector3 Forward;           // scenario frame (flat)
        internal Player Observer;           // the player
        internal string Placement;
        internal bool ShieldWanted;
        internal readonly List<Participant> People = new List<Participant>();
        internal readonly Dictionary<string, BotsGroup> Groups = new Dictionary<string, BotsGroup>();
        internal readonly Dictionary<string, BotZone> Zones = new Dictionary<string, BotZone>();
        internal bool KeepTruce;            // the truce between participants lasts the whole scenario
        internal readonly List<KeyValuePair<string, string>> KeptTruces = new List<KeyValuePair<string, string>>();   // or only these pairs (both ways)
        internal bool TruceLifted;
        internal readonly List<string> Forced = new List<string>();
        internal readonly List<string> Conditions = new List<string>();  // starting conditions set up (health, ammo, kit)
        internal string SetupFailed;
        internal readonly List<TimelineEntry> Timeline = new List<TimelineEntry>();
        internal readonly List<ScheduledEvent> Events = new List<ScheduledEvent>();
        internal readonly List<SetupStep> Steps = new List<SetupStep>();
        internal readonly List<string> Overrides = new List<string>();   // temporary test overrides in force
        internal int StepIndex;
        internal bool Cancelled;
        internal string EndWhy;

        internal string Title => $"#{Id} {Preset.Name}";

        internal Participant Find(string tag)
        {
            for (int i = 0; i < People.Count; i++) if (People[i].Tag == tag) return People[i];
            return null;
        }

        internal Participant Of(Player p)
        {
            if ((object)p == null) return null;
            for (int i = 0; i < People.Count; i++) if ((object)People[i].Player == p) return People[i];
            return null;
        }

        internal float Since => Time.time - LaunchedAt;

        // fromLog: the line came from our own log (the timeline listener): already in the log.
        // Everything else the scenario notes is logged as it happens, failures as warnings, so
        // the log alone tells the story (the overlay and the end report are views of the same).
        // An exact repeat is counted, and logged again every 10th time.
        internal void Note(TL kind, string text, bool fromLog = false)
        {
            float t = Since;
            if (Timeline.Count > 0)
            {
                TimelineEntry last = Timeline[Timeline.Count - 1];
                if (last.Kind == kind && last.Text == text)
                {
                    last.Repeats++;
                    last.At = t;
                    if (!fromLog && (last.Repeats + 1) % 10 == 0) Write(kind, $"{text} (x{last.Repeats + 1})");
                    return;
                }
            }
            Timeline.Add(new TimelineEntry { At = t, Kind = kind, Text = text });
            if (Timeline.Count > 300) Timeline.RemoveAt(0);
            if (!fromLog) Write(kind, text);
        }

        private void Write(TL kind, string text)
        {
            string line = $"Scenario {Title} +{Since:0.0}s [{Tag(kind)}]: {text}";
            if (kind == TL.ExecFail) HandsAreHighPlugin.Log.LogWarning(line);
            else HandsAreHighPlugin.Log.LogInfo(line);
        }

        internal void Force(string what)
        {
            Forced.Add(what);
            Note(TL.Forced, what);
        }

        internal void After(float seconds, string label, Action run) =>
            Events.Add(new ScheduledEvent { At = Time.time + seconds, Label = label, Run = run });

        internal void OnceWhen(Func<bool> when, float within, string label, Action run) =>
            Events.Add(new ScheduledEvent { At = Time.time, When = when, Until = Time.time + within, Label = label, Run = run });

        internal SetupStep Step(string label, Func<string> run, float timeout = 8f)
        {
            var st = new SetupStep { Label = label, Run = run, Timeout = timeout };
            Steps.Add(st);
            return st;
        }

        internal static string Tag(TL k)
        {
            switch (k)
            {
                case TL.Forced: return "FORCED";
                case TL.Simulated: return "SIMULATED";
                case TL.Real: return "real";
                case TL.Choice: return "choice";
                case TL.NoAct: return "no action";
                case TL.Gate: return "not eligible";
                case TL.ExecFail: return "EXECUTION FAILED";
                case TL.Event: return "event";
                default: return "info";
            }
        }
    }
}
