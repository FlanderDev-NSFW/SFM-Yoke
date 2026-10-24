using System;
using System.Globalization;
using System.Reflection;
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
    /// Owns the yoke: ring entry, lock state, width keys, the arm pose and the model. Bridge diagnostics (session started with
    /// <c>--allow-invoke</c>): <c>game call Yoke.YokeController LockHere</c> / <c>Release</c>, <c>game get Yoke.YokeController::Status</c>.
    /// </summary>
    public sealed class YokeController : MonoBehaviour
    {
        internal static YokeModel Model;
        internal static YokeController Instance;
        public YokeController(IntPtr pointer) : base(pointer) { }

        public const int RingItemValue = 9300; // SpreaderBar 9100, WallRestraint 9200
        internal RingEntry Ring;
        internal readonly LockMachine Machine = new LockMachine();
        internal bool InteractHeld;
        private readonly GameUi _ui = new GameUi();
        private readonly PlusMinusKeys _widthKeys = new PlusMinusKeys();
        private IntPtr _gameData;
        private static int _failures;

        /// <summary>Live state for diagnostics: fit, width, both arms' cuff place / forearm tilt / miss.</summary>
        public static string Status = "";

        internal static void Fail(string where, Exception e)
        {
            if (_failures++ < 20) Plugin.Logger.LogWarning(where + " failed: " + e);
        }

        private void Awake()
        {
            Instance = this;
            Ring = new RingEntry(RingItemValue, Patches.LabelKey, OnRingChosen, Plugin.Logger, "yoke");
        }

        private void Update()
        {
            try { Tick(); }
            catch (Exception e) { Fail("Update", e); }
        }

        [HideFromIl2Cpp]
        private void Tick()
        {
            bool panel = _ui.PanelOpen();
            Ring.Tick(panel);
            TrackSaveIdentity();
            if (PlayerController.Instance == null || GameState.GameStateData == null) return;
            WatchSize();
            WatchWidthKeys(panel);
            Machine.AttemptSeconds = Plugin.AttemptSeconds.Value; // live: config edits apply without a restart
            Machine.BaseChance = Plugin.SuccessChance.Value;
            // Interactions take precedence: no struggling inside an interaction area, in menus or with the ring open.
            bool unlockAllowed = InteractHeld && Machine.State == LockState.Locked && !panel && !GameUi.RingOpen() && !_ui.InInteractArea();
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

        // --- ring choice, refusals ------------------------------------------------------------------------------------------

        [HideFromIl2Cpp]
        private void OnRingChosen()
        {
            if (Machine.Worn)
            {
                GameUi.Message("The yoke is already on - hold F to struggle free.");
                return;
            }
            string result = LockHere();
            if (Machine.Worn) GameUi.PlaySe(InGameSoundManager.SeType.CommonEquip);
            else GameUi.Message("Can't put the yoke on: " + (result.StartsWith("refused: ") ? result.Substring(9) : result));
        }

        private float _refusedAt = -10f;

        /// <summary>The input hook dropped something that needs her hands.</summary>
        internal void OnRefused()
        {
            if (Time.unscaledTime - _refusedAt < 1f) return;
            _refusedAt = Time.unscaledTime;
            GameUi.Message("Can't do that with the yoke on.");
            Plugin.Logger.LogInfo("[SFM-ACCEPT] refused: an action that needs her hands");
        }

        // --- save identity ----------------------------------------------------------------------------------------------------

        [HideFromIl2Cpp]
        internal void OnSaveLoaded(bool locked, float chance, int shortcuts)
        {
            Release();
            if (locked)
            {
                Machine.Restore(true, chance);
                _measured = false; // measured on her body in the next LateUpdate
            }
            Ring.OnShortcutsLoaded(shortcuts);
            _gameData = GameState.GameStateData == null ? IntPtr.Zero : GameState.GameStateData.Pointer;
        }

        /// <summary>A new game or the title screen replaces the game data without a load: off, forget the keys.</summary>
        [HideFromIl2Cpp]
        private void TrackSaveIdentity()
        {
            var data = GameState.GameStateData;
            IntPtr ptr = data == null ? IntPtr.Zero : data.Pointer;
            if (ptr == _gameData) return;
            _gameData = ptr;
            if (Machine.Worn) Release();
            Ring.Reset();
        }

        // --- lock / release -------------------------------------------------------------------------------------------------

        private YokeRig _rig;
        private YokePoser.Body _body;
        private float _scale;
        private bool _measured;

        /// <summary>Put the yoke on (the ring entry; also a bridge diagnostic).</summary>
        public static string LockHere()
        {
            try
            {
                var c = Instance;
                var player = PlayerController.Instance;
                if (c == null || player == null || player.Pca == null || player.Pca.Animator == null || GameState.GameStateData == null) return "not in game";
                if (c.Machine.Worn) return "already on";
                if (GameStateFacade.IsAttachSomeHandcuffs() || GameState.GameStateData.PlayerIsAttachMapHandcuffs) return "refused: her hands are cuffed";
                if (WallRestraintWorn()) return "refused: she is restrained to a wall";
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
            _scale = YokePose.Scale(Model, _body.NeckRadius, Plugin.NeckClearanceCm.Value / 100f, Plugin.Size.Value);
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
            var at = Model.AtWidth(width);
            float half = (Model.Cuffs[1].Top - Model.Cuffs[1].Bottom) / 2f * _scale;
            return YokePoser.Solve(anim, frame, at.CuffL, at.CuffR, _body, Plugin.CuffOnForearm.Value, half);
        }

        [HideFromIl2Cpp]
        private void Describe((ArmPose L, ArmPose R) fit, float width)
        {
            var at = Model.AtWidth(width);
            Status = "scale " + F(_scale) + " (neck r " + F(_body.NeckRadius) + ", neck ring hole r " + F(Model.Neck.InnerRadius * _scale) + ", cuff hole r "
                     + F(Model.Cuffs[1].InnerRadius * _scale) + "), upper arm " + F(_body.UpperArm) + " forearm " + F(_body.Forearm) + "; width " + F(width)
                     + " (cuffs " + F(at.CuffR.X * _scale * 2f) + " m apart); left: cuff at " + F(fit.L.Along) + " of the forearm, tilt " + F(fit.L.TiltDeg)
                     + " deg, miss " + F(fit.L.Miss) + "; right: " + F(fit.R.Along) + ", " + F(fit.R.TiltDeg) + " deg, " + F(fit.R.Miss);
        }

        private static float Width() => Math.Max(Plugin.Width.Value, Model.MinWidth());

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

        /// <summary>WallRestraint holds her arms too: no yoke while it is on (soft dependency by reflection, shape checked).</summary>
        private static bool WallRestraintWorn()
        {
            try
            {
                var type = Type.GetType("WallRestraint.WallController, WallRestraint", false);
                var inst = type?.GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null);
                var machine = inst == null ? null : type.GetField("Machine", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(inst);
                // its own copy of SfmKit's LockMachine (another type than ours): read Worn by name
                var worn = machine?.GetType().GetProperty("Worn");
                return worn != null && worn.PropertyType == typeof(bool) && (bool)worn.GetValue(machine);
            }
            catch (Exception) { return false; }
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
        private void WatchWidthKeys(bool panel)
        {
            if (Machine.State != LockState.Locked || !_measured || panel || GameUi.RingOpen()) return;
            int dir = _widthKeys.Poll();
            if (dir == 0) return;
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
                // No wider once the forearms would lie across the cuffs (unless they already do: the yoke went on wider).
                var before = Fit(anim, now);
                float tilt = Math.Max(fit.L.TiltDeg, fit.R.TiltDeg), tiltBefore = Math.Max(before.L.TiltDeg, before.R.TiltDeg);
                if (tilt > Math.Max(YokePose.MaxTiltDeg, tiltBefore))
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
                _rig.Place(YokePoser.Frame(anim, Plugin.NeckRingAt.Value, _scale), Width());
            }
            catch (Exception e) { Fail("placing the yoke", e); }
        }

        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
