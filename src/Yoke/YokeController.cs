using System;
using System.Globalization;
using ExposureUnnoticed2.Master.Action;
using ExposureUnnoticed2.Object3D.Player.Scripts;
using ExposureUnnoticed2.Scripts.InGame;
using ExposureUnnoticed2.Scripts.Sound;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Attributes;
using SfmKit.Game;
using SfmKit.Pure;
using UnityEngine;
using Yoke.Logic;

namespace Yoke
{
    /// <summary>
    /// Owns the yoke: lock state, width, the arm pose and the model. The ring entry, number shortcuts, F / + / - routing and the
    /// input blocking are RestraintHub's (<see cref="Plugin.Entry"/>). Bridge diagnostics (session started with
    /// <c>--allow-invoke</c>): <c>game call Yoke.YokeController LockHere</c> / <c>Release</c>, <c>game get Yoke.YokeController::Status</c>.
    /// </summary>
    public sealed class YokeController : MonoBehaviour
    {
        internal static YokeModel Model;
        internal static YokeController Instance;
        public YokeController(IntPtr pointer) : base(pointer) { }

        public const int RingItemValue = 9300; // SpreaderBar 9100, WallRestraint 9200
        internal readonly LockMachine Machine = new LockMachine();
        private static int _failures;

        /// <summary>Live state for diagnostics: fit, width, both arms' cuff place / forearm tilt / miss.</summary>
        public static string Status = "";

        /// <summary>Diagnostics: how far the neck ring is turned with her neck (degrees, as last drawn).</summary>
        public static float NeckDeg;

        internal static void Fail(string where, Exception e)
        {
            if (_failures++ < 20) Plugin.Logger.LogWarning(where + " failed: " + e);
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            try { Tick(); }
            catch (Exception e) { Fail("Update", e); }
        }

