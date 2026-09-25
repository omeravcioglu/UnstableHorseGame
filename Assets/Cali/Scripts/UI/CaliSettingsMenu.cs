using UnityEngine;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>
    /// Master / music / SFX volume + fullscreen + quality, persisted lightly via PlayerPrefs.
    /// </summary>
    public class CaliSettingsMenu : MonoBehaviour
    {
        const string PrefMaster = "Cali.Audio.Master";
        const string PrefMusic = "Cali.Audio.Music";
        const string PrefSfx = "Cali.Audio.Sfx";

        public GameObject root;

        GameObject _canvasRoot;
        GraphicRaycaster _raycaster;
        CanvasGroup _canvasGroup;
        bool _bound;

        Slider _master;
        Slider _music;
        Slider _sfx;
        Slider _quality;
        Toggle _fullscreen;
        Button _close;

        public static float MusicVolume { get; private set; } = 1f;
        public static float SfxVolume { get; private set; } = 1f;

        public bool IsOpen =>
            _canvasRoot != null && _canvasRoot.activeSelf && root != null && root.activeSelf;

        public void Bind(GameObject settingsRoot)
        {
            _canvasRoot = settingsRoot != null ? settingsRoot : gameObject;
            var content = _canvasRoot.transform.Find("Content");
            root = content != null ? content.gameObject : _canvasRoot;

            _raycaster = _canvasRoot.GetComponent<GraphicRaycaster>();
            _canvasGroup = _canvasRoot.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = _canvasRoot.AddComponent<CanvasGroup>();

            _master = FindSlider("MasterVolume");
            _music = FindSlider("MusicVolume");
            _sfx = FindSlider("SfxVolume");
            _quality = FindSlider("Quality");
            _fullscreen = FindToggle("Fullscreen");
            _close = FindButton("BtnClose");

            float master = PlayerPrefs.GetFloat(PrefMaster, 1f);
            MusicVolume = PlayerPrefs.GetFloat(PrefMusic, 1f);
            SfxVolume = PlayerPrefs.GetFloat(PrefSfx, 1f);

            if (_master != null)
            {
                _master.value = master;
                _master.onValueChanged.RemoveAllListeners();
                _master.onValueChanged.AddListener(OnMaster);
                OnMaster(master);
            }

            if (_music != null)
            {
                _music.value = MusicVolume;
                _music.onValueChanged.RemoveAllListeners();
                _music.onValueChanged.AddListener(v =>
                {
                    MusicVolume = v;
                    PlayerPrefs.SetFloat(PrefMusic, v);
                });
            }

            if (_sfx != null)
            {
                _sfx.value = SfxVolume;
                _sfx.onValueChanged.RemoveAllListeners();
                _sfx.onValueChanged.AddListener(v =>
                {
                    SfxVolume = v;
                    PlayerPrefs.SetFloat(PrefSfx, v);
                });
            }

            if (_quality != null)
            {
                _quality.wholeNumbers = true;
                _quality.minValue = 0f;
                _quality.maxValue = Mathf.Max(0, QualitySettings.names.Length - 1);
                _quality.value = QualitySettings.GetQualityLevel();
                _quality.onValueChanged.RemoveAllListeners();
                _quality.onValueChanged.AddListener(v => QualitySettings.SetQualityLevel(Mathf.RoundToInt(v), true));
            }

            if (_fullscreen != null)
            {
                _fullscreen.isOn = Screen.fullScreen;
                _fullscreen.onValueChanged.RemoveAllListeners();
                _fullscreen.onValueChanged.AddListener(v => Screen.fullScreen = v);
            }

            if (_close != null)
            {
                _close.onClick.RemoveAllListeners();
                _close.onClick.AddListener(OnBackClicked);
            }

            var blocker = FantasyUiFactory.FindDeepChild(root.transform, "Blocker");
            var blockerBtn = blocker != null ? blocker.GetComponent<Button>() : null;
            if (blockerBtn != null)
            {
                blockerBtn.onClick.RemoveAllListeners();
                blockerBtn.onClick.AddListener(OnBackClicked);
            }

            _bound = true;
            SetVisible(false);
        }

        UnityEngine.Events.UnityAction _backOverride;

        public void SetBackHandler(UnityEngine.Events.UnityAction handler)
        {
            _backOverride = handler;
        }

        void OnBackClicked()
        {
            if (_backOverride != null)
                _backOverride.Invoke();
            else
                Hide();
        }

        public void NotifyOpenedByDirector()
        {
            if (!_bound)
                Bind(_canvasRoot != null ? _canvasRoot : gameObject);
            SetVisible(true, preserveAlpha: true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void NotifyClosedByDirector()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
            PlayerPrefs.Save();
        }

        void Start()
        {
            if (!_bound)
                Bind(gameObject);
        }

        public void Show()
        {
            if (!_bound)
                Bind(_canvasRoot != null ? _canvasRoot : gameObject);
            SetVisible(true, preserveAlpha: false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Hide()
        {
            SetVisible(false, preserveAlpha: false);
            PlayerPrefs.Save();
        }

        void SetVisible(bool visible, bool preserveAlpha = false)
        {
            if (_canvasRoot == null)
                _canvasRoot = gameObject;

            if (_canvasRoot != null && visible)
                _canvasRoot.SetActive(true);

            if (root != null)
                root.SetActive(visible);

            if (_canvasRoot != null && !visible)
                _canvasRoot.SetActive(false);

            if (visible)
            {
                var canvas = _canvasRoot.GetComponent<Canvas>();
                if (canvas != null)
                    canvas.sortingOrder = CaliPauseMenu.PauseSortOrder + 10;
                if (_raycaster != null)
                    _raycaster.enabled = true;
                if (_canvasGroup != null)
                {
                    if (!preserveAlpha)
                        _canvasGroup.alpha = 1f;
                    _canvasGroup.interactable = true;
                    _canvasGroup.blocksRaycasts = true;
                }
            }
        }

        void OnMaster(float v)
        {
            AudioListener.volume = Mathf.Clamp01(v);
            PlayerPrefs.SetFloat(PrefMaster, v);
        }

        Slider FindSlider(string rowName)
        {
            var row = FantasyUiFactory.FindDeepChild(root.transform, rowName);
            return row != null ? row.GetComponentInChildren<Slider>(true) : null;
        }

        Toggle FindToggle(string rowName)
        {
            var row = FantasyUiFactory.FindDeepChild(root.transform, rowName);
            if (row == null) return null;
            return row.GetComponentInChildren<Toggle>(true);
        }

        Button FindButton(string name)
        {
            var t = FantasyUiFactory.FindDeepChild(root.transform, name);
            return t != null ? t.GetComponentInChildren<Button>(true) : null;
        }
    }
}
