using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT.InputSystem;
using EFT.Settings;
using HarmonyLib;
using UnityEngine;

namespace HandsAreHigh
{
    // Keeps the game's hotbar from acting on keys the order menu uses.
    //
    // How the game routes keys (Assembly-CSharp, verified against C:\HUH 2026-11-19):
    // InputManager.Update turns the bound keys into a List<ECommand> (InputPreset.UpdateInput)
    // and InputManager.DispatchInput hands that one list to InputTree.TranslateInput, which
    // walks the input nodes (UI screens first, GamePlayerOwner last). Each node may remove a
    // command from the shared list. The hotbar lives at the end of that walk:
    // PlayerOwner.TranslateCommand -> PlayerInputTranslator.TranslateCommand ->
    // SetSlotItem(index) for SelectKnife / SelectSecondaryWeapon / SelectFirstPrimaryWeapon /
    // SelectSecondPrimaryWeapon / QuickSelectSecondaryWeapon / SelectFastSlot4..0; the
    // quick-use radial (QuickUseSelectorUtil) reacts to PressSlot4..0. Depending on the
    // player's press type (ControlPressTypeOverride) a slot fires on press OR on release.
    //
    // So the filter is a prefix on InputManager.DispatchInput (one call per delivery; input
    // is delivered from Update or FixedUpdate depending on the game's update queue): the
    // commands are taken out of the list before any node sees them. Nothing about the
    // player's bindings is read back or changed, and nothing else is touched:
    //   - While the menu owns input (open, bound to a valid bot, no game UI over it: the
    //     cursor is hidden), every hotbar command and the mouse-wheel scroll commands are
    //     removed, whatever key produced them. Ownership is re-claimed every frame and lapses
    //     on its own after two frames, so a menu that stops updating (exception, disable, hot
    //     reload) can't leave input blocked.
    //   - Every hotbar-bound key pressed while the menu owns input is latched: after the menu
    //     closes, the hotbar commands bound to THAT key stay removed until the key has been
    //     released for a moment (two frames and two fixed steps, so a release-type slot or a
    //     delivery in a later FixedUpdate can't leak). A fresh press then works as before.
    //     Other keys and other commands are never filtered after the menu closes.
    internal static class InputGuard
    {
        // The hotbar, as EGameKey -> ECommand (InputManager.Init's combinations).
        private static readonly KeyValuePair<EGameKey, ECommand>[] HotbarKeys =
        {
            new KeyValuePair<EGameKey, ECommand>(EGameKey.Knife, ECommand.SelectKnife),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.SecondaryWeapon, ECommand.SelectSecondaryWeapon),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.QuickSecondaryWeapon, ECommand.QuickSelectSecondaryWeapon),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.PrimaryWeaponFirst, ECommand.SelectFirstPrimaryWeapon),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.PrimaryWeaponSecond, ECommand.SelectSecondPrimaryWeapon),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.Slot4, ECommand.SelectFastSlot4),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.Slot5, ECommand.SelectFastSlot5),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.Slot6, ECommand.SelectFastSlot6),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.Slot7, ECommand.SelectFastSlot7),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.Slot8, ECommand.SelectFastSlot8),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.Slot9, ECommand.SelectFastSlot9),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.Slot0, ECommand.SelectFastSlot0),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.PressSlot4, ECommand.PressSlot4),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.PressSlot5, ECommand.PressSlot5),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.PressSlot6, ECommand.PressSlot6),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.PressSlot7, ECommand.PressSlot7),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.PressSlot8, ECommand.PressSlot8),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.PressSlot9, ECommand.PressSlot9),
            new KeyValuePair<EGameKey, ECommand>(EGameKey.PressSlot0, ECommand.PressSlot0),
        };

        // Indexed by (int)ECommand.
        private static readonly int CommandCount = MaxCommand() + 1;
        private static readonly bool[] Hotbar = new bool[CommandCount];
        private static readonly bool[] WhileOpen = new bool[CommandCount];   // hotbar + wheel
        private static readonly bool[] Latched = new bool[CommandCount];     // commands of latched keys

        // Key -> the hotbar commands bound to it (read from the game's settings at menu open).
        private struct Binding { internal KeyCode Key; internal ECommand Command; }
        private static readonly List<Binding> Bindings = new List<Binding>(64);
        private static readonly List<KeyCode> BoundKeys = new List<KeyCode>(32);
        private static bool _bindingsRead;

        // Our own number keys: latched with the whole hotbar set if the bindings can't be read.
        private static readonly KeyCode[] Fallback =
        {
            KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
            KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9,
            KeyCode.Keypad0, KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4,
            KeyCode.Keypad5, KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9,
        };

        private sealed class Latch
        {
            internal KeyCode Key;
            internal float UpSince = -1f;   // realtime the key was first seen up
            internal int UpFrame;
            internal float Since;
        }
        private static readonly List<Latch> Latches = new List<Latch>(8);
        private static readonly Stack<Latch> Pool = new Stack<Latch>(8);

        private static int _claimFrame = -100;
        private static int _blocked;           // commands removed, for the log

        // One bool read per delivery when nothing is going on.
        internal static bool Engaged;

        internal static bool Owning => Time.frameCount - _claimFrame <= 2;
        internal static int LatchCount => Latches.Count;

        static InputGuard()
        {
            foreach (KeyValuePair<EGameKey, ECommand> kv in HotbarKeys)
            {
                Hotbar[(int)kv.Value] = true;
                WhileOpen[(int)kv.Value] = true;
            }
            WhileOpen[(int)ECommand.ScrollNext] = true;       // mouse wheel moves the highlight
            WhileOpen[(int)ECommand.ScrollPrevious] = true;
        }

        private static int MaxCommand()
        {
            int max = 0;
            foreach (ECommand c in (ECommand[])Enum.GetValues(typeof(ECommand)))
                if ((int)c > max) max = (int)c;
            return max;
        }

        // The game's current bindings for the hotbar keys. Read when the menu opens (the
        // player can rebind between raids); allocation only on that read.
        internal static void ReadBindings()
        {
            Bindings.Clear();
            BoundKeys.Clear();
            _bindingsRead = false;
            try
            {
                KeyGroup[] groups = Singleton<SettingsManager>.Instance?.Control?.Settings?.GetAllKeyBindings();
                if (groups == null) return;
                foreach (KeyGroup g in groups)
                {
                    if (g?.variants == null) continue;
                    ECommand command;
                    if (!HotbarCommand(g.keyName, out command)) continue;
                    foreach (InputSource v in g.variants)
                    {
                        if (v == null || v.isAxis || v.keyCode == null) continue;
                        foreach (KeyCode k in v.keyCode)
                        {
                            if (IsModifier(k) || k == KeyCode.None) continue;
                            Bindings.Add(new Binding { Key = k, Command = command });
                            if (!BoundKeys.Contains(k)) BoundKeys.Add(k);
                        }
                    }
                }
                _bindingsRead = true;
            }
            catch (Exception e)
            {
                HandsAreHighPlugin.Report("read key bindings", e);
            }
        }

        private static bool HotbarCommand(EGameKey key, out ECommand command)
        {
            foreach (KeyValuePair<EGameKey, ECommand> kv in HotbarKeys)
                if (kv.Key == key) { command = kv.Value; return true; }
            command = default;
            return false;
        }

        private static bool IsModifier(KeyCode k) =>
            k == KeyCode.LeftShift || k == KeyCode.RightShift || k == KeyCode.LeftControl || k == KeyCode.RightControl ||
            k == KeyCode.LeftAlt || k == KeyCode.RightAlt || k == KeyCode.LeftCommand || k == KeyCode.RightCommand;

        // Called every frame the menu owns input. Also latches every hotbar-bound key that
        // went down this frame: its press was eaten, so its release (and any hold) must be too.
        internal static void Claim()
        {
            _claimFrame = Time.frameCount;
            Engaged = true;
            List<KeyCode> keys = _bindingsRead ? BoundKeys : null;
            if (keys != null)
            {
                for (int i = 0; i < keys.Count; i++)
                    if (Input.GetKeyDown(keys[i])) LatchKey(keys[i]);
            }
            else
            {
                for (int i = 0; i < Fallback.Length; i++)
                    if (Input.GetKeyDown(Fallback[i])) LatchKey(Fallback[i]);
            }
        }

        // The menu closed (order given, cancelled, target lost): ownership ends now; latched
        // keys keep their own commands blocked until released.
        internal static void Release()
        {
            if (_claimFrame < 0) return;
            _claimFrame = -100;
            Engaged = Latches.Count > 0;
            if (_blocked > 0 && Settings.VerboseLogs.Value)
                HandsAreHighPlugin.Log.LogInfo($"Order menu: {_blocked} hotbar/wheel command(s) kept from the game while it was open");
            _blocked = 0;
        }

        // New raid, disable, teardown: nothing blocked, nothing latched.
        internal static void Reset()
        {
            _claimFrame = -100;
            _blocked = 0;
            for (int i = 0; i < Latches.Count; i++) Pool.Push(Latches[i]);
            Latches.Clear();
            Array.Clear(Latched, 0, Latched.Length);
            Engaged = false;
        }

        private static void LatchKey(KeyCode key)
        {
            for (int i = 0; i < Latches.Count; i++)
                if (Latches[i].Key == key) { Latches[i].UpSince = -1f; return; }
            Latch l = Pool.Count > 0 ? Pool.Pop() : new Latch();
            l.Key = key;
            l.UpSince = -1f;
            l.Since = Time.realtimeSinceStartup;
            Latches.Add(l);
            RebuildLatched();
        }

        private static void RebuildLatched()
        {
            Array.Clear(Latched, 0, Latched.Length);
            for (int i = 0; i < Latches.Count; i++)
            {
                KeyCode key = Latches[i].Key;
                if (!_bindingsRead)
                {
                    Array.Copy(Hotbar, Latched, Hotbar.Length);
                    return;
                }
                for (int b = 0; b < Bindings.Count; b++)
                    if (Bindings[b].Key == key) Latched[(int)Bindings[b].Command] = true;
            }
        }

        // A latch ends once its key has been up for two frames and long enough for two fixed
        // steps (input delivered from FixedUpdate may arrive frames after the release). A
        // latch never outlives 30 s (a key that reads held forever, focus lost).
        private static void ExpireLatches()
        {
            if (Latches.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            float tail = Mathf.Max(0.06f, 2f * Time.fixedDeltaTime);
            bool changed = false;
            for (int i = Latches.Count - 1; i >= 0; i--)
            {
                Latch l = Latches[i];
                bool held = Input.GetKey(l.Key);
                if (held) l.UpSince = -1f;
                else if (l.UpSince < 0f) { l.UpSince = now; l.UpFrame = Time.frameCount; }
                bool done = (!held && now - l.UpSince >= tail && Time.frameCount - l.UpFrame >= 2) || now - l.Since > 30f;
                if (!done) continue;
                Latches.RemoveAt(i);
                Pool.Push(l);
                changed = true;
            }
            if (changed) RebuildLatched();
            if (Latches.Count == 0 && !Owning) Engaged = false;
        }

        // Also from Runtime.Update, so latches expire even when no input is delivered.
        internal static void Tick()
        {
            if (!Engaged) return;
            ExpireLatches();
            if (!Owning && _claimFrame >= 0) Release();   // lapsed without a Release (menu stopped)
        }

        internal static void Filter(List<ECommand> commands)
        {
            ExpireLatches();
            bool owning = Owning;
            if (!owning && Latches.Count == 0) { Engaged = false; return; }
            for (int i = commands.Count - 1; i >= 0; i--)
            {
                int c = (int)commands[i];
                if (c < 0 || c >= CommandCount) continue;
                if ((owning && WhileOpen[c]) || Latched[c])
                {
                    commands.RemoveAt(i);
                    _blocked++;
                }
            }
        }
    }

    // InputManager.DispatchInput(List<ECommand>, float[]): one call per input delivery, before
    // InputTree passes the list to any node (see InputGuard).
    [HarmonyPatch(typeof(InputManager), nameof(InputManager.DispatchInput))]
    internal static class DispatchInputPatch
    {
        [HarmonyPrefix]
        private static void Prefix(List<ECommand> commandsList)
        {
            if (!InputGuard.Engaged || commandsList == null || commandsList.Count == 0) return;
            try { InputGuard.Filter(commandsList); }
            catch (Exception e) { HandsAreHighPlugin.Report("input guard", e); }
        }
    }
}
