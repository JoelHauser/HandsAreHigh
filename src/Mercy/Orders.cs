using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace Mercy
{
    // The one way an order reaches a held bot, whoever gives it: you from the menu, or a bot
    // captor from his plan. Checked here (alive, hands up, range, sight), applied once, now.
    // A spoken line (yours optional, a bot's by setting) is only feedback: the order below is
    // applied whether or not any audio plays.
    internal static class Orders
    {
        // debug: the kneel test keys give orders from any distance (same path otherwise).
        internal static bool CanGive(SurrenderState s, Player by, bool human, out string why, bool debug = false)
        {
            why = null;
            if (s?.Bot == null || s.Dead || !Perception.Alive(s.Bot)) { why = "he's down"; return false; }
            if (s.Phase != Phase.HandsUp) { why = "he isn't holding his hands up"; return false; }
            if (!Perception.Alive(by)) { why = "the one giving it is down"; return false; }
            if (debug) return true;
            float range = human ? Settings.InteractionRange.Value + 0.5f : Settings.AiOrderRange.Value;
            Vector3 from = Perception.Eye(by), to = Perception.Chest(s.Bot);
            if (Vector3.Distance(from, to) > range) { why = "out of range"; return false; }
            if (!Perception.LineClear(from, to)) { why = "no clear line"; return false; }
            if (!human && s.Custody?.Bot != by) { why = "not his prisoner"; return false; }
            return true;
        }

        internal static bool Give(SurrenderState s, OrderKind order, Player by, bool human, out string why, bool debug = false)
        {
            if (!CanGive(s, by, human, out why, debug))
            {
                if (human) OrderMenu.Notify($"Too late: {why}", NoteKind.Error);
                else if (Settings.VerboseLogs.Value) MercyPlugin.Log.LogInfo($"{Restraint.Name(by)}: order {order} to {s?.Name} not given ({why})");
                return false;
            }
            // 0.25.0: also when he is somehow both yours and a bot's (never two captors).
            if (human && (s.Captor != by || s.Custody != null))
            {
                // Taking over: from nobody, a captor who's gone, or an allied bot (the menu only
                // offers it then). His custody, if any, ends cleanly first.
                MercyPlugin.Log.LogInfo($"{s.Name}: you take over from {Restraint.Name(s.Captor)}" +
                    (s.Custody != null ? $" (DEBUG: {Restraint.Name(s.Custody.Bot)} held him; his custody ends first)" : ""));
                Captors.EndCustody(s, "the player took over");
            }
            s.Captor = by;
            s.OrderByHuman = human;
            // 0.15.0: who gave it, kept apart from custody and from where he looks (Presence
            // looks at his captor only while he can see him; an old issuer is never a target).
            s.OrderIssuer = by;
            MercyPlugin.Log.LogInfo($"{s.Name}: ordered {order} by {(human ? "you" : Restraint.Name(by))}{(debug ? " (debug key)" : "")}");
            if (!debug) { s.KneelTestLeft = 0; s.KneelTestArmed = false; }   // a real order ends a kneel test

            s.Accept(human ? "gave an order" : "his captor gives orders");
            if (human) MercyPlugin.Try("order speech", () => CommandVoice.Speak(by, order));
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
                {
                    // 0.16.0: one decision per accepted demand (Compliance): all of it, all but one
                    // he keeps back, or (told again) a refusal; never a weapon he doesn't carry.
                    var all = Arsenal.ItemsFor(s.Bot, Arsenal.WeaponSlots, false);
                    if (all.Count == 0) { QueueDrop(s, all, "drop your weapons", human); break; }
                    var drop = Compliance.OnDemand(s, by, human, all, out string outcome);
                    if (drop == null)
                    {
                        // Refused or escalating: nothing dropped this time (the escalation, if any,
                        // is the layer's real draw on its next check).
                        s.DropQueue.Clear();
                        s.Ordered.Clear();
                        s.OrderName = "drop your weapons";
                        s.DemandDoneAt = Time.time;   // answered (nothing dropped): his captor judges it
                        if (human) OrderMenu.Notify($"{s.Name} {outcome}", NoteKind.Error);
                        Answer(s, VoiceEvent.Refuse);
                        break;
                    }
                    QueueDrop(s, drop, "drop your weapons", human, all.Count);
                    break;
                }
                case OrderKind.DropKit:
                    QueueDrop(s, Arsenal.ItemsFor(s.Bot, Arsenal.KitSlots, false), "drop your kit", human);
                    break;
                case OrderKind.ArmourOff:
                    QueueDrop(s, Arsenal.ItemsFor(s.Bot, Arsenal.ArmourOrderSlots, true), "armour off", human);
                    break;
                case OrderKind.DropItem:
                {
                    // 0.23.0: one named item he wears (a bot captor's demand, GearNeeds): only if he
                    // still wears it there; the drop is the same queued throw as every other order.
                    var one = new List<KeyValuePair<EquipmentSlot, Item>>(1);
                    if (s.DemandItem != null && Arsenal.StillWorn(s.Bot, s.DemandSlot, s.DemandItem))
                        one.Add(new KeyValuePair<EquipmentSlot, Item>(s.DemandSlot, s.DemandItem));
                    QueueDrop(s, one, $"drop your {GearNeeds.SlotWord(s.DemandSlot)}", human);
                    break;
                }
                case OrderKind.StepBack:
                    // 0.23.0: off the gear at his feet, to the spot his captor picked (StepBackTo).
                    if (Kneel.Active(s) || s.Order == Order.Kneel) break;
                    s.Order = Order.StepBack;
                    s.StepBackUntil = Time.time + 5f;
                    Answer(s, VoiceEvent.Acknowledge);
                    break;
                case OrderKind.Follow:
                    s.Order = Order.Follow;
                    Facing.Clear(s);   // he walks after you, facing where he goes
                    if (human) OrderMenu.Notify($"{s.Name} follows you, {(s.HandsOnHead ? "hands on his head" : "hands up")}", NoteKind.Success);
                    Answer(s, VoiceEvent.Acknowledge);
                    break;
                case OrderKind.Kneel:
                {
                    // Kneel.Update does the rest; asking again while he's going down or down
                    // changes nothing, asking while he rises sends him back down from there.
                    s.Order = Order.Kneel;
                    s.TurnReKneel = false;
                    // 0.16.0: posture and hands are separate orders. Get down still means hands on
                    // the head by default, unless he was told Hands up this surrender.
                    if (Settings.KneelHandsOnHead.Value && !s.HandsOrdered) HandsPose.Set(s, true, "comes with Get down");
                    string note = Kneel.Request(s, by, out bool ok);
                    if (human) OrderMenu.Notify(note, ok ? NoteKind.Success : NoteKind.Error);
                    Answer(s, VoiceEvent.Acknowledge);
                    break;
                }
                case OrderKind.Stand:
                    if (s.Order != Order.Kneel && !Kneel.Active(s))
                    {
                        if (human) OrderMenu.Notify($"{s.Name} is already on his feet", NoteKind.Error);
                        break;
                    }
                    s.Order = Order.None;
                    s.TurnReKneel = false;
                    if (human) OrderMenu.Notify($"{s.Name} gets back on his feet, {(s.HandsOnHead ? "hands on his head" : "hands up")}", NoteKind.Success);
                    Answer(s, VoiceEvent.Acknowledge);
                    break;
                case OrderKind.HandsOnHead:
                case OrderKind.HandsUp:
                {
                    // Where his hands go; his posture stays as it is (standing, kneeling, getting
                    // up or down). With something still in his hands the drop finishes first: the
                    // pose only moves empty hands (HandsPose).
                    bool on = order == OrderKind.HandsOnHead;
                    s.HandsOrdered = true;
                    string note = HandsPose.Set(s, on, human ? "your order" : $"{Restraint.Name(by)}'s order");
                    if (human) OrderMenu.Notify(note, NoteKind.Success);
                    Answer(s, VoiceEvent.Acknowledge);
                    break;
                }
                case OrderKind.TurnAround:
                {
                    string note = Facing.Request(s, by, out bool ok);
                    if (human) OrderMenu.Notify(note, ok ? NoteKind.Success : NoteKind.Error);
                    Answer(s, ok ? VoiceEvent.Acknowledge : VoiceEvent.Refuse);
                    break;
                }
                case OrderKind.Stay:
                    // Stops a follow; a man on his knees stays on his knees.
                    if (s.Order != Order.Kneel) s.Order = Order.None;
                    if (human) OrderMenu.Notify(s.Order == Order.Kneel ? $"{s.Name} stays down" : $"{s.Name} stays put", NoteKind.Success);
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

        // covered: how many items the order asked for (Drop your weapons may drop fewer: Compliance);
        // the note on screen only says what you can see him do, never what he kept back.
        private static void QueueDrop(SurrenderState s, List<KeyValuePair<EquipmentSlot, Item>> items, string name, bool human, int covered = -1)
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
            // 0.16.0: Drop your weapons says what it covers, never how many he is really dropping
            // (that would tell you about anything he keeps back).
            if (human) OrderMenu.Notify(name == "drop your weapons"
                ? $"{s.Name}: told to drop his weapons ({Compliance.Covers})"
                : $"{s.Name}: {name} ({items.Count} item{(items.Count == 1 ? "" : "s")})", NoteKind.Success);
            Answer(s, VoiceEvent.Acknowledge);
        }
    }
}
