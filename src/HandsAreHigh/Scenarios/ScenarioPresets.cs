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
        internal bool ShieldDefault;
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
            p.ShieldDefault = shield;
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
                Prereq = "Forced default (surrender + custody given). Natural: they fight; it may end with no surrender (valid).",
                ShieldDefault = true
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
                Natural = false, ShieldDefault = true
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
                Tests = "The captor puts cover and combat first; the prisoner reassesses what he sees.",
                Watch = "'Guard -> Cover' or '-> Engage', 'takes over', the prisoner's opening line (gunfire heard / looks away), 'Engage -> Guard (quiet ...)'.",
                Prereq = "Forced custody. Threat: a real hostile bot (spawned 40 m out), a SIMULATED suppression event (tests decisions only), or you fire near the captor.",
                Natural = false, ShieldDefault = true
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
                Natural = false, ShieldDefault = true
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
                Natural = false, ShieldDefault = true
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
                Natural = false, ShieldDefault = true
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
                Natural = false, ShieldDefault = true
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
                Natural = false, ShieldDefault = true
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
                Natural = false, ShieldDefault = true
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
                Prereq = "Natural: pin him at range yourself (a DMR helps). Forced: he gives up to you at once at that range.",
                ShieldDefault = false
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
                Prereq = "Needs a living Pit Fireteam follower already in this raid (found through its pitAIBossPlayer boss). A spawned friendly PMC is NOT Pit Fireteam and is never used instead.",
                ShieldDefault = false
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
                Natural = false, ShieldDefault = false
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
                Natural = false, ShieldDefault = false
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
                Natural = false, ShieldDefault = true
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
                Natural = false, ShieldDefault = true
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
                Natural = false, ShieldDefault = true
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
                Natural = false, ShieldDefault = true
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
                Natural = false, ShieldDefault = true
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
                Watch = "'after 'drop your weapons'', 'demands it again', 'lethal? chance', 'done with ... kill instead of letting go? chance', 'DECIDES TO KILL' or 'lets him go', 'killed with his hands up by ...: as decided (...)' vs 'RESTRAINT LEAK'. Squadmates: their own 'holds fire' lines stand.",
                Prereq = "Forced custody and plan (Stay, Drop your weapons). The trigger is forced or SIMULATED as named; the outcome is the real roll unless forced.",
                Natural = false, ShieldDefault = true
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
                Natural = false, ShieldDefault = true, NeedSight = false, MaxStart = 40f
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
                Watch = "'DECIDES TO KILL', 'Restraint: ... refuses the surrender of ... decides to kill him', 'opens fire on ... s after deciding', 'KILL DECISION CARRIED OUT' (or 'NOT CARRIED OUT after 10 s: layer, target, hands, sight, restraint'), 'killed with his hands up by ...: as decided'. Squadmates keep 'holds fire'. Natural: '... runs from his custody: fire on him? chance ...'.",
                Prereq = "Forced custody. 'Kill now' forces ONLY the decision; everything after is the real game. 'He bolts' forces his run; the captor's answer is his own roll unless forced.",
                Natural = false, ShieldDefault = true
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

            // Retreat from captivity
            p = new ScenarioPreset
            {
                Name = "Retreat from captivity",
                Tests = "A man leaving captivity commits to it: a destination outside the captivity area along a route that doesn't pass his captor, legs through cover when there's no way straight out, no looping back to the gun he dropped there.",
                Watch = "'flees to a spot ... m from where he was held', 'still inside ... keeps going (leg n)', 'another way', 'retreat ends (...): ... m from where he was held, ... covered in ... s', 'lies where he was held: not going back for it'. Place it in the open, inside a building, and in a dead end.",
                Prereq = "Forced custody; the captor's answers forced to mercy so the flight itself is what is tested. Let go: his real Leave order. Bolts: a forced run.",
                Natural = false, ShieldDefault = true
            };
            p.Params.Add(Place()); p.Params.Add(Dist(25f)); p.Params.Add(Sep(8f));
            p.Params.Add(ParamDef.Choice("how", "He leaves", new[] { "Let go", "Bolts (FORCED run)" }));
            p.Params.Add(ParamDef.Choice("pose", "Prisoner", new[] { "Standing", "Kneeling" }));
            p.Params.Add(Delay(8f, "Leaves after (s)"));
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
                sc.Step($"{cap.Tag}: holds him, mercy forced", () =>
                {
                    if (!cap.Active || !pr.Active) return (cap.Dead || pr.Dead) ? "a participant died" : "";
                    Judgement.Force(cap.Player, JudgeForce.Mercy);
                    sc.Force($"{cap.Tag}'s answers forced to mercy (the flight is the test)");
                    return Captors.DebugSteps(cap.Player, "retreat test", OrderKind.Stay, sc.Values.Is("pose", "Kneeling") ? OrderKind.Kneel : OrderKind.Stay);
                });
                sc.After(sc.Values.F("delay"), "he leaves", () => ScenarioEvents.Run(sc, sc.Values.Is("how", "Let go") ? ScenarioEvent.CaptorLetsGo : ScenarioEvent.ForceRun, pr));
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
