using System.Collections.Generic;
using EFT;
using UnityEngine;

namespace Mercy
{
    // 0.16.0: HandsOnHead / HandsUp (where his hands are, apart from posture) and TurnAround. New kinds
    // go at the end: the menu's numbering of the older orders never changes.
    // 0.23.0 DropItem / StepBack: bot captors only (never in the menu): one named item he wears
    // (GearNeeds), and a step off the gear at his feet (CaptorLoot.StandPoint).
    internal enum OrderKind { DropWeapons, DropKit, ArmourOff, Follow, Kneel, Stay, Leave, Stand, HandsOnHead, HandsUp, TurnAround, DropItem, StepBack }

    internal enum NoteKind { Info, Success, Error }

    // The order menu for the held bot you're looking at.
    //
    // Targeting: the menu is bound to the one bot it was opened on. Aim moving to another bot
    // never redirects it; it closes instead (looked away, out of range, out of sight, dead,
    // no longer hands up, asleep). Range, sight, phase and the order's availability are
    // checked again when the order is given.
    //
    // Input: one path for every way of choosing (number keys, numpad, the confirm key after
    // wheel / next / previous): Submit, at most once a frame, closing the menu on success.
    // While it is open and no game screen is over it (the game shows the cursor for
    // inventory, pause, chat...), InputGuard keeps the hotbar commands away from the game.
    //
    // Drawing (Draw, from RuntimeGui.OnGUI): IMGUI, repaint events only, no GUILayout.
    // Strings, contents and rects are rebuilt only when something they show changes (open,
    // availability, screen size, scale); a frame of drawing allocates nothing.
    internal sealed class OrderMenu
    {
        // ---- notes (toasts) -----------------------------------------------------------

        private static string _note;
        private static NoteKind _noteKind;
        private static float _noteUntil;
        private static int _noteVersion;

        internal static bool HasNote => _note != null && Time.time < _noteUntil;

        internal static void Notify(string msg) => Notify(msg, NoteKind.Info);

        internal static void Notify(string msg, NoteKind kind)
        {
            _note = msg;
            _noteKind = kind;
            _noteUntil = Time.time + (kind == NoteKind.Error ? 3.5f : 3f);
            _noteVersion++;
        }

        internal static void ClearNotes()
        {
            _note = null;
            _noteUntil = 0f;
            _noteVersion++;
        }

        // ---- state --------------------------------------------------------------------

        private struct Option
        {
            internal OrderKind Kind;
            internal bool Available;
            internal bool Current;      // what he's already doing (follow / kneel)
            internal string Reason;     // why not, short
            internal int Digit;         // 0.17.0: its own number key, fixed (never renumbered)
        }

        private static readonly KeyCode[] Alpha =
        {
            KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4,
            KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9
        };
        private static readonly KeyCode[] Keypad =
        {
            KeyCode.Keypad0, KeyCode.Keypad1, KeyCode.Keypad2, KeyCode.Keypad3, KeyCode.Keypad4,
            KeyCode.Keypad5, KeyCode.Keypad6, KeyCode.Keypad7, KeyCode.Keypad8, KeyCode.Keypad9
        };

        private const float LookAwayAngle = 40f;   // degrees off the bot's chest: you looked away
        private const float RangeSlack = 0.75f;    // hysteresis over the order range while open
        private const float SightGrace = 0.35f;    // s of blocked sight before it closes

        private readonly Option[] _options = new Option[MaxOptions];
        internal const int MaxOptions = 10;
        private int _count;
        private SurrenderState _open;      // the bot the menu is bound to
        private SurrenderState _hover;     // closed: the bot you could open it on
        private int _index;
        private float _openedAt;           // realtime, for the fade-in
        private float _nextAvail;
        private float _nextSight;
        private float _sightLostAt = -1f;
        private float _nextHover;
        private int _submitFrame = -1;
        private int _openFrame = -1;       // 0.25.0: the key press that opened it never also submits
        private int _flashIndex = -1;
        private float _flashAt = -10f;
        private string _footerMsg;
        private NoteKind _footerKind;
        private float _footerUntil;
        private int _version;              // anything the view shows changed

        internal bool IsOpen => _open != null;

        // Raid end, disable, teardown: closed, nothing blocked.
        internal void Reset()
        {
            _open = null;
            _hover = null;
            InputGuard.Reset();
        }

