using System;
using System.Collections.Generic;
using System.Text;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // ---- parameters ------------------------------------------------------------------------

    internal sealed class ParamDef
    {
        internal string Key, Label;
        internal string[] Options;          // a choice; else a number
        internal float Min, Max, StepSize, Default;
        internal bool IsChoice => Options != null;

        internal static ParamDef Choice(string key, string label, string[] options, int def = 0) =>
            new ParamDef { Key = key, Label = label, Options = options, Default = def };

        internal static ParamDef Number(string key, string label, float min, float max, float step, float def) =>
            new ParamDef { Key = key, Label = label, Min = min, Max = max, StepSize = step, Default = def };
    }

    internal sealed class ParamValues
    {
        private readonly List<ParamDef> _defs;
        private readonly Dictionary<string, float> _v = new Dictionary<string, float>();

        internal ParamValues(List<ParamDef> defs)
        {
            _defs = defs;
            foreach (ParamDef d in defs) _v[d.Key] = d.Default;
        }

        internal ParamValues Clone()
        {
            var c = new ParamValues(_defs);
            foreach (KeyValuePair<string, float> kv in _v) c._v[kv.Key] = kv.Value;
            return c;
        }

        internal ParamDef Def(string key)
        {
            foreach (ParamDef d in _defs) if (d.Key == key) return d;
            return null;
        }

        internal bool Has(string key) => _v.ContainsKey(key);
        internal float F(string key) => _v.TryGetValue(key, out float f) ? f : 0f;
        internal int I(string key) => Mathf.RoundToInt(F(key));

        internal string S(string key)
        {
            ParamDef d = Def(key);
            if (d == null) return null;
            if (!d.IsChoice) return Text(d);
            int i = Mathf.Clamp(I(key), 0, d.Options.Length - 1);
            return d.Options[i];
        }

        internal bool Is(string key, string option) => S(key) == option;

        internal void Step(ParamDef d, int dir)
        {
            if (d.IsChoice)
            {
                int n = d.Options.Length;
                _v[d.Key] = ((I(d.Key) + dir) % n + n) % n;
            }
            else _v[d.Key] = Mathf.Clamp(F(d.Key) + dir * d.StepSize, d.Min, d.Max);
        }

        internal string Text(ParamDef d) => d.IsChoice ? S(d.Key) : F(d.Key).ToString(d.StepSize < 1f ? "0.00" : "0");

        internal string Describe()
        {
            var sb = new StringBuilder();
            foreach (ParamDef d in _defs)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(d.Label).Append('=').Append(Text(d));
            }
            return sb.ToString();
        }
    }

    internal sealed class ScenarioPreset
    {
        internal string Name;
        internal string Tests;              // what it tests (shown in the menu)
        internal string Watch;              // what to look for
        internal string Prereq;             // prerequisites / limits (shown in the menu)
        internal bool Natural = true, Forced = true;
        internal ScenarioMode Mode;         // the menu's current mode
        internal readonly List<ParamDef> Params = new List<ParamDef>();
        internal ParamValues Values;
        internal Func<string> Unavailable;  // a missing prerequisite (null: available)
        internal Func<Scenario, string> Plan;   // participants (offsets in the frame); a failure stops the launch
        internal Action<Scenario> Setup;        // steps and scheduled events, once everyone is active
        internal bool OwnAnchor;            // Plan sets the anchor and frame itself
        // 0.16.0: start-state checks at the release (between each participant and the one he faces):
        // a line of sight (NeedSight) and a distance (MaxStart, m; 0 = any). Failing either cleans
        // the launch up with the reason; without them the values are only reported.
        internal bool NeedSight;
        internal float MaxStart;

        internal ScenarioPreset Done()
        {
            Values = new ParamValues(Params);
            if (!Natural) Mode = ScenarioMode.Forced;
            else if (!Forced) Mode = ScenarioMode.Natural;
            return this;
        }
    }

    // ---- the presets -------------------------------------------------------------------------
    //
    // Every preset is a plan (who, where, what kit) and a setup (steps through ScenarioOps and
    // scheduled ScenarioEvents). None of them decides anything a participant would decide: the
    // surrender checks, the captor's decision, the prisoner's openings, restraint and custody
    // upkeep are the mod's normal code. Forced presets start later in the story through the
    // same transitions, and say so.
    internal static class ScenarioPresets
    {
        private static readonly string[] YesNo = { "Yes", "No" };
        private static readonly string[] OnOff = { "On", "Off" };
        private static readonly string[] Openings = { "Captor glance (real)", "Captor reload (real)", "Simulated opening", "None (you act)" };
        private static readonly string[] Captor = { "PMC bot", "You" };

        // After the option arrays: static fields initialise in order, and Build reads them.
        internal static readonly List<ScenarioPreset> All = Build();

        // ---- shared params ----

        private static ParamDef Place() => ParamDef.Choice("place", "Placement", new[] { "Ahead of you", "At your crosshair" });
        private static ParamDef Dist(float def) => ParamDef.Number("dist", "Distance from you (m)", 6f, 200f, 2f, def);
        private static ParamDef Sep(float def, float min = 3f, float max = 60f) => ParamDef.Number("sep", "Separation (m)", min, max, 1f, def);
        private static ParamDef Health(string key, string label, int def = 0) => ParamDef.Choice(key, label, ScenarioOps.HealthPresets, def);
        private static ParamDef Ammo(string key, string label, int def = 0) => ParamDef.Choice(key, label, ScenarioOps.AmmoPresets, def);
        private static ParamDef Kit(string key, string label, int def = 0) => ParamDef.Choice(key, label, ScenarioOps.KitPresets, def);
        private static ParamDef NerveP(string key, string label) => ParamDef.Choice(key, label, ScenarioOps.NervePresets);
        private static ParamDef Pers(string key, string label) => ParamDef.Choice(key, label, ScenarioOps.Personalities);
        private static ParamDef Delay(float def, string label = "Threat or opening after (s)") => ParamDef.Number("delay", label, 0f, 90f, 1f, def);
        private static ParamDef Seed() => ParamDef.Number("seed", "Seed (0 = off)", 0f, 9999f, 1f, 0f);

        private static void Common(ScenarioPreset p, bool shield)
        {
            p.Params.Add(ParamDef.Choice("shield", "Observer override", OnOff, shield ? 0 : 1));
            p.Params.Add(Seed());
        }

        // ---- shared plan pieces ----

        private static Participant Add(Scenario sc, string tag, string kind, string squad, float right, float forward, string face)
        {
            var p = new Participant { Owner = sc, Index = sc.People.Count, Tag = tag, Squad = squad, Offset = new Vector3(right, 0f, forward), FaceTag = face };
            SetKind(p, kind);
            sc.People.Add(p);
            return p;
        }

        internal static void SetKind(Participant p, string kind)
        {
            switch (kind)
            {
                case "BEAR": p.Role = WildSpawnType.pmcBEAR; p.Side = EPlayerSide.Bear; break;
                case "USEC": p.Role = WildSpawnType.pmcUSEC; p.Side = EPlayerSide.Usec; break;
                default: p.Role = WildSpawnType.assault; p.Side = EPlayerSide.Savage; break;
            }
        }

        // The participant's own conditions from the params (nerve and personality are applied at
        // spawn; health, ammo and kit once he's active).
        private static void From(Participant p, ParamValues v, string health, string ammo, string kit, string nerve, string pers)
        {
            if (health != null && v.Has(health)) p.Health = v.S(health);
            if (ammo != null && v.Has(ammo)) p.Ammo = v.S(ammo);
            if (kit != null && v.Has(kit)) p.Kit = v.S(kit);
            if (nerve != null && v.Has(nerve) && v.S(nerve) != "Rolled") p.Nerve = float.Parse(v.S(nerve), System.Globalization.CultureInfo.InvariantCulture);
            if (pers != null && v.Has(pers) && v.S(pers) != "SAIN's own") p.Personality = v.S(pers);
        }

        // A captor squad: the captor at `at`, the rest a few metres behind him.
        private static Participant Squad(Scenario sc, string kind, int size, float right, float forward, string face)
        {
            Participant lead = Add(sc, size > 1 ? "PMC-1" : "PMC", kind, "A", right, forward, face);
            float back = right < 0f ? -2.5f : 2.5f;
            for (int i = 2; i <= size; i++)
                Add(sc, $"PMC-{i}", kind, "A", right + back * (i == 4 ? 2f : 1f), forward + (i % 2 == 0 ? 2.5f : -2.5f), face);
            return lead;
        }

        // ---- shared setup pieces ----

        // Health, ammo and kit for every participant that has them (non-default).
        internal static void Conditions(Scenario sc)
        {
            foreach (Participant p in sc.People)
            {
                Participant q = p;
                if (q.Health == "Full" && q.Ammo == "As generated" && q.Kit == "As generated") continue;
                sc.Step($"{q.Tag}: starting conditions", () =>
                {
                    if (q.Dead) return $"{q.Tag} died";
                    if (!q.Active) return "";
                    string a = ScenarioOps.ApplyHealth(q.Player, q.Health);
                    string b = ScenarioOps.ApplyAmmo(sc, q, q.Ammo);
                    string c = ScenarioOps.ApplyKit(sc, q, q.Kit);
                    foreach (string s in new[] { a, b, c })
                        if (s != null) { sc.Conditions.Add($"{q.Tag}: {s}"); sc.Note(TL.Info, $"{q.Tag}: starting condition: {s}"); }
                    return null;
                });
            }
        }

        // The prisoner gives up to a captor (a participant, or you when captor is null) through the
        // real surrender entry. custody: the captor takes custody through ForceAccept (forced);
        // else, for a bot captor, his own decision runs (decideNow).
        internal static void PrisonerTo(Scenario sc, Participant prisoner, Participant captor, bool custody, string why)
        {
            if (prisoner.Kit == "Keeps slung primary")
            {
                bool asked = false;
                sc.Step($"{prisoner.Tag}: sidearm in hand (so the drop is the sidearm)", () =>
                {
                    if (!prisoner.Active) return prisoner.Dead ? "dead" : "";
                    string r = ScenarioOps.HoldSidearm(prisoner, ref asked);
                    if (r == null || r == "") return r;
                    sc.Note(TL.ExecFail, $"{prisoner.Tag}: {r}; he drops whatever is in his hands instead");
                    return null;
                }, 6f);
            }
            sc.Step($"{prisoner.Tag} surrenders to {(captor == null ? "you" : captor.Tag)}", () =>
            {
                if (!prisoner.Active || (captor != null && !captor.Active)) return (prisoner.Dead || (captor != null && captor.Dead)) ? "a participant died" : "";
                return ScenarioOps.Surrender(sc, prisoner, captor == null ? sc.Observer : captor.Player, decideNow: !custody, why);
            }).Fatal = true;
            if (custody && captor != null)
                sc.Step($"{captor.Tag} takes custody of {prisoner.Tag}", () =>
                {
                    if (!captor.Active) return captor.Dead ? "the captor died" : "";
                    return ScenarioOps.Custody(sc, prisoner, captor);
                }).Fatal = true;
            sc.Step($"{prisoner.Tag}: hands up", () =>
            {
                SurrenderState s = ScenarioOps.StateOf(prisoner);
                if (s == null) return "he isn't surrendered any more";
                return s.Weight >= 0.95f ? null : "";
            }, 8f);
        }

        // An opening of the chosen kind after a delay, through the event code the menu uses.
        internal static void Opening(Scenario sc, string kind, float delay, Participant prisoner, Participant captor)
        {
            if (kind == "None (you act)") return;
            sc.After(delay, $"opening: {kind}", () =>
            {
                switch (kind)
                {
                    case "Captor glance (real)": ScenarioEvents.Run(sc, ScenarioEvent.Glance, captor); break;
                    case "Captor reload (real)": ScenarioEvents.Run(sc, ScenarioEvent.Reload, captor); break;
                    default: ScenarioEvents.Run(sc, ScenarioEvent.Opening, prisoner); break;
                }
            });
        }

        // ---- the list ----

        private static List<ScenarioPreset> Build()
        {
            var list = new List<ScenarioPreset>();

            // 1
            var p = new ScenarioPreset
            {
                Name = "Player versus isolated Scav",
                Tests = "Natural surrender to you: injury, suppression (your near misses), your Stop demand.",
                Watch = "Overlay 'last check' line: pressure, each reason, eligibility, chance and roll. Fire past him, wound him, say Stop. Holding on is a valid outcome.",
                Prereq = "Natural only. The Scav fights you for real (observer override off).",
                Forced = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f));
            p.Params.Add(Health("h", "Scav health")); p.Params.Add(Ammo("a", "Scav ammo")); p.Params.Add(NerveP("n", "Scav nerve")); p.Params.Add(Pers("pe", "Scav personality"));
            Common(p, false);
            p.Plan = sc => { From(Add(sc, "Scav", "Scav", "B", 0f, 0f, "you"), sc.Values, "h", "a", null, "n", "pe"); return null; };
            p.Setup = sc => Conditions(sc);
            list.Add(p.Done());

            // 2
            p = new ScenarioPreset
            {
                Name = "Player versus PMC",
                Tests = "The same as the Scav preset with a PMC, for comparing resistance under the same conditions.",
                Watch = "Compare the 'last check' lines with the Scav run: role base, threshold, max chance.",
                Prereq = "Natural only. Needs 01. General: PMCs can surrender on (else the overlay shows 'role not eligible').",
                Forced = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f));
            p.Params.Add(ParamDef.Choice("side", "PMC side", new[] { "USEC", "BEAR" }));
            p.Params.Add(Health("h", "PMC health")); p.Params.Add(Ammo("a", "PMC ammo")); p.Params.Add(NerveP("n", "PMC nerve")); p.Params.Add(Pers("pe", "PMC personality"));
            Common(p, false);
            p.Plan = sc => { From(Add(sc, "PMC", sc.Values.S("side"), "B", 0f, 0f, "you"), sc.Values, "h", "a", null, "n", "pe"); return null; };
            p.Setup = sc => Conditions(sc);
            list.Add(p.Done());

            // 3
            p = new ScenarioPreset
            {
                Name = "Matched Scav and PMC",
                Tests = "A Scav and a PMC side by side with identical health, ammo and nerve, both facing you.",
                Watch = "Two 'last check' lines under the same fire: the difference is role odds and personality.",
                Prereq = "Natural. FORCED override: the two never fire at each other (truce for the whole scenario).",
                Forced = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f));
            p.Params.Add(Health("h", "Both: health")); p.Params.Add(Ammo("a", "Both: ammo")); p.Params.Add(NerveP("n", "Both: nerve"));
            Common(p, false);
            p.Plan = sc =>
            {
                From(Add(sc, "Scav", "Scav", "B", -3f, 0f, "you"), sc.Values, "h", "a", null, "n", null);
                From(Add(sc, "PMC", "USEC", "C", 3f, 0f, "you"), sc.Values, "h", "a", null, "n", null);
                sc.KeepTruce = true;
                return null;
            };
            p.Setup = sc => { sc.Force("Scav and PMC never fire at each other (comparison truce)"); Conditions(sc); };
            list.Add(p.Done());

            // 4
            p = new ScenarioPreset
            {
                Name = "PMC captures Scav",
                Tests = "A bot accepts a surrender, holds the prisoner, speaks and gives orders.",
                Watch = "Forced: 'holds', the plan, 'ordered Stay/DropWeapons by', captor voice lines, 'let go'. Natural: the Scav's checks under the PMC's fire, then the PMC's decision line.",
                Prereq = "Forced default (surrender + custody given). Natural: they fight; it may end with no surrender (valid)."
            };
            p.Mode = ScenarioMode.Forced;
            p.Params.Add(Place()); p.Params.Add(Dist(40f)); p.Params.Add(Sep(12f));
            p.Params.Add(Health("h", "Scav health", 2)); p.Params.Add(Ammo("a", "Scav ammo", 1)); p.Params.Add(Kit("k", "Scav keeps"));
            p.Params.Add(NerveP("n", "Scav nerve")); p.Params.Add(Pers("pe", "Scav personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Squad(sc, "BEAR", 1, -h, 0f, "Scav");
                From(Add(sc, "Scav", "Scav", "B", h, 0f, "PMC"), sc.Values, "h", "a", "k", "n", "pe");
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                if (sc.Mode == ScenarioMode.Forced) PrisonerTo(sc, sc.Find("Scav"), sc.Find("PMC"), custody: true, "forced start: already giving up");
            };
            list.Add(p.Done());

            // 5
            p = new ScenarioPreset
            {
                Name = "PMC squad captures a bot",
                Tests = "Squadmates recognise their captor's decision and hold fire at the prisoner only.",
                Watch = "'Restraint: <squadmate> holds fire at ... (squad)' in the same second as the decision; zero RESTRAINT LEAK; squadmates watching.",
                Prereq = "Forced surrender. Captor decision: the real roll (may refuse) or forced custody.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(40f)); p.Params.Add(Sep(12f));
            p.Params.Add(ParamDef.Number("squad", "Captor squad size", 2f, 4f, 1f, 3f));
            p.Params.Add(ParamDef.Choice("pr", "Prisoner", new[] { "Scav", "USEC PMC" }));
            p.Params.Add(ParamDef.Choice("dec", "Captor decision", new[] { "Real roll", "Forced custody" }));
            p.Params.Add(Kit("k", "Prisoner keeps")); p.Params.Add(NerveP("n", "Prisoner nerve"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Squad(sc, "BEAR", sc.Values.I("squad"), -h, 0f, "Prisoner");
                From(Add(sc, "Prisoner", sc.Values.Is("pr", "Scav") ? "Scav" : "USEC", "B", h, 0f, "PMC-1"), sc.Values, null, null, "k", "n", null);
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                bool forced = sc.Values.Is("dec", "Forced custody");
                PrisonerTo(sc, sc.Find("Prisoner"), sc.Find("PMC-1"), custody: forced, forced ? "forced start" : "forced surrender; the captor decides for real");
            };
            list.Add(p.Done());

            // 6
            p = new ScenarioPreset
            {
                Name = "Captor comes under fire",
                Tests = "0.21.0: fire on the captor breaks the hold-up: his combat AI has him at once, the prisoner gets to safety and is handed to his own AI. A hostile bot that only shows up (no fire yet) is Contact: the captor engages and may hand custody to a squadmate.",
                Watch = "'INCOMING FIRE: ... (the captor) ...: the hold-up breaks' (or '-> Engage' + 'takes over' for a bot only seen), '<prisoner> under fire (...): ... -> ...', 'back to his own AI (...)'. The hostile bot must actually fire at the captor (no 'Scenario shield: ... blocked at trigger' while it aims at him).",
                Prereq = "Forced custody. Threat: a real hostile bot (spawned 40 m out), a SIMULATED suppression event (tests decisions only), or you fire near the captor.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(40f)); p.Params.Add(Sep(8f));
            p.Params.Add(ParamDef.Choice("threat", "Threat", new[] { "Hostile bot (real)", "Suppression (simulated)", "None (you shoot)" }));
            p.Params.Add(Delay(8f)); p.Params.Add(ParamDef.Number("squad", "Captor squad size", 1f, 3f, 1f, 2f));
            p.Params.Add(Kit("k", "Prisoner keeps", 1)); p.Params.Add(NerveP("n", "Prisoner nerve"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Squad(sc, "BEAR", sc.Values.I("squad"), -h, 0f, "Scav");
                From(Add(sc, "Scav", "Scav", "B", h, 0f, "PMC-1"), sc.Values, null, null, "k", "n", null);
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find("PMC-1") ?? sc.Find("PMC");
                PrisonerTo(sc, sc.Find("Scav"), cap, custody: true, "forced start");
                string t = sc.Values.S("threat");
                if (t == "Hostile bot (real)") sc.After(sc.Values.F("delay"), "hostile third party", () => ScenarioEvents.Run(sc, ScenarioEvent.ThirdParty, cap));
                else if (t == "Suppression (simulated)") sc.After(sc.Values.F("delay"), "synthetic suppression", () => ScenarioEvents.Run(sc, ScenarioEvent.Suppress, cap));
            };
            list.Add(p.Done());

            // 7
            p = new ScenarioPreset
            {
                Name = "Captor reloads or looks away",
                Tests = "The prisoner sees a real opening and weighs fleeing, drawing or recovering his gun.",
                Watch = "'reacts', 'takes an opening (...)' with every option weighed, or a roll where he stays (valid).",
                Prereq = "Forced custody. The opening itself is the captor's real reload or real glance (or SIMULATED).",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(40f)); p.Params.Add(Sep(7f));
            p.Params.Add(ParamDef.Choice("op", "Opening", Openings)); p.Params.Add(Delay(10f));
            p.Params.Add(Kit("k", "Prisoner keeps", 1)); p.Params.Add(NerveP("n", "Prisoner nerve")); p.Params.Add(Pers("pe", "Prisoner personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Squad(sc, "BEAR", 1, -h, 0f, "Scav");
                From(Add(sc, "Scav", "Scav", "B", h, 0f, "PMC"), sc.Values, null, null, "k", "n", "pe");
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                PrisonerTo(sc, sc.Find("Scav"), sc.Find("PMC"), custody: true, "forced start");
                Opening(sc, sc.Values.S("op"), sc.Values.F("delay"), sc.Find("Scav"), sc.Find("PMC"));
            };
            list.Add(p.Done());

            // 8
            p = new ScenarioPreset
            {
                Name = "Nearby dropped weapon",
                Tests = "With nothing else to fight with, the prisoner goes for the gun he actually dropped.",
                Watch = "'takes an opening' with 'scramble' weighed; 'Recovering' with that gun as target; 'picks up'; draw timeline.",
                Prereq = "Forced custody (or held by you). Kit 'Disarmed' by default so his own gun is the main way back.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(40f)); p.Params.Add(Sep(10f));
            p.Params.Add(ParamDef.Choice("cap", "Captor", Captor)); p.Params.Add(ParamDef.Choice("op", "Opening", Openings)); p.Params.Add(Delay(10f));
            p.Params.Add(Kit("k", "Prisoner keeps", 4)); p.Params.Add(NerveP("n", "Prisoner nerve"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                bool you = sc.Values.Is("cap", "You");
                if (!you) Squad(sc, "BEAR", 1, -h, 0f, "Scav");
                From(Add(sc, "Scav", "Scav", "B", you ? 0f : h, 0f, you ? "you" : "PMC"), sc.Values, null, null, "k", "n", null);
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find("PMC");
                PrisonerTo(sc, sc.Find("Scav"), cap, custody: cap != null, "forced start");
                string op = sc.Values.S("op");
                if (cap == null && op != "None (you act)") op = "Simulated opening";
                Opening(sc, op, sc.Values.F("delay"), sc.Find("Scav"), cap);
            };
            list.Add(p.Done());

            // 9
            p = new ScenarioPreset
            {
                Name = "Missing or blocked weapon",
                Tests = "The prisoner's recovery target becomes invalid; he abandons it and decides again.",
                Watch = "'sees his ... is gone' (if he can see the spot), or a recovery that gives up ('gone'), then another decision.",
                Prereq = "Forced custody. The gun is removed through the game's loose-loot removal. A physically BLOCKED path cannot be created on demand (no supported obstacle placement): test that next to real geometry.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(40f)); p.Params.Add(Sep(10f));
            p.Params.Add(ParamDef.Choice("when", "Gun removed", new[] { "Before the opening", "When he goes for it" }));
            p.Params.Add(Delay(10f)); p.Params.Add(Kit("k", "Prisoner keeps", 4)); p.Params.Add(NerveP("n", "Prisoner nerve"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Squad(sc, "BEAR", 1, -h, 0f, "Scav");
                From(Add(sc, "Scav", "Scav", "B", h, 0f, "PMC"), sc.Values, null, null, "k", "n", null);
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant pr = sc.Find("Scav"), cap = sc.Find("PMC");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                float d = sc.Values.F("delay");
                if (sc.Values.Is("when", "Before the opening"))
                    sc.After(Mathf.Max(0f, d - 3f), "remove his dropped gun", () => ScenarioEvents.Run(sc, ScenarioEvent.RemoveGun, pr));
                else
                    sc.OnceWhen(() => { SurrenderState s = ScenarioOps.StateOf(pr); return s != null && s.TargetWeapon != null && (s.Phase == Phase.Recovering || s.Phase == Phase.PickingUp); },
                        180f, "remove the gun he's going for", () => ScenarioEvents.Run(sc, ScenarioEvent.RemoveGun, pr));
                sc.After(d, "opening (simulated)", () => ScenarioEvents.Run(sc, ScenarioEvent.Opening, pr));
            };
            list.Add(p.Done());

            // 10
            p = new ScenarioPreset
            {
                Name = "Armed versus unarmed prisoner",
                Tests = "The same opening with a retained sidearm, a retained (slung) primary, a knife only, or nothing.",
                Watch = "Which options 'takes an opening' weighs (draw slung / sidearm / knife / scramble / run) and the draw timeline.",
                Prereq = "Forced. 'Keeps slung primary' needs a generated primary + sidearm (he drops the sidearm). Run once per kit.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(40f)); p.Params.Add(Sep(8f));
            p.Params.Add(Kit("k", "Prisoner keeps", 1)); p.Params.Add(ParamDef.Choice("cap", "Captor", Captor));
            p.Params.Add(ParamDef.Choice("op", "Opening", Openings, 2)); p.Params.Add(Delay(8f)); p.Params.Add(NerveP("n", "Prisoner nerve"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                bool you = sc.Values.Is("cap", "You");
                if (!you) Squad(sc, "BEAR", 1, -h, 0f, "Scav");
                From(Add(sc, "Scav", "Scav", "B", you ? 0f : h, 0f, you ? "you" : "PMC"), sc.Values, null, null, "k", "n", null);
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find("PMC");
                PrisonerTo(sc, sc.Find("Scav"), cap, custody: cap != null, "forced start");
                string op = sc.Values.S("op");
                if (cap == null && op != "None (you act)") op = "Simulated opening";
                Opening(sc, op, sc.Values.F("delay"), sc.Find("Scav"), cap);
            };
            list.Add(p.Done());

            // 11
            p = new ScenarioPreset
            {
                Name = "Captor dies",
                Tests = "The prisoner reacts to his captor's death; squadmates take over or reassess, no automatic execution.",
                Watch = "'takes over' or 'nobody takes over, restraint stays as decided'; no squad volley; the prisoner's leave/decision lines.",
                Prereq = "Forced custody; the captor is killed for real (health controller Kill) after the delay.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(40f)); p.Params.Add(Sep(8f));
            p.Params.Add(ParamDef.Number("squad", "Captor squad size", 1f, 4f, 1f, 2f)); p.Params.Add(Delay(10f, "Captor killed after (s)"));
            p.Params.Add(Kit("k", "Prisoner keeps", 1)); p.Params.Add(NerveP("n", "Prisoner nerve"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Squad(sc, "BEAR", sc.Values.I("squad"), -h, 0f, "Scav");
                From(Add(sc, "Scav", "Scav", "B", h, 0f, "PMC-1"), sc.Values, null, null, "k", "n", null);
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find("PMC-1") ?? sc.Find("PMC");
                PrisonerTo(sc, sc.Find("Scav"), cap, custody: true, "forced start");
                sc.After(sc.Values.F("delay"), "kill the captor", () => ScenarioEvents.Run(sc, ScenarioEvent.Kill, cap));
            };
            list.Add(p.Done());

            // 12
            p = new ScenarioPreset
            {
                Name = "Third-party observer",
                Tests = "A bot outside the captor's squad notices the surrender and decides for himself.",
                Watch = "'Restraint: <observer> holds fire at / ignores / lets go / refuses ...' with his reasons; a later re-decision when the prisoner runs.",
                Prereq = "Forced custody. Optional FORCED truce between observer and captor so the test isolates the prisoner decision.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(40f)); p.Params.Add(Sep(8f));
            p.Params.Add(ParamDef.Choice("ob", "Observer", new[] { "USEC PMC", "Scav" }));
            p.Params.Add(ParamDef.Number("od", "Observer distance (m)", 8f, 50f, 1f, 22f));
            p.Params.Add(ParamDef.Choice("tr", "Observer ignores the captor", YesNo));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Squad(sc, "BEAR", 1, -h, 0f, "Scav");
                Add(sc, "Scav", "Scav", "B", h, 0f, "PMC");
                Add(sc, "Observer", sc.Values.Is("ob", "Scav") ? "Scav" : "USEC", "C", h, sc.Values.F("od"), "Scav");
                if (sc.Values.Is("tr", "Yes")) sc.KeptTruces.Add(new KeyValuePair<string, string>("Observer", "PMC"));
                return null;
            };
            p.Setup = sc =>
            {
                if (sc.KeptTruces.Count > 0) sc.Force("observer and captor never fire at each other (isolates the prisoner decision)");
                PrisonerTo(sc, sc.Find("Scav"), sc.Find("PMC"), custody: true, "forced start");
            };
            list.Add(p.Done());

            // 13
            p = new ScenarioPreset
            {
                Name = "Long-range surrender",
                Tests = "A surrender beyond 25 m stays an offer (acknowledged or unanswered) instead of being cancelled by distance.",
                Watch = "No 'walked away' while you stand still; aim at him: 'acknowledged'; or wait: 'nobody came for his surrender'.",
                Prereq = "Natural: pin him at range yourself (a DMR helps). Forced: he gives up to you at once at that range."
            };
            p.Mode = ScenarioMode.Forced;
            p.Params.Add(Place()); p.Params.Add(Dist(60f));
            p.Params.Add(Health("h", "Scav health", 1)); p.Params.Add(Ammo("a", "Scav ammo")); p.Params.Add(NerveP("n", "Scav nerve"));
            Common(p, false);
            p.Plan = sc => { From(Add(sc, "Scav", "Scav", "B", 0f, 0f, "you"), sc.Values, "h", "a", null, "n", null); return null; };
            p.Setup = sc =>
            {
                Conditions(sc);
                if (sc.Mode == ScenarioMode.Forced) PrisonerTo(sc, sc.Find("Scav"), null, custody: false, $"forced start at {sc.Values.F("dist"):0} m");
            };
            list.Add(p.Done());

            // 14
            p = new ScenarioPreset
            {
                Name = "Pit Fireteam teammate capture",
                Tests = "An enemy gives up to a real Pit Fireteam teammate; the teammate stays under your orders.",
                Watch = "The teammate's decision line ('a player ally: holds fire, the player decides'), his active layer stays Pit Fireteam's, your Pit orders still work; take the prisoner over with the order menu.",
                Prereq = "Needs a living Pit Fireteam follower already in this raid (found through its pitAIBossPlayer boss). A spawned friendly PMC is NOT Pit Fireteam and is never used instead."
            };
            p.Mode = ScenarioMode.Forced;
            p.Params.Add(Sep(12f)); p.Params.Add(Health("h", "Scav health", 2)); p.Params.Add(Ammo("a", "Scav ammo", 1)); p.Params.Add(NerveP("n", "Scav nerve"));
            Common(p, false);
            p.OwnAnchor = true;
            p.Unavailable = () => FindPitFollower() == null
                ? "no Pit Fireteam teammate in this raid (Pit Fireteam not installed, or no follower spawned yet). Spawning a friendly PMC would not test Pit Fireteam, so this preset won't run."
                : null;
            p.Plan = sc =>
            {
                Player mate = FindPitFollower();
                if (mate == null) return "no Pit Fireteam teammate";
                Vector3 f = mate.LookDirection; f.y = 0f;
                if (f.sqrMagnitude < 0.01f) f = mate.Transform.forward;
                sc.Forward = f.normalized;
                sc.Anchor = mate.Position;
                var t = new Participant { Owner = sc, Index = sc.People.Count, Tag = "Teammate", Squad = "T", Existing = mate, Player = mate, Owner2 = mate.AIData?.BotOwner, Spawned = true, Role = mate.Profile.Info.Settings.Role, Side = mate.Side };
                sc.People.Add(t);
                From(Add(sc, "Scav", "Scav", "B", 0f, sc.Values.F("sep"), "Teammate"), sc.Values, "h", "a", null, "n", null);
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                if (sc.Mode == ScenarioMode.Forced) PrisonerTo(sc, sc.Find("Scav"), sc.Find("Teammate"), custody: false, "forced surrender; the teammate decides for real");
            };
            list.Add(p.Done());

            // 15
            p = new ScenarioPreset
            {
                Name = "Commands, speech and gestures",
                Tests = "Your orders, his acknowledgements, Scav babble, wiggles and the optional middle finger.",
                Watch = "Order menu on him (he's close); 'ordered X' once each, 'says ...: audible'; Events: force a wiggle or the finger and see 'wiggle'/'middle finger' lines.",
                Prereq = "Forced: he gives up to you a few metres ahead. Gestures still need their own start conditions (wall checks, you watching).",
                Natural = false
            };
            p.Params.Add(Dist(6f)); p.Params.Add(ParamDef.Choice("role", "Prisoner", new[] { "Scav", "USEC PMC", "BEAR PMC" }));
            p.Params.Add(NerveP("n", "Nerve")); p.Params.Add(Pers("pe", "Personality"));
            Common(p, false);
            p.Plan = sc =>
            {
                string r = sc.Values.S("role");
                From(Add(sc, "Prisoner", r == "Scav" ? "Scav" : r == "USEC PMC" ? "USEC" : "BEAR", "B", 0f, 0f, "you"), sc.Values, null, null, null, "n", "pe");
                return null;
            };
            p.Setup = sc => PrisonerTo(sc, sc.Find("Prisoner"), null, custody: false, "forced start: gives up to you");
            list.Add(p.Done());

            // 16
            p = new ScenarioPreset
            {
                Name = "Death during surrender or recovery",
                Tests = "Death at a chosen moment: everything of ours stops, the state leaves the registry, the ragdoll is the game's own.",
                Watch = "'killed with his hands up' / state removed; no errors, no pose or hands work after death; the timeline's 'state removed' check.",
                Prereq = "Forced surrender to you; 'going for his gun' uses the FORCED grab (no roll) to get him there. The kill is real (health controller Kill).",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(12f));
            p.Params.Add(ParamDef.Choice("at", "Killed", new[] { "Mid-drop", "Hands up", "Going for his gun" }, 1)); p.Params.Add(Delay(5f, "Kill after (s)"));
            Common(p, false);
            p.Plan = sc => { Add(sc, "Scav", "Scav", "B", 0f, 0f, "you"); return null; };
            p.Setup = sc =>
            {
                Participant pr = sc.Find("Scav");
                string at = sc.Values.S("at");
                if (at == "Mid-drop")
                {
                    sc.Step("surrender to you, killed mid-drop", () =>
                    {
                        if (!pr.Active) return pr.Dead ? "dead" : "";
                        string r = ScenarioOps.Surrender(sc, pr, sc.Observer, true, "forced start");
                        if (r != null) return r;
                        ScenarioEvents.Run(sc, ScenarioEvent.Kill, pr);
                        return null;
                    }).Fatal = true;
                }
                else
                {
                    PrisonerTo(sc, pr, null, custody: false, "forced start");
                    if (at == "Hands up") sc.After(sc.Values.F("delay"), "kill him", () => ScenarioEvents.Run(sc, ScenarioEvent.Kill, pr));
                    else
                    {
                        sc.After(sc.Values.F("delay"), "forced gun grab", () => ScenarioEvents.Run(sc, ScenarioEvent.ForceGrab, pr));
                        sc.OnceWhen(() => { SurrenderState s = ScenarioOps.StateOf(pr); return s != null && (s.Phase == Phase.Recovering || s.Phase == Phase.PickingUp); },
                            120f, "kill him while recovering", () => ScenarioEvents.Run(sc, ScenarioEvent.Kill, pr));
                    }
                }
                sc.OnceWhen(() => pr.Dead, 300f, "check cleanup after death", () => sc.After(1.5f, "cleanup check", () =>
                    sc.Note(SurrenderRegistry.HoldsBot(pr.Player) ? TL.ExecFail : TL.Event,
                        SurrenderRegistry.HoldsBot(pr.Player) ? $"{pr.Tag}: still in the surrender registry 1.5 s after death" : $"{pr.Tag}: state removed after death (registry, custody, restraint); corpse left to the game")));
            };
            list.Add(p.Done());

            // 17
            p = new ScenarioPreset
            {
                Name = "Multiple simultaneous encounters",
                Tests = "Several bot-held prisoners at once: ownership, reservations, coordination, cost.",
                Watch = "Each captor 'holds' only his own; no shared gun claims; with Profile on: no 'Profile spike:' and bounded counts.",
                Prereq = "Forced custody for every pair. FORCED truce between different encounters (optional) so they don't fight each other.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(45f));
            p.Params.Add(ParamDef.Number("pairs", "Encounters", 2f, 4f, 1f, 3f)); p.Params.Add(Sep(7f));
            p.Params.Add(ParamDef.Choice("tr", "Encounters ignore each other", YesNo));
            p.Params.Add(ParamDef.Choice("op", "Opening for all", Openings, 3)); p.Params.Add(Delay(12f));
            Common(p, true);
            p.Plan = sc =>
            {
                int n = sc.Values.I("pairs");
                float h = sc.Values.F("sep") / 2f;
                for (int i = 0; i < n; i++)
                {
                    float fwd = (i - (n - 1) / 2f) * 14f;
                    Add(sc, $"PMC-{i + 1}", i % 2 == 0 ? "BEAR" : "USEC", "A" + i, -h, fwd, $"Scav-{i + 1}");
                    Add(sc, $"Scav-{i + 1}", "Scav", "B" + i, h, fwd, $"PMC-{i + 1}");
                }
                if (sc.Values.Is("tr", "Yes"))
                    for (int i = 0; i < n; i++)
                        for (int j = 0; j < n; j++)
                            if (i != j)
                            {
                                sc.KeptTruces.Add(new KeyValuePair<string, string>($"PMC-{i + 1}", $"PMC-{j + 1}"));
                                sc.KeptTruces.Add(new KeyValuePair<string, string>($"PMC-{i + 1}", $"Scav-{j + 1}"));
                            }
                return null;
            };
            p.Setup = sc =>
            {
                if (sc.KeptTruces.Count > 0) sc.Force("different encounters never fire at each other");
                int n = sc.Values.I("pairs");
                for (int i = 1; i <= n; i++)
                {
                    PrisonerTo(sc, sc.Find($"Scav-{i}"), sc.Find($"PMC-{i}"), custody: true, "forced start");
                    Opening(sc, sc.Values.S("op"), sc.Values.F("delay") + i, sc.Find($"Scav-{i}"), sc.Find($"PMC-{i}"));
                }
            };
            list.Add(p.Done());

            // 0.15.0
            p = new ScenarioPreset
            {
                Name = "Captor closes in and kneels prisoner",
                Tests = "A bot captor walks up to a disarmed prisoner and orders him onto his knees; the prisoner weighs resisting as he comes; kneeling facing and look limits.",
                Watch = "'close in on', 'closes in on', 'stops closing in', 'puts ... on his knees' / 'can't kneel there', 'turned to face where the order came from', 'sees ... on his knees', 'opening (... range x, posture x)', 'is behind him, out of view', 'loses sight of his captor'. Pose: Log pose diagnostics on, walk round the kneeling man.",
                Prereq = "Forced custody. Captor plan forces the choice (or leaves it to the real rolls). Ground: place it on flat open ground, then on stairs or in clutter for the unsuitable case.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f)); p.Params.Add(Sep(16f, 6f, 40f));
            p.Params.Add(ParamDef.Number("squad", "Captor squad size", 1f, 3f, 1f, 1f));
            p.Params.Add(ParamDef.Choice("plan", "Captor plan", new[] { "Close in, then kneel", "Kneel from where he stands", "Close in, no kneel", "Real rolls" }));
            p.Params.Add(Kit("k", "Prisoner keeps")); p.Params.Add(NerveP("n", "Prisoner nerve")); p.Params.Add(Pers("pe", "Prisoner personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Squad(sc, "BEAR", sc.Values.I("squad"), -h, 0f, "Prisoner");
                From(Add(sc, "Prisoner", "Scav", "B", h, 0f, sc.Values.I("squad") > 1 ? "PMC-1" : "PMC"), sc.Values, null, null, "k", "n", "pe");
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant captor = sc.Find(sc.Values.I("squad") > 1 ? "PMC-1" : "PMC");
                Participant prisoner = sc.Find("Prisoner");
                PrisonerTo(sc, prisoner, captor, custody: true, "forced start");
                string plan = sc.Values.S("plan");
                if (plan != "Real rolls")
                    sc.Step($"{captor.Tag}: plan '{plan}'", () =>
                    {
                        if (!captor.Active) return captor.Dead ? "the captor died" : "";
                        bool close = plan != "Kneel from where he stands";
                        bool kneel = plan != "Close in, no kneel";
                        return Captors.DebugPlan(captor.Player, close, kneel);
                    });
            };
            list.Add(p.Done());

            // ---- 0.16.0 ----

            // Captor disarms and takes the gear
            p = new ScenarioPreset
            {
                Name = "Captor disarms and takes the gear",
                Tests = "A bot captor demands the weapons, judges what he sees, then weighs each dropped item (worth, room, risk, nature) and fetches some, all or none, now or after letting the man go.",
                Watch = "'told to drop his weapons ... ->', 'order 'drop your weapons': ... on the ground ...; disarmed / NOT disarmed', 'after 'drop your weapons'', 'weighs what ... dropped' (one line per item), 'goes to take', 'took ... into his ... slot', 'stops going for'. The prisoner: 'opening (his captor is picking up ...)'.",
                Prereq = "Forced custody and a forced plan (Stay, Drop your weapons, optional Turn around). Everything after is real: compliance (unless forced), notice, gear choices, the pick-up transaction.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f)); p.Params.Add(Sep(12f));
            p.Params.Add(ParamDef.Number("squad", "Captor squad size", 1f, 3f, 1f, 1f));
            p.Params.Add(ParamDef.Choice("pc", "Prisoner answer", new[] { "His own decision", "Drops all (FORCED)", "Keeps one back (FORCED)" }));
            p.Params.Add(ParamDef.Choice("turn", "Turn him round first", YesNo, 1));
            p.Params.Add(ParamDef.Choice("want", "Captor wants the gear", new[] { "His own roll", "Always (FORCED appetite)" }));
            p.Params.Add(Kit("k", "Prisoner keeps")); p.Params.Add(NerveP("n", "Prisoner nerve")); p.Params.Add(Pers("cpe", "Captor personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Participant cap = Squad(sc, "BEAR", sc.Values.I("squad"), -h, 0f, "Prisoner");
                From(cap, sc.Values, null, null, null, null, "cpe");
                From(Add(sc, "Prisoner", "Scav", "B", h, 0f, cap.Tag), sc.Values, null, null, "k", "n", null);
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find(sc.Values.I("squad") > 1 ? "PMC-1" : "PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                sc.Step($"{cap.Tag}: plan and forced choices", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    ForceAnswer(sc, pr, sc.Values.S("pc"));
                    if (sc.Values.Is("want", "Always (FORCED appetite)")) { CaptorLoot.ForceWant(cap.Player, true); sc.Force($"{cap.Tag} wants every item worth anything (appetite roll skipped; room, risk, fetch real)"); }
                    return sc.Values.Is("turn", "Yes")
                        ? Captors.DebugSteps(cap.Player, "disarm, turn him round, gear", OrderKind.Stay, OrderKind.DropWeapons, OrderKind.TurnAround)
                        : Captors.DebugSteps(cap.Player, "disarm, gear", OrderKind.Stay, OrderKind.DropWeapons);
                });
            };
            list.Add(p.Done());

            // Prisoner keeps a weapon back
            p = new ScenarioPreset
            {
                Name = "Prisoner keeps a weapon back",
                Tests = "Deceptive compliance: he drops one weapon and keeps another; the captor may notice it (a slung gun at any range, a holstered pistol only close) and demand it again; a real distraction (the captor fetching gear) may give him his opening, weighed against the other guards he can see.",
                Watch = "'keeps the ... back', 'NOT disarmed: still carries', 'notices it; demands it again' or 'doesn't notice it', 'told again ... comply / refuse / escalate', 'opening (his captor is picking up ...)', 'takes an opening ... draw ... (kept back for this)', 'his resistance shows', 'DECIDES TO KILL' / 'lets him go'.",
                Prereq = "Forced custody and plan (Stay, Drop your weapons). Kit: he holds his sidearm, keeps a slung gun (or keeps his sidearm). Which weapon he keeps back is forced; everything else is real.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f)); p.Params.Add(Sep(10f));
            p.Params.Add(ParamDef.Number("squad", "Captor squad size", 1f, 3f, 1f, 1f));
            p.Params.Add(ParamDef.Choice("keep", "He keeps back", new[] { "Holstered pistol (seen only close)", "Slung gun (seen)", "Knife (never seen)", "His own decision" }));
            p.Params.Add(ParamDef.Choice("again", "Told again, he", new[] { "Decides himself", "Hands it over (FORCED)", "Refuses (FORCED)" }));
            p.Params.Add(ParamDef.Choice("want", "Captor wants the gear", new[] { "Always (FORCED appetite)", "His own roll" }));
            p.Params.Add(NerveP("n", "Prisoner nerve")); p.Params.Add(Pers("pe", "Prisoner personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Participant cap = Squad(sc, "BEAR", sc.Values.I("squad"), -h, 0f, "Prisoner");
                Participant pr = Add(sc, "Prisoner", "Scav", "B", h, 0f, cap.Tag);
                From(pr, sc.Values, null, null, null, "n", "pe");
                // The weapon in his hands is the one he gives up at once; what stays on him is the test.
                string keep = sc.Values.S("keep");
                pr.Kit = keep.StartsWith("Holstered") ? "Keeps sidearm" : keep.StartsWith("Slung") ? "Keeps slung primary" : keep.StartsWith("Knife") ? "Knife only" : "As generated";
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find(sc.Values.I("squad") > 1 ? "PMC-1" : "PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                sc.Step($"{pr.Tag}: what he keeps back", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    string keep = sc.Values.S("keep");
                    string again = sc.Values.S("again");
                    if (keep != "His own decision")
                    {
                        EquipmentSlot slot = keep.StartsWith("Holstered") ? EquipmentSlot.Holster : keep.StartsWith("Knife") ? EquipmentSlot.Scabbard : EquipmentSlot.FirstPrimaryWeapon;
                        if (slot == EquipmentSlot.FirstPrimaryWeapon && pr.Player.Equipment?.GetSlot(slot)?.ContainedItem == null) slot = EquipmentSlot.SecondPrimaryWeapon;
                        Compliance.ForceSlot(pr.Player, slot);
                        Compliance.Force(pr.Player, again.StartsWith("Refuses") ? ComplianceChoice.Refuses : ComplianceChoice.KeepsOne);
                        sc.Force($"{pr.Tag} keeps his {slot} weapon back at the first demand{(again.StartsWith("Refuses") ? " and refuses when told again" : again.StartsWith("Hands") ? " and hands it over when told again" : "")}");
                    }
                    if (sc.Values.Is("want", "Always (FORCED appetite)")) { CaptorLoot.ForceWant(cap.Player, true); sc.Force($"{cap.Tag} wants the dropped gear (appetite roll skipped)"); }
                    return Captors.DebugSteps(cap.Player, "disarm (then his own judgement and gear choices)", OrderKind.Stay, OrderKind.DropWeapons);
                });
            };
            list.Add(p.Done());

            // Turn around and hands on head
            p = new ScenarioPreset
            {
                Name = "Turn around and hands on head",
                Tests = "Turn around (his back to where the issuer stood, kept while the issuer walks round him; from his knees: up, turn, down) and Hands on head standing and kneeling (posture unchanged, head within its hands-on-head limits).",
                Watch = "'turns his back to', 'turned round in ... s', 'turn around from his knees: gets up', 'kneels again facing away', 'hands on his head (... posture unchanged)', 'is behind him, out of view', 'loses sight of his captor'. Walk round him: he must not turn back. Log pose diagnostics on for the head.",
                Prereq = "Forced surrender (to you or to a bot captor with custody). The orders are given on a timer through the real order path (yours from wherever you stand: the test path, labelled).",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(8f));
            p.Params.Add(ParamDef.Choice("by", "Orders from", new[] { "You", "PMC bot" }));
            p.Params.Add(ParamDef.Choice("pose", "Posture", new[] { "Standing", "Kneeling" }));
            p.Params.Add(ParamDef.Choice("seq", "Orders", new[] { "Hands on head, then turn", "Turn, then hands on head", "Turn only", "Hands on head only" }));
            p.Params.Add(ParamDef.Number("gap", "Between orders (s)", 2f, 20f, 1f, 6f));
            p.Params.Add(Pers("pe", "Prisoner personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                bool bot = sc.Values.Is("by", "PMC bot");
                if (bot) Squad(sc, "BEAR", 1, -4f, 0f, "Prisoner");
                From(Add(sc, "Prisoner", "Scav", "B", bot ? 4f : 0f, 0f, bot ? "PMC" : "you"), sc.Values, null, null, null, null, "pe");
                return null;
            };
            p.Setup = sc =>
            {
                Participant pr = sc.Find("Prisoner");
                Participant cap = sc.Find("PMC");
                PrisonerTo(sc, pr, cap, custody: cap != null, "forced start");
                if (cap != null)
                    sc.Step($"{cap.Tag}: holds him for the test (no plan of his own)", () => cap.Active ? Captors.DebugSteps(cap.Player, "order test (the scenario gives the orders)", OrderKind.Stay) : (cap.Dead ? "died" : ""));
                float gap = sc.Values.F("gap"), at = 2f;
                ScenarioEvent hands = cap != null ? ScenarioEvent.CaptorOrderHands : ScenarioEvent.OrderHands;
                ScenarioEvent turn = cap != null ? ScenarioEvent.CaptorOrderTurn : ScenarioEvent.OrderTurn;
                if (sc.Values.Is("pose", "Kneeling"))
                {
                    sc.After(at, "posture: kneel", () =>
                    {
                        SurrenderState s = ScenarioOps.StateOf(pr);
                        if (s == null) return;
                        Player by = cap?.Player ?? sc.Observer;
                        sc.Note(TL.Real, $"kneel order: {(Orders.Give(s, OrderKind.Kneel, by, cap == null, out string why, debug: true) ? "given" : $"refused ({why})")}");
                    });
                    at += gap + 3f;
                }
                string seq = sc.Values.S("seq");
                if (seq.StartsWith("Hands")) { sc.After(at, "hands on head", () => ScenarioEvents.Run(sc, hands, pr)); at += gap; }
                if (seq.Contains("urn")) { sc.After(at, "turn around", () => ScenarioEvents.Run(sc, turn, pr)); at += gap; }
                if (seq == "Turn, then hands on head") sc.After(at, "hands on head", () => ScenarioEvents.Run(sc, hands, pr));
            };
            list.Add(p.Done());

            // Captor judgement
            p = new ScenarioPreset
            {
                Name = "Captor judgement",
                Tests = "The captor's decision points: notice and repeat a demand, a refusal, the release decision (mercy or a lethal decision) with what he knows of the man; an observed draw is always fought.",
                Watch = "'intent about ... (decided once, at custody): chance ... -> MEANS TO KILL HIM / means to let him go', 'after 'drop your weapons'', 'demands it again', 'lethal? chance', 'done with ...: carries out what he decided' / 'lets him go, as he decided' / 'something new since he took him', 'DECIDES TO KILL', 'FIRES at', 'KILL DECISION CARRIED OUT' or 'EXECUTION FAILURE', 'killed with his hands up by ...: as decided (...)' vs 'RESTRAINT LEAK'. Squadmates: their own 'holds fire' lines stand. Run 'His own roll' several times: both outcomes should appear (0.25 base x aggression x rivals).",
                Prereq = "Forced custody and plan (Stay, Drop your weapons). The trigger is forced or SIMULATED as named; the outcome is the real roll unless forced.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f)); p.Params.Add(Sep(10f));
            p.Params.Add(ParamDef.Choice("trig", "Situation", new[] { "He complies", "He keeps a slung gun (seen)", "He refuses when told again", "He shot the captor before (SIMULATED)", "He draws during custody (FORCED)" }));
            p.Params.Add(ParamDef.Choice("out", "Captor decides", new[] { "His own roll", "Mercy (FORCED)", "Lethal (FORCED)" }));
            p.Params.Add(ParamDef.Number("squad", "Captor squad size", 1f, 3f, 1f, 1f));
            p.Params.Add(Pers("cpe", "Captor personality")); p.Params.Add(Delay(12f, "Draw after (s)"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Participant cap = Squad(sc, "BEAR", sc.Values.I("squad"), -h, 0f, "Prisoner");
                From(cap, sc.Values, null, null, null, null, "cpe");
                Participant pr = Add(sc, "Prisoner", "Scav", "B", h, 0f, cap.Tag);
                string trig = sc.Values.S("trig");
                if (trig.Contains("slung") || trig.Contains("refuses") || trig.Contains("draws")) pr.Kit = "Keeps slung primary";
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find(sc.Values.I("squad") > 1 ? "PMC-1" : "PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                string trig = sc.Values.S("trig"), outc = sc.Values.S("out");
                sc.Step($"{cap.Tag}: situation '{trig}', outcome '{outc}'", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    if (outc.StartsWith("Mercy")) { Judgement.Force(cap.Player, JudgeForce.Mercy); sc.Force($"{cap.Tag}'s decisions forced to mercy"); }
                    else if (outc.StartsWith("Lethal")) { Judgement.Force(cap.Player, JudgeForce.Lethal); sc.Force($"{cap.Tag}'s decisions forced to lethal"); }
                    if (trig.Contains("slung")) { Compliance.ForceSlot(pr.Player, EquipmentSlot.FirstPrimaryWeapon); Compliance.Force(pr.Player, ComplianceChoice.KeepsOne); sc.Force($"{pr.Tag} keeps his slung gun back, hands it over when told again"); }
                    else if (trig.Contains("refuses")) { Compliance.ForceSlot(pr.Player, EquipmentSlot.FirstPrimaryWeapon); Compliance.Force(pr.Player, ComplianceChoice.Refuses); sc.Force($"{pr.Tag} keeps his slung gun back and refuses when told again"); }
                    else if (trig.Contains("complies")) { Compliance.Force(pr.Player, ComplianceChoice.DropsAll); sc.Force($"{pr.Tag} drops everything"); }
                    if (trig.Contains("shot the captor")) ScenarioEvents.Run(sc, ScenarioEvent.CaptorWasHit, pr);
                    return Captors.DebugSteps(cap.Player, "disarm (then his own judgement)", OrderKind.Stay, OrderKind.DropWeapons);
                });
                if (trig.Contains("draws")) sc.After(sc.Values.F("delay"), "the prisoner draws", () => ScenarioEvents.Run(sc, ScenarioEvent.DrawKept, pr));
            };
            list.Add(p.Done());

            // Spawn staging test
            p = new ScenarioPreset
            {
                Name = "Spawn staging test",
                Tests = "Scenario start-up: every profile asked for at once, each bot held from creation until all are created, active and on the hold, the start state checked, all released together; failures and cancellation clean up with the stage each participant had reached.",
                Watch = "'profile requested', 'profile ready after', 'spawned ... held until everyone is ready', 'everyone ready after ... released together; stage timings: ...', 'start state: ...'. Slow: the last one's activation 8 s late (SIMULATED), nobody moves meanwhile. Failure / cancel: 'setup failed: ... (stage: ...)', the report's 'spawn stages', nobody left frozen or spawned late.",
                Prereq = "Then the same start as PMC squad captures a bot (forced custody), so the release is visible.",
                Natural = false, NeedSight = false, MaxStart = 40f
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f)); p.Params.Add(Sep(12f));
            p.Params.Add(ParamDef.Choice("test", "Test", new[] { "Normal start", "Slow last spawn (SIMULATED 8 s)", "One spawn fails (SIMULATED)", "Cancel mid-spawn" }));
            p.Params.Add(ParamDef.Number("squad", "Captor squad size", 1f, 3f, 1f, 2f));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Squad(sc, "BEAR", sc.Values.I("squad"), -h, 0f, "Prisoner");
                Participant pr = Add(sc, "Prisoner", "Scav", "B", h, 0f, sc.Values.I("squad") > 1 ? "PMC-1" : "PMC");
                string test = sc.Values.S("test");
                Participant last = sc.People[sc.People.Count - 1];
                if (test.StartsWith("Slow")) { last.ActivateAfter = Time.time + 8f; sc.Note(TL.Simulated, $"{last.Tag}: activation held 8 s (test: slow initialisation)"); }
                else if (test.StartsWith("One")) { last.InjectFail = true; sc.Note(TL.Simulated, $"{last.Tag}: his activation will fail (test)"); }
                else if (test.StartsWith("Cancel")) { sc.CancelAt = Time.time + 2.5f; sc.Note(TL.Simulated, "the scenario will be cleaned up 2.5 s after launch, mid-spawn (test)"); }
                return null;
            };
            p.Setup = sc =>
            {
                Participant cap = sc.Find(sc.Values.I("squad") > 1 ? "PMC-1" : "PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start (after a staged spawn)");
            };
            list.Add(p.Done());

            // ---- 0.17.0 ----

            // Captor execution check
            p = new ScenarioPreset
            {
                Name = "Captor execution check",
                Tests = "The whole path from a captor's lethal decision to real fire: Reject on that captor only (final), his target, the AI's own aim and trigger, his squadmates' restraint unchanged. Or the natural decision when the prisoner bolts.",
                Watch = "'DECIDES TO KILL', 'Restraint: ... refuses the surrender of ... decides to kill him', 'restraint on ... now Reject (final); still holding fire at him: no; ignore flag: clear', 'has ... as his target', 'SAIN had chosen ...; his target set to ...', 'FIRES at ... (trigger pressed)', 'KILL DECISION CARRIED OUT' (or 'no shot ... yet, 3 s after' and 'EXECUTION FAILURE ... after 10 s: layer, game target, SAIN target, hands, sight, aim, restraint, shield'), 'killed with his hands up by ...: as decided'. Squadmates keep 'holds fire'. Natural: '... runs from his custody: fire on him? chance ...'. Ctrl+U (debug keys on) forces the same decision on any bot captor.",
                Prereq = "Forced custody. 'Kill now' forces ONLY the decision; everything after is the real game. 'He bolts' forces his run; the captor's answer is his own roll unless forced.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f)); p.Params.Add(Sep(10f));
            p.Params.Add(ParamDef.Choice("what", "Trigger", new[] { "Kill now (FORCED decision only)", "He bolts (captor's own roll)", "He bolts (FORCED lethal answer)" }));
            p.Params.Add(ParamDef.Choice("pose", "Prisoner", new[] { "Standing", "Kneeling", "Hands on head" }));
            p.Params.Add(ParamDef.Number("squad", "Captor squad size", 1f, 3f, 1f, 2f));
            p.Params.Add(Pers("cpe", "Captor personality")); p.Params.Add(Delay(10f, "Trigger after (s)"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Participant cap = Squad(sc, "BEAR", sc.Values.I("squad"), -h, 0f, "Prisoner");
                From(cap, sc.Values, null, null, null, null, "cpe");
                Add(sc, "Prisoner", "Scav", "B", h, 0f, cap.Tag);
                return null;
            };
            p.Setup = sc =>
            {
                Participant cap = sc.Find(sc.Values.I("squad") > 1 ? "PMC-1" : "PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                string what = sc.Values.S("what"), pose = sc.Values.S("pose");
                sc.Step($"{cap.Tag}: holds him (no plan of his own)", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    if (what.Contains("FORCED lethal")) { Judgement.Force(cap.Player, JudgeForce.Lethal); sc.Force($"{cap.Tag}'s answers forced to lethal"); }
                    return Captors.DebugSteps(cap.Player, "execution check", OrderKind.Stay, pose == "Kneeling" ? OrderKind.Kneel : pose == "Hands on head" ? OrderKind.HandsOnHead : OrderKind.Stay);
                });
                float at = sc.Values.F("delay");
                if (what.StartsWith("Kill")) sc.After(at, "the captor's decision", () => ScenarioEvents.Run(sc, ScenarioEvent.CaptorKillsNow, pr));
                else sc.After(at, "the prisoner bolts", () => ScenarioEvents.Run(sc, ScenarioEvent.ForceRun, pr));
            };
            list.Add(p.Done());

            // ---- 0.18.0 ----

            // Head straight check
            p = new ScenarioPreset
            {
                Name = "Head straight check",
                Tests = "A stationary prisoner with his captor directly in front: head straight ahead and upright, chest square, standing or kneeling, hands up or on his head. Then walk to his left and right: head and chest turn together, no head roll; standing, past 70 deg his feet step round.",
                Watch = "'head rig checked: ... animated head N deg yaw ... (game free-look N deg)', then once a second 'gaze: ... captor at +0 deg ...; after: head yaw ~0 pitch small roll ~0; chest ~0'. Walking round him: 'after: head yaw' follows you, 'roll' stays within a degree or two, 'chest' and 'our torso turn' grow together. Variation On: glances come back, roll still ~0.",
                Prereq = "Forced surrender (to you, or to a PMC standing in front of him). Pose diagnostics are on for this scenario; Variation Off is the Head neutral test (no glances, demeanor tilt, tremble, flinch, kneeling idle or gestures).",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(6f));
            p.Params.Add(ParamDef.Choice("by", "Captor", new[] { "You", "PMC bot (in front of him)" }));
            p.Params.Add(ParamDef.Choice("pose", "Posture", new[] { "Standing", "Kneeling" }));
            p.Params.Add(ParamDef.Choice("hands", "Hands", new[] { "Up", "On his head" }));
            p.Params.Add(ParamDef.Choice("var", "Variation", new[] { "Off (neutral test)", "On" }));
            p.Params.Add(Pers("pe", "Prisoner personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                bool bot = sc.Values.S("by").StartsWith("PMC");
                if (bot) Squad(sc, "BEAR", 1, -2.5f, 0f, "Prisoner");
                From(Add(sc, "Prisoner", "Scav", "B", bot ? 2.5f : 0f, 0f, bot ? "PMC" : "you"), sc.Values, null, null, null, null, "pe");
                return null;
            };
            p.Setup = sc =>
            {
                Participant pr = sc.Find("Prisoner");
                Participant cap = sc.Find("PMC");
                bool neutral = sc.Values.S("var").StartsWith("Off");
                Presence.TestDiag = true;
                Presence.TestNeutral = neutral;
                sc.Force($"pose diagnostics on{(neutral ? "; Head neutral test on (no glances, tilt, tremble, flinch, idle, gestures)" : "")} for this scenario");
                PrisonerTo(sc, pr, cap, custody: cap != null, "forced start");
                if (cap != null)
                    sc.Step($"{cap.Tag}: holds him in front (no plan of his own)", () => cap.Active ? Captors.DebugSteps(cap.Player, "head check (stands in front)", OrderKind.Stay) : (cap.Dead ? "died" : ""));
                float at = 2f;
                if (sc.Values.Is("pose", "Kneeling"))
                {
                    sc.After(at, "posture: kneel", () =>
                    {
                        SurrenderState s = ScenarioOps.StateOf(pr);
                        if (s == null) return;
                        Player by = cap?.Player ?? sc.Observer;
                        sc.Note(TL.Real, $"kneel order: {(Orders.Give(s, OrderKind.Kneel, by, cap == null, out string why, debug: true) ? "given" : $"refused ({why})")}");
                    });
                    at += 6f;
                }
                if (sc.Values.Is("hands", "On his head"))
                    sc.After(at, "hands on head", () => ScenarioEvents.Run(sc, cap != null ? ScenarioEvent.CaptorOrderHands : ScenarioEvent.OrderHands, pr));
            };
            list.Add(p.Done());

            // Weapon pickup opportunity
            p = new ScenarioPreset
            {
                Name = "Weapon pickup opportunity",
                Tests = "The captor demands the weapons and goes to collect the one he made him drop (the game's pick-up animation and transaction); a prisoner with a weapon still on him weighs that window (draw, run or keep still); a covering squadmate closes it; an interrupted pick-up ends cleanly and is weighed again, never retried at once.",
                Watch = "'weighs what (else) ... dropped': '<weapon>: take (the weapon he made him drop ...)', 'goes to take ... (the weapon he made him drop)', 'took ... into his ... slot (... pick-up animation ...)' and the item in his kit, or 'stops going for the ...: <why>' + 'will weigh ... again'. Prisoner: 'opening (his captor is picking up ...)' with 'pick-up window x...', 'takes an opening ... draw' or 'keeps still'; captor: 'sees ... reaching for a weapon'. Squad 2: 'his captor isn't covering him, but ... is'. No 'transaction threw', no stuck hands.",
                Prereq = "Forced custody and plan (Stay, Drop your weapons). What the prisoner keeps is forced by his kit and the first-demand answer; whether he acts is his own roll unless forced. Interruptions are FORCED (kill) or SIMULATED (the weapon removed), each at the moment the pick-up starts.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f)); p.Params.Add(Sep(8f));
            p.Params.Add(ParamDef.Number("squad", "Captor squad size", 1f, 2f, 1f, 1f));
            p.Params.Add(ParamDef.Choice("keep", "He keeps back", new[] { "Holstered pistol", "Slung gun", "Nothing" }));
            p.Params.Add(ParamDef.Choice("act", "During the pick-up he", new[] { "Decides himself", "Draws (FORCED)" }));
            p.Params.Add(ParamDef.Choice("test", "Interruption", new[] { "None", "Prisoner killed (FORCED)", "Captor killed (FORCED)", "Weapon gone before he gets there (SIMULATED)" }));
            p.Params.Add(NerveP("n", "Prisoner nerve")); p.Params.Add(Pers("pe", "Prisoner personality")); p.Params.Add(Pers("cpe", "Captor personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Participant cap = Squad(sc, "BEAR", sc.Values.I("squad"), -h, 0f, "Prisoner");
                From(cap, sc.Values, null, null, null, null, "cpe");
                Participant pr = Add(sc, "Prisoner", "Scav", "B", h, 0f, cap.Tag);
                From(pr, sc.Values, null, null, null, "n", "pe");
                string keep = sc.Values.S("keep");
                pr.Kit = keep.StartsWith("Holstered") ? "Keeps sidearm" : keep.StartsWith("Slung") ? "Keeps slung primary" : "As generated";
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find(sc.Values.I("squad") > 1 ? "PMC-1" : "PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                string keep = sc.Values.S("keep"), act = sc.Values.S("act"), test = sc.Values.S("test");
                sc.Step($"{cap.Tag}: disarm and collect", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    if (keep == "Nothing") { Compliance.Force(pr.Player, ComplianceChoice.DropsAll); sc.Force($"{pr.Tag} drops everything on the demand"); }
                    else
                    {
                        EquipmentSlot slot = keep.StartsWith("Holstered") ? EquipmentSlot.Holster : EquipmentSlot.FirstPrimaryWeapon;
                        if (slot == EquipmentSlot.FirstPrimaryWeapon && pr.Player.Equipment?.GetSlot(slot)?.ContainedItem == null) slot = EquipmentSlot.SecondPrimaryWeapon;
                        Compliance.ForceSlot(pr.Player, slot);
                        Compliance.Force(pr.Player, ComplianceChoice.KeepsOne);
                        sc.Force($"{pr.Tag} keeps his {slot} weapon back at the demand");
                    }
                    return Captors.DebugSteps(cap.Player, "disarm, collect the weapons", OrderKind.Stay, OrderKind.DropWeapons);
                });
                Func<bool> bending = () => CaptorLoot.Busy(cap.Player, out string b) && !b.StartsWith("walking");
                if (act.StartsWith("Draws")) sc.OnceWhen(bending, 120f, "the prisoner draws during the pick-up", () => ScenarioEvents.Run(sc, ScenarioEvent.DrawKept, pr));
                if (test.StartsWith("Prisoner killed")) sc.OnceWhen(bending, 120f, "the prisoner is killed mid pick-up", () => ScenarioEvents.Run(sc, ScenarioEvent.Kill, pr));
                else if (test.StartsWith("Captor killed")) sc.OnceWhen(bending, 120f, "the captor is killed mid pick-up", () => ScenarioEvents.Run(sc, ScenarioEvent.Kill, cap));
                else if (test.StartsWith("Weapon gone"))
                    sc.OnceWhen(() => CaptorLoot.TryJob(cap.Player, out LootJob j) && j.Stage == LootStage.Walk, 120f, "the weapon disappears before he gets there", () =>
                    {
                        if (CaptorLoot.TryJob(cap.Player, out LootJob j)) sc.Note(TL.Simulated, $"{j.C.Name}: {ScenarioOps.CleanItem(sc, j.C.Item, remove: true)}");
                    });
            };
            list.Add(p.Done());

            // 0.20.0: Squad cover
            p = new ScenarioPreset
            {
                Name = "Squad cover",
                Tests = "A squad's captor keeps custody while one squadmate (chosen by the mod, not forced) takes a spot to the side and covers; the captor waits for that cover before fetching the dropped weapon; the guard reacts to a draw only once he sees it; a blocked guard moves; a guard drawn into a fight stops covering; custody goes to the guard when the captor dies; a lone captor knows he is alone.",
                Watch = "'<squad>: <guard> covers him while he holds ...', 'covers ... from where he stands' or '...; moves to cover ... from N m away', 'in position', 'covering ... (N m, gun on him)' / 'not covering ...: <reason>', 'waits for <guard> to cover him before ...' then '..., <guard> covering him' or '... without cover after waiting ...: accepts the risk'. Draw: '<guard>: sees ... drawing a weapon (N deg ...); reacts in N s', 'GUARD REACTS: ... engages ...' (or 'doesn't see ...' + 'takes his squad's word'). Distracted: 'stops covering ... to fight', 'quiet again' or 'stops covering ... (fighting ...)'. Captor killed: 'takes over ... (he was covering him)'. Alone: 'nobody can cover him ... he holds him alone'. Every role ends with 'stops covering ... after N s: covering N% of it'.",
                Prereq = "Forced custody and plan (Stay, Drop your weapons). The guard is the mod's own pick. Draw, kills and the hostile bot are FORCED or real events at the stated moment; everything the guard and captor do in between is their own.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f)); p.Params.Add(Sep(8f, 4f, 12f));
            p.Params.Add(ParamDef.Number("squad", "Captor squad size", 1f, 3f, 1f, 2f));
            p.Params.Add(ParamDef.Choice("start", "Squadmate starts", new[] { "Beside the captor", "Behind the captor (line blocked)" }));
            p.Params.Add(ParamDef.Choice("keep", "He keeps back", new[] { "Holstered pistol", "Slung gun", "Nothing" }));
            p.Params.Add(ParamDef.Choice("test", "Event", new[] { "None (pick-up with cover)", "Prisoner draws at the pick-up (FORCED)", "Hostile bot at the guard (real)", "Captor killed at the pick-up (FORCED)", "Guard killed in position (FORCED)", "You take the prisoner over (your order)" }));
            p.Params.Add(NerveP("n", "Prisoner nerve")); p.Params.Add(Pers("pe", "Prisoner personality")); p.Params.Add(Pers("cpe", "Captor personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                int n = sc.Values.I("squad");
                bool behind = sc.Values.S("start").StartsWith("Behind");
                Participant cap = Add(sc, n > 1 ? "PMC-1" : "PMC", "BEAR", "A", -h, 0f, "Prisoner");
                From(cap, sc.Values, null, null, null, null, "cpe");
                if (n >= 2) Add(sc, "PMC-2", "BEAR", "A", behind ? -h - 2.5f : -h - 1f, behind ? 0f : 3f, "Prisoner");
                if (n >= 3) Add(sc, "PMC-3", "BEAR", "A", -h - 2f, -3.5f, "Prisoner");
                Participant pr = Add(sc, "Prisoner", "Scav", "B", h, 0f, cap.Tag);
                From(pr, sc.Values, null, null, null, "n", "pe");
                string keep = sc.Values.S("keep");
                pr.Kit = keep.StartsWith("Holstered") ? "Keeps sidearm" : keep.StartsWith("Slung") ? "Keeps slung primary" : "As generated";
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find(sc.Values.I("squad") > 1 ? "PMC-1" : "PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                string keep = sc.Values.S("keep"), test = sc.Values.S("test");
                sc.Step($"{cap.Tag}: disarm and collect", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    if (keep == "Nothing") { Compliance.Force(pr.Player, ComplianceChoice.DropsAll); sc.Force($"{pr.Tag} drops everything on the demand"); }
                    else
                    {
                        EquipmentSlot slot = keep.StartsWith("Holstered") ? EquipmentSlot.Holster : EquipmentSlot.FirstPrimaryWeapon;
                        if (slot == EquipmentSlot.FirstPrimaryWeapon && pr.Player.Equipment?.GetSlot(slot)?.ContainedItem == null) slot = EquipmentSlot.SecondPrimaryWeapon;
                        Compliance.ForceSlot(pr.Player, slot);
                        Compliance.Force(pr.Player, ComplianceChoice.KeepsOne);
                        sc.Force($"{pr.Tag} keeps his {slot} weapon back at the demand");
                    }
                    return Captors.DebugSteps(cap.Player, "disarm, collect the weapons", OrderKind.Stay, OrderKind.DropWeapons);
                });
                // Whoever the mod picked to cover (whatever captor holds him now).
                Func<Participant> guard = () =>
                {
                    Player gp = ScenarioOps.StateOf(pr)?.Custody?.Guard?.Bot;
                    if (gp == null) return null;
                    foreach (Participant q in sc.People) if (q.Player == gp) return q;
                    return null;
                };
                Func<bool> inPosition = () => ScenarioOps.StateOf(pr)?.Custody is CaptorState cs && Overwatch.InPosition(cs, out _);
                Func<bool> bending = () => CaptorLoot.Busy(cap.Player, out string b) && !b.StartsWith("walking");
                if (test.StartsWith("Prisoner draws")) sc.OnceWhen(bending, 120f, "the prisoner draws during the pick-up", () => ScenarioEvents.Run(sc, ScenarioEvent.DrawKept, pr));
                else if (test.StartsWith("Captor killed")) sc.OnceWhen(bending, 120f, "the captor is killed mid pick-up", () => ScenarioEvents.Run(sc, ScenarioEvent.Kill, cap));
                else if (test.StartsWith("Hostile bot"))
                    sc.OnceWhen(inPosition, 90f, "a hostile bot comes for the guard", () =>
                    {
                        Participant g = guard();
                        if (g == null) sc.Note(TL.ExecFail, "no guard to aim the hostile bot at");
                        else ScenarioEvents.Run(sc, ScenarioEvent.ThirdParty, g);
                    });
                else if (test.StartsWith("Guard killed"))
                    sc.OnceWhen(inPosition, 90f, "the guard is killed in position", () =>
                    {
                        Participant g = guard();
                        if (g == null) sc.Note(TL.ExecFail, "no guard to kill");
                        else ScenarioEvents.Run(sc, ScenarioEvent.Kill, g);
                    });
                else if (test.StartsWith("You take"))
                    sc.OnceWhen(inPosition, 90f, "you take the prisoner over", () =>
                    {
                        SurrenderState s = ScenarioOps.StateOf(pr);
                        if (s == null) { sc.Note(TL.ExecFail, "no prisoner"); return; }
                        bool ok = Orders.Give(s, OrderKind.Stay, sc.Observer, true, out string why, debug: true);
                        sc.Note(ok ? TL.Real : TL.ExecFail, ok ? "you order him to stay: you take him over (the real order path, from any distance)" : $"your order was refused: {why}");
                    });
            };
            list.Add(p.Done());

            // Retreat from captivity (0.21.0: where he goes after: his own AI, followed in the log)
            p = new ScenarioPreset
            {
                Name = "Retreat from captivity",
                Tests = "A man leaving captivity gets clear of his captor along a reachable route (never through a wall or a shut door), decides at once (a kept gun, his own gun if it is safe, another gun near, or nothing), and is handed to his own AI, which carries on with the raid: no stop-and-crouch, no lingering at the capture spot.",
                Watch = "'flees to a spot ... m from where he was held', 'retreat ends (...)', then 'back to his own AI (...; armed: ... / unarmed, N m from where he was held, N s after leaving captivity)'. Then 'after release +15 s / +30 s / +60 s (last check): N m from where he was held ... layer <AI layer>, moving / still for N s, standing / crouched, target ...'; never 'STILL NEAR THE CAPTURE SPOT'. A door on the way: he stops at it and opens it (no walking through). 'the route crosses ...' (Verbose) = a route refused for geometry. Place it in the open, inside a building (crosshair placement), at a door, on stairs and in a dead end.",
                Prereq = "Forced start: already surrendered, forced custody, the captor's answers forced to mercy so the flight itself is what is tested. Let go: his real Leave order. Bolts: a forced run. Gun removed: real loot removal.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(25f)); p.Params.Add(Sep(8f));
            p.Params.Add(ParamDef.Choice("how", "He leaves", new[] { "Let go", "Bolts (FORCED run)" }));
            p.Params.Add(ParamDef.Choice("pose", "Prisoner", new[] { "Standing", "Kneeling" }));
            p.Params.Add(ParamDef.Choice("gun", "His dropped gun", new[] { "Left where it fell", "Removed (none for him)" }));
            p.Params.Add(Kit("k", "Prisoner keeps", 0));
            p.Params.Add(Delay(8f, "Leaves after (s)"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Squad(sc, "BEAR", 1, -h, 0f, "Prisoner");
                From(Add(sc, "Prisoner", "Scav", "B", h, 0f, "PMC"), sc.Values, null, null, "k", null, null);
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find("PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                sc.Step($"{cap.Tag}: holds him, mercy forced", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    Judgement.Force(cap.Player, JudgeForce.Mercy);
                    sc.Force($"{cap.Tag}'s answers forced to mercy (the flight is the test)");
                    return Captors.DebugSteps(cap.Player, "retreat test", OrderKind.Stay, sc.Values.Is("pose", "Kneeling") ? OrderKind.Kneel : OrderKind.Stay);
                });
                if (sc.Values.S("gun").StartsWith("Removed"))
                    sc.After(Mathf.Max(1f, sc.Values.F("delay") - 2f), "his dropped gun is removed", () => ScenarioEvents.Run(sc, ScenarioEvent.RemoveGun, pr));
                sc.After(sc.Values.F("delay"), "he leaves", () => ScenarioEvents.Run(sc, sc.Values.Is("how", "Let go") ? ScenarioEvent.CaptorLetsGo : ScenarioEvent.ForceRun, pr));
            };
            list.Add(p.Done());

            // 0.21.0: incoming fire breaks the hold-up
            p = new ScenarioPreset
            {
                Name = "Incoming fire breaks the hold-up",
                Tests = "Fire on either the prisoner or his captor, at any moment of the hold-up (standing, kneeling, the captor collecting the gear, the prisoner dropping his gear, being let go, the prisoner going for his gun), ends it for both: the captor goes to his combat AI at once (custody, orders, fetch and squad cover dropped), the prisoner gets to safety (cover from the fire, a kept weapon, or his own gun if close and safe) and is handed to his own AI; nobody takes or surrenders again while the fire lasts.",
                Watch = "'INCOMING FIRE: <who> (the prisoner / the captor) hit / a round N m past him by ...: the hold-up breaks', '<captor>: stops holding ... (incoming fire: ...)', '<prisoner> under fire (...): cover ...; draw ...; his gun ...; -> <choice>', then 'cover N m away, hidden from the fire' and 'back to his own AI (in cover from the fire ...)', or 'draws his ... under fire' / 'scrambles for his ... (under fire, ...)' and 'keeps going' or 'leaves it'. Kneeling: an escape rise, no walking on his knees. Squad 2: the guard's 'stops covering ...'. 'no surrender to ...: ... still under the fire' if anyone tries again. Then the 'after release +N s' lines.",
                Prereq = "Forced start: already surrendered, forced custody and plan. The fire is a real hostile bot (spawned 40 m out on the chosen man: tests detection too), a SIMULATED near miss or hit (the same queue a real one takes; tests the reactions only), or you shooting near them.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f)); p.Params.Add(Sep(7f));
            p.Params.Add(ParamDef.Choice("at", "Fire at", new[] { "The prisoner", "The captor" }));
            p.Params.Add(ParamDef.Choice("fire", "Fire", new[] { "Near miss (SIMULATED)", "Hit (SIMULATED)", "Hostile bot (real)", "None (you shoot)" }));
            p.Params.Add(ParamDef.Choice("when", "When", new[] { "Standing", "Kneeling", "Captor collecting the gear", "Prisoner dropping his gear", "Being let go", "Prisoner going for his gun" }));
            p.Params.Add(ParamDef.Number("squad", "Captor squad size", 1f, 2f, 1f, 1f));
            p.Params.Add(Kit("k", "Prisoner keeps", 0)); p.Params.Add(NerveP("n", "Prisoner nerve")); p.Params.Add(Pers("pe", "Prisoner personality"));
            p.Params.Add(Delay(8f, "Fire after (s, Standing)"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Participant cap = Squad(sc, "BEAR", sc.Values.I("squad"), -h, 0f, "Prisoner");
                From(Add(sc, "Prisoner", "Scav", "B", h, 0f, cap.Tag), sc.Values, null, null, "k", "n", "pe");
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find(sc.Values.I("squad") > 1 ? "PMC-1" : "PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                string when = sc.Values.S("when"), fire = sc.Values.S("fire");
                bool atCaptor = sc.Values.Is("at", "The captor");
                OrderKind[] steps =
                    when == "Kneeling" ? new[] { OrderKind.Kneel } :
                    when == "Captor collecting the gear" || when == "Prisoner dropping his gear" ? new[] { OrderKind.Stay, OrderKind.DropWeapons } :
                    new[] { OrderKind.Stay };
                sc.Step($"{cap.Tag}: plan for {when}", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    Judgement.Force(cap.Player, JudgeForce.Mercy);
                    if (when == "Prisoner dropping his gear") Compliance.Force(pr.Player, ComplianceChoice.DropsAll);
                    sc.Force($"{cap.Tag}'s answers forced to mercy (the fire is the test)");
                    return Captors.DebugSteps(cap.Player, $"fire test: {when}", steps);
                });
                // Once the hold-up is over (being let go, going for his gun) fire at the captor is not
                // about him any more: the prisoner takes it.
                Action<Participant> shootAt = t =>
                {
                    if (fire.StartsWith("Near miss")) ScenarioEvents.Run(sc, ScenarioEvent.FireNearMiss, t);
                    else if (fire.StartsWith("Hit")) ScenarioEvents.Run(sc, ScenarioEvent.FireHit, t);
                    else if (fire.StartsWith("Hostile")) ScenarioEvents.Run(sc, ScenarioEvent.ThirdParty, t);
                    else sc.Note(TL.Info, $"now: shoot near {t.Tag} yourself (the real detection)");
                };
                Participant target = atCaptor ? cap : pr;
                Func<SurrenderState> st = () => ScenarioOps.StateOf(pr);
                switch (when)
                {
                    case "Kneeling":
                        sc.OnceWhen(() => st()?.Kneel == KneelPhase.Down, 60f, "fire while he kneels", () => shootAt(target));
                        break;
                    case "Captor collecting the gear":
                        sc.OnceWhen(() => CaptorLoot.Busy(cap.Player, out _), 90f, "fire while the captor collects the gear", () => shootAt(target));
                        break;
                    case "Prisoner dropping his gear":
                        sc.OnceWhen(() => st() is SurrenderState s0 && (s0.DropQueue.Count > 0 || s0.ReportAt > 0f), 60f, "fire while he drops his gear", () => shootAt(target));
                        break;
                    case "Being let go":
                        sc.After(sc.Values.F("delay"), "he is let go", () => ScenarioEvents.Run(sc, ScenarioEvent.CaptorLetsGo, pr));
                        sc.OnceWhen(() => st()?.Phase == Phase.Retreating, 30f, "fire while he gets away", () => shootAt(pr));
                        break;
                    case "Prisoner going for his gun":
                        sc.After(sc.Values.F("delay"), "he goes for his gun (FORCED)", () => ScenarioEvents.Run(sc, ScenarioEvent.ForceGrab, pr));
                        sc.OnceWhen(() => st()?.Phase == Phase.Recovering || st()?.Phase == Phase.PickingUp, 20f, "fire while he goes for his gun", () => shootAt(pr));
                        break;
                    default:
                        sc.After(sc.Values.F("delay"), "fire", () => shootAt(target));
                        break;
                }
            };

            // ---- 0.23.0: encounter endings, squads, kneeling turns, gear, pick-ups ----

            // A lone captor walks away (or lets him go): the ending is his own unless forced.
            p = new ScenarioPreset
            {
                Name = "Encounter ending: captor walks away",
                Tests = "Once a lone captor is done with his prisoner he lingers, decides once how it ends (Leave order or simply walking off), goes back to his own AI; a prisoner left behind notices it from what he sees (back turned, walking off, gun away, out of sight), takes it in, lowers his hands, gets up if kneeling and walks off (or gets his own gun if it is still there and safe). Nobody recaptures anybody.",
                Watch = "'done with ... lingers N s', 'how his hold on ... ends: ... -> walks off / tells him to go' (or FORCED), 'stops holding ... (walks away; ... is left standing / on his knees, hands up)', 'back to his own AI (walked away from ...)', '4 s after the hand-back: layer <SAIN / ORBIT / vanilla layer>, N m moved, moving'. Prisoner: 'notices <captor> walks off with his back to him ... takes it in for N s', 'left by ...: ... -> walk off / get his gun', 'back to his own AI (left by ...: walks off ...)', then 'after release +15 s / +30 s'. Never 'STILL NEAR THE CAPTURE SPOT', never 'flees to a spot'.",
                Prereq = "Forced start: surrendered, forced custody, mercy forced (the ending is the test). The plan's steps are forced (Stay or Kneel); the linger, the ending (unless forced), the prisoner's noticing and decision are real.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(25f)); p.Params.Add(Sep(8f));
            p.Params.Add(ParamDef.Choice("pose", "Prisoner", new[] { "Standing", "Kneeling", "Kneeling, turned away" }));
            p.Params.Add(ParamDef.Choice("end", "Ending", new[] { "His own decision", "Walks away (FORCED)", "Leave order (FORCED)" }));
            p.Params.Add(ParamDef.Choice("gun", "His dropped gun", new[] { "Left where it fell", "Removed (none for him)" }));
            p.Params.Add(NerveP("n", "Prisoner nerve")); p.Params.Add(Pers("pe", "Prisoner personality")); p.Params.Add(Pers("cpe", "Captor personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Participant cap = Squad(sc, "BEAR", 1, -h, 0f, "Prisoner");
                From(cap, sc.Values, null, null, null, null, "cpe");
                From(Add(sc, "Prisoner", "Scav", "B", h, 0f, "PMC"), sc.Values, null, null, null, "n", "pe");
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find("PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                string pose = sc.Values.S("pose"), end = sc.Values.S("end");
                sc.Step($"{cap.Tag}: plan and ending", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    Judgement.Force(cap.Player, JudgeForce.Mercy);
                    CaptorLoot.ForceWant(cap.Player, true);
                    sc.Force($"{cap.Tag}'s answers forced to mercy; he wants the dropped weapon (so the fetch comes before the ending)");
                    if (end.StartsWith("Walks")) { Encounter.ForceEnding(cap.Player, Encounter.Ending.WalkAway); sc.Force($"{cap.Tag} walks away at the end (FORCED ending; the linger and everything after are real)"); }
                    else if (end.StartsWith("Leave")) { Encounter.ForceEnding(cap.Player, Encounter.Ending.Release); sc.Force($"{cap.Tag} ends it with a Leave order (FORCED ending)"); }
                    return pose == "Standing" ? Captors.DebugSteps(cap.Player, "ending test", OrderKind.Stay)
                         : pose == "Kneeling" ? Captors.DebugSteps(cap.Player, "ending test", OrderKind.Kneel)
                         : Captors.DebugSteps(cap.Player, "ending test", OrderKind.Kneel, OrderKind.TurnAround);
                });
                if (sc.Values.S("gun").StartsWith("Removed"))
                    sc.After(4f, "his dropped gun is removed", () => ScenarioEvents.Run(sc, ScenarioEvent.RemoveGun, pr));
            };
            list.Add(p.Done());

            // A squad moves on and leaves one guard behind.
            p = new ScenarioPreset
            {
                Name = "Encounter ending: squad leaves a guard",
                Tests = "A squad's captor is done; with a squadmate covering, the squad may move on and leave that squadmate to watch the prisoner (custody handed over with the leader's decision to let him go: no new lethal roll). The prisoner, still covered by the guard, stays put; when the guard ends it too, he notices and leaves.",
                Watch = "'how his hold on ... ends: ...; <guard> covers him: leave him to watch? ... -> yes' (or FORCED GuardBehind), 'hands ... over to <guard> (stays behind to watch him ...)', 'moves on and leaves <guard> to watch ...', '<guard>: holds ... (takes over from ...); plan watches him after ...', the leader's '4 s after the hand-back: layer ...'. Prisoner: 'his captor isn't on him, but <guard> still covers him: stays put' (if the old captor is still his reference), then the guard's own 'how his hold ... ends', 'notices <guard> walks off ...', 'left by <guard>: ...'.",
                Prereq = "Forced start: surrendered, forced custody of PMC-1, mercy forced. The guard is the mod's own pick (Squad cover on). Ending of PMC-1 forced to 'leave a guard' (falls back to walking away if nobody covers him at that moment); the guard's own ending is his.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f)); p.Params.Add(Sep(8f, 4f, 12f));
            p.Params.Add(ParamDef.Number("squad", "Captor squad size", 2f, 3f, 1f, 3f));
            p.Params.Add(ParamDef.Choice("end", "Leader's ending", new[] { "Leave a guard (FORCED)", "His own decision" }));
            p.Params.Add(Pers("pe", "Prisoner personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                int n = sc.Values.I("squad");
                Add(sc, "PMC-1", "BEAR", "A", -h, 0f, "Prisoner");
                Add(sc, "PMC-2", "BEAR", "A", -h - 1f, 3f, "Prisoner");
                if (n >= 3) Add(sc, "PMC-3", "BEAR", "A", -h - 2f, -3.5f, "Prisoner");
                From(Add(sc, "Prisoner", "Scav", "B", h, 0f, "PMC-1"), sc.Values, null, null, null, null, "pe");
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find("PMC-1"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                sc.Step($"{cap.Tag}: plan and ending", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    foreach (Participant q in sc.People) if (q.Tag.StartsWith("PMC") && q.Player != null) Judgement.Force(q.Player, JudgeForce.Mercy);
                    sc.Force("every captor's answers forced to mercy (the ending is the test)");
                    if (sc.Values.S("end").StartsWith("Leave")) { Encounter.ForceEnding(cap.Player, Encounter.Ending.GuardBehind); sc.Force($"{cap.Tag} leaves his covering squadmate to watch the prisoner (FORCED ending)"); }
                    return Captors.DebugSteps(cap.Player, "squad ending test", OrderKind.Stay, OrderKind.DropWeapons);
                });
            };
            list.Add(p.Done());

            // Squad dialogue: one voice per encounter.
            p = new ScenarioPreset
            {
                Name = "Squad dialogue",
                Tests = "A squad of three takes a prisoner: only the captor gives orders; the covering squadmate speaks only for his role; one line at a time with a short gap (the prisoner included); an urgent warning (a hostile bot) cuts in at once.",
                Watch = "'says Demand / Stay / Disarm ...' from the captor only; '<guard>: says Covering ...' once; Verbose: 'captor voice <line>: waits (<who> is speaking)' and '<line>: not his to say (<captor> speaks for the encounter)'. No two 'says' lines of the encounter within ~1 s of each other, except UnderFire / Threat / Resist. With the hostile bot: 'says UnderFire' or 'says Threat' at once.",
                Prereq = "Forced start: surrendered, forced custody, plan forced (Stay, Drop your weapons, Hands on head). Speech on, Log voice on (Verbose for the waits). Hostile bot: real (spawned 40 m out).",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(25f)); p.Params.Add(Sep(8f));
            p.Params.Add(ParamDef.Choice("threat", "Urgent warning", new[] { "None", "Hostile bot after 15 s (real)" }));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Squad(sc, "BEAR", 3, -h, 0f, "Prisoner");
                Add(sc, "Prisoner", "Scav", "B", h, 0f, "PMC-1");
                return null;
            };
            p.Setup = sc =>
            {
                Participant cap = sc.Find("PMC-1"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                sc.Step($"{cap.Tag}: plan", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    Judgement.Force(cap.Player, JudgeForce.Mercy);
                    return Captors.DebugSteps(cap.Player, "dialogue test", OrderKind.Stay, OrderKind.DropWeapons, OrderKind.HandsOnHead);
                });
                if (sc.Values.S("threat").StartsWith("Hostile")) sc.After(15f, "a hostile bot comes for the captor", () => ScenarioEvents.Run(sc, ScenarioEvent.ThirdParty, cap));
            };
            list.Add(p.Done());

            // Deliberate versus accidental prisoner targeting by a squadmate.
            p = new ScenarioPreset
            {
                Name = "Squadmate and the prisoner",
                Tests = "A squadmate far from the capture (outside the old notice radius) walks up while his squad holds a man: he must hold fire (told by his squad, no sight needed). With the forced deliberate decision he may kill him, and the log says it was a decision, not a leak.",
                Watch = "'Restraint: <far squadmate> holds fire at ... (squad): <captor>'s prisoner' at custody (he was 40 m away: 0.22 gave him no decision). Never 'PRISONER HIT WITHOUT A DECISION' or 'RESTRAINT LEAK'. Deliberate: '<squadmate>: DECIDES TO KILL ... against his squad (FORCED ...)', then 'Restraint: ... shot ... as decided (refused: ...)' and 'killed with his hands up ...'.",
                Prereq = "Forced start: surrendered, forced custody of PMC-1 (plan: Stay). PMC-3 starts Distance behind and is sent towards the prisoner by his own squad AI (not forced). Deliberate: the decision only is forced at 12 s; his own AI targets and fires.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f)); p.Params.Add(Sep(8f));
            p.Params.Add(ParamDef.Number("far", "Far squadmate behind (m)", 20f, 80f, 5f, 45f));
            p.Params.Add(ParamDef.Choice("kill", "Squadmate decision", new[] { "His squad's (hold fire)", "Deliberate kill (FORCED decision)" }));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Add(sc, "PMC-1", "BEAR", "A", -h, 0f, "Prisoner");
                Add(sc, "PMC-2", "BEAR", "A", -h - 1f, 3f, "Prisoner");
                Add(sc, "PMC-3", "BEAR", "A", -h - sc.Values.F("far"), 0f, "Prisoner");
                Add(sc, "Prisoner", "Scav", "B", h, 0f, "PMC-1");
                return null;
            };
            p.Setup = sc =>
            {
                Participant cap = sc.Find("PMC-1"), far = sc.Find("PMC-3"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                sc.Step($"{cap.Tag}: plan", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    Judgement.Force(cap.Player, JudgeForce.Mercy);
                    return Captors.DebugSteps(cap.Player, "squad targeting test", OrderKind.Stay);
                });
                if (sc.Values.S("kill").StartsWith("Deliberate"))
                    sc.After(12f, "the far squadmate decides to kill the prisoner (FORCED decision only)", () =>
                    {
                        SurrenderState s = ScenarioOps.StateOf(pr);
                        string r = s == null ? "no prisoner" : far.Owner2 == null ? "no squadmate" : Restraint.ForceSquadKill(s, far.Owner2);
                        sc.Note(r == null ? TL.Forced : TL.ExecFail, r ?? $"{far.Tag} decides to kill {pr.Tag} (FORCED; his own AI does the rest)");
                    });
            };
            list.Add(p.Done());

            // Kneeling turns.
            p = new ScenarioPreset
            {
                Name = "Kneeling turns",
                Tests = "A kneeling prisoner told to turn (repeatedly, from either side, on whatever ground you place him on) gets up, turns with the game's stepping turn, and kneels again only once his body has stopped turning; his root is held on his knees while he kneels: no foot left behind, no twisted ankle, no slide.",
                Watch = "'turn around from his knees: gets up, turns N deg', 'stepped round N deg ... the rest by a pivot', 'turned round in N s', 'kneel: waits (still turning / his body is still turning ...)' (Verbose), 'kneels again facing away', 'kneel: on his knees'. Never 'kneel: moved while down' right after a re-kneel; 'kneel: his body turned N deg off the way his knees point ...; held on his knees' means something turned him and was stopped (should be rare and small). Look at his feet: they turn with him.",
                Prereq = "Forced start: surrendered to a bot captor (custody), kneeling. The turn orders are given on a timer from the captor's real order path; the captor walks round him between them (Side change) so the turn direction alternates. Place him on stairs, a slope, rubble (crosshair placement) for uneven ground.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(10f));
            p.Params.Add(ParamDef.Number("turns", "Turn orders", 1f, 5f, 1f, 3f));
            p.Params.Add(ParamDef.Number("gap", "Between orders (s)", 3f, 20f, 1f, 9f));
            p.Params.Add(ParamDef.Choice("cut", "Interrupt", new[] { "None", "Get down again mid-rise", "Stand up mid-turn" }));
            Common(p, true);
            p.Plan = sc =>
            {
                Squad(sc, "BEAR", 1, -3.5f, 0f, "Prisoner");
                Add(sc, "Prisoner", "Scav", "B", 3.5f, 0f, "PMC");
                return null;
            };
            p.Setup = sc =>
            {
                Participant cap = sc.Find("PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                sc.Step($"{cap.Tag}: holds him for the test", () => cap.Active ? Captors.DebugSteps(cap.Player, "kneeling turn test (the scenario gives the orders)", OrderKind.Kneel) : (cap.Dead ? "died" : ""));
                int n = sc.Values.I("turns");
                float gap = sc.Values.F("gap"), at = 8f;
                string cut = sc.Values.S("cut");
                for (int i = 0; i < n; i++)
                {
                    sc.After(at, $"turn order {i + 1}", () => ScenarioEvents.Run(sc, ScenarioEvent.CaptorOrderTurn, pr));
                    if (i == 0 && cut != "None")
                        sc.After(at + (cut.StartsWith("Get") ? 0.8f : 2.2f), $"interrupt: {cut}", () =>
                        {
                            SurrenderState s = ScenarioOps.StateOf(pr);
                            if (s == null || cap.Player == null) return;
                            OrderKind k = cut.StartsWith("Get") ? OrderKind.Kneel : OrderKind.Stand;
                            sc.Note(TL.Real, $"{k} order mid-transition: {(Orders.Give(s, k, cap.Player, false, out string why, debug: true) ? "given" : $"refused ({why})")}");
                        });
                    at += gap;
                }
            };
            list.Add(p.Done());

            // Equipment demands.
            p = new ScenarioPreset
            {
                Name = "Captor demands gear",
                Tests = "A captor without body armour, a rig or a backpack weighs his prisoner's (the game must be able to put it in his empty slot; class and durability; carrying space), tells him to drop a suitable one, picks it up into that slot (contents kept), weighs again, and never asks twice or for something unsuitable.",
                Watch = "'gear he lacks, from ...: body armour: none of his own; the man wears a <item> (class N, N % durability): want ..., roll ... -> tells him to drop it' (or '... won't do (<why>)'), 'ordered DropItem', 'order 'drop your body armour': ... did it', 'weighs what else ... dropped ... <item>: (the armour he lacks and told him to drop ...)', 'stands to pick up the <item> N m from it', 'took ...'s <item> into his ArmorVest slot', then the next slot weighed, or 'done with ...'. Never the same slot asked twice.",
                Prereq = "Forced start: surrendered, forced custody, mercy forced. The captor's missing gear is removed at spawn (real transaction). The prisoner keeps his generated kit (relaunch or change the seed if he wears nothing suitable).",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(25f)); p.Params.Add(Sep(7f));
            p.Params.Add(ParamDef.Choice("lack", "Captor lacks", ScenarioOps.CaptorGearPresets, 4));
            p.Params.Add(Pers("cpe", "Captor personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Participant cap = Squad(sc, "BEAR", 1, -h, 0f, "Prisoner");
                From(cap, sc.Values, null, null, null, null, "cpe");
                cap.Kit = sc.Values.S("lack");
                Add(sc, "Prisoner", "BEAR", "B", h, 0f, "PMC");
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find("PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                sc.Step($"{cap.Tag}: plan", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    Judgement.Force(cap.Player, JudgeForce.Mercy);
                    Compliance.Force(pr.Player, ComplianceChoice.DropsAll);
                    sc.Force($"{cap.Tag}'s answers forced to mercy; {pr.Tag} drops every weapon he is told to");
                    return Captors.DebugSteps(cap.Player, "gear demand test", OrderKind.Stay, OrderKind.DropWeapons);
                });
            };
            list.Add(p.Done());

            // Loot at the prisoner's feet in a confined space.
            p = new ScenarioPreset
            {
                Name = "Pick-up at the prisoner's feet",
                Tests = "The gun lands at the prisoner's feet; the captor picks a spot beside it (in reach, clear of the prisoner's body and the walls), walks there smoothly without stopping and restarting, picks it up with the game's own pick-up, and never walks into the prisoner. In a corridor or a small room with no clean spot he asks the man to step back once, or leaves it for after the release.",
                Watch = "'goes to take ...', 'stands to pick up the <item> N m from it, N m from <prisoner>', one walk (no repeated 'goes to take' for the same item, no 'stops going for ...: something else needs his attention' without a real threat), 'took ... (pick-up animation ...)'. Confined: 'no clean spot by ...'s <item> (<why>); tells him to step back off it', '<prisoner>: stepped back off his gear', then the fetch; or '... takes it after letting him go'. Prisoner moved into the spot: 'picks another spot by ...'. Watch the captor: no jitter, no capsule pushing into the prisoner.",
                Prereq = "Forced start: surrendered (the gun in his hands drops at his feet), forced custody, plan forced (Stay), the captor wants the gun (FORCED appetite: always the case for the dropped weapon). Place it in a corridor, doorway or small room with crosshair placement and a small separation.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(8f)); p.Params.Add(Sep(4f, 2f, 10f));
            p.Params.Add(ParamDef.Choice("pose", "Prisoner", new[] { "Standing", "Kneeling" }));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Squad(sc, "BEAR", 1, -h, 0f, "Prisoner");
                Add(sc, "Prisoner", "Scav", "B", h, 0f, "PMC");
                return null;
            };
            p.Setup = sc =>
            {
                Participant cap = sc.Find("PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                sc.Step($"{cap.Tag}: plan", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    Judgement.Force(cap.Player, JudgeForce.Mercy);
                    CaptorLoot.ForceWant(cap.Player, true);
                    return Captors.DebugSteps(cap.Player, "pick-up test", sc.Values.Is("pose", "Kneeling") ? OrderKind.Kneel : OrderKind.Stay);
                });
            };
            list.Add(p.Done());

            // ---- 0.24.0: open hands, the dropped knife, release and escape ----

            // Open hands through repeated hands-up <-> hands-on-head switches.
            p = new ScenarioPreset
            {
                Name = "Open hands",
                Tests = "Hands up with open, relaxed fingers (not the game's gripping curl); hands on head with the fingers resting round the head; each switch, both ways, moves fingers, wrists, elbows and shoulders together from where they are, round the side of the head (never through the face or a helmet, never back to a grip), also when reversed half-way, kneeling, and after a turn. Fingers are the game's own again as soon as the arms come down (released at the end).",
                Watch = "His hands, zoomed in, through every switch: fingers open and slightly curved with hands up, curved round the head with hands on head, no grip shape at any moment, no snap at a reversal. Log: 'hands on his head' / 'hands up' per order; 'hands-up pose: wrists ...' once. At the end (your Leave): the arms come down and the fingers loosen into the game's own. Compare: F12 > 26. Release and escape > Open hands = 0 (the game's own fingers).",
                Prereq = "Forced surrender to you: your orders (the real order path, given on a timer) toggle Hands on head / Hands up; kneel and turn orders the same way. Gloves and kit as generated: relaunch (or change the seed / the side) for other gloves and helmets.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(6f));
            p.Params.Add(ParamDef.Choice("side", "Prisoner", new[] { "Scav", "PMC" }));
            p.Params.Add(ParamDef.Choice("pose", "Posture", new[] { "Standing", "Kneeling" }));
            p.Params.Add(ParamDef.Number("n", "Switches", 2f, 10f, 1f, 4f));
            p.Params.Add(ParamDef.Number("gap", "Between switches (s)", 1f, 10f, 0.5f, 3f));
            p.Params.Add(ParamDef.Choice("rev", "Interrupt", new[] { "None", "Reverse each switch half-way (0.3 s)" }));
            p.Params.Add(ParamDef.Choice("turn", "Turn", new[] { "No", "Turn around after the first switch" }));
            Common(p, true);
            p.Plan = sc =>
            {
                Add(sc, "Prisoner", sc.Values.Is("side", "PMC") ? "BEAR" : "Scav", "B", 0f, 0f, "you");
                return null;
            };
            p.Setup = sc =>
            {
                Participant pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, null, custody: false, "forced start");
                float gap = sc.Values.F("gap"), at = 3f;
                if (sc.Values.Is("pose", "Kneeling"))
                {
                    sc.After(at, "posture: kneel", () =>
                    {
                        SurrenderState s0 = ScenarioOps.StateOf(pr);
                        if (s0 == null) return;
                        sc.Note(TL.Real, $"kneel order: {(Orders.Give(s0, OrderKind.Kneel, sc.Observer, true, out string why, debug: true) ? "given" : $"refused ({why})")}");
                    });
                    at += 4f;
                }
                int n = sc.Values.I("n");
                bool rev = sc.Values.S("rev").StartsWith("Reverse");
                for (int i = 0; i < n; i++)
                {
                    int k = i + 1;
                    sc.After(at, $"switch {k}", () => ScenarioEvents.Run(sc, ScenarioEvent.OrderHands, pr));
                    if (rev) sc.After(at + 0.3f, $"switch {k} reversed half-way", () => ScenarioEvents.Run(sc, ScenarioEvent.OrderHands, pr));
                    at += gap;
                    if (i == 0 && sc.Values.S("turn").StartsWith("Turn")) { sc.After(at, "turn around", () => ScenarioEvents.Run(sc, ScenarioEvent.OrderTurn, pr)); at += gap + 1f; }
                }
                sc.After(at + 1f, "let him go (your Leave)", () => ScenarioEvents.Run(sc, ScenarioEvent.OrderLeave, pr));
            };
            list.Add(p.Done());

            // The knife from his scabbard, dropped on Drop your weapons: optional for the captor.
            p = new ScenarioPreset
            {
                Name = "Dropped knife: captor's choice",
                Tests = "The prisoner's only weapon left after the surrender is his knife; told to drop his weapons, he drops it. The captor weighs it as what it is (a melee item, not a gun: worth, whether he has one, room for it, the risk of bending down by the man), often leaves it, and leaving it is final: no second walk over, no wait for it, the encounter ends normally.",
                Watch = "'weighs what ... dropped': '<knife>: leave (melee weapon (Knife, KnifeComponent: not a firearm), N roubles, into his ...; he has his own <knife> | he has no knife of his own; want W (base ... x greed ... x use ... x value ...), roll R: leaves it)' or a 'take' with one fetch. Left: never 'goes to take ...'s <knife>', never 'later' or 'after release' for it, then 'done with ...' and the ending. Taken (FORCED appetite or a high roll): one walk, 'took ...'s <knife>'.",
                Prereq = "Forced start: surrendered, forced custody, mercy forced, the prisoner forced to drop everything he is told to. The prisoner keeps only his knife (kit 'Knife only'; his gun in hands drops at the surrender). The captor's own knife as generated or removed.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(25f)); p.Params.Add(Sep(7f));
            p.Params.Add(ParamDef.Choice("ck", "Captor's own knife", new[] { "As generated", "Removed" }));
            p.Params.Add(ParamDef.Choice("want", "Captor's appetite", new[] { "His own roll", "Always (FORCED appetite)" }));
            p.Params.Add(Pers("cpe", "Captor personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Participant cap = Squad(sc, "BEAR", 1, -h, 0f, "Prisoner");
                From(cap, sc.Values, null, null, null, null, "cpe");
                if (sc.Values.Is("ck", "Removed")) cap.Kit = "No knife";
                Participant pr = Add(sc, "Prisoner", "Scav", "B", h, 0f, "PMC");
                pr.Kit = "Knife only";
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find("PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                sc.Step($"{cap.Tag}: plan", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    Judgement.Force(cap.Player, JudgeForce.Mercy);
                    Compliance.Force(pr.Player, ComplianceChoice.DropsAll);
                    if (sc.Values.S("want").StartsWith("Always")) { CaptorLoot.ForceWant(cap.Player, true); sc.Force($"{cap.Tag} wants whatever is dropped (FORCED appetite)"); }
                    sc.Force($"{cap.Tag}'s answers forced to mercy; {pr.Tag} drops every weapon he is told to");
                    return Captors.DebugSteps(cap.Player, "dropped knife test", OrderKind.Stay, OrderKind.DropWeapons);
                });
            };
            list.Add(p.Done());

            // Release and escape: what he has, where he goes, and that he doesn't come back.
            p = new ScenarioPreset
            {
                Name = "Release and escape",
                Tests = "A lone captor walks away (or lets him go). The prisoner gets clear: a route and spot judged against where his captor is and where he SEES him walking (not alongside him, not past him), leg by leg until he is well away and out of sight; then a usable gun (loaded, or ammo for it on him) if he has one, else his own AI. A grenade never counts as a weapon; an empty pistol doesn't either. After the hand-back his AI knows he is unarmed (no stale 'has bullets'): no walking back at his captor, no grenade throws from afar; if his AI still drifts him back to the capture spot, one keep-away leg (at most 3). Followed for 2.5 minutes.",
                Watch = "Prisoner: 'left by ...: ... -> walk off', 'walks off to a spot N m away ... (N spots tried: ..., N across his path, N his way (penalised) ...)', maybe '<captor> is N m from where he is going and heading the same way: another way', 'retreat ends (..., clear)', 'away from his captor ..., what he has: firearm: ...; melee: ...; throwables: N (not counted as armed)', then 'draws the sidearm he kept (sidearm <pistol>: N round(s) in it)' (loaded) or 'not going for his <gun>: ...' and 'back to his own AI (away, nothing to fight with ...)'; 'remembers for 150 s ...'; once: ''has bullets' reads false for his AI'. 'after release +15/+30/+60/+90/+120 s: ... (furthest N m) ... layer ..., target ...': never CIRCLED BACK; if 'keeps away, leg N (drifting back: ... layer L, target T ...)' appears, L and T say what pulled him back. Grenades only: never 'RENEWED HOSTILITY: ... takes a grenade in his hands'.",
                Prereq = "Forced start: surrendered, forced custody, mercy forced, the plan forced to Stay (no disarm order: what he keeps is the kit's). Ending forced or his own. 'Keeps empty sidearm': the kit 'Keeps sidearm' with every round removed (real transactions; the game may refill a bot's magazines, the equipment line says what he had). 'Grenades only' needs a generated grenade (the start condition says if there is none: relaunch or change the seed).",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f)); p.Params.Add(Sep(8f));
            p.Params.Add(ParamDef.Choice("side", "Prisoner", new[] { "Scav", "PMC" }));
            p.Params.Add(ParamDef.Choice("kit", "He keeps", new[] { "Nothing (his gun lies where it fell)", "Nothing (his gun removed)", "Loaded sidearm", "Empty sidearm, no ammo", "Grenades only" }));
            p.Params.Add(ParamDef.Choice("end", "Ending", new[] { "Walks away (FORCED)", "Leave order (FORCED)", "His own decision" }));
            p.Params.Add(NerveP("n", "Prisoner nerve")); p.Params.Add(Pers("pe", "Prisoner personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Squad(sc, "BEAR", 1, -h, 0f, "Prisoner");
                Participant pr = Add(sc, "Prisoner", sc.Values.Is("side", "PMC") ? "BEAR" : "Scav", "B", h, 0f, "PMC");
                From(pr, sc.Values, null, null, null, "n", "pe");
                string k = sc.Values.S("kit");
                pr.Kit = k.StartsWith("Loaded") || k.StartsWith("Empty") ? "Keeps sidearm" : k.StartsWith("Grenades") ? "Grenades only" : "Disarmed";
                if (k.StartsWith("Empty")) pr.Ammo = "Empty gun";
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find("PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                string end = sc.Values.S("end");
                sc.Step($"{cap.Tag}: plan and ending", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    Judgement.Force(cap.Player, JudgeForce.Mercy);
                    sc.Force($"{cap.Tag}'s answers forced to mercy");
                    if (end.StartsWith("Walks")) { Encounter.ForceEnding(cap.Player, Encounter.Ending.WalkAway); sc.Force($"{cap.Tag} walks away at the end (FORCED ending)"); }
                    else if (end.StartsWith("Leave")) { Encounter.ForceEnding(cap.Player, Encounter.Ending.Release); sc.Force($"{cap.Tag} ends it with a Leave order (FORCED ending)"); }
                    return Captors.DebugSteps(cap.Player, "release test", OrderKind.Stay);
                }, 15f);
                if (sc.Values.S("kit").Contains("removed"))
                    sc.After(4f, "his dropped gun is removed", () => ScenarioEvents.Run(sc, ScenarioEvent.RemoveGun, pr));
            };
            list.Add(p.Done());

            // The ex-captor turns on him after letting him go: self-defence.
            p = new ScenarioPreset
            {
                Name = "Captor attacks after release",
                Tests = "After the captor walks away, he decides (FORCED, logged) to attack the man he let go: while the man is still walking off under our control, or a few seconds after the hand-back. The man reacts at once to the fire (a near miss is enough): with a usable gun on him he may fight back (draw, then his own AI), without one he gets to cover / keeps getting away; no busy-hands lock, no restriction of ours on his aim or trigger.",
                Watch = "'RENEWED HOSTILITY: <captor> DECIDES TO ATTACK <prisoner> (FORCED ...)'. Still ours: '<prisoner> under fire while Retreating (near miss | hit ...): fight back (sidearm ...) W; cover W -> ...', then 'Retreating -> Rearmed (fights back under fire with his sidearm)' and 'back to normal AI', or 'cover N m away, hidden from the fire'. After the hand-back: '... keep-away leg ... ends (under fire: his own AI defends him)' if one was running; ReleaseWatch 'layer SAIN : Combat Layer, target <captor>' with a gun, or a cover/retreat layer without. Never 'hands busy' loops.",
                Prereq = "Forced start: surrendered, forced custody, mercy forced, plan Stay, ending forced to walking away. The attack is a FORCED decision only (his hold-fire dropped, the man made his vanilla target if he has an EnemyInfo on him); aiming, firing and the man's reaction are real.",
                Natural = false
            };
            p.Params.Add(Place()); p.Params.Add(Dist(30f)); p.Params.Add(Sep(8f));
            p.Params.Add(ParamDef.Choice("kit", "He keeps", new[] { "Loaded sidearm", "Nothing" }));
            p.Params.Add(ParamDef.Choice("when", "Attack", new[] { "While he walks off (still ours)", "5 s after the hand-back" }));
            p.Params.Add(NerveP("n", "Prisoner nerve")); p.Params.Add(Pers("pe", "Prisoner personality"));
            Common(p, true);
            p.Plan = sc =>
            {
                float h = sc.Values.F("sep") / 2f;
                Squad(sc, "BEAR", 1, -h, 0f, "Prisoner");
                Participant pr = Add(sc, "Prisoner", "Scav", "B", h, 0f, "PMC");
                From(pr, sc.Values, null, null, null, "n", "pe");
                pr.Kit = sc.Values.S("kit").StartsWith("Loaded") ? "Keeps sidearm" : "Disarmed";
                return null;
            };
            p.Setup = sc =>
            {
                Conditions(sc);
                Participant cap = sc.Find("PMC"), pr = sc.Find("Prisoner");
                PrisonerTo(sc, pr, cap, custody: true, "forced start");
                sc.Step($"{cap.Tag}: plan and ending", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    Judgement.Force(cap.Player, JudgeForce.Mercy);
                    Encounter.ForceEnding(cap.Player, Encounter.Ending.WalkAway);
                    sc.Force($"{cap.Tag}'s answers forced to mercy; he walks away at the end (FORCED ending)");
                    return Captors.DebugSteps(cap.Player, "attack after release test", OrderKind.Stay);
                }, 15f);
                bool early = sc.Values.S("when").StartsWith("While");
                Action attack = () =>
                {
                    string r = Restraint.ForceAttack(cap.Player, pr.Player, "FORCED by the scenario");
                    if (r != null) sc.Note(TL.ExecFail, $"{cap.Tag} can't attack: {r}"); else sc.Force($"{cap.Tag} decides to attack {pr.Tag} (FORCED decision; his aim, fire and {pr.Tag}'s reaction are real)");
                };
                if (early)
                    sc.OnceWhen(() => ScenarioOps.StateOf(pr)?.Phase == Phase.Retreating, 180f, "he is walking off", () => sc.After(2f, "the captor attacks", attack));
                else
                {
                    bool seenHeld = false;
                    sc.OnceWhen(() =>
                    {
                        if (pr.Player == null || pr.Dead) return false;
                        if (SurrenderRegistry.HoldsBot(pr.Player)) { seenHeld = true; return false; }
                        return seenHeld;
                    }, 240f, "he is back to his own AI", () => sc.After(5f, "the captor attacks", attack));
                }
            };
            list.Add(p.Done());

            return list;
        }

        // 0.16.0: the prisoner's answer to Drop your weapons, forced (or left to him).
        private static void ForceAnswer(Scenario sc, Participant pr, string v)
        {
            if (v.StartsWith("Drops")) { Compliance.Force(pr.Player, ComplianceChoice.DropsAll); sc.Force($"{pr.Tag} drops everything he is told to"); }
            else if (v.StartsWith("Keeps")) { Compliance.Force(pr.Player, ComplianceChoice.KeepsOne); sc.Force($"{pr.Tag} keeps one weapon back at the first demand (hands it over if told again)"); }
        }

        internal static Player FindPitFollower()
        {
            List<Player> all = Comfort.Common.Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (all == null) return null;
            foreach (Player pl in all)
                if (pl != null && pl.IsAI && Teammates.IsPitFollower(pl.AIData?.BotOwner)) return pl;
            return null;
        }
    }
}
