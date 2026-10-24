using System;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.ObjectUI.InGame.RingMenu;
using ExposureUnnoticed2.ObjectUI.InGame.TempUnlockPanel;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using HarmonyLib;

namespace Yoke
{
    /// <summary>Harmony hooks into the game. Every hook fails open: on an exception the game behaves as without the mod.</summary>
    internal static class Patches
    {
        public const string LabelKey = "yoke_ring_label";
        public const string SaveKeyLocked = "Yoke.Locked";
        public const string SaveKeyChance = "Yoke.Chance";
        public const string SaveKeyShortcuts = "Yoke.Shortcuts";

        public static void Apply(Harmony harmony)
        {
            var me = typeof(Patches);
            void Hook(System.Reflection.MethodBase target, string postfix) => harmony.Patch(target, postfix: new HarmonyMethod(me, postfix));
            Hook(AccessTools.Method(typeof(InputResolver), nameof(InputResolver.Resolve)), nameof(ResolvePostfix));
            Hook(AccessTools.Method(typeof(Localizer), nameof(Localizer.Trans), new[] { typeof(string) }), nameof(TransPostfix));
            Hook(AccessTools.Method(typeof(TempUnlockPanelView), nameof(TempUnlockPanelView.OnLateUpdate)), nameof(UnlockPanelPostfix));
            Hook(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Load)), nameof(LoadPostfix));
            Hook(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Save)), nameof(SavePostfix));
        }

        // --- input: remember the interact key (struggling); with the yoke on, nothing that needs her hands, no running -------

        private static void ResolvePostfix(InputData __result)
        {
            try
            {
                var c = YokeController.Instance;
                if (c == null || __result == null) return;
                c.InteractHeld = __result.Interact;
                if (!c.Machine.Worn || (Plugin.DebugDisable.Value & 4) != 0) return;
                // Walking speed: no running (as SpreaderBar) - except in a pose skill, which Shift + move ends (as in vanilla).
                if (!YokeController.InPose(PlayerController.Instance)) __result.Dash = false;
                __result.JustAutoRun = __result.JustAutoChase = false;
                // Her hands are locked beside her head: what needs them is refused with a message.
                bool refused = __result.JustOnaniNormal || __result.JustRingDildo || __result.JustRingHandcuff || __result.JustRingEyeMask
                               || __result.JustEyeMask || __result.JustDrinkWater || __result.JustDrop || __result.JustDropHandcuffKey
                               || __result.JustDropVibeRemocon || __result.JustBra || __result.JustPants || __result.JustOpenClothesA
                               || __result.JustOpenClothesB || __result.JustCloseClothesA || __result.JustCloseClothesB
                               || __result.JustPoseSkill != RingMenuParentView.RingItemType.None;
                __result.JustOnaniNormal = __result.JustRingDildo = __result.JustRingHandcuff = __result.JustRingEyeMask = false;
                __result.JustEyeMask = __result.JustDrinkWater = __result.JustDrop = __result.JustDropHandcuffKey = false;
                __result.JustDropVibeRemocon = __result.JustBra = __result.JustPants = false;
                __result.JustOpenClothesA = __result.JustOpenClothesB = __result.JustCloseClothesA = __result.JustCloseClothesB = false;
                __result.JustPoseSkill = RingMenuParentView.RingItemType.None;
                __result.MoveForwardOneHand = false;
                if (refused) c.OnRefused();
            }
            catch (Exception e) { YokeController.Fail("input hook", e); }
        }

        private static void TransPostfix(string key, ref string __result)
        {
            if (key == LabelKey) __result = Plugin.RingLabel.Value;
        }

        // --- the game's "Unlocking..." gauge shows our attempt too ----------------------------------------------------------

        private static void UnlockPanelPostfix(TempUnlockPanelView __instance)
        {
            try
            {
                var c = YokeController.Instance;
                if (c == null || __instance == null) return;
                float attempt = c.Machine.Attempt;
                if (attempt <= 0f) return;
                if (__instance.contents != null && !__instance.contents.activeSelf) __instance.contents.SetActive(true);
                if (__instance.circleGauge != null) __instance.circleGauge.fillAmount = attempt;
            }
            catch (Exception e) { YokeController.Fail("unlock gauge hook", e); }
        }

        // --- save / load: the lock, its chance and the number shortcuts (written right after SaveManager.Save into the slot it
        // just wrote, like SpreaderBar [observed 2026-10-11]) -----------------------------------------------------------------

        private static void SavePostfix()
        {
            try
            {
                var c = YokeController.Instance;
                if (c == null || SaveRapper.IsSystemData || SaveRapper.currentDataPath == SaveRapper.SystemDataPath) return;
                bool locked = c.Machine.Worn;
                SaveRapper.Save(SaveKeyLocked, locked);
                SaveRapper.Save(SaveKeyChance, (double)c.Machine.Chance);
                SaveRapper.Save(SaveKeyShortcuts, c.Ring.ShortcutMask);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] save: locked=" + locked + " shortcuts=" + c.Ring.ShortcutMask + " path=" + SaveRapper.currentDataPath);
            }
            catch (Exception e) { YokeController.Fail("save hook", e); }
        }

        private static void LoadPostfix()
        {
            try
            {
                var c = YokeController.Instance;
                if (c == null || SaveRapper.IsSystemData || SaveRapper.currentDataPath == SaveRapper.SystemDataPath) return;
                bool locked = SaveRapper.Load(SaveKeyLocked, false);
                double chance = SaveRapper.Load(SaveKeyChance, -1.0);
                int shortcuts = SaveRapper.Load(SaveKeyShortcuts, 0);
                c.OnSaveLoaded(locked, (float)Math.Max(chance, 0.0), shortcuts);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] load: locked=" + locked + " shortcuts=" + shortcuts + " path=" + SaveRapper.currentDataPath);
            }
            catch (Exception e) { YokeController.Fail("load hook", e); }
        }
    }
}
