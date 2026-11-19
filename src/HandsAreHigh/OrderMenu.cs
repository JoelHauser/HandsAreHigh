using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    internal enum OrderKind { DropWeapons, DropKit, ArmourOff, Follow, Kneel, Stay, Leave }

    // A small menu of orders for the held bot you're looking at, drawn with IMGUI.
    // Orders are checked again when given (range, sight, phase), and a bot following or
    // kneeling still watches for an opening.
    internal sealed class OrderMenu
    {
        private static string _note;
        private static float _noteUntil;

        private readonly List<OrderKind> _options = new List<OrderKind>();
        private SurrenderState _target;
        private SurrenderState _open;
        private int _index;
        private GUIStyle _box, _line, _hint;

        internal static void Notify(string msg)
        {
            _note = msg;
            _noteUntil = Time.time + 4f;
        }

        internal static void ClearNotes()
        {
            _note = null;
            _noteUntil = 0f;
        }

        internal void Update(Player me)
        {
            _target = null;
            if (!Settings.Enabled.Value || !Settings.OrdersEnabled.Value || me == null || !Perception.Alive(me))
            {
                _open = null;
                return;
            }
            _target = LookedAt(me);
            if (_open != null && _open != _target) _open = null;

            if (Settings.MenuKey.Value.IsDown() && _target != null)
            {
                if (_open == null)
                {
                    BuildOptions();
                    _open = _target;
                    _index = 0;
                }
                else
                {
                    Give(_open, _options[_index], me);
                    _open = null;
                }
                return;
            }
            if (_open == null) return;
            if (Settings.NextKey.Value.IsDown()) _index = (_index + 1) % _options.Count;
            if (Settings.PrevKey.Value.IsDown()) _index = (_index + _options.Count - 1) % _options.Count;
        }

        private void BuildOptions()
        {
            _options.Clear();
            _options.Add(OrderKind.DropWeapons);
            if (Settings.KitOrder.Value) _options.Add(OrderKind.DropKit);
            if (Settings.ArmourOrder.Value) _options.Add(OrderKind.ArmourOff);
            if (Settings.FollowOrder.Value) _options.Add(OrderKind.Follow);
            _options.Add(OrderKind.Kneel);
            _options.Add(OrderKind.Stay);
            _options.Add(OrderKind.Leave);
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
                if (s.Bot == null || s.Phase != Phase.HandsUp || s.Suspended) continue;
                Vector3 to = Perception.Chest(s.Bot) - eye;
                if (to.magnitude > range) continue;
                float angle = Vector3.Angle(me.LookDirection, to);
                if (angle >= bestAngle || !Perception.LineClear(eye, Perception.Chest(s.Bot))) continue;
                bestAngle = angle;
                best = s;
            }
            return best;
        }

        private static void Give(SurrenderState s, OrderKind order, Player me)
        {
            // Checked again now, not when the menu opened.
            if (s.Bot == null || s.Phase != Phase.HandsUp ||
                Vector3.Distance(Perception.Eye(me), Perception.Chest(s.Bot)) > Settings.InteractionRange.Value + 0.5f ||
                !Perception.LineClear(Perception.Eye(me), Perception.Chest(s.Bot)))
            {
                Notify("Too late: he's not in front of you with his hands up");
                return;
            }
            if (s.Captor != me) s.Captor = me;
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: ordered {order}");

            switch (order)
            {
                case OrderKind.DropWeapons:
                    QueueDrop(s, Arsenal.ItemsFor(s.Bot, Arsenal.WeaponSlots, false), "drop your weapons");
                    break;
                case OrderKind.DropKit:
                    QueueDrop(s, Arsenal.ItemsFor(s.Bot, Arsenal.KitSlots, false), "drop your kit");
                    break;
                case OrderKind.ArmourOff:
                    QueueDrop(s, Arsenal.ItemsFor(s.Bot, Arsenal.ArmourOrderSlots, true), "armour off");
                    break;
                case OrderKind.Follow:
                    s.Order = Order.Follow;
                    Notify($"{s.Name} follows you, hands up");
                    Voice.Say(s, VoiceEvent.Acknowledge);
                    break;
                case OrderKind.Kneel:
                    s.Order = Order.Kneel;
                    Notify($"{s.Name} gets down");
                    Voice.Say(s, VoiceEvent.Acknowledge);
                    break;
                case OrderKind.Stay:
                    s.Order = Order.None;
                    Notify($"{s.Name} stays put");
                    Voice.Say(s, VoiceEvent.Acknowledge);
                    break;
                case OrderKind.Leave:
                    Notify($"{s.Name} is let go");
                    s.BeginRetreat("let go", emergency: false);
                    break;
            }
        }

        private static void QueueDrop(SurrenderState s, List<KeyValuePair<EquipmentSlot, Item>> items, string name)
        {
            s.DropQueue.Clear();
            s.Ordered.Clear();
            s.OrderName = name;
            if (items.Count == 0)
            {
                Notify($"{s.Name} has nothing like that on him");
                Voice.Say(s, VoiceEvent.Refuse);
                return;
            }
            foreach (var kv in items)
            {
                s.DropQueue.Enqueue(kv);
                s.Ordered.Add(kv);
            }
            s.NextDropAt = Time.time + 0.3f;
            Notify($"{s.Name}: {name} ({items.Count} item(s))");
            Voice.Say(s, VoiceEvent.Acknowledge);
        }

        internal void OnGUI()
        {
            if (_box == null)
            {
                _box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 16, padding = new RectOffset(12, 12, 8, 8) };
                _line = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true };
                _hint = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, richText = true };
            }

            float cx = Screen.width * 0.5f;
            float y = Screen.height * 0.62f;

            if (_open != null)
            {
                float h = 34f + _options.Count * 24f;
                GUI.Box(new Rect(cx - 150f, y, 300f, h), $"Orders: {_open.Name}", _box);
                for (int i = 0; i < _options.Count; i++)
                {
                    string label = Label(_options[i]);
                    _line.normal.textColor = i == _index ? Color.yellow : Color.white;
                    GUI.Label(new Rect(cx - 135f, y + 30f + i * 24f, 270f, 24f), (i == _index ? "> " : "  ") + label, _line);
                }
                GUI.Label(new Rect(cx - 250f, y + h + 4f, 500f, 24f),
                    $"{Settings.PrevKey.Value} / {Settings.NextKey.Value} choose, {Settings.MenuKey.Value} give the order", _hint);
            }
            else if (_target != null && Settings.Hints.Value)
            {
                GUI.Label(new Rect(cx - 250f, y, 500f, 24f), $"{Settings.MenuKey.Value}: give {_target.Name} orders", _hint);
            }

            if (_note != null && Time.time < _noteUntil && Settings.Hints.Value)
                GUI.Label(new Rect(cx - 300f, y - 30f, 600f, 24f), _note, _hint);
        }

        private static string Label(OrderKind k)
        {
            switch (k)
            {
                case OrderKind.DropWeapons: return "Drop your weapons";
                case OrderKind.DropKit: return "Drop your kit";
                case OrderKind.ArmourOff: return "Armour off";
                case OrderKind.Follow: return "Follow me";
                case OrderKind.Kneel: return "Get down";
                case OrderKind.Stay: return "Stay there";
                default: return "Get lost";
            }
        }
    }
}
