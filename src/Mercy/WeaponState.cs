using System.Collections.Generic;
using EFT;
using HarmonyLib;

namespace Mercy
{
    // 0.24.0: a disarmed bot knows he is unarmed.
    //
    // Verified in the 0.16.9 decompile: BotWeaponManager.HaveBullets is
    //   _currentWeaponInfo.Reload.BulletCount > CriticalBulletCount
    // and BotReload.BulletCount reads BotWeaponManager.ShootController.Item, the firearm controller
    // of the LAST gun he held. ShootController is assigned when a firearm controller is set
    // (UpdateFirearmsController) and only cleared in Dispose (the bot is destroyed): an empty-hands
    // or knife controller leaves it alone. So after our drop (the gun thrown, hands empty), HaveBullets
    // kept reading the thrown gun's magazine: true. SAIN 4.5.1's decisions start from it
    // (EnemyDecisionClass.GetDecision: "!HaveBullets -> Retreat" is the first test; past it come
    // StandAndShoot, Rush, ThrowGrenade, MoveToEngage, Search; DogFightDecisionClass and the squad
    // decisions likewise), so a released, empty-handed man was pushed at his ex-captor, sent to
    // search for him, or given a grenade throw (the grenade check needs the enemy NOT visible,
    // which is exactly what a man who got away has): the circling back and the long throws of the
    // 0.23.0 raid.
    //
    // For bots whose gun this mod took out of their hands (Track, from Arsenal.EmptyHands) and only
    // while their hands hold no firearm, HaveBullets reads false: SAIN retreats and takes cover
    // (SeekCoverAction), vanilla ShootData ends a shot. Because "no bullets" makes vanilla and SAIN
    // ask BotReload.TryReload (ShootData.Shoot, Shoot, BotWeaponManager.TryReloadWeapon...), and
    // that would act on the stale controller of a gun he no longer holds, TryReload returns false
    // for the same bots in the same state. A firearm back in his hands (his own draw, a pick-up,
    // SAIN's own weapon switch): both patches step aside at once (the game's values again).
    // Everyone else: one count test. Off: "Disarmed bots know they are unarmed".
    internal static class WeaponState
    {
        private static readonly HashSet<BotWeaponManager> Tracked = new HashSet<BotWeaponManager>();
        private static readonly HashSet<BotWeaponManager> Logged = new HashSet<BotWeaponManager>();
        internal static int Count => Tracked.Count;

        internal static void Track(Player bot)
        {
            BotWeaponManager wm = bot?.AIData?.BotOwner?.WeaponManager;
            if (wm != null) Tracked.Add(wm);
        }

        // Tracked, the fix on, and no firearm in his hands now.
        internal static bool Disarmed(BotWeaponManager wm)
        {
            if (wm == null || !Tracked.Contains(wm) || !Settings.FixStaleWeapon.Value) return false;
            Player p = wm._owner?.GetPlayer;
            if (p == null) return false;
            return !(p.HandsController is Player.FirearmController);
        }

        internal static void Note(BotWeaponManager wm, string what)
        {
            if (!Logged.Add(wm) || !(Settings.LogEscape.Value || Settings.VerboseLogs.Value)) return;
            Player p = wm._owner?.GetPlayer;
            MercyPlugin.Log.LogInfo($"{(p != null ? Restraint.Name(p) : "a bot")}: {what} (his last gun is no longer in his hands; logged once per bot)");
        }

        // What the game's TryReload does when it finds nothing to load (BotReload.TryReload ->
        // Selector.TrySwitchToLauncherOrChangeWeapon), minus the underbarrel branch (it would act on
        // the stale gun): with an enemy and a usable gun in one of his slots (picked up since, say),
        // the selector's own change. At most every 2 s per bot (Arsenal.Usable's ammo walk is cached).
        private static readonly Dictionary<BotWeaponManager, float> NextChange = new Dictionary<BotWeaponManager, float>();
        internal static void MaybeChange(BotWeaponManager wm)
        {
            float now = UnityEngine.Time.time;
            if (NextChange.TryGetValue(wm, out float t) && now < t) return;
            NextChange[wm] = now + 2f;
            try
            {
                BotOwner o = wm._owner;
                if (o?.Memory == null || !o.Memory.HaveEnemy || wm.Selector == null || wm.Selector.IsChanging) return;
                // Ours (held, fleeing, drawing): our layer owns his hands; a change here would be a
                // second hands operation on top of ours.
                if (SurrenderRegistry.HoldsBot(o.GetPlayer) || Escape.KeepAway(o.GetPlayer)) return;
                // 0.31.1: only from empty hands or a knife. "Disarmed" is any non-firearm controller, so a
                // released man healing (meds in hands) whose AI asked for a reload got a weapon change on
                // top of the meds: the busy-hands glitch Joel saw on healing bots (2026-11-22).
                Player.AbstractHandsController hc = o.GetPlayer?.HandsController;
                if (!(hc is Player.EmptyHandsController) && !(hc is Player.BaseKnifeController)) return;
                if (Arsenal.UsableGun(o.GetPlayer) == null) return;
                wm.Selector.TryChangeWeapon(ignoreDistance: true);
            }
            catch (System.Exception e) { MercyPlugin.Report("weapon change", e); }
        }

        internal static void Clear() { Tracked.Clear(); Logged.Clear(); NextChange.Clear(); }
    }

    [HarmonyPatch(typeof(BotWeaponManager), nameof(BotWeaponManager.HaveBullets), MethodType.Getter)]
    internal static class HaveBulletsPatch
    {
        private static void Postfix(BotWeaponManager __instance, ref bool __result)
        {
            if (!__result || WeaponState.Count == 0) return;
            if (!WeaponState.Disarmed(__instance)) return;
            __result = false;
            WeaponState.Note(__instance, "'has bullets' reads false for his AI (the game still read the thrown gun's magazine)");
        }
    }

    [HarmonyPatch(typeof(BotReload), nameof(BotReload.TryReload))]
    internal static class TryReloadPatch
    {
        private static bool Prefix(BotReload __instance, ref bool __result)
        {
            if (WeaponState.Count == 0) return true;
            BotWeaponManager wm = __instance._owner?.WeaponManager;
            if (!WeaponState.Disarmed(wm)) return true;
            __result = false;
            WeaponState.Note(wm, "no reload started on a gun that is no longer in his hands");
            WeaponState.MaybeChange(wm);
            return false;
        }
    }
}
