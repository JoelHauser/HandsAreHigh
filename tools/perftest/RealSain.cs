using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

namespace Mercy
{
    // `dotnet run -c Release -- sain <SPT root>`: the same members SainAdapter and SainTargetFilter
    // resolve, from the installed SAIN.dll, through FastGetter. Real types are loaded (the game's
    // Managed folder resolves their references) and each reader is run on an uninitialised
    // instance where the member is a plain field or auto-property (no Unity call), compared with
    // reflection. Members whose getter calls into other objects are only resolved and compiled.
    internal static class RealSain
    {
        internal static int Run(string spt, Action<bool, string> check)
        {
            string managed = Path.Combine(spt, "EscapeFromTarkov_Data", "Managed");
            string plugins = Path.Combine(spt, "BepInEx", "plugins");
            AppDomain.CurrentDomain.AssemblyResolve += (s, a) =>
            {
                string n = new AssemblyName(a.Name).Name + ".dll";
                foreach (string dir in new[] { managed, Path.Combine(spt, "BepInEx", "core"), plugins, Path.Combine(plugins, "SAIN") })
                {
                    string p = Path.Combine(dir, n);
                    if (File.Exists(p)) return Assembly.LoadFrom(p);
                }
                return null;
            };
            Assembly sain = Assembly.LoadFrom(Path.Combine(plugins, "SAIN", "SAIN.dll"));
            const BindingFlags Inst = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // SainAdapter.Resolve
            Type external = sain.GetType("SAIN.Interop.SAINExternal");
            Type botOwner = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll")).GetType("EFT.BotOwner");
            MethodInfo getComp = external?.GetMethod("GetBotComponent", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null, new[] { botOwner }, null);
            check(getComp != null, "real SAIN: SAINExternal.GetBotComponent(BotOwner) found");
            Type comp = getComp?.ReturnType;
            PropertyInfo suppression = comp?.GetProperty("Suppression", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo suppressionState = suppression?.PropertyType.GetProperty("CurrentState", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo activeLayer = comp?.GetProperty("ActiveLayer", BindingFlags.Public | BindingFlags.Instance);
            var supRead = FastGetter<object>.For(suppression);
            var stateRead = FastGetter<int>.For(suppressionState);
            var layerRead = FastGetter<int>.For(activeLayer);
            check(supRead?.Compiled == true, "real SAIN: BotComponent.Suppression compiled reader");
            check(stateRead?.Compiled == true, $"real SAIN: SAINBotSuppressClass.CurrentState compiled as int ({suppressionState?.PropertyType.Name}, {(suppressionState != null ? Enum.GetUnderlyingType(suppressionState.PropertyType).Name : "?")})");
            check(layerRead?.Compiled == true, $"real SAIN: BotComponent.ActiveLayer compiled as int ({activeLayer?.PropertyType.Name}, can write {activeLayer?.CanWrite})");

            // CurrentState is an auto-property: run it for real on an uninitialised instance.
            if (stateRead != null && suppressionState != null)
            {
                object s = FormatterServices.GetUninitializedObject(suppressionState.DeclaringType);
                bool same = true;
                for (int i = 0; i < 10; i++) same &= stateRead.Get(s) == Convert.ToInt32(suppressionState.GetValue(s, null));
                check(same && stateRead.Verified, "real SAIN: CurrentState compiled read = reflection on a live (uninitialised) instance, verified");
            }

            // SainTargetFilter.TryPatch
            Type ctrl = sain.GetType("SAIN.SAINComponent.Classes.EnemyClasses.SAINEnemyController");
            Type enemy = sain.GetType("SAIN.SAINComponent.Classes.EnemyClasses.Enemy");
            var owner = FastGetter<object>.For(ctrl?.GetProperty("BotOwner", Inst));
            var visible = FastGetter<System.Collections.IList>.For(ctrl?.GetProperty("VisibleEnemies", Inst));
            var known = FastGetter<System.Collections.IList>.For(ctrl?.GetProperty("KnownEnemies", Inst));
            FieldInfo goalField = ctrl?.GetField("_goalEnemy", Inst);
            var goal = FastGetter<object>.For(goalField);
            var enemyId = FastGetter<string>.For(enemy?.GetProperty("EnemyProfileId", Inst));
            check(owner?.Compiled == true, "real SAIN: SAINEnemyController.BotOwner compiled reader");
            check(visible?.Compiled == true && known?.Compiled == true, "real SAIN: VisibleEnemies / KnownEnemies compiled as IList");
            check(goal?.Compiled == true, "real SAIN: private _goalEnemy compiled reader");
            check(enemyId?.Compiled == true, "real SAIN: Enemy.EnemyProfileId compiled reader");

            if (ctrl != null && goal != null && visible != null)
            {
                object c = FormatterServices.GetUninitializedObject(ctrl);
                bool ok = true;
                for (int i = 0; i < 10; i++)
                {
                    ok &= goal.Get(c) == goalField.GetValue(c);
                    ok &= visible.Get(c) == ctrl.GetProperty("VisibleEnemies", Inst).GetValue(c, null);
                }
                check(ok && goal.Verified && visible.Verified, "real SAIN: _goalEnemy and VisibleEnemies compiled reads = reflection on a live instance, verified");
            }
            if (enemy != null && enemyId != null)
            {
                object en = FormatterServices.GetUninitializedObject(enemy);
                bool ok = true;
                for (int i = 0; i < 10; i++) ok &= enemyId.Get(en) == (string)enemy.GetProperty("EnemyProfileId", Inst).GetValue(en, null);
                check(ok && enemyId.Verified, "real SAIN: EnemyProfileId compiled read = reflection on a live instance, verified");
            }
            return 0;
        }

    }
}
