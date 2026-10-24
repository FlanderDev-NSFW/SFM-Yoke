using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using RestraintHub;
using Yoke.Logic;

namespace Yoke
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Hub.Guid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BasePlugin
    {
        public const string PluginGuid = "com.local.yoke";
        public const string PluginName = "Yoke";
        public const string PluginVersion = "0.3.0";
        private const string ModelResource = "Yoke.assets.yoke.ykm";

        internal static ManualLogSource Logger;
        /// <summary>The yoke's registration with RestraintHub (ring entry, selection, F and + / -).</summary>
        internal static Restraint Entry;
        public static ConfigEntry<string> RingLabel;
        public static ConfigEntry<float> AttemptSeconds, SuccessChance;
        public static ConfigEntry<float> Width, StepPercent, Size, NeckClearanceCm, CuffClearanceCm, CuffMaxAngle, NeckRingMaxAngle, MetalShade, NeckRingAt, CuffOnForearm;
        public static ConfigEntry<int> DebugDisable;

        public override void Load()
        {
            Logger = Log;
            RingLabel = Config.Bind("General", "RingLabel", "Yoke", "Name of the ring menu entry.");
            AttemptSeconds = Config.Bind("Unlock", "AttemptSeconds", 4.5f, new ConfigDescription(
                "Seconds of holding the interact key (F) per attempt to struggle free.", new AcceptableValueRange<float>(0.5f, 30f)));
            SuccessChance = Config.Bind("Unlock", "SuccessChance", 1f, new ConfigDescription(
                "Chance that an attempt frees her (1 = the first attempt always does; failures raise it like the game's handcuffs).",
                new AcceptableValueRange<float>(0.01f, 1f)));
            Width = Config.Bind("Yoke", "Width", 1f, new ConfigDescription(
                "Distance of the wrist cuffs from the neck ring, as a factor of the model (1 = as modelled; less = narrower, her " +
                "forearms stand more upright; wider until her arms are straight or the cuffs reach CuffMaxAngle). + / - while it is on change it and store the result here.", new AcceptableValueRange<float>(0.35f, 1.6f)));
            StepPercent = Config.Bind("Yoke", "StepPercent", 2f, new ConfigDescription(
                "How much one press of + / - changes the width (percent of the model's width).", new AcceptableValueRange<float>(0.5f, 20f)));
            Size = Config.Bind("Model", "Size", 1f, new ConfigDescription(
                "Size of the yoke (rods, cuffs' spacing) relative to her neck (1 = as fitted); the rings are sized to her neck and wrists.",
                new AcceptableValueRange<float>(0.7f, 2f)));
            NeckClearanceCm = Config.Bind("Model", "NeckClearanceCm", 0.4f, new ConfigDescription(
                "Room between her neck and the neck ring (cm). The neck is estimated from her spine length (7 %); lower it (down to -1) " +
                "for a tighter ring.", new AcceptableValueRange<float>(-1f, 5f)));
            CuffClearanceCm = Config.Bind("Model", "CuffClearanceCm", 0.5f, new ConfigDescription(
                "Room between her wrist and the wrist cuffs (cm); the wrist is estimated from her forearm (7 % of its length). Raise it " +
                "for sleeves or a thicker wrist, lower it (down to -1) for tighter cuffs; large values leave the cuffs as modelled.",
                new AcceptableValueRange<float>(-1f, 5f)));
            CuffMaxAngle = Config.Bind("Model", "CuffMaxAngle", 70f, new ConfigDescription(
                "How far the wrist cuffs swivel on their rods to follow the forearms (degrees; 0 = fixed upright as modelled). The " +
                "yoke gets no wider once a cuff is at this limit.", new AcceptableValueRange<float>(0f, 80f)));
            NeckRingMaxAngle = Config.Bind("Model", "NeckRingMaxAngle", 45f, new ConfigDescription(
                "How far the neck ring turns to follow her neck (degrees; 0 = fixed to the shoulders). The rods follow its knobs.",
                new AcceptableValueRange<float>(0f, 80f)));
            MetalShade = Config.Bind("Model", "MetalShade", 0.3f, new ConfigDescription(
                "Brightness of the metal (0 = black, 1 = white).", new AcceptableValueRange<float>(0f, 1f)));
            NeckRingAt = Config.Bind("Pose", "NeckRingAt", 0.25f, new ConfigDescription(
                "Height of the neck ring on her neck (0 = the neck's base, 1 = the head joint).", new AcceptableValueRange<float>(-0.5f, 1f)));
            CuffOnForearm = Config.Bind("Pose", "CuffOnForearm", 0.88f, new ConfigDescription(
                "Preferred place of the wrist cuffs on the forearm (0 = elbow, 1 = wrist); the pose may move it a little to keep the " +
                "forearms upright.", new AcceptableValueRange<float>(0.55f, 0.97f)));
            DebugDisable = Config.Bind("Debug", "Disable", 0,
                "Bit mask for tests: 1 = do not pose the arms; 2 = aim with the LateUpdate bone lengths (not the drawn ones). (Input blocking: RestraintHub's Debug.Disable.)");

            try
            {
                YokeController.Model = LoadModel();
            }
            catch (Exception e)
            {
                Logger.LogError("Yoke model could not be loaded, the mod is disabled: " + e.Message);
                return;
            }
            Patches.Apply(new HarmonyLib.Harmony(PluginGuid));
            ClassInjector.RegisterTypeInIl2Cpp<YokeController>();
            var controller = AddComponent<YokeController>();
            Entry = new Restraint
            {
                Id = "Yoke",
                RingValue = YokeController.RingItemValue,
                Label = () => RingLabel.Value,
                ShortcutSaveKey = Patches.SaveKeyShortcuts,
                Holds = BodyParts.Neck | BodyParts.Arms,
                IsWorn = () => YokeController.Instance != null && YokeController.Instance.Machine.Worn,
                // Like handcuffs: walking and crouching stay, no running, nothing that needs her hands, no pose skills.
                Blocks = () => RestraintHub.Blocks.Running | RestraintHub.Blocks.HandActions | RestraintHub.Blocks.PoseSkills,
                OnChosen = () => { var c = YokeController.Instance; if (c != null) c.OnRingChosen(); },
                Attempt = () => YokeController.Instance == null ? 0f : YokeController.Instance.Machine.Attempt,
                Controls = "+/- width",
            };
            Hub.Register(Entry);
            Logger.LogInfo("[SFM-ACCEPT] load: " + PluginName + " " + PluginVersion + " ready (" + YokeController.Model.Parts.Count + " parts)");
        }

        private static YokeModel LoadModel()
        {
            using Stream s = typeof(Plugin).Assembly.GetManifestResourceStream(ModelResource)
                             ?? throw new FileNotFoundException("embedded resource " + ModelResource + " missing");
            return YokeModel.Read(s);
        }
    }
}
