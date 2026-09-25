using Cali.Audio;
using Cali.Network;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Cali.UI
{
    /// <summary>
    /// Fullscreen MP4 intro after host Start. Horses stay locked until the host finishes or skips.
    /// Assign the clip on Resources/Cutscene/CaliIntroCutscene.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class CaliIntroCutscene : MonoBehaviour
    {
        public static CaliIntroCutscene Instance { get; private set; }
        public static bool IsBlocking => Instance != null && Instance._blocking;

        /// <summary>
        /// Set on every peer when the host finishes or skips. Survives a scene load so a
        /// client that is still arriving does not start the MP4 after the host already left it.
        /// </summary>
        static bool _dismissed;

        public CaliIntroCutsceneCatalog catalog;
        [Tooltip("Optional override. Leave empty to use the catalog slot.")]
        public VideoClip videoClip;

        Canvas _canvas;
        RawImage _image;
        TMP_Text _hint;
        VideoPlayer _player;
        AudioSource _audio;
        CaliVideoSurface _surface;
        bool _blocking;
        bool _started;
        bool _finished;
        bool _hostFinishing;
        float _timeoutAt;

        public static CaliIntroCutscene Ensure()
        {
            if (Instance != null)
                return Instance;

            var existing = FindFirstObjectByType<CaliIntroCutscene>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var go = new GameObject("CaliIntroCutscene");
            return go.AddComponent<CaliIntroCutscene>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (catalog == null)
                catalog = CaliIntroCutsceneCatalog.Get();
            BuildOverlay();
            SetVisible(false);
        }

        void OnDestroy()
        {
            if (_player != null)
            {
                _player.loopPointReached -= OnVideoEnded;
                _player.prepareCompleted -= OnPrepared;
            }

            _surface?.Release();
            if (Instance == this)
                Instance = null;
            CaliGameplayAudio.SetSuppressed(false);
        }

        /// <summary>
        /// Stay on black after the play scene loads until the intro overlay is up (or the
        /// host has already skipped). Stops a frame of falling horses before the video.
        /// </summary>
        public static bool HoldWorldFade
        {
            get
            {
                if (IsBlocking)
                    return true;
                if (SceneManager.GetActiveScene().name != CaliMainMenuController.GameSceneName)
                    return false;

                var coop = CoopGameController.Instance;
                if (coop == null || coop.Object == null || !coop.Object.IsValid)
                    return true;

                if (coop.IsMatchPlaying && coop.IntroComplete)
                    return false;

                return true;
            }
        }

        /// <summary>New hosted room — allow the intro to play again.</summary>
        public static void ResetForNewSession()
        {
            _dismissed = false;
            if (Instance != null)
                Instance.ResetPlayback();
        }

        void ResetPlayback()
        {
            _started = false;
            _finished = false;
            _blocking = false;
            _hostFinishing = false;
            if (_player != null && _player.isPlaying)
                _player.Stop();
            SetVisible(false);
            CaliGameplayAudio.SetSuppressed(false);
        }

        /// <summary>Host skip / video end. Safe to call more than once, including before this peer has spawned the overlay.</summary>
        public static void NotifyHostFinished()
        {
            _dismissed = true;
            if (Instance != null)
                Instance.HideLocal();
        }

        void Update()
        {
            var coop = CoopGameController.Instance;
            if (coop == null || !coop.Object || !coop.Object.IsValid)
                return;

            if (coop.IsWaitingForMatch)
                _dismissed = false;

            if (_dismissed || coop.IntroComplete || coop.IsMatchPlaying)
            {
                if (_blocking || (_started && !_finished))
                    HideLocal();
                return;
            }

            if (!coop.IsInIntro)
                return;

            if (SceneManager.GetActiveScene().name != CaliMainMenuController.GameSceneName)
                return;

            if (!_started)
                Begin(coop);

            if (_blocking && CanHostSkip(coop) && WasSkipPressed())
                coop.FinishIntro();

            if (_blocking && Time.unscaledTime >= _timeoutAt)
                RequestHostFinish(coop);
        }

        void Begin(CoopGameController coop)
        {
            _started = true;
            var clip = ResolveClip();
            if (clip == null)
            {
                Debug.Log("[CaliIntroCutscene] No MP4 assigned — skipping intro.", this);
                RequestHostFinish(coop);
                return;
            }

            _blocking = true;
            _timeoutAt = Time.unscaledTime + ResolveDuration(clip) + 1.5f;
            SetVisible(true);
            CaliGameplayAudio.SetSuppressed(true);
            if (CaliScreenFader.Instance != null)
                CaliScreenFader.Instance.SetAlpha(0f);
            CoopSessionStarter.Instance?.SetGameplayCursor(locked: false);
            SyncHud(false);

            _player.clip = clip;
            _surface.BindClip(clip);
            _player.Prepare();
        }

        void OnPrepared(VideoPlayer source)
        {
            if (!_blocking || source != _player)
                return;

            _surface.BindPrepared();
            source.Play();
        }

        void OnVideoEnded(VideoPlayer source)
        {
            if (source != _player)
                return;
            RequestHostFinish(CoopGameController.Instance);
        }

        void RequestHostFinish(CoopGameController coop)
        {
            if (coop == null || _hostFinishing)
                return;
            if (!coop.Object || !coop.Object.IsValid || !coop.HasStateAuthority)
                return;

            _hostFinishing = true;
            coop.FinishIntro();
        }

        void HideLocal()
        {
            if (_finished && !_blocking)
                return;

            bool wasShowing = _blocking;
            _finished = true;
            _blocking = false;
            _started = true;
            _dismissed = true;
            if (_player != null && _player.isPlaying)
                _player.Stop();
            SetVisible(false);
            CaliGameplayAudio.SetSuppressed(false);

            if (!wasShowing)
                return;

            SyncHud(true);
            CoopSessionStarter.Instance?.SetGameplayCursor(locked: true);
            if (CaliScreenFader.Instance != null)
                StartCoroutine(FadeIntoGameplay());
        }

        System.Collections.IEnumerator FadeIntoGameplay()
        {
            var fader = CaliScreenFader.Instance;
            if (fader == null)
                yield break;

            fader.ShowBlack();
            yield return null;
            yield return null;
            fader.StartFadeFromBlack(0.85f);
        }

        VideoClip ResolveClip()
        {
            if (videoClip != null)
                return videoClip;
            return catalog != null ? catalog.videoClip : null;
        }

        float ResolveDuration(VideoClip clip)
        {
            float fallback = catalog != null ? catalog.durationSeconds : 30f;
            if (clip != null && clip.length > 0.25)
                return (float)clip.length;
            return Mathf.Max(1f, fallback);
        }

        bool CanHostSkip(CoopGameController coop)
        {
            if (coop == null || !coop.HasStateAuthority)
                return false;
            return catalog == null || catalog.allowHostSkip;
        }

        static bool WasSkipPressed()
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(0))
                return true;
#if ENABLE_INPUT_SYSTEM
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame))
                return true;
