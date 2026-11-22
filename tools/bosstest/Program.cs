using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using EFT;

// 0.29.0 offline checks (see bosstest.csproj). Calls the mod's own methods by reflection:
//   NaturalSurrender.ClassOfRole / EligibleRole / LoneBossGate / LoneBossStress / LoneBossChanceFor,
//   BossSquad.IsSquadBoss, and BossSquad's confirmed-death set. Settings are bound on a temp cfg
//   (defaults), as tools/cfgbind does. Players are absent (null), which is exactly what a
//   despawned guard looks like to the mod (a destroyed Player compares equal to null).
// Usage: bosstest [path to Mercy.dll] [SPT path]
internal static class Program
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static int _fail, _pass;
    private static Assembly _mod;

    private static int Main(string[] args)
    {
        string dll = args.Length > 0 ? args[0] : Path.GetFullPath(@"..\..\src\Mercy\bin\Debug\Mercy.dll");
        string spt = args.Length > 1 ? args[1] : @"H:\SPT4.1.X";
        string[] dirs = { Path.Combine(spt, @"BepInEx\core"), Path.Combine(spt, @"EscapeFromTarkov_Data\Managed"), Path.Combine(spt, @"BepInEx\plugins"), Path.GetDirectoryName(dll) };
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string n = new AssemblyName(e.Name).Name;
            foreach (string d in dirs) { string p = Path.Combine(d, n + ".dll"); if (File.Exists(p)) return Assembly.LoadFrom(p); }
            return null;
        };
        _mod = Assembly.LoadFrom(dll);
        Console.WriteLine($"mod: {dll} ({_mod.GetName().Version})");

        BindSettings();
        Roles();
        BossGate();
        Refusals();
        Console.WriteLine($"\n{_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------------------------------
    private static Type T(string name) => _mod.GetType("Mercy." + name, true);
    private static object Call(string type, string method, params object[] a) => T(type).GetMethod(method, Any).Invoke(null, a);
    private static object Setting(string name) => T("Settings").GetField(name, Any).GetValue(null);
    private static void Set<V>(string name, V v) => ((ConfigEntry<V>)Setting(name)).Value = v;

    private static void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {what}");
    }

    private static void BindSettings()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "hah-bosstest-" + Guid.NewGuid().ToString("N") + ".cfg");
        var cfg = new ConfigFile(tmp, true);
        T("MercyPlugin").GetField("Log", Any).SetValue(null, new BepInEx.Logging.ManualLogSource("test"));
        T("Settings").GetMethod("Bind", Any).Invoke(null, new object[] { cfg });
        Console.WriteLine($"settings bound on {tmp}: Boss last survivor can surrender = {((ConfigEntry<bool>)Setting("LoneBossSurrender")).Value}, chance = {((ConfigEntry<float>)Setting("LoneBossChance")).Value}");
    }

    private static string Class(WildSpawnType r) => Call("NaturalSurrender", "ClassOfRole", r).ToString();
    private static bool SquadBoss(WildSpawnType r) => (bool)Call("BossSquad", "IsSquadBoss", r);
    private static bool EligibleRole(string rc) =>
        (bool)Call("NaturalSurrender", "EligibleRole", Enum.Parse(T("NaturalSurrender+RoleClass"), rc));

    // ---------------------------------------------------------------------------------------
    private static void Roles()
    {
        Console.WriteLine("\n[roles] every WildSpawnType through the mod's classification (the game's real table):");
        foreach (WildSpawnType r in Enum.GetValues(typeof(WildSpawnType)))
            Console.WriteLine($"    {r,-26} {Class(r),-7}{(SquadBoss(r) ? " squad boss (last-survivor rule)" : "")}");

        Check(r_IsBossOrFollower(WildSpawnType.pmcBot), "game marks pmcBot (Raider) as boss-or-follower (the old trap)");
        Check(Class(WildSpawnType.pmcBot) == "Raider", "pmcBot classifies as Raider, not Boss");
        Check(!SquadBoss(WildSpawnType.pmcBot), "Raider is never under the last-survivor rule");
        Set("IncludeBosses", false);
        Check(EligibleRole("Raider"), "Raider eligible with Bosses can surrender OFF");
        Check(!EligibleRole("Boss"), "Boss class not eligible with Bosses can surrender off");
        foreach (var b in new[] { WildSpawnType.bossBully, WildSpawnType.bossGluhar, WildSpawnType.bossKojaniy, WildSpawnType.bossSanitar,
                                  WildSpawnType.bossKolontay, WildSpawnType.bossBoar, WildSpawnType.bossKnight, WildSpawnType.bossZryachiy,
                                  WildSpawnType.bossKilla, WildSpawnType.bossTagilla, WildSpawnType.bossPartisan })
            Check(Class(b) == "Boss" && SquadBoss(b), $"{b}: Boss class, squad boss");
        foreach (var f in new[] { WildSpawnType.followerBully, WildSpawnType.followerGluharAssault, WildSpawnType.followerGluharSecurity,
                                  WildSpawnType.followerGluharScout, WildSpawnType.followerGluharSnipe, WildSpawnType.followerSanitar,
                                  WildSpawnType.followerKojaniy, WildSpawnType.followerBigPipe, WildSpawnType.followerBirdEye,
                                  WildSpawnType.followerZryachiy, WildSpawnType.followerTagilla, WildSpawnType.followerKolontayAssault,
                                  WildSpawnType.followerKolontaySecurity, WildSpawnType.followerBoar, WildSpawnType.bossBoarSniper, WildSpawnType.exUsec })
            Check(Class(f) == "Boss" && !SquadBoss(f), $"{f}: Boss class (unchanged follower rules), not a squad boss");
        Check(Class(WildSpawnType.assault) == "Scav" && Class(WildSpawnType.pmcUSEC) == "Pmc", "Scav / PMC unchanged");
        Set("IncludeBosses", true);
    }

    private static bool r_IsBossOrFollower(WildSpawnType r) => r.IsBossOrFollower();

    // ---------------------------------------------------------------------------------------
    private static object NewHistory(params string[] guards)
    {
        object h = Activator.CreateInstance(T("BotHistory"), true);
        Type ht = h.GetType();
        ht.GetField("SquadBoss", Any).SetValue(h, true);
        if (guards.Length > 0)
        {
            ht.GetField("GuardIds", Any).SetValue(h, new List<string>(guards));
            var players = new List<Player>();
            foreach (string _ in guards) players.Add(null);   // not in the world (despawned / unknown)
            ht.GetField("Guards", Any).SetValue(h, players);
        }
        return h;
    }

    private static void Kill(string id) => ((HashSet<string>)T("BossSquad").GetField("Dead", Any).GetValue(null)).Add(id);
    private static void ResetDeaths() => Call("BossSquad", "Clear");

    private static bool Gate(object h, float stress, out string why)
    {
        object[] a = { h, stress, null };
        bool ok = (bool)T("NaturalSurrender").GetMethod("LoneBossGate", Any).Invoke(null, a);
        why = (string)a[2];
        return ok;
    }

    private static float Stress(float injury, float fire, float ammo) => (float)Call("NaturalSurrender", "LoneBossStress", injury, fire, ammo);

    private static float Chance(float stress, float nerve, float mult)
    {
        object[] a = { stress, nerve, mult, null };
        return (float)T("NaturalSurrender").GetMethod("LoneBossChanceFor", Any).Invoke(null, a);
    }

    private static void BossGate()
    {
        Console.WriteLine("\n[boss gate] Reshala with 4 guards:");
        ResetDeaths();
        object h = NewHistory("g1", "g2", "g3", "g4");
        float bad = Stress(0.6f, 0.5f, 0f);
        Check(!Gate(h, bad, out string w) && w.Contains("4 of 4"), $"all 4 guards alive -> refuses to consider it ({w})");
        Kill("g1"); Kill("g2"); Kill("g3");
        Check(!Gate(h, bad, out w) && w.Contains("1 of 4"), $"3 dead, 1 alive/asleep/far -> still no ({w})");
        Check(!Gate(h, 1f, out w), "...even at full stress");
        Console.WriteLine("  (g4 is a null Player and not in the death set: the same state as despawned or missing)");
        Kill("g4");
        Check(Gate(h, bad, out w), $"final guard's confirmed death -> eligible ({w})");

        Console.WriteLine("\n[boss gate] other cases:");
        ResetDeaths();
        object alone = NewHistory();
        Check(!Gate(alone, 1f, out w) && w.Contains("alone"), $"Killa / Tagilla spawned alone -> never ({w})");
        object h2 = NewHistory("a", "b");
        Kill("a");
        Check(!Gate(h2, 1f, out w), $"one guard despawned (left the group, no death event) -> never ({w})");
        Kill("b");
        Set("LoneBossSurrender", false);
        Check(!Gate(h2, 1f, out w) && w.Contains("off"), $"toggle off -> never ({w})");
        Set("LoneBossSurrender", true);
        Check(!Gate(h2, 0.1f, out w), $"last survivor but unhurt, not under fire, ammo left -> no roll ({w})");
        Check(Stress(0f, 0f, 0f) == 0f && Stress(1f, 0f, 0f) == 1f, "stress 0 when fine, 1 when dying");
    }

    // ---------------------------------------------------------------------------------------
    private static void Refusals()
    {
        Console.WriteLine("\n[refusals] limited, escalating rolls:");
        ResetDeaths();
        object h = NewHistory("g");
        Kill("g");
        Type ht = h.GetType();
        FieldInfo rolls = ht.GetField("LoneBossRolls", Any), last = ht.GetField("LoneBossStress", Any);
        void Refuse(float s) { rolls.SetValue(h, (int)rolls.GetValue(h) + 1); last.SetValue(h, s); }   // what Evaluate does on a failed roll

        Check(Gate(h, 0.5f, out string w), $"first roll at stress 0.50 ({w})");
        Refuse(0.5f);
        Check(!Gate(h, 0.5f, out w), $"refused; same state 3 s later -> no new roll ({w})");
        Check(!Gate(h, 0.6f, out w), "slightly worse (0.60) -> no new roll");
        Check(Gate(h, 0.66f, out w), $"clearly worse (0.66) -> second roll ({w})");
        Refuse(0.66f);
        Check(Gate(h, 0.85f, out w), "worse again (0.85) -> third roll");
        Refuse(0.85f);
        Check(!Gate(h, 1f, out w) && w.Contains("refused 3"), $"after 3 refusals -> never again this raid ({w})");

        // 0.29.2: a first roll at high stress must not lock him out (Lighthouse: 0.86 -> needed 1.01).
        object hh = NewHistory("g9"); Kill("g9");
        FieldInfo r2 = hh.GetType().GetField("LoneBossRolls", Any), l2 = hh.GetType().GetField("LoneBossStress", Any);
        r2.SetValue(hh, 1); l2.SetValue(hh, 0.86f);
        Check(!Gate(hh, 0.95f, out w), $"refused at 0.86; 0.95 -> no roll ({w})");
        Check(Gate(hh, 1f, out w), $"refused at 0.86; full stress 1.00 -> second roll ({w})");
        l2.SetValue(hh, 1f);
        Check(!Gate(hh, 1f, out w), "refused at full stress; still full -> no roll");

        Console.WriteLine("\n[odds] chance per roll and the most he can ever reach in a raid (3 rolls):");
        float nerveMid = 0.5f;
        float scavTypical = 0.35f * 0.6f * (1f - 0.85f * nerveMid);   // Scav base x mid curve x nerve, per check every ~3 s
        foreach (var c in new[] { (0.4f, 0.5f, 0.75f), (0.66f, 0.5f, 0.75f), (1f, 0.5f, 0.75f), (1f, 0f, 1.5f) })
        {
            float p = Chance(c.Item1, c.Item2, c.Item3);
            Console.WriteLine($"    stress {c.Item1:0.00}, nerve {c.Item2:0.0}, personality x experience {c.Item3:0.00}: {p:0.000} per roll");
        }
        float w1 = Chance(0.5f, nerveMid, 0.75f), w2 = Chance(0.66f, nerveMid, 0.75f), w3 = Chance(0.85f, nerveMid, 0.75f);
        float cumTypical = 1f - (1f - w1) * (1f - w2) * (1f - w3);
        float cap = (float)((ConfigEntry<float>)Setting("LoneBossChance")).Value;
        float cumWorst = 1f - (float)Math.Pow(1f - cap, 3);
        Console.WriteLine($"    typical escalating fight (0.50 -> 0.66 -> 0.85, mid nerve): {cumTypical:P1} over the whole raid");
        Console.WriteLine($"    absolute ceiling (3 rolls at the cap {cap:0.00}): {cumWorst:P1}; a Scav in a long bad fight rolls ~{scavTypical:0.00} every 3 s");
        Check(Chance(1f, 0f, 10f) <= cap + 1e-6f, "chance never above the setting");
        Check(cumWorst < 0.7f, "eventual surrender is never close to certain");
        Check(cumTypical < 0.5f, $"a typical lone boss more likely fights on than gives up ({cumTypical:P0} over the raid)");
        Check(Chance(1f, 1f, 1f) < Chance(1f, 0f, 1f), "nerve lowers the chance");
    }
}