        internal void Update(Player me)
        {
            // 0.31.3: Hide all Mercy UI means no order menu either: an invisible menu (and the hover claim
            // that keeps the interact key from the game) took Joel's F presses near prisoners with no
            // prompt on screen (0.31.2, 2026-11-22). Hidden: closed, F is the game's.
            if (!Settings.Enabled.Value || !Settings.OrdersEnabled.Value || Settings.HideUi.Value || me == null || !Perception.Alive(me))
            {
                Close(null, NoteKind.Info);
                _hover = null;
                return;
            }

            // A game screen over the raid (inventory, pause menu, chat, the F12 config) shows the
            // cursor; it has the keyboard then, not us.
            bool screenFree = !Cursor.visible;

            if (_open != null)
            {
                // First: any hotbar key pressed this frame is latched, even if the menu closes
                // below for another reason (looked away, out of range): it was pressed while
                // the menu owned the keys, so its press, hold and release stay ours.
                InputGuard.Claim();
                if (!screenFree) { Close(null, NoteKind.Info); return; }
                string invalid = Validate(_open, me, forceSight: false);
                if (invalid != null)
                {
                    Close(invalid, invalid.Length == 0 ? NoteKind.Info : NoteKind.Error);
                    return;
                }
                if (Time.time >= _nextAvail) RefreshAvailability();
                HandleInput(me);
                return;
            }

            if (!screenFree || !SurrenderRegistry.Any) { if (_hover != null) _version++; _hover = null; return; }
            // 0.25.0: the interact key (F) opens it too. Looked for before the hover refresh so a man
            // just aimed at is found on the press itself.
            bool interact = Settings.InteractOpensMenu.Value && InputGuard.InteractDown();
            bool menuKey = Pressed(Settings.MenuKey.Value) || interact;
            if (menuKey || Time.time >= _nextHover)
            {
                _nextHover = Time.time + 0.1f;
                SurrenderState h = LookedAt(me);
                if (h != _hover) { _hover = h; _version++; }
            }
            // While you look at a man it could open on, the interact key is the menu's (no door,
            // no loot, no pick-up on the same press); released as soon as you look away.
            if (_hover != null && Settings.InteractOpensMenu.Value) InputGuard.ClaimInteract();
            if (menuKey && _hover != null) Open(_hover);
        }

        // 0.25.0: debug keys on and "Order prisoners held by bots": the menu also opens on a
        // bot's prisoner (debug scenarios). Explicit and debug-only; the order takes him over
        // (Orders.Give ends the bot's custody first: one captor, one controller).
        private static bool DebugAuthority => Settings.DebugKeys.Value && Settings.DebugOrderBotPrisoners.Value;

        private void Open(SurrenderState s)
        {
            InputGuard.ReadBindings();
            BuildOptions();
            _open = s;
            _openFrame = Time.frameCount;
            _openedAt = Time.realtimeSinceStartup;
            if (s.Custody != null)
                MercyPlugin.Log.LogInfo($"Order menu (DEBUG): opened on {s.Name}, held by {Restraint.Name(s.Custody.Bot)}; the first order you give takes him over");
            _sightLostAt = -1f;
            _nextSight = 0f;
            _footerMsg = null;
            _flashIndex = -1;
            RefreshAvailability();
            _index = 0;
            for (int i = 0; i < _count; i++)
                if (_options[i].Available) { _index = i; break; }
            _version++;
            InputGuard.Claim();
        }

        // msg: null = quiet; "" = quiet too (looked away); otherwise a note of that kind.
        private void Close(string msg, NoteKind kind)
        {
            if (_open == null) return;
            _open = null;
            _version++;
            InputGuard.Release();
            if (!string.IsNullOrEmpty(msg)) Notify(msg, kind);
        }

        // 0.17.0: one row per order with a fixed number key, whatever is turned off in the config or
        // unavailable right now (shown greyed with the reason, never renumbered). Posture is one
        // slot (Get down / Stand up) and hands another (Hands on head / Hands up): each pair is one
        // order with two states, so the nine fit 1-9 and nothing is left without a key.
        //   1 Drop weapons  2 Drop kit  3 Armour off  4 Follow me  5 Get down / Stand up
        //   6 Hands on head / Hands up  7 Turn around  8 Stay  9 Let go
        private void BuildOptions()
        {
            _count = 0;
            Add(OrderKind.DropWeapons, 1);
            if (Settings.KitOrder.Value) Add(OrderKind.DropKit, 2);
            if (Settings.ArmourOrder.Value) Add(OrderKind.ArmourOff, 3);
            if (Settings.FollowOrder.Value) Add(OrderKind.Follow, 4);
            Add(OrderKind.Kneel, 5);
            if (Settings.HandsOnHeadOrder.Value) Add(OrderKind.HandsOnHead, 6);
            if (Settings.TurnOrder.Value) Add(OrderKind.TurnAround, 7);
            Add(OrderKind.Stay, 8);
            Add(OrderKind.Leave, 9);
        }

        private void Add(OrderKind k, int digit)
        {
            if (_count >= _options.Length) return;
            _options[_count++] = new Option { Kind = k, Available = true, Digit = digit };
        }

