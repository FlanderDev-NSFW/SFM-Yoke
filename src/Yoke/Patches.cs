using System;
using ExposureUnnoticed2.Scripts.Base;
using ExposureUnnoticed2.Scripts.InGame;
using RestraintHub;

namespace Yoke
{
    /// <summary>Harmony hooks into the game: the yoke's own save data. The ring entry, input blocking, unlock gauge and number
    /// shortcuts are RestraintHub's. Save and load go through RestraintHub (OnSave / OnLoad).</summary>
    internal static class Patches
    {
        public const string SaveKeyLocked = "Yoke.Locked";
        public const string SaveKeyChance = "Yoke.Chance";
        public const string SaveKeyShortcuts = "Yoke.Shortcuts"; // written by the hub (kept from 0.1.0)

        // --- save / load: the lock and its chance (RestraintHub calls these right after the game wrote / read a slot) -----

        internal static void Save(SaveSlot slot)
        {
            try
            {
                var c = YokeController.Instance;
                if (c == null) return;
                bool locked = c.Machine.Worn;
                slot.Save(SaveKeyLocked, locked);
                slot.Save(SaveKeyChance, (double)c.Machine.Chance);
                Plugin.Logger.LogInfo("[SFM-ACCEPT] save: locked=" + locked + " path=" + slot.Path);
            }
            catch (Exception e) { YokeController.Fail("save hook", e); }
        }

        internal static void Load(SaveSlot slot)
        {
            try
            {
                var c = YokeController.Instance;
                if (c == null) return;
                bool locked = slot.Load(SaveKeyLocked, false);
                double chance = slot.Load(SaveKeyChance, -1.0);
                c.OnSaveLoaded(locked, (float)Math.Max(chance, 0.0));
                Plugin.Logger.LogInfo("[SFM-ACCEPT] load: locked=" + locked + " path=" + slot.Path);
            }
            catch (Exception e) { YokeController.Fail("load hook", e); }
        }
    }
}
