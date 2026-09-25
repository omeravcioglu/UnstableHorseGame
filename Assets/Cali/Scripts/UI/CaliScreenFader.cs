using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>
    /// Persistent black overlay. Every scene change fades out, loads, then fades in.
    /// </summary>
    [DefaultExecutionOrder(-5000)]
    public class CaliScreenFader : MonoBehaviour
    {
        public const int SortOrder = 32760;

        public static CaliScreenFader Instance { get; private set; }

        public float defaultDuration = 0.75f;

        CanvasGroup _group;
        Coroutine _fade;
        bool _ownedLoad;
        bool _networkHold;
        bool _fading;
        float _alpha = 1f;

        public float Alpha => _alpha;
        public bool IsFading => _fading;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoBoot() => Ensure();

        static void Boot()
        {
            if (Instance != null)
                return;
            var go = new GameObject("<<<ScreenFade>>>");
            DontDestroyOnLoad(go);
            go.AddComponent<CaliScreenFader>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            _alpha = 1f;
            BuildOverlay();
            ApplyAlpha();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this)
                Instance = null;
        }

        void Start()
        {
            if (_ownedLoad || _networkHold || _fading)
                return;
            if (CaliSceneIds.IsActive(CaliMainMenuController.SplashSceneName))
            {
                SetAlpha(0f);
                return;
            }

            if (_alpha > 0.5f)
                StartFadeFromBlack(defaultDuration);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single)
                return;
            if (_ownedLoad || _networkHold)
            {
                SetAlpha(1f);
                return;
            }

            if (scene.name == CaliMainMenuController.SplashSceneName)
            {
                SetAlpha(0f);
                return;
            }

            if (CaliIntroCutscene.IsBlocking || CaliIntroCutscene.HoldWorldFade)
                return;

            StartFadeFromBlack(defaultDuration);
        }

        public static CaliScreenFader Ensure()
        {
            if (Instance != null)
                return Instance;
            Boot();
            return Instance;
        }

        public static void LoadScene(string sceneName, float duration = -1f)
        {
            var fader = Ensure();
            if (fader._fade != null)
                fader.StopCoroutine(fader._fade);
            fader._fade = fader.StartCoroutine(fader.LoadRoutine(sceneName, duration));
        }

        public static void RunAfterFadeOut(System.Action then, float duration = -1f)
        {
            if (then == null)
                return;
            var fader = Ensure();
            if (fader._fade != null)
                fader.StopCoroutine(fader._fade);
            fader._fade = fader.StartCoroutine(fader.FadeOutThen(then, duration));
        }

        public static void NotifyNetworkLoadStart()
        {
            var fader = Ensure();
            fader._networkHold = true;
            fader.ShowBlack();
        }

        public static void NotifyNetworkLoadDone()
        {
            var fader = Ensure();
            if (CaliIntroCutscene.IsBlocking || CaliIntroCutscene.HoldWorldFade)
            {
                fader.ShowBlack();
                return;
            }

            fader._networkHold = false;
            fader.StartFadeFromBlack(fader.defaultDuration);
        }

        IEnumerator LoadRoutine(string sceneName, float duration)
        {
            float dur = duration > 0f ? duration : defaultDuration;
            yield return FadeToBlack(dur);
            _ownedLoad = true;
            SceneManager.LoadScene(sceneName);
            yield return null;
            yield return FadeFromBlack(dur);
            _ownedLoad = false;
            _fade = null;
        }

        IEnumerator FadeOutThen(System.Action then, float duration)
        {
            float dur = duration > 0f ? duration : defaultDuration;
            yield return FadeToBlack(dur);
            _networkHold = true;
            then();
            _fade = null;
        }

        public void ShowBlack()
        {
            SetAlpha(1f);
        }

        public void SetAlpha(float alpha)
        {
            _alpha = Mathf.Clamp01(alpha);
            ApplyAlpha();
        }

        public void StartFadeFromBlack(float duration)
        {
            _networkHold = false;
            if (_fade != null)
                StopCoroutine(_fade);
            _fade = StartCoroutine(FadeFromBlack(duration));
        }

        public IEnumerator FadeToBlack(float duration)
        {
            yield return Fade(_alpha, 1f, duration, skipHitch: false);
        }

        public IEnumerator FadeFromBlack(float duration)
        {
            SetAlpha(1f);
            yield return Fade(1f, 0f, duration, skipHitch: true);
        }

        IEnumerator Fade(float from, float to, float duration, bool skipHitch)
        {
            _fading = true;
            float dur = Mathf.Max(0.05f, duration);
            float t = 0f;
            _alpha = from;
            ApplyAlpha();

            while (t < 1f)
            {
                float dt = Time.unscaledDeltaTime;
                if (skipHitch)
                {
                    skipHitch = false;
                    dt = 0f;
                }

                t += dt / dur;
                _alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t));
                ApplyAlpha();
                yield return null;
            }

            _alpha = to;
            ApplyAlpha();
            _fading = false;
        }

        void BuildOverlay()
        {
            var canvasGo = new GameObject("FadeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = SortOrder;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(FantasyUiFactory.RefWidth, FantasyUiFactory.RefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            _group = canvasGo.AddComponent<CanvasGroup>();
            _group.ignoreParentGroups = true;

            var imageGo = new GameObject("Black", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageGo.transform.SetParent(canvasGo.transform, false);
            var rt = imageGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var image = imageGo.GetComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = true;
        }

        void ApplyAlpha()
        {
            if (_group == null)
                return;
            _group.alpha = _alpha;
            bool block = _alpha > 0.02f;
            _group.blocksRaycasts = block;
            _group.interactable = block;
            _group.gameObject.SetActive(_alpha > 0.001f);
        }
    }
}