        // Cheap (slot lookups, no lists); on open, 4x a second while open, and at Submit.
        private void RefreshAvailability()
        {
            _nextAvail = Time.time + 0.25f;
            SurrenderState s = _open;
            if (s?.Bot == null) return;
            bool changed = false;
            for (int i = 0; i < _count; i++)
            {
                Option o = _options[i];
                bool available = true, current = false;
                string reason = null;
                bool dropping = s.DropQueue.Count > 0;
                switch (o.Kind)
                {
                    case OrderKind.DropWeapons:
                        if (dropping) reason = "Still dropping";
                        else if (!Arsenal.HasItems(s.Bot, Arsenal.WeaponSlots, false)) reason = "Nothing to drop";
                        break;
                    case OrderKind.DropKit:
                        if (dropping) reason = "Still dropping";
                        else if (!Arsenal.HasItems(s.Bot, Arsenal.KitSlots, false)) reason = "Nothing left";
                        break;
                    case OrderKind.ArmourOff:
                        if (dropping) reason = "Still dropping";
                        else if (!Arsenal.HasItems(s.Bot, Arsenal.ArmourOrderSlots, true)) reason = "No armour";
                        break;
                    case OrderKind.Follow:
                        if (s.Order == Order.Follow) { current = true; reason = "Following"; }
                        break;
                    case OrderKind.Kneel:
                        // Get down, or Stand up once he is down / going down (Resolve).
                        if (s.Order != Order.Kneel && Kneel.Active(s)) reason = "Getting up";
                        else if (s.KneelFallback != null) { current = true; reason = "Can't kneel here"; }
                        break;
                    case OrderKind.HandsOnHead:
                        if (dropping || s.DropPending || s.HandsPending) reason = "Hands busy";
                        break;
                    case OrderKind.TurnAround:
                        reason = Facing.Unavailable(s);
                        if (reason == null && s.TurnedAway) current = true;
                        break;
                }
                available = reason == null;
                if (o.Available == available && o.Current == current && o.Reason == reason) continue;
                o.Available = available;
                o.Current = current;
                o.Reason = reason;
                _options[i] = o;
                changed = true;
            }
            if (changed) _version++;
        }

        // Null while the bound bot is still a valid target; "" = close quietly; else why not.
        private string Validate(SurrenderState s, Player me, bool forceSight)
        {
            if (s.Dead || s.Bot == null || !Perception.Alive(s.Bot)) return $"{s.Name} is down";
            if (!SurrenderRegistry.TryGet(s.Bot, out SurrenderState now) || now != s) return $"{s.Name} isn't surrendered any more";
            if (s.Phase != Phase.HandsUp) return $"{s.Name} isn't holding his hands up";
            if (s.Suspended) return $"{s.Name} is out of reach";
            if (s.Custody != null && !DebugAuthority) return $"{s.Name} is {Restraint.Name(s.Custody.Bot)}'s prisoner";
            Vector3 eye = Perception.Eye(me);
            Vector3 chest = Perception.Chest(s.Bot);
            Vector3 to = chest - eye;
            if (to.magnitude > Settings.InteractionRange.Value + RangeSlack) return $"{s.Name} is out of range";
            if (Vector3.Angle(me.LookDirection, to) > LookAwayAngle) return "";

            if (forceSight || Time.time >= _nextSight)
            {
                _nextSight = Time.time + 0.1f;
                if (Perception.LineClear(eye, chest)) _sightLostAt = -1f;
                else if (forceSight) return $"Lost sight of {s.Name}";
                else if (_sightLostAt < 0f) _sightLostAt = Time.time;
            }
            if (_sightLostAt >= 0f && Time.time - _sightLostAt > SightGrace) return $"Lost sight of {s.Name}";
            return null;
        }

        private void HandleInput(Player me)
        {
            if (Settings.NumberKeysChoice.Value != NumberKeysMode.Off)
            {
                bool pad = Settings.NumberKeysChoice.Value == NumberKeysMode.Numpad;
                for (int d = 0; d < 10; d++)
                {
                    if (!Input.GetKeyDown(Alpha[d]) && !(pad && Input.GetKeyDown(Keypad[d]))) continue;
                    if (d == 0) { Close(null, NoteKind.Info); return; }
                    int at = -1;
                    for (int i = 0; i < _count; i++) if (_options[i].Digit == d) { at = i; break; }
                    if (at >= 0)
                    {
                        if (_index != at) { _index = at; _version++; }
                        Submit(at, me);
                    }
                    else Footer(NoDigit[d], NoteKind.Error);
                    return;
                }
            }
            if (Pressed(Settings.NextKey.Value)) Move(1);
            if (Pressed(Settings.PrevKey.Value)) Move(-1);
            if (Settings.WheelSelect.Value)
            {
                float wheel = Input.mouseScrollDelta.y;
                if (wheel < -0.01f) Move(1);
                else if (wheel > 0.01f) Move(-1);
            }
            // 0.25.0: the confirm key or the interact key, never on the frame the menu opened (the
            // opening press). A held key is one press (GetKeyDown), and the menu closes on Submit.
            bool confirm = Pressed(Settings.MenuKey.Value) || (Settings.InteractOpensMenu.Value && InputGuard.InteractDown());
            if (confirm && Time.frameCount != _openFrame) Submit(_index, me);
        }

        private static readonly string[] NoDigit =
        {
            "", "No order on 1", "No order on 2", "No order on 3", "No order on 4",
            "No order on 5", "No order on 6", "No order on 7", "No order on 8", "No order on 9"
        };

