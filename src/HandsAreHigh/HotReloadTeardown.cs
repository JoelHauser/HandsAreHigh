// Drop-in hot-reload cleanup for any BepInEx plugin. Copy this file into the mod (internal, so
// several mods can each carry their own copy without clashing) and set its namespace.
//
//   private readonly HotReloadTeardown _teardown = new HotReloadTeardown();
//
//   private void Awake()
//   {
//       var patch = new MyPatch(); patch.Enable(); _teardown.Add(patch.Disable);   // SPT ModulePatch
//       var harmony = new Harmony(GUID); harmony.PatchAll(); _teardown.Add(harmony.UnpatchSelf);
//       SomeEvent += OnSomething; _teardown.Add(() => SomeEvent -= OnSomething);
//       var go = _teardown.Own(new GameObject("MyModUI"));
//       var res = _teardown.OwnDisposable(new SomeResources());                            // any IDisposable
//   }
//
//   private void OnDestroy() => _teardown.Run(Logger);
//
// Anything not registered here survives a reload and keeps running the old code next to the new.
// Statics in the old assembly also stay alive; avoid static events and caches, or clear them here.

using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Object = UnityEngine.Object;

namespace HandsAreHigh
{
internal sealed class HotReloadTeardown
{
    private readonly List<Action> _undo = new List<Action>();

    public void Add(Action undo) => _undo.Add(undo);

    public T Own<T>(T obj) where T : Object
    {
        Add(() => { if (obj) Object.Destroy(obj); });
        return obj;
    }

    public T OwnDisposable<T>(T obj) where T : IDisposable
    {
        Add(() => obj?.Dispose());
        return obj;
    }

    // Undo runs in reverse order of setup; one failing step never skips the rest.
    public void Run(ManualLogSource log)
    {
        for (int i = _undo.Count - 1; i >= 0; i--)
        {
            try { _undo[i](); }
            catch (Exception e) { log?.LogError($"Hot-reload teardown step {i} failed: {e}"); }
        }
        _undo.Clear();
    }
}
}