#endif
            return false;
        }

        static void SyncHud(bool playing)
        {
            var hud = CaliGameplayHud.Instance;
            if (hud == null)
                hud = FindFirstObjectByType<CaliGameplayHud>(FindObjectsInactive.Include);
            if (hud == null)
                return;
            var target = hud.root != null ? hud.root : hud.gameObject;
            if (target != null && target.activeSelf != playing)
                target.SetActive(playing);
        }

        void BuildOverlay()
        {
            _canvas = FantasyUiFactory.CreateCanvas("IntroCutsceneRoot", 180);
            _canvas.transform.SetParent(transform, false);

            var black = new GameObject("Black", typeof(RectTransform), typeof(Image));
            black.transform.SetParent(_canvas.transform, false);
            Stretch(black.GetComponent<RectTransform>());
            var bg = black.GetComponent<Image>();
            bg.color = Color.black;
            bg.raycastTarget = true;

            // Sized by the aspect fitter in CaliVideoSurface, so the black fill shows as bars.
            var imageGo = new GameObject("Video", typeof(RectTransform), typeof(RawImage));
            imageGo.transform.SetParent(_canvas.transform, false);
            _image = imageGo.GetComponent<RawImage>();
            _image.color = Color.white;
            _image.raycastTarget = false;

            var hintGo = new GameObject("SkipHint", typeof(RectTransform));
            hintGo.transform.SetParent(_canvas.transform, false);
            var hintRt = hintGo.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0f, 0f);
            hintRt.anchorMax = new Vector2(1f, 0f);
            hintRt.pivot = new Vector2(0.5f, 0f);
            hintRt.anchoredPosition = new Vector2(0f, 48f);
            hintRt.sizeDelta = new Vector2(-80f, 64f);
            _hint = hintGo.AddComponent<TextMeshProUGUI>();
            _hint.alignment = TextAlignmentOptions.Center;
            _hint.fontSize = 36;
            _hint.color = new Color(1f, 1f, 1f, 0.72f);
            _hint.text = "Host: Space or click to skip";
            _hint.raycastTarget = false;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.loop = false;
            _audio.ignoreListenerPause = true;

            _player = gameObject.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.isLooping = false;
            _player.skipOnDrop = true;
            _player.renderMode = VideoRenderMode.RenderTexture;
            _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _player.SetTargetAudioSource(0, _audio);
            _player.loopPointReached += OnVideoEnded;
            _player.prepareCompleted += OnPrepared;

            _surface = new CaliVideoSurface(_player, _image, "CaliIntroRT");
        }

        void SetVisible(bool visible)
        {
            if (_canvas != null)
                _canvas.gameObject.SetActive(visible);

            bool host = CoopSessionStarter.Instance != null && CoopSessionStarter.Instance.IsHost;
            if (_hint != null)
                _hint.gameObject.SetActive(visible && host && (catalog == null || catalog.allowHostSkip));
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