        // BepInEx's KeyboardShortcut.IsDown refuses any other held key, so the menu key did
        // nothing while you walked (W held). Here: the main key went down and its own
        // modifiers are held; other keys don't matter.
        private static bool Pressed(BepInEx.Configuration.KeyboardShortcut k)
        {
            if (k.MainKey == KeyCode.None || !Input.GetKeyDown(k.MainKey)) return false;
            foreach (KeyCode m in k.Modifiers)
                if (!Input.GetKey(m)) return false;
            return true;
        }

        private void Move(int step)
        {
            if (_count == 0) return;
            _index = (_index + step + _count) % _count;
            _version++;
        }

        private void Footer(string msg, NoteKind kind)
        {
            _footerMsg = msg;
            _footerKind = kind;
            _footerUntil = Time.realtimeSinceStartup + 1.6f;
            _version++;
        }

        // The one way an order is given: checked again now (target and availability), once a
        // frame at most; the menu closes before the order runs, so nothing can submit twice.
        private void Submit(int i, Player me)
        {
            if (_open == null || i < 0 || i >= _count) return;
            if (_submitFrame == Time.frameCount) return;
            _submitFrame = Time.frameCount;

            RefreshAvailability();
            Option o = _options[i];
            if (!o.Available)
            {
                _flashIndex = i;
                _flashAt = Time.realtimeSinceStartup;
                Footer($"{Label(Resolve(_open, o.Kind))}: {o.Reason}", NoteKind.Error);
                return;
            }
            SurrenderState s = _open;
            string invalid = Validate(s, me, forceSight: true);
            if (invalid != null)
            {
                Close(invalid.Length == 0 ? $"{s.Name} isn't in front of you" : invalid, NoteKind.Error);
                return;
            }
            Close(null, NoteKind.Info);
            Orders.Give(s, Resolve(s, o.Kind), me, human: true, out _);
        }

        // The held bot (hands up) you're looking at, close enough and in clear sight.
        private static SurrenderState LookedAt(Player me)
        {
            if (!SurrenderRegistry.Any) return null;
            Vector3 eye = Perception.Eye(me);
            float range = Settings.InteractionRange.Value;
            SurrenderState best = null;
            float bestAngle = 15f;
            foreach (SurrenderState s in SurrenderRegistry.All)
            {
                // A bot guarding him (custody) keeps him: no menu. Nobody guarding him (yours, a
                // spared man, an allied bot's, or his captor gone): you can take over by ordering.
                if (s.Bot == null || s.Dead || s.Phase != Phase.HandsUp || s.Suspended || (s.Custody != null && !DebugAuthority)) continue;
                Vector3 to = Perception.Chest(s.Bot) - eye;
                if (to.magnitude > range) continue;
                float angle = Vector3.Angle(me.LookDirection, to);
                if (angle >= bestAngle || !Perception.LineClear(eye, Perception.Chest(s.Bot))) continue;
                bestAngle = angle;
                best = s;
            }
            return best;
        }

        // The one slot for the hands: Hands on head, or Hands up while they are there.
        internal static OrderKind Resolve(SurrenderState s, OrderKind k)
        {
            if (s == null) return k;
            if (k == OrderKind.HandsOnHead && s.HandsOnHead) return OrderKind.HandsUp;
            if (k == OrderKind.Kneel && (s.Order == Order.Kneel || Kneel.Active(s))) return OrderKind.Stand;
            return k;
        }

        internal static string Label(OrderKind k)
        {
            switch (k)
            {
                case OrderKind.DropWeapons: return "Drop weapons";
                case OrderKind.DropKit: return "Drop kit";
                case OrderKind.ArmourOff: return "Armour off";
                case OrderKind.Follow: return "Follow me";
                case OrderKind.Kneel: return "Get down";
                case OrderKind.Stay: return "Stay";
                case OrderKind.Stand: return "Stand up";
                case OrderKind.HandsOnHead: return "Hands on head";
                case OrderKind.HandsUp: return "Hands up";
                case OrderKind.TurnAround: return "Turn around";
                default: return "Let go";
            }
        }

        // ---- view ---------------------------------------------------------------------

        private readonly MenuView _view = new MenuView();

        internal void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            _view.Draw(this);
        }

        // What the view reads (it never changes menu state).
        private sealed class MenuView
        {
            // Palette, after the game's own HUD: near-black panels, warm off-white text,
            // the pale khaki highlight of its interaction list, muted status colours.
            private static readonly Color PanelBg = new Color(0.035f, 0.037f, 0.035f, 0.86f);
            private static readonly Color HeaderBg = new Color(0.10f, 0.10f, 0.095f, 0.92f);
            private static readonly Color Border = new Color(1f, 1f, 0.95f, 0.10f);
            private static readonly Color Accent = new Color(0.773f, 0.765f, 0.698f, 1f);   // #C5C3B2
            private static readonly Color Text = new Color(0.86f, 0.85f, 0.80f, 1f);
            private static readonly Color TextDim = new Color(0.55f, 0.55f, 0.52f, 1f);
            private static readonly Color TextOff = new Color(0.40f, 0.40f, 0.38f, 1f);
            private static readonly Color TextDark = new Color(0.07f, 0.07f, 0.065f, 1f);
            private static readonly Color Ok = new Color(0.56f, 0.66f, 0.43f, 1f);
            private static readonly Color Bad = new Color(0.78f, 0.34f, 0.29f, 1f);
            private static readonly Color Info = new Color(0.773f, 0.765f, 0.698f, 1f);