        [HideFromIl2Cpp]
        private void Tick()
        {
            if (PlayerController.Instance == null || GameState.GameStateData == null) return;
            WatchSize();
            WatchWidthKeys();
            Machine.AttemptSeconds = Plugin.AttemptSeconds.Value; // live: config edits apply without a restart
            Machine.BaseChance = Plugin.SuccessChance.Value;
            // F counts only while the yoke is the selected restraint, outside menus and interaction areas (the hub decides).
            bool unlockAllowed = Plugin.Entry.StruggleHeld && Machine.State == LockState.Locked;
            switch (Machine.Tick(Time.deltaTime, unlockAllowed, UnityEngine.Random.value))
            {
                case LockMachine.Event.Locked:
                    GameUi.PlaySe(InGameSoundManager.SeType.AttachHandcuffs);
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] lock: yoke locked");
                    break;
                case LockMachine.Event.AttemptFailed:
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] attempt: failed, chance now " + F(Machine.Chance));
                    break;
                case LockMachine.Event.Unlocking:
                    GameUi.PlaySe(InGameSoundManager.SeType.Unlock);
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] attempt: success after " + Machine.Failures + " failures");
                    break;
                case LockMachine.Event.Unlocked:
                    Release();
                    Plugin.Logger.LogInfo("[SFM-ACCEPT] release: struggled free");
                    break;
            }
        }

        // --- ring choice -----------------------------------------------------------------------------------------------------

        /// <summary>The ring entry or a number key, when the yoke is off and nothing worn holds her neck or arms (the hub checked).</summary>
        [HideFromIl2Cpp]
        internal void OnRingChosen()
        {
            string result = LockHere();
            if (Machine.Worn) GameUi.PlaySe(InGameSoundManager.SeType.CommonEquip);
            else GameUi.Message("Can't put the yoke on: " + (result.StartsWith("refused: ") ? result.Substring(9) : result));
        }

        // --- save identity ----------------------------------------------------------------------------------------------------

        [HideFromIl2Cpp]
        internal void OnSaveLoaded(bool locked, float chance)
        {
            Release();
            if (locked)
            {
                Machine.Restore(true, chance);
                _measured = false; // measured on her body in the next LateUpdate
            }
        }

        /// <summary>A new game or the title screen replaces the game data without a load (RestraintHub's OnNewGame): off.</summary>
        [HideFromIl2Cpp]
        internal void OnNewGame()
        {
            if (Machine.Worn) Release();
        }

        // --- lock / release -------------------------------------------------------------------------------------------------

        private YokeRig _rig;
        private YokePoser.Body _body;
        private float _scale;
        private bool _measured;

        /// <summary>Put the yoke on (the ring entry; also a bridge diagnostic - which skips the hub's check for restraints that
        /// hold the same parts).</summary>
        public static string LockHere()
        {
            try
            {
                var c = Instance;
                var player = PlayerController.Instance;
                if (c == null || player == null || player.Pca == null || player.Pca.Animator == null || GameState.GameStateData == null) return "not in game";
                if (c.Machine.Worn) return "already on";
                if (!c.Measure(player.Pca.Animator, out string problem)) return "refused: " + problem;
                // Pose skills need her arms (hands behind the head ...) and cannot be chosen while the yoke is on: end one that runs.
                if (InPose(player)) player.Pca.PlayerAction.ChangeAction(ActionType.None);
                c.Machine.RequestLock();
                Plugin.Logger.LogInfo("[SFM-ACCEPT] lock: " + Status);
                return "locked: " + Status;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("lock failed: " + e);
                return "failed: " + e.Message;
            }
        }

        public static string Release()
        {
            var c = Instance;
            if (c == null) return "no controller";
            bool was = c.Machine.Worn;
            c.Machine.Reset();
            c._measured = false;
            YokePoser.ForgetDrawn();
            c._rig?.SetVisible(false);
            if (was) Plugin.Logger.LogInfo("[SFM-ACCEPT] release: yoke off");
            return "released";
        }

        /// <summary>Her body and the yoke's size; refuses when her arms cannot reach the cuffs at the configured width.</summary>
        [HideFromIl2Cpp]
        private bool Measure(Animator anim, out string problem)
        {
            if (!YokePoser.Measure(anim, out _body, out problem)) return false;
            // The yoke's size from her neck with a fixed allowance; the neck ring itself is fitted on its own (NeckFit), so tightening
            // it does not move the cuffs.
            _scale = YokePose.Scale(Model, _body.NeckRadius, SizeClearance, Plugin.Size.Value);
            float width = Width();
            var fit = Fit(anim, width);
            if (Math.Max(fit.L.Miss, fit.R.Miss) > MaxMiss)
            {
                problem = "her arms can't reach the cuffs at this width (Yoke.Width " + F(width) + ")";
                return false;
            }
            if (_rig == null || !_rig.Built)
            {
                _rig = new YokeRig(Model);
                _rig.Build(anim.gameObject.layer);
            }
            _rig.SetVisible(true);
            _measured = true;
            Describe(fit, width);
            return true;
        }

        private const float MaxMiss = 0.01f; // m the upper arm may be off the elbow target before a width counts as out of reach

        [HideFromIl2Cpp]
        private (ArmPose L, ArmPose R) Fit(Animator anim, float width)
        {
            var frame = YokePoser.Frame(anim, Plugin.NeckRingAt.Value, _scale);
            return YokePoser.Solve(anim, Model, frame, width, CuffFit(), Plugin.CuffMaxAngle.Value, _body, Plugin.CuffOnForearm.Value);
        }

        [HideFromIl2Cpp]
        private void Describe((ArmPose L, ArmPose R) fit, float width)
        {
            var at = Model.AtWidth(width);
            string Arm(ArmPose a) => "cuff at " + F(a.Along) + " of the forearm, lean " + F(a.LeanDeg) + " deg, cuff turned " + F(CuffDeg(a))
                                      + " deg, forearm to cuff " + F(a.TiltDeg) + " deg, miss " + F(a.Miss);
            Status = "scale " + F(_scale) + " (neck r " + F(_body.NeckFitRadius) + ", neck ring hole r " + F(Model.Neck.InnerRadius * _scale * NeckFit()) + ", wrist r "
                     + F(WristRadius) + ", cuff fit " + F(CuffFit()) + ", cuff hole r " + F(Model.Cuffs[1].InnerRadius * _scale * CuffFit()) + "), upper arm "
                     + F(_body.UpperArm) + " forearm " + F(_body.Forearm) + "; width " + F(width) + " (cuffs " + F(at.CuffR.X * _scale * 2f)
                     + " m apart); left: " + Arm(fit.L) + "; right: " + Arm(fit.R);
        }

        private static float Width() => Math.Max(Plugin.Width.Value, Model.MinWidth());

        private static float CuffDeg(ArmPose a) => 2f * (float)(Math.Acos(Math.Clamp(Math.Abs(a.CuffTilt.W), 0f, 1f)) * 180.0 / Math.PI);

        // --- wrist fit: the cuffs sized to her wrists -----------------------------------------------------------------------------
        // Estimated from the forearm (7 %, like the neck from the spine; 10 % left the cuffs visibly loose). Measuring her skin like SpreaderBar's ankle cuffs does
        // not work here: the baked mesh is off her arms by 10 cm and more (1 cm at the feet, 25 cm at the shoulder) - her
        // body settings move the bones after the bake can see them [observed 2026-10-24].
        private const float WristPerForearm = 0.07f;

        /// <summary>The cuffs' scale about their joints: the hole <see cref="Plugin.CuffClearanceCm"/> wider than her wrist,
        /// never larger than modelled.</summary>
        private float CuffFit()
        {
            if (_scale <= 0f || _body.Forearm <= 0f) return 1f;
            return LimbGirth.CuffScale(WristRadius, Plugin.CuffClearanceCm.Value / 100f, Model.Cuffs[1].InnerRadius * _scale, 0.3f, 1f);
        }

        private float WristRadius => WristPerForearm * _body.Forearm;

        private const float SizeClearance = 0.006f; // m: the yoke is sized as if the ring had 0.6 cm room (0.2.0 behaviour), whatever its fit

        /// <summary>The neck ring's scale about its centre: the hole <see cref="Plugin.NeckClearanceCm"/> wider than her neck.</summary>
        private float NeckFit()
        {
            if (_scale <= 0f || _body.NeckFitRadius <= 0f) return 1f;
            return LimbGirth.CuffScale(_body.NeckFitRadius, Plugin.NeckClearanceCm.Value / 100f, Model.Neck.InnerRadius * _scale, 0.3f, 1.5f);
        }

        /// <summary>
        /// Measured in Update, where the bones have the lengths they were drawn with (the game changes them after LateUpdate;
        /// measuring in LateUpdate and Update would disagree). After a load she is measured before her body settings apply
        /// (neck 3.9 instead of 4.9 cm: the yoke too small) [observed 2026-10-24], so the yoke is sized again whenever her
        /// bones change.
        /// </summary>
        [HideFromIl2Cpp]
        private void WatchSize()
        {
            if (!Machine.Worn) return;
            var player = PlayerController.Instance;
            var anim = player.Pca == null ? null : player.Pca.Animator;
            if (anim == null) return;
            if (_measured)
            {
                if (Time.frameCount % 60 != 0 || !SizeChanged(anim)) return;
                Plugin.Logger.LogInfo("[SFM-ACCEPT] resize: her body changed, measuring again");
            }
            if (Measure(anim, out string problem)) return;
            // A loaded save with the yoke on, but her arms cannot reach (body changed, width edited): take it off.
            Plugin.Logger.LogWarning("yoke taken off: " + problem);
            GameUi.Message("The yoke was taken off: " + problem + ".");
            Release();
        }

        /// <summary>Her bone lengths differ by more than 2 % from the measurement.</summary>
        [HideFromIl2Cpp]
        private bool SizeChanged(Animator anim)
        {
            if (!YokePoser.Measure(anim, out var now, out _)) return false;
            return Math.Abs(now.UpperArm - _body.UpperArm) > 0.02f * _body.UpperArm || Math.Abs(now.NeckRadius - _body.NeckRadius) > 0.02f * _body.NeckRadius;
        }

        /// <summary>She is in a pose skill (action types 50000 and up: Spread-Leg Walk 50000, Armpit Squat 50008) [observed 2026-10-11 / 2026-10-24].</summary>
        internal static bool InPose(PlayerController player)
        {
            var action = player == null || player.Pca == null || player.Pca.PlayerState == null ? null : player.Pca.PlayerState.CurrentAction;
            return action != null && (int)action.Type >= (int)ActionType.GanimataWalk;
        }

        // --- width: + / - while it is on -------------------------------------------------------------------------------------

        private string _said;
        private float _saidAt = -10f;

        /// <summary>A message, but not the same one again while the key repeats (one line per repeat stacked up).</summary>
        [HideFromIl2Cpp]
        private void Say(string text)
        {
            if (text == _said && Time.unscaledTime - _saidAt < 1.5f) return;
            _said = text;
            _saidAt = Time.unscaledTime;
            GameUi.Message(text);
        }

        [HideFromIl2Cpp]
        private void WatchWidthKeys()
        {
            // + / - reach the yoke only while it is the selected restraint (the hub routes them, outside menus).
            int dir = Plugin.Entry.TakeStep();
            if (dir == 0 || Machine.State != LockState.Locked || !_measured) return;
            var anim = PlayerController.Instance.Pca.Animator;
            float now = Width();
            float min = Model.MinWidth(), max = 1.6f;
            float want = Math.Min(Math.Max(now + dir * Plugin.StepPercent.Value / 100f, min), max);
            if (Math.Abs(want - now) < 1e-4f)
            {
                Say(dir > 0 ? "The yoke is as wide as it goes." : "The yoke is as narrow as it goes.");
                return;
            }
            var fit = Fit(anim, want);
            if (dir > 0 && Math.Max(fit.L.Miss, fit.R.Miss) > MaxMiss)
            {
                Say("Her arms can't reach any further.");
                return;
            }
            if (dir > 0)
            {
                // No wider once the forearms would lie across the cuffs - the cuffs at their swivel limit (unless they already
                // do: the yoke went on wider). Fixed cuffs (CuffMaxAngle 0) allow a 45 deg lean [observed 2026-10-24: 84 deg at
                // the full reach went through the ring's wall].
                var before = Fit(anim, now);
                float limit = Plugin.CuffMaxAngle.Value > 0f ? YokePose.MaxTiltDeg : 45f;
                float tilt = Math.Max(fit.L.TiltDeg, fit.R.TiltDeg), tiltBefore = Math.Max(before.L.TiltDeg, before.R.TiltDeg);
                if (tilt > Math.Max(limit, tiltBefore))
                {
                    Say("Any wider and her wrists would not fit the cuffs.");
                    return;
                }
            }
            Plugin.Width.Value = want; // applied from the next frame and remembered (saved to the config)
            Describe(fit, want);
            Plugin.Logger.LogInfo("[SFM-ACCEPT] width: " + Status);
        }

        // --- the pose (LateUpdate) and the model (onPreCull) -------------------------------------------------------------------

        private Camera.CameraCallback _preCull;
        private int _placedFrame = -1;

        private void Start()
        {
            _preCull = DelegateSupport.ConvertDelegate<Camera.CameraCallback>(new Action<Camera>(OnPreCull));
            Camera.onPreCull = Camera.onPreCull == null
                ? _preCull
                : Il2CppSystem.Delegate.Combine(Camera.onPreCull, _preCull).Cast<Camera.CameraCallback>();
        }

        private void OnDestroy()
        {
            if (_preCull != null && Camera.onPreCull != null)
                Camera.onPreCull = Il2CppSystem.Delegate.Remove(Camera.onPreCull, _preCull)?.Cast<Camera.CameraCallback>();
            _rig?.Destroy();
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            if (!Machine.Worn) return;
            try
            {
                var player = PlayerController.Instance;
                var anim = player == null || player.Pca == null ? null : player.Pca.Animator;
                if (anim == null || !_measured || (Plugin.DebugDisable.Value & 1) != 0) return;
                YokePoser.UseDrawnLengths(anim, (Plugin.DebugDisable.Value & 2) == 0);
                var fit = Fit(anim, Width());
                YokePoser.Apply(anim, fit.L, fit.R);
                YokePoser.RestoreAnimatedLengths(anim);
                if (Time.frameCount % 30 == 0) Describe(fit, Width());
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError("holding the arms failed, yoke off: " + e);
                Release();
            }
        }

        [HideFromIl2Cpp]
        private void OnPreCull(Camera cam)
        {
            try
            {
                if (!Machine.Worn || !_measured || _placedFrame == Time.frameCount) return;
                // The yoke follows the bones as drawn (they still move after LateUpdate) [observed 2026-10-24, WallRestraint].
                _placedFrame = Time.frameCount;
                var player = PlayerController.Instance;
                var anim = player == null || player.Pca == null ? null : player.Pca.Animator;
                if (anim == null) return;
                YokePoser.RecordDrawn(anim);
                // The cuffs turn with the forearms as drawn, so the visible cuff follows the visible arm.
                var frame = YokePoser.Frame(anim, Plugin.NeckRingAt.Value, _scale);
                float maxCuff = Plugin.CuffMaxAngle.Value;
                var neckTilt = YokePoser.DrawnNeckTilt(anim, frame, Plugin.NeckRingMaxAngle.Value);
                NeckDeg = 2f * (float)(Math.Acos(Math.Clamp(Math.Abs(neckTilt.W), 0f, 1f)) * 180.0 / Math.PI);
                _rig.Place(frame, Width(), NeckFit(), CuffFit(), neckTilt, YokePoser.DrawnCuffTilt(anim, frame, 0, maxCuff), YokePoser.DrawnCuffTilt(anim, frame, 1, maxCuff));
            }
            catch (Exception e) { Fail("placing the yoke", e); }
        }

        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
