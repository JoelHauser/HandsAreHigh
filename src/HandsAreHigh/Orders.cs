using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace HandsAreHigh
{
    // The one way an order reaches a held bot, whoever gives it: you from the menu, or a bot
    // captor from his plan. Checked here (alive, hands up, range, sight), applied once, now.
    // A spoken line (yours optional, a bot's by setting) is only feedback: the order below is
    // applied whether or not any audio plays.
    internal static class Orders
    {
        internal static bool CanGive(SurrenderState s, Player by, bool human, out string why)
        {
            why = null;
            if (s?.Bot == null || s.Dead || !Perception.Alive(s.Bot)) { why = "he's down"; return false; }
            if (s.Phase != Phase.HandsUp) { why = "he isn't holding his hands up"; return false; }
            if (!Perception.Alive(by)) { why = "the one giving it is down"; return false; }
            float range = human ? Settings.InteractionRange.Value + 0.5f : Settings.AiOrderRange.Value;
            Vector3 from = Perception.Eye(by), to = Perception.Chest(s.Bot);
            if (Vector3.Distance(from, to) > range) { why = "out of range"; return false; }
            if (!Perception.LineClear(from, to)) { why = "no clear line"; return false; }
            if (!human && s.Custody?.Bot != by) { why = "not his prisoner"; return false; }
            return true;
        }

        internal static bool Give(SurrenderState s, OrderKind order, Player by, bool human, out string why)
        {
            if (!CanGive(s, by, human, out why))
            {
                if (human) OrderMenu.Notify($"Too late: {why}", NoteKind.Error);
                else if (Settings.VerboseLogs.Value) HandsAreHighPlugin.Log.LogInfo($"{Restraint.Name(by)}: order {order} to {s?.Name} not given ({why})");
                return false;
            }
            if (human && s.Captor != by)
            {
                // Taking over: from nobody, a captor who's gone, or an allied bot (the menu only
                // offers it then). His custody, if any, ends cleanly first.
                HandsAreHighPlugin.Log.LogInfo($"{s.Name}: you take over from {Restraint.Name(s.Captor)}");
                Captors.EndCustody(s, "the player took over");
            }
            s.Captor = by;
            s.OrderByHuman = human;
            HandsAreHighPlugin.Log.LogInfo($"{s.Name}: ordered {order} by {(human ? "you" : Restraint.Name(by))}");

            s.Accept(human ? "gave an order" : "his captor gives orders");
            if (human) HandsAreHighPlugin.Try("order speech", () => CommandVoice.Speak(by, order));
            else CaptorVoice.Say(by, CaptorVoice.ForOrder(order));   // queued; the order applies now either way
            Gestures.Cancel(s, "given an order");
            if (order != OrderKind.Leave)
            {
                s.ComplyUntil = Mathf.Max(s.ComplyUntil, Time.time + Settings.OrderCompliance.Value * Dice.Range(0.7f, 1.3f));
                s.DecideAt = -1f;
                s.OpenSince = -1f;
                s.LastCovered = true;
            }

            switch (order)
            {
                case OrderKind.DropWeapons:
                    QueueDrop(s, Arsenal.ItemsFor(s.Bot, Arsenal.WeaponSlots, false), "drop your weapons", human);
                    break;
                case OrderKind.DropKit:
                    QueueDrop(s, Arsenal.ItemsFor(s.Bot, Arsenal.KitSlots, false), "drop your kit", human);
                    break;
                case OrderKind.ArmourOff:
                    QueueDrop(s, Arsenal.ItemsFor(s.Bot, Arsenal.ArmourOrderSlots, true), "armour off", human);
                    break;
                case OrderKind.Follow:
                    s.Order = Order.Follow;
                    if (human) OrderMenu.Notify($"{s.Name} follows you, hands up", NoteKind.Success);
                    Answer(s, VoiceEvent.Acknowledge);
                    break;
                case OrderKind.Kneel:
                    s.Order = Order.Kneel;
                    if (human) OrderMenu.Notify($"{s.Name} gets down", NoteKind.Success);
                    Answer(s, VoiceEvent.Acknowledge);
                    break;
                case OrderKind.Stay:
                    s.Order = Order.None;
                    if (human) OrderMenu.Notify($"{s.Name} stays put", NoteKind.Success);
                    Answer(s, VoiceEvent.Acknowledge);
                    break;
                case OrderKind.Leave:
                    if (human) OrderMenu.Notify($"{s.Name} is let go", NoteKind.Success);
                    s.BeginRetreat(human ? "let go" : $"let go by {Restraint.Name(by)}", emergency: false);
                    Answer(s, VoiceEvent.Released);
                    break;
            }
            return true;
        }

        // His answer, after a short varied delay; never holds up the order itself.
        private static void Answer(SurrenderState s, VoiceEvent e)
        {
            if (Settings.Acknowledge.Value) Voice.Say(s, e, Voice.AckDelay());
        }

        private static void QueueDrop(SurrenderState s, List<KeyValuePair<EquipmentSlot, Item>> items, string name, bool human)
        {
            s.DropQueue.Clear();
            s.Ordered.Clear();
            s.OrderName = name;
            if (items.Count == 0)
            {
                if (human) OrderMenu.Notify($"{s.Name} has nothing like that on him", NoteKind.Error);
                Answer(s, VoiceEvent.Refuse);
                return;
            }
            foreach (var kv in items)
            {
                s.DropQueue.Enqueue(kv);
                s.Ordered.Add(kv);
            }
            s.NextDropAt = Time.time + 0.3f;
            if (human) OrderMenu.Notify($"{s.Name}: {name} ({items.Count} item{(items.Count == 1 ? "" : "s")})", NoteKind.Success);
            Answer(s, VoiceEvent.Acknowledge);
        }
    }
}