            private static readonly GUIContent[] Digits =
            {
                new GUIContent("0"), new GUIContent("1"), new GUIContent("2"), new GUIContent("3"), new GUIContent("4"),
                new GUIContent("5"), new GUIContent("6"), new GUIContent("7"), new GUIContent("8"), new GUIContent("9")
            };

            private GUIStyle _title, _tag, _sub, _row, _badge, _reason, _footer, _toast, _hint;
            private bool _fontLooked;
            private Font _font, _fontBold;

            // Layout cache.
            private int _builtVersion = -1, _builtNote = -1;
            private int _screenW, _screenH;
            private float _scale;
            private float _k;
            private Rect _panel, _header, _rowsArea, _footerRect;
            private readonly Rect[] _rows = new Rect[MaxOptions];
            private readonly GUIContent[] _labels = new GUIContent[MaxOptions];
            private readonly GUIContent[] _reasons = new GUIContent[MaxOptions];
            private readonly GUIContent _titleC = new GUIContent(), _tagC = new GUIContent(), _subC = new GUIContent(),
                _footerC = new GUIContent(), _hintKeyC = new GUIContent(), _hintC = new GUIContent(), _noteC = new GUIContent();
            private Rect _hintRect, _hintKey, _noteRect;
            private float _alpha = 1f;

            internal void Draw(OrderMenu m)
            {
                bool hints = Settings.Hints.Value;
                bool showNote = hints && HasNote;
                if (m._open == null && (m._hover == null || !hints) && !showNote) return;

                EnsureStyles();
                if (_screenW != Screen.width || _screenH != Screen.height || !Mathf.Approximately(_scale, Settings.MenuScale.Value))
                {
                    _screenW = Screen.width; _screenH = Screen.height; _scale = Settings.MenuScale.Value;
                    _builtVersion = -1; _builtNote = -1;
                    ApplyFontSizes();
                }
                if (_builtVersion != m._version) Build(m);
                if (showNote && _builtNote != _noteVersion) BuildNote(m);

                Color saved = GUI.color;
                if (m._open != null) DrawMenu(m);
                else if (m._hover != null && hints) DrawHint();
                if (showNote) DrawNote(m);
                GUI.color = saved;
            }

            // ---- styles -----------------------------------------------------------------

