using System;
using System.Collections.Generic;
using System.Text;
using EFT;
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

            return list;
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
