using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Cali.UI
{
    /// <summary>
    /// Company logo splash. Assign a Video Clip, then it plays once and fades into the main menu.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class CaliSplashController : MonoBehaviour
    {
        [Tooltip("Company splash / logo animation. Leave empty to skip after a short beat.")]
        public VideoClip videoClip;
        public bool allowSkip = true;
        [Tooltip("If no clip is assigned, wait this long then go to the menu.")]
        public float missingClipDelay = 0.4f;

        Canvas _canvas;
        RawImage _image;
        TMP_Text _hint;
        VideoPlayer _player;
        AudioSource _audio;
        CaliVideoSurface _surface;
        bool _finished;
        float _skipAfter;

        void Awake()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }

            BuildOverlay();
        }

        void Start()
        {
            if (CaliScreenFader.Instance != null)
                CaliScreenFader.Instance.SetAlpha(0f);

            _skipAfter = Time.unscaledTime + 0.35f;
            if (videoClip == null)
            {
                Invoke(nameof(GoToMenu), Mathf.Max(0.05f, missingClipDelay));
                return;
            }

            _player.clip = videoClip;
            _surface.BindClip(videoClip);
            _player.Prepare();
        }

        void Update()
        {
            if (_finished || !allowSkip || Time.unscaledTime < _skipAfter)
                return;
            if (WasSkipPressed())
                GoToMenu();
        }

        void OnDestroy()
        {
            if (_player != null)
            {
                _player.loopPointReached -= OnVideoEnded;
                _player.prepareCompleted -= OnPrepared;
            }

            _surface?.Release();
        }

        void OnPrepared(VideoPlayer source)
        {
            if (_finished || source != _player)
                return;
            _surface.BindPrepared();
            source.Play();
        }

        void OnVideoEnded(VideoPlayer source)
        {
            if (source != _player)
                return;
            GoToMenu();
        }

        void GoToMenu()
        {
            if (_finished)
                return;
            _finished = true;
            CancelInvoke(nameof(GoToMenu));
            if (_player != null && _player.isPlaying)
                _player.Stop();
            CaliScreenFader.LoadScene(CaliMainMenuController.MenuSceneName);
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

        void BuildOverlay()
        {
            _canvas = FantasyUiFactory.CreateCanvas("SplashVideoRoot", 200);
            _canvas.transform.SetParent(transform, false);

            var black = new GameObject("Black", typeof(RectTransform), typeof(Image));
            black.transform.SetParent(_canvas.transform, false);
            Stretch(black.GetComponent<RectTransform>());
            var bg = black.GetComponent<Image>();
            bg.color = Color.black;
            bg.raycastTarget = false;

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
            _hint.fontSize = 32;
            _hint.color = new Color(1f, 1f, 1f, 0.55f);
            _hint.text = allowSkip ? "Click or Space to skip" : "";
            _hint.raycastTarget = false;
            _hint.gameObject.SetActive(allowSkip);

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

            _surface = new CaliVideoSurface(_player, _image, "CaliSplashRT");
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