            private void EnsureStyles()
            {
                if (_title != null) return;
                if (!_fontLooked)
                {
                    _fontLooked = true;
                    // The game's UI typeface (Bender), when a legacy Font copy of it is loaded;
                    // looked up once. Unity's default font otherwise.
                    try
                    {
                        foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
                        {
                            if (f == null || f.name == null || f.name.IndexOf("Bender", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                            bool bold = f.name.IndexOf("Bold", System.StringComparison.OrdinalIgnoreCase) >= 0;
                            if (bold) { if (_fontBold == null) _fontBold = f; }
                            else if (_font == null) _font = f;
                        }
                    }
                    catch (System.Exception e) { MercyPlugin.Report("menu font", e); }
                    if (_fontBold == null) _fontBold = _font;
                    if (Settings.VerboseLogs.Value)
                        MercyPlugin.Log.LogInfo($"Order menu font: {(_font != null ? _font.name : "Unity default")}");
                }
                GUIStyle Make(Font f, FontStyle fs, TextAnchor a, bool clip = true)
                {
                    var st = new GUIStyle(GUI.skin.label)
                    {
                        font = f, fontStyle = f == null ? fs : (fs == FontStyle.Bold && f == _fontBold && _fontBold != _font ? FontStyle.Normal : fs),
                        alignment = a, wordWrap = false, richText = false,
                        clipping = clip ? TextClipping.Clip : TextClipping.Overflow,
                        padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0)
                    };
                    st.normal.textColor = Color.white;   // tinted with GUI.color per draw
                    return st;
                }
                _title = Make(_fontBold, FontStyle.Bold, TextAnchor.MiddleLeft);
                _tag = Make(_fontBold, FontStyle.Bold, TextAnchor.MiddleRight);
                _sub = Make(_font, FontStyle.Normal, TextAnchor.MiddleLeft);
                _row = Make(_font, FontStyle.Normal, TextAnchor.MiddleLeft);
                _badge = Make(_fontBold, FontStyle.Bold, TextAnchor.MiddleCenter, clip: false);
                _reason = Make(_font, FontStyle.Normal, TextAnchor.MiddleRight);
                _footer = Make(_font, FontStyle.Normal, TextAnchor.MiddleLeft);
                _toast = Make(_font, FontStyle.Normal, TextAnchor.MiddleLeft);
                _hint = Make(_font, FontStyle.Normal, TextAnchor.MiddleLeft);
                ApplyFontSizes();
            }

            // Scale: screen height over 1080 (so 1440p and 4K look the same size relative to the
            // screen, ultrawide doesn't stretch it), times the player's Menu size.
            // Never below MinK: under that the text hits its pixel floor while the rows keep
            // shrinking (cramped at 720p).
            private const float MinK = 0.8f;

            private void ApplyFontSizes() =>
                SetSizes(Mathf.Max(MinK, Mathf.Clamp(Screen.height / 1080f, 0.5f, 4f) * Settings.MenuScale.Value));

            private void SetSizes(float k)
            {
                _k = k;
                if (_title == null) return;
                int Px(float v) => Mathf.Max(10, Mathf.RoundToInt(v * _k));
                _title.fontSize = Px(14.5f);
                _tag.fontSize = Px(11f);
                _sub.fontSize = Px(11f);
                _row.fontSize = Px(13.5f);
                _badge.fontSize = Px(11.5f);
                _reason.fontSize = Px(11f);
                _footer.fontSize = Px(10.5f);
                _toast.fontSize = Px(14f);
                _hint.fontSize = Px(14f);
            }

            // ---- layout (only when something shown changed) -------------------------------

            private void Build(OrderMenu m)
            {
                _builtVersion = m._version;
                _builtNote = -1;   // the toast sits under the panel or the prompt: re-anchor it
                float k = _k;
                float pad = Pad * k, rowH = RowH * k, badge = Badge * k, gap = 8f * k;

                SurrenderState s = m._open ?? m._hover;
                if (s != null)
                {
                    _titleC.text = s.Bot?.Profile?.Nickname ?? s.Name;
                    // 0.17.0: one header line: side and what he is doing now, short.
                    string state = s.Order == Order.Follow ? "following"
                        : Kneel.Active(s) ? "kneeling"
                        : s.TurnedAway || s.Turning ? "facing away"
                        : "standing";
                    _tagC.text = $"{SideTag(s.Bot)}  {state}{(s.HandsOnHead ? ", hands on head" : "")}" +
                                 (s.Custody != null ? $"  DEBUG: {Restraint.Name(s.Custody.Bot)}'s" : "");
                    _subC.text = "";
                }

                if (m._open == null)
                {
                    // The closed-menu prompt: [ / ]  Orders: Name
                    _hintKeyC.text = Settings.InteractOpensMenu.Value ? InputGuard.InteractLabel : KeyText(Settings.MenuKey.Value.MainKey);
                    _hintC.text = s != null ? $"Orders  {s.Name}" : "";
                    Vector2 kz = _badge.CalcSize(_hintKeyC);
                    Vector2 hz = _hint.CalcSize(_hintC);
                    float kw = Mathf.Max(badge + 4f * k, kz.x + 10f * k);
                    float h = 26f * k;
                    Vector2 at = Anchor(kw + gap + hz.x + 2f * pad, h);
                    _hintRect = new Rect(at.x, at.y, kw + gap + hz.x + 2f * pad, h);
                    _hintKey = new Rect(at.x + pad * 0.6f, at.y + (h - badge) * 0.5f, kw, badge);
                    return;
                }

                // Too wide to sit beside the bot (4:3, 5:4, a big Menu size): smaller, down to
                // the readable floor, before it would have to go under him.
                float w = 0f, hTotal = 0f;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    Measure(m, out w, out hTotal);
                    if (w <= Screen.width * 0.5f - 115f * _k - 16f || _k <= MinK + 0.001f) break;
                    SetSizes(Mathf.Max(MinK, _k * 0.85f));
                }

                k = _k;
                pad = Pad * k; rowH = RowH * k;
                float headerH = HeaderH * k;
                float footerH = FooterH * k;
                Vector2 p = Anchor(w, hTotal);
                _panel = new Rect(p.x, p.y, w, hTotal);
                _header = new Rect(p.x, p.y, w, headerH);
                _rowsArea = new Rect(p.x, p.y + headerH + 3f * k, w, m._count * rowH);
                for (int i = 0; i < m._count; i++)
                    _rows[i] = new Rect(p.x + 3f * k, _rowsArea.y + i * rowH, w - 6f * k, rowH - 1f * k);
                _footerRect = new Rect(p.x + pad, _rowsArea.yMax + 3f * k, w - 2f * pad, footerH);
            }

