using System.Collections;
using Cali;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>
    /// Fades UI and moves the camera between Main / Host / Join / Settings.
    /// Optional start intro: play AnimationClip on the menu camera, then show main menu.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class CaliMenuFlowDirector : MonoBehaviour
    {
        public static CaliMenuFlowDirector Instance { get; private set; }

        public CaliMenuCameraRig cameraRig;
        public float fadeDuration = 0.35f;

        [Header("Intro")]
        public bool playIntroOnStart = true;
        public AnimationClip introCameraClip;
        [Tooltip("0 = use clip length. Otherwise wait this many seconds after starting the clip.")]
        public float introDurationOverride = 0f;
        [Tooltip("After the intro clip, seconds to ease the camera to CamAnchor_Main.")]
        public float introMoveToAnchorDuration = 1.5f;
        public float screenFadeDuration = 3f;

        GameObject _mainRoot;
        GameObject _hostRoot;
        GameObject _joinRoot;
        GameObject _settingsRoot;

        CanvasGroup _mainGroup;
        CanvasGroup _hostGroup;
        CanvasGroup _joinGroup;
        CanvasGroup _settingsGroup;

        CaliHostSetupPanel _hostPanel;
        CaliJoinBrowserPanel _joinPanel;
        CaliSettingsMenu _settingsMenu;

        CaliMenuState _state = CaliMenuState.Main;
        bool _busy;
        Coroutine _flow;

        public CaliMenuState State => _state;
        public bool IsBusy => _busy;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Initialize(
            GameObject mainRoot,
            CaliHostSetupPanel host,
            CaliJoinBrowserPanel join,
            CaliSettingsMenu settings,
            CaliMenuCameraRig rig)
        {
            cameraRig = rig;
            _mainRoot = mainRoot;
            _hostPanel = host;
            _joinPanel = join;
            _settingsMenu = settings;

            _hostRoot = host != null ? ResolveCanvasRoot(host.gameObject) : null;
            _joinRoot = join != null ? ResolveCanvasRoot(join.gameObject) : null;
            _settingsRoot = settings != null ? ResolveCanvasRoot(settings.gameObject) : null;

            _mainGroup = EnsureGroup(_mainRoot);
            _hostGroup = EnsureGroup(_hostRoot);
            _joinGroup = EnsureGroup(_joinRoot);
            _settingsGroup = EnsureGroup(_settingsRoot);

            WireBackButtons();
            PrepareWorldSpace();
            SilenceCompetingDrivers(cameraRig != null && cameraRig.menuCamera != null
                ? cameraRig.menuCamera.gameObject
                : null);
            HoldIntroHorses();

            SetPanelActive(_hostRoot, false);
            SetPanelActive(_joinRoot, false);
            SetPanelActive(_settingsRoot, false);
            SetPanelActive(_mainRoot, true);

            _state = CaliMenuState.Main;

            if (playIntroOnStart)
            {
                if (_mainGroup != null)
                {
                    _mainGroup.alpha = 0f;
                    _mainGroup.interactable = false;
                    _mainGroup.blocksRaycasts = false;
                }

                _busy = true;
                if (_flow != null)
                    StopCoroutine(_flow);
                _flow = StartCoroutine(PlayIntroRoutine());
            }
            else
            {
                if (_mainGroup != null)
                {
                    _mainGroup.alpha = 1f;
                    _mainGroup.interactable = true;
                    _mainGroup.blocksRaycasts = true;
                }

                if (cameraRig != null)
                    cameraRig.SnapTo(CaliMenuState.Main);
            }
        }

        public void GoTo(CaliMenuState target)
        {
            if (_busy)
                return;
            if (_flow != null)
                StopCoroutine(_flow);
            _flow = StartCoroutine(GoToRoutine(target));
        }

        public void GoToMain() => GoTo(CaliMenuState.Main);
        public void GoToHost() => GoTo(CaliMenuState.Host);
        public void GoToJoin() => GoTo(CaliMenuState.Join);
        public void GoToSettings() => GoTo(CaliMenuState.Settings);

        IEnumerator PlayIntroRoutine()
        {
            _busy = true;
            SetInteractable(false);

            var camGo = cameraRig != null && cameraRig.menuCamera != null
                ? cameraRig.menuCamera.gameObject
                : null;

            SilenceCompetingDrivers(camGo);
            WarnIfClipUnusable();
            yield return WaitUntilScreenVisible();
            StartIntroHorses();

            if (introCameraClip != null && camGo != null)
                yield return PlayCameraClip(introCameraClip, camGo);
            else if (cameraRig != null)
                cameraRig.SnapTo(CaliMenuState.Main);

            if (cameraRig != null)
                yield return cameraRig.MoveTo(CaliMenuState.Main, this, introMoveToAnchorDuration);

            if (_mainRoot != null)
            {
                SetPanelActive(_mainRoot, true);
                if (_mainGroup != null)
                {
                    _mainGroup.alpha = 0f;
                    yield return Fade(_mainGroup, 0f, 1f);
                }
            }

            _state = CaliMenuState.Main;
            SetInteractable(true);
            _busy = false;
            _flow = null;
        }

        IEnumerator PlayCameraClip(AnimationClip clip, GameObject cameraGo)
        {
            if (clip == null || cameraGo == null)
                yield break;

            float wait = introDurationOverride > 0.05f
                ? introDurationOverride
                : Mathf.Max(0.05f, clip.length);

            var animator = cameraGo.GetComponent<Animator>();
            if (animator == null)
                animator = cameraGo.AddComponent<Animator>();
            animator.enabled = true;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.runtimeAnimatorController = null;

            var graph = PlayableGraph.Create("CaliMenuIntroCam");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "Camera", animator);
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetDuration(wait);
            output.SetSourcePlayable(playable);

            var camTx = cameraGo.transform;
            float t = 0f;
            bool skipHitch = true;
            DriveIntroHorses(0f);
            while (t < wait)
            {
                playable.SetTime(t);
                graph.Evaluate();
                DriveIntroHorses(t);
                yield return null;
                float dt = Time.unscaledDeltaTime;
                if (skipHitch)
                {
                    skipHitch = false;
                    dt = 0f;
                }
                else if (dt > 0.1f)
                    dt = 0.1f;
                t += dt;
            }

            playable.SetTime(wait);
            graph.Evaluate();
            Vector3 endPos = camTx.position;
            Quaternion endRot = camTx.rotation;
            graph.Destroy();
            animator.enabled = false;
            camTx.SetPositionAndRotation(endPos, endRot);
        }

        static void SilenceCompetingDrivers(GameObject camGo)
        {
            if (camGo == null)
                return;

            var director = camGo.GetComponent<PlayableDirector>();
            if (director != null)
            {
                director.playOnAwake = false;
                director.timeUpdateMode = DirectorUpdateMode.Manual;
                director.Stop();
                director.enabled = false;
            }

            var existing = camGo.GetComponent<Animator>();
            if (existing != null && existing.runtimeAnimatorController != null)
                existing.enabled = false;
        }

        static IEnumerator WaitUntilScreenVisible()
        {
            var fader = CaliScreenFader.Instance;
            if (fader == null)
                yield break;

            float waited = 0f;
            while (fader.IsFading || fader.Alpha > 0.05f)
            {
                waited += Time.unscaledDeltaTime;
                if (waited > 4f)
                    break;
                yield return null;
            }
        }

        static void HoldIntroHorses()
        {
            var paths = FindIntroHorses();
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i] != null)
                    paths[i].HoldAtSpawn();
            }
        }

        static void StartIntroHorses()
        {
            var paths = FindIntroHorses();
            for (int i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                if (path == null)
                    continue;
                path.Play();
                var life = path.GetComponent<DestroyAfterSeconds>();
                if (life != null)
                    life.Play();
            }
        }

        static void DriveIntroHorses(float elapsed)
        {
            var paths = FindIntroHorses();
            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i] != null)
                    paths[i].SetElapsed(elapsed);
            }
        }

        static RootMotionPositionOnly[] FindIntroHorses()
        {
            return Object.FindObjectsByType<RootMotionPositionOnly>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        }

        void WarnIfClipUnusable()
        {
            if (!playIntroOnStart || introCameraClip == null)
                return;

            if (introCameraClip.empty)
            {
                Debug.LogWarning(
                    $"[Cali Menu] Intro clip '{introCameraClip.name}' has no keyframes — record camera " +
                    "position/rotation keys in the Animation window, ending at CamAnchor_Main.",
                    introCameraClip);
            }
        }

        /// <summary>
        /// The rig and clip sampling both write the camera transform directly, so any Animator
        /// playing on the camera must be switched off or it wins every frame.
        /// </summary>
        void ReleaseCameraFromAnimators(GameObject camGo)
        {
            if (camGo == null)
                return;

            var animator = camGo.GetComponent<Animator>();
            if (animator != null && animator.enabled)
            {
                animator.enabled = false;
                Debug.LogWarning(
                    $"[Cali Menu] Disabled Animator on '{camGo.name}' — the menu camera is driven by " +
                    "CaliMenuCameraRig and the intro clip. Put the Animator on a separate object if you need it.",
                    camGo);
            }
        }

        IEnumerator GoToRoutine(CaliMenuState target)
        {
            if (target == _state && !_busy)
                yield break;

            _busy = true;
            SetInteractable(false);

            var fromRoot = RootFor(_state);
            var fromGroup = GroupFor(_state);
            var toRoot = RootFor(target);
            var toGroup = GroupFor(target);

            // Fade out current
            if (fromGroup != null && fromRoot != null && fromRoot.activeInHierarchy)
                yield return Fade(fromGroup, fromGroup.alpha, 0f);

            if (fromRoot != null && fromRoot != toRoot)
                SetPanelActive(fromRoot, false);

            // Notify panel Hide for cleanup (join lobby shutdown etc.) without fighting visibility
            if (_state == CaliMenuState.Join && _joinPanel != null && target != CaliMenuState.Join)
                _joinPanel.NotifyClosedByDirector();
            if (_state == CaliMenuState.Host && _hostPanel != null && target != CaliMenuState.Host)
                _hostPanel.NotifyClosedByDirector();
            if (_state == CaliMenuState.Settings && _settingsMenu != null && target != CaliMenuState.Settings)
                _settingsMenu.NotifyClosedByDirector();

            // Camera move
            if (cameraRig != null)
                yield return cameraRig.MoveTo(target, this);

            // Show destination
            if (toRoot != null)
            {
                SetPanelActive(toRoot, true);
                if (toGroup != null)
                {
                    toGroup.alpha = 0f;
                    toGroup.interactable = false;
                    toGroup.blocksRaycasts = false;
                }

                if (target == CaliMenuState.Host && _hostPanel != null)
                    _hostPanel.NotifyOpenedByDirector();
                else if (target == CaliMenuState.Join && _joinPanel != null)
                    _joinPanel.NotifyOpenedByDirector();
                else if (target == CaliMenuState.Settings && _settingsMenu != null)
                    _settingsMenu.NotifyOpenedByDirector();

                if (toGroup != null)
                {
                    toGroup.alpha = 0f;
                    toGroup.interactable = false;
                    toGroup.blocksRaycasts = false;
                    yield return Fade(toGroup, 0f, 1f);
                }
            }

            _state = target;
            SetInteractable(true);
            _busy = false;
            _flow = null;
        }

        void WireBackButtons()
        {
            if (_hostPanel != null)
                _hostPanel.SetBackHandler(GoToMain);
            if (_joinPanel != null)
                _joinPanel.SetBackHandler(GoToMain);
            if (_settingsMenu != null)
                _settingsMenu.SetBackHandler(GoToMain);
        }

        void PrepareWorldSpace()
        {
            // Only ensure world-space rendering — never move/scale panels.
            // Place MainMenuRoot / HostSetupRoot / etc. yourself; aim CamAnchor_* separately.
            ConfigureWorldCanvas(_mainRoot, cameraRig != null ? cameraRig.menuCamera : null);
            ConfigureWorldCanvas(_hostRoot, cameraRig != null ? cameraRig.menuCamera : null);
            ConfigureWorldCanvas(_joinRoot, cameraRig != null ? cameraRig.menuCamera : null);
            ConfigureWorldCanvas(_settingsRoot, cameraRig != null ? cameraRig.menuCamera : null);

            DisableBlockers(_hostRoot);
            DisableBlockers(_joinRoot);
            DisableBlockers(_settingsRoot);
        }

        static void ConfigureWorldCanvas(GameObject root, Camera cam)
        {
            if (root == null)
                return;

            var canvas = root.GetComponent<Canvas>();
            if (canvas == null)
                canvas = root.GetComponentInChildren<Canvas>(true);
            if (canvas == null)
                return;

            var tx = canvas.transform;
            Vector3 pos = tx.position;
            Quaternion rot = tx.rotation;
            Vector3 scale = tx.localScale;
            var rt = canvas.transform as RectTransform;
            Vector2 size = rt != null ? rt.sizeDelta : Vector2.zero;

            if (canvas.renderMode != RenderMode.WorldSpace)
                canvas.renderMode = RenderMode.WorldSpace;
            if (cam != null)
                canvas.worldCamera = cam;

            tx.SetPositionAndRotation(pos, rot);
            if (scale.sqrMagnitude > 0.0000001f)
                tx.localScale = scale;
            if (rt != null && size.sqrMagnitude > 1f)
                rt.sizeDelta = size;

            var scaler = root.GetComponent<CanvasScaler>();
            if (scaler != null)
                scaler.enabled = false;
        }

        static void DisableBlockers(GameObject root)
        {
            if (root == null)
                return;
            var blocker = FantasyUiFactory.FindDeepChild(root.transform, "Blocker");
            if (blocker != null)
                blocker.gameObject.SetActive(false);
        }

        void SetInteractable(bool on)
        {
            ApplyInteractable(_mainGroup, on && _state == CaliMenuState.Main);
            ApplyInteractable(_hostGroup, on && _state == CaliMenuState.Host);
            ApplyInteractable(_joinGroup, on && _state == CaliMenuState.Join);
            ApplyInteractable(_settingsGroup, on && _state == CaliMenuState.Settings);
        }

        static void ApplyInteractable(CanvasGroup g, bool on)
        {
            if (g == null)
                return;
            g.interactable = on;
            g.blocksRaycasts = on;
        }

        GameObject RootFor(CaliMenuState s) => s switch
        {
            CaliMenuState.Host => _hostRoot,
            CaliMenuState.Join => _joinRoot,
            CaliMenuState.Settings => _settingsRoot,
            _ => _mainRoot,
        };

        CanvasGroup GroupFor(CaliMenuState s) => s switch
        {
            CaliMenuState.Host => _hostGroup,
            CaliMenuState.Join => _joinGroup,
            CaliMenuState.Settings => _settingsGroup,
            _ => _mainGroup,
        };

        IEnumerator Fade(CanvasGroup group, float from, float to)
        {
            yield return Fade(group, from, to, fadeDuration);
        }

        IEnumerator Fade(CanvasGroup group, float from, float to, float duration)
        {
            if (group == null)
                yield break;
            float dur = Mathf.Max(0.05f, duration);
            float t = 0f;
            group.alpha = from;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / dur;
                group.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t));
                yield return null;
            }

            group.alpha = to;
        }

        static void SetPanelActive(GameObject root, bool on)
        {
            if (root != null)
                root.SetActive(on);
        }

        static CanvasGroup EnsureGroup(GameObject root)
        {
            if (root == null)
                return null;
            var g = root.GetComponent<CanvasGroup>();
            if (g == null)
                g = root.AddComponent<CanvasGroup>();
            return g;
        }

        static GameObject ResolveCanvasRoot(GameObject go)
        {
            if (go == null)
                return null;
            if (go.GetComponent<Canvas>() != null)
                return go;
            var c = go.GetComponentInParent<Canvas>();
            return c != null ? c.gameObject : go;
        }
    }
}
