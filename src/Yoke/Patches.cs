using System;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using HarmonyLib;

namespace Yoke
{
    /// <summary>Harmony hooks into the game: the yoke's own save data. The ring entry, input blocking, unlock gauge and number
    /// shortcuts are RestraintHub's. Every hook fails open.</summary>
    internal static class Patches
    {
        public const string SaveKeyLocked = "Yoke.Locked";
        public const string SaveKeyChance = "Yoke.Chance";
        public const string SaveKeyShortcuts = "Yoke.Shortcuts"; // written by the hub (kept from 0.1.0)

        public static void Apply(Harmony harmony)
        {
            var me = typeof(Patches);
            void Hook(System.Reflection.MethodBase target, string postfix) => harmony.Patch(target, postfix: new HarmonyMethod(me, postfix));
            Hook(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Load)), nameof(LoadPostfix));
            Hook(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.Save)), nameof(SavePostfix));
        }

        // --- save / load: the lock and its chance (written right after SaveManager.Save into the slot it just wrote, like
        // SpreaderBar [observed 2026-10-11]) ----------------------------------------------------------------------------------

        private static void SavePostfix()
        {
            try
            {
                var c = YokeController.Instance;
                if (c == null || SaveRapper.IsSystemData || SaveRapper.currentDataPath == SaveRapper.SystemDataPath) return;
                bool locked = c.Machine.Worn;
                SaveRapper.Save(SaveKeyLocked, locked);
                SaveRapper.Save(SaveKeyChance, (double)c.Machine.Chance);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] save: locked=" + locked + " path=" + SaveRapper.currentDataPath);
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
                c.OnSaveLoaded(locked, (float)Math.Max(chance, 0.0));
                Plugin.Logger.LogInfo("[SFM-ACCEPT] load: locked=" + locked + " path=" + SaveRapper.currentDataPath);
            }
            catch (Exception e) { YokeController.Fail("load hook", e); }
        }
    }
}