            // Panel size at the current scale: the widest row (label + reason), the header or
            // the key hint. A passing footer message is clipped; it never resizes the panel.
            private void Measure(OrderMenu m, out float w, out float h)
            {
                float k = _k;
                float pad = Pad * k, rowH = RowH * k, badge = Badge * k, gap = 8f * k;
                float labelW = 0f, reasonW = 0f;
                for (int i = 0; i < m._count; i++)
                {
                    Option o = m._options[i];
                    if (_labels[i] == null) _labels[i] = new GUIContent();
                    if (_reasons[i] == null) _reasons[i] = new GUIContent();
                    _labels[i].text = OrderMenu.Label(Resolve(m._open, o.Kind));
                    _reasons[i].text = o.Reason ?? "";
                    labelW = Mathf.Max(labelW, _row.CalcSize(_labels[i]).x);
                    if (o.Reason != null) reasonW = Mathf.Max(reasonW, _reason.CalcSize(_reasons[i]).x);
                }
                float headerW = _title.CalcSize(_titleC).x + gap + _tag.CalcSize(_tagC).x;
                _footerC.text = FooterHint(m._count);
                float footerW = _footer.CalcSize(_footerC).x;
                if (m._footerMsg != null && Time.realtimeSinceStartup < m._footerUntil) _footerC.text = m._footerMsg;
                w = Mathf.Max(190f * k, pad + badge + gap + labelW + (reasonW > 0f ? gap * 2f + reasonW : 0f) + pad,
                              headerW + 2f * pad, footerW + 2f * pad);
                w = Mathf.Min(w, Screen.width - 32f);
                h = HeaderH * k + 3f * k + m._count * rowH + 3f * k + FooterH * k;
            }

            // Right of the crosshair and a little above it, clear of the bot you're looking at
            // (at order range his raised arms span ~ +-100 px at 1080p); anchored on the screen
            // centre so ultrawide keeps it near your eyes. Then: against the right edge if that
            // still clears him, the same two on the left, and only then under his feet.
            private Vector2 Anchor(float w, float h)
            {
                float k = _k;
                float W = Screen.width;
                float cx = W * 0.5f, cy = Screen.height * 0.5f;
                float wide = 150f * k, near = 115f * k;
                float y = cy - 80f * k;
                float x;
                if (cx + wide + w <= W - 16f) x = cx + wide;
                else if (W - 16f - w >= cx + near) x = W - 16f - w;
                else if (cx - wide - w >= 16f) x = cx - wide - w;
                else if (16f + w <= cx - near) x = 16f;
                else { x = cx - w * 0.5f; y = cy + 210f * k; }
                x = Mathf.Clamp(x, 16f, Mathf.Max(16f, W - 16f - w));
                y = Mathf.Clamp(y, 16f, Mathf.Max(16f, Screen.height - 16f - h));
                return new Vector2(Mathf.Round(x), Mathf.Round(y));
            }

            private void BuildNote(OrderMenu m)
            {
                _builtNote = _noteVersion;
                _noteC.text = _note ?? "";
                float k = _k;
                Vector2 z = _toast.CalcSize(_noteC);
                float w = Mathf.Min(z.x + 30f * k, Screen.width - 32f);
                float h = 30f * k;
                Vector2 p;
                if (m._open != null) p = new Vector2(_panel.x, _panel.yMax + 8f * k);
                else
                {
                    p = Anchor(w, h);
                    if (m._hover != null && Settings.Hints.Value) p.y += 34f * k;   // under the prompt
                }
                p.y = Mathf.Min(p.y, Screen.height - 16f - h);
                _noteRect = new Rect(p.x, p.y, w, h);
            }

            // ---- drawing (repaint only, no allocation) -------------------------------------

            private void DrawMenu(OrderMenu m)
            {
                float k = _k;
                float t = Time.realtimeSinceStartup - m._openedAt;
                _alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.10f));   // brief fade-in
                float slide = (1f - _alpha) * 6f * k;
                Rect panel = Shift(_panel, slide);

                Fill(panel, PanelBg);
                Fill(Shift(_header, slide), HeaderBg);
                Fill(new Rect(panel.x, panel.y, panel.width, Mathf.Max(1f, 2f * k)), Accent * new Color(1f, 1f, 1f, 0.9f));
                Outline(panel, Border);

                float pad = Pad * k;
                Rect h = Shift(_header, slide);
                Print(new Rect(h.x + pad, h.y, h.width - 2f * pad, h.height), _titleC, _title, Text);
                Print(new Rect(h.x + pad, h.y, h.width - 2f * pad, h.height), _tagC, _tag, TextDim);
                Fill(new Rect(h.x, h.yMax, h.width, 1f), Border);

                float now = Time.realtimeSinceStartup;
                float badge = Badge * k;
                for (int i = 0; i < m._count; i++)
                {
                    Option o = m._options[i];
                    Rect r = Shift(_rows[i], slide);
                    bool sel = i == m._index;
                    if (sel) Fill(r, o.Available ? Accent : new Color(0.30f, 0.30f, 0.28f, 0.95f));
                    if (i == m._flashIndex && now - m._flashAt < 0.4f)
                        Fill(r, new Color(Bad.r, Bad.g, Bad.b, 0.45f * (1f - (now - m._flashAt) / 0.4f)));

                    Rect b = new Rect(r.x + 6f * k, r.y + (r.height - badge) * 0.5f, badge, badge);
                    Color fg = sel && o.Available ? TextDark : o.Available ? Text : TextOff;
                    if (sel && o.Available) Fill(b, TextDark * new Color(1f, 1f, 1f, 0.85f));
                    else Outline(b, o.Available ? new Color(Text.r, Text.g, Text.b, 0.45f) : new Color(TextOff.r, TextOff.g, TextOff.b, 0.5f));
                    if (o.Digit > 0 && o.Digit < Digits.Length)
                        Print(b, Digits[o.Digit], _badge, sel && o.Available ? Accent : o.Available ? Text : TextOff);

                    Rect lab = new Rect(b.xMax + 8f * k, r.y, r.xMax - b.xMax - 14f * k, r.height);
                    Print(lab, _labels[i], _row, fg);
                    if (o.Reason != null)
                        Print(lab, _reasons[i], _reason, o.Current ? (sel ? TextDark : Accent) : (sel ? Text : TextOff));
                }

