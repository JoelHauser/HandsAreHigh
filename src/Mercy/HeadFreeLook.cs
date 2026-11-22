using EFT;
using HarmonyLib;

namespace Mercy
{
    // 0.18.0: the game's own bot head free-look (BotHeadData, see Presence's header) turns a bot's
    // head up to 40 deg towards his best observed enemy, or 25 deg either way on patrol glances,
    // and holds it (Player.Look withReturn false). On a held man that fought the look (Presence
    // sets the head absolutely now, so it no longer shows, but the AI's head state and the
    // HeadRotation it feeds would still point elsewhere). While he holds his hands up, the target is
    // centred and the game's own UpdateHeadRotation brings the head back at its own speed; his
    // states aren't run. Everything else (released, rearmed, dead, asleep) runs untouched.
    [HarmonyPatch(typeof(BotHeadData), nameof(BotHeadData.ManualUpdate))]
    internal static class HeadFreeLookPatch
    {
        private static bool Prefix(BotHeadData __instance)
        {
            if (!SurrenderRegistry.Any || !Settings.Enabled.Value || !Settings.HeadMovement.Value) return true;
            long t0 = Perf.Begin();
            Player p = __instance._owner?.GetPlayer;
            if (p == null || !SurrenderRegistry.TryGet(p, out SurrenderState s) || s.Dead || s.Suspended || s.Phase != Phase.HandsUp)
            {
                Perf.End(Perf.Sec.HeadLook, t0);
                return true;
            }
            __instance._targetHorizontalRotation = 0f;
            __instance.UpdateHeadRotation();
            Perf.End(Perf.Sec.HeadLook, t0);
            return false;
        }
    }
}