                bool msg = m._footerMsg != null && now < m._footerUntil;
                if (!msg && m._footerMsg != null && _footerC.text == m._footerMsg) _builtVersion = -1;   // message over: hint back next frame
                Print(Shift(_footerRect, slide), _footerC, _footer,
                    msg ? (m._footerKind == NoteKind.Error ? Bad : Info) : TextDim);
            }

            private void DrawHint()
            {
                _alpha = 1f;
                float k = _k;
                Fill(_hintRect, PanelBg);
                Outline(_hintRect, Border);
                Outline(_hintKey, new Color(Text.r, Text.g, Text.b, 0.6f));
                Print(_hintKey, _hintKeyC, _badge, Text);
                Print(new Rect(_hintKey.xMax + 10f * k, _hintRect.y, _hintRect.xMax - _hintKey.xMax - 10f * k, _hintRect.height), _hintC, _hint, Text);
            }

            private void DrawNote(OrderMenu m)
            {
                float k = _k;
                float left = _noteUntil - Time.time;
                _alpha = Mathf.Clamp01(left / 0.4f);
                Color bar = _noteKind == NoteKind.Success ? Ok : _noteKind == NoteKind.Error ? Bad : Info;
                Fill(_noteRect, PanelBg);
                Fill(new Rect(_noteRect.x, _noteRect.y, Mathf.Max(2f, 3f * k), _noteRect.height), bar);
                Outline(_noteRect, Border);
                Print(new Rect(_noteRect.x + 14f * k, _noteRect.y, _noteRect.width - 20f * k, _noteRect.height), _noteC, _toast, Text);
            }

            private void Fill(Rect r, Color c)
            {
                c.a *= _alpha;
                GUI.color = c;
                GUI.DrawTexture(r, Texture2D.whiteTexture);
            }

            private void Outline(Rect r, Color c)
            {
                Fill(new Rect(r.x, r.y, r.width, 1f), c);
                Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
                Fill(new Rect(r.x, r.y + 1f, 1f, r.height - 2f), c);
                Fill(new Rect(r.xMax - 1f, r.y + 1f, 1f, r.height - 2f), c);
            }

            private void Print(Rect r, GUIContent c, GUIStyle st, Color col)
            {
                col.a *= _alpha;
                GUI.color = col;
                GUI.Label(r, c, st);
            }

            private static Rect Shift(Rect r, float dx) => dx == 0f ? r : new Rect(r.x + dx, r.y, r.width, r.height);

            private static string SideTag(Player bot)
            {
                EPlayerSide side = bot?.Profile?.Info?.Side ?? EPlayerSide.Savage;
                return side == EPlayerSide.Savage ? "SCAV" : side == EPlayerSide.Usec ? "USEC" : "BEAR";
            }

            // 0.17.0: the key hint, short (a passing message replaces it).
            private static string FooterHint(int count)
            {
                string pick = Settings.NumberKeysChoice.Value != NumberKeysMode.Off ? "1-9 give  0 close  " : "";
                string sel = Settings.WheelSelect.Value ? "wheel" : $"{KeyText(Settings.PrevKey.Value.MainKey)}{KeyText(Settings.NextKey.Value.MainKey)}";
                return $"{pick}{sel} + {KeyText(Settings.MenuKey.Value.MainKey)}";
            }

            // 0.17.0 compact sizes (x the scale k): 0.15 used 30 px rows, a 50 px two-line header.
            private const float Pad = 9f, RowH = 23f, Badge = 16f, HeaderH = 25f, FooterH = 19f;

            private static string KeyText(KeyCode k)
            {
                switch (k)
                {
                    case KeyCode.Slash: return "/";
                    case KeyCode.Period: return ".";
                    case KeyCode.Equals: return "=";
                    case KeyCode.Comma: return ",";
                    case KeyCode.Minus: return "-";
                    case KeyCode.Semicolon: return ";";
                    case KeyCode.Quote: return "'";
                    case KeyCode.BackQuote: return "`";
                    case KeyCode.Backslash: return "\\";
                    case KeyCode.LeftBracket: return "[";
                    case KeyCode.RightBracket: return "]";
                    case KeyCode.None: return "-";
                    default: return k.ToString();
                }
            }
        }
    }
}
