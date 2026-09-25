using System.Collections.Generic;
using Cali.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>
    /// Esc / Start toggles pause overlay. Offline freezes timeScale; online freezes local UI/input only.
    /// Clicks are resolved here (unscaled) because Input System UI often ignores timeScale 0.
    /// </summary>
    public class CaliPauseMenu : MonoBehaviour
    {
        public const int PauseSortOrder = 220;

        public GameObject root;
        public KeyCode toggleKey = KeyCode.Escape;

        GameObject _canvasRoot;
        GraphicRaycaster _raycaster;
        Button _resume;
        Button _settings;
        Button _addSave;
        Button _resetSave;
        Button _quit;
        CaliSettingsMenu _settingsMenu;
        bool _paused;
        float _prevTimeScale = 1f;
        bool _inputFrozen;
        bool _bound;
        float _clickLock;
        readonly List<RaycastResult> _hits = new();

        public bool IsPaused => _paused;

        public void Bind(GameObject pauseRoot)
        {
            _canvasRoot = pauseRoot != null ? pauseRoot : gameObject;
            if (_canvasRoot.GetComponent<Canvas>() == null)
            {
                var canvas = _canvasRoot.GetComponentInParent<Canvas>();
                if (canvas != null)
                    _canvasRoot = canvas.gameObject;
            }

            var content = _canvasRoot.transform.Find("Content");
            root = content != null ? content.gameObject : _canvasRoot;
            if (root == null)
                return;

            NeutralizeBlocker();
            DisableDecorRaycasts();
            EnsureAddSaveButton();
            EnsureResetSaveButton();

            _resume = WireBtn("BtnResume", Resume);
            _settings = WireBtn("BtnSettings", OpenSettings);
            _addSave = WireBtn("BtnAddSave", AddSavePoint);
            _resetSave = WireBtn("BtnResetSave", ResetSave);
            _quit = WireBtn("BtnQuit", QuitToMenu);

            LayoutPauseButtons();
            RaiseButtons();

            var canvasComp = _canvasRoot.GetComponent<Canvas>();
            if (canvasComp != null)
            {
                canvasComp.overrideSorting = true;
                canvasComp.sortingOrder = PauseSortOrder;
            }

            _raycaster = _canvasRoot.GetComponent<GraphicRaycaster>();
            if (_raycaster == null)
                _raycaster = _canvasRoot.AddComponent<GraphicRaycaster>();
            _raycaster.enabled = true;

            root.SetActive(false);
            _paused = false;
            _bound = true;
            SetPauseRaycasts(false);
        }

        void Start()
        {
            if (!_bound)
                Bind(_canvasRoot != null ? _canvasRoot : gameObject);

            _settingsMenu = FindFirstObjectByType<CaliSettingsMenu>(FindObjectsInactive.Include);
        }

        void Update()
        {
            if (_paused && PrimaryClicked())
                TryClickPausedButton();

            bool toggle = Input.GetKeyDown(toggleKey);
#if ENABLE_INPUT_SYSTEM
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null && pad.startButton.wasPressedThisFrame)
                toggle = true;
#endif
            if (!toggle)
                return;

            if (CaliIntroCutscene.IsBlocking)
                return;

            if (_settingsMenu != null && _settingsMenu.IsOpen)
            {
                _settingsMenu.Hide();
                return;
            }

            if (_paused)
                Resume();
            else
                Pause();
        }

        public void Pause()
        {
            if (_paused)
                return;

            if (!_bound)
                Bind(_canvasRoot != null ? _canvasRoot : gameObject);

            FantasyUiFactory.EnsureEventSystem();
            _paused = true;
            if (root != null)
                root.SetActive(true);
            SetPauseRaycasts(true);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (!Cali.Network.CoopSessionStarter.IsOnline)
            {
                _prevTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }
            else
            {
                FreezeLocalInput(true);
            }

            SetButtonLabel(_addSave, "Add Save Point");
            SetButtonLabel(_resetSave, "Reset Save");
        }

        public void Resume()
        {
            if (!_paused)
                return;

            _paused = false;
            if (root != null)
                root.SetActive(false);
            SetPauseRaycasts(false);

            if (!Cali.Network.CoopSessionStarter.IsOnline)
                Time.timeScale = _prevTimeScale > 0f ? _prevTimeScale : 1f;
            else
                FreezeLocalInput(false);
        }

        void SetPauseRaycasts(bool enabled)
        {
            var host = _canvasRoot != null ? _canvasRoot : gameObject;
            if (_raycaster == null)
                _raycaster = host.GetComponent<GraphicRaycaster>();
            if (_raycaster == null)
                _raycaster = host.AddComponent<GraphicRaycaster>();
            _raycaster.enabled = true;

            var cg = host.GetComponent<CanvasGroup>();
            if (cg == null)
                cg = host.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = enabled;
            cg.interactable = enabled;
            cg.ignoreParentGroups = true;
            cg.alpha = 1f;
        }

        void TryClickPausedButton()
        {
            if (Time.unscaledTime < _clickLock)
                return;
            if (_settingsMenu != null && _settingsMenu.IsOpen)
                return;

            var pos = PointerScreenPos();
            var cam = PauseEventCamera();
            Button[] order = { _quit, _resetSave, _settings, _resume };
            for (int i = 0; i < order.Length; i++)
            {
                if (TryInvokeIfHit(order[i], pos, cam))
                    return;
            }

            var es = EventSystem.current;
            if (es == null)
            {
                FantasyUiFactory.EnsureEventSystem();
                es = EventSystem.current;
            }

            if (_raycaster == null || es == null)
                return;

            var data = new PointerEventData(es) { position = pos };
            _hits.Clear();
            _raycaster.Raycast(data, _hits);
            if (_hits.Count == 0)
                es.RaycastAll(data, _hits);

            for (int i = 0; i < _hits.Count; i++)
            {
                var go = _hits[i].gameObject;
                if (go == null)
                    continue;
                var btn = go.GetComponentInParent<Button>();
                if (TryInvokeIfHit(btn, pos, cam))
                    return;
            }
        }

        bool TryInvokeIfHit(Button btn, Vector2 screenPos, Camera cam)
        {
            if (btn == null || !btn.interactable || !btn.isActiveAndEnabled)
                return false;
            if (root != null && !btn.transform.IsChildOf(root.transform) && btn.transform != root.transform)
                return false;

            var rt = btn.transform as RectTransform;
            if (rt == null || !RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, cam))
                return false;

            _clickLock = Time.unscaledTime + 0.25f;
            btn.onClick.Invoke();
            return true;
        }

        Camera PauseEventCamera()
        {
            var canvas = _canvasRoot != null ? _canvasRoot.GetComponent<Canvas>() : null;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;
            return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
        }

        static bool PrimaryClicked()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                return true;
            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
                return true;
#endif
            return Input.GetMouseButtonDown(0);
        }

        static Vector2 PointerScreenPos()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
                return mouse.position.ReadValue();
#endif
            return Input.mousePosition;
        }

        void OpenSettings()
        {
            if (_settingsMenu == null)
                _settingsMenu = FindFirstObjectByType<CaliSettingsMenu>(FindObjectsInactive.Include);
            if (_settingsMenu != null)
                _settingsMenu.Show();
        }

        void AddSavePoint()
        {
            if (CoopSavePoint.TrySaveHere())
            {
                SetButtonLabel(_addSave, "Point saved");
                Resume();
            }
            else
            {
                SetButtonLabel(_addSave, "Need both horses");
            }
        }

        void ResetSave()
        {
            CoopSavePoint.ClearCheckpoint();
            SetButtonLabel(_resetSave, "Save cleared");
        }

        void QuitToMenu()
        {
            Resume();
            Time.timeScale = 1f;
            if (Cali.Network.CoopSessionStarter.Instance != null && Cali.Network.CoopSessionStarter.IsOnline)
                Cali.Network.CoopSessionStarter.Instance.Shutdown();
            CaliScreenFader.LoadScene(CaliMainMenuController.MenuSceneName);
        }

        void FreezeLocalInput(bool freeze)
        {
            _inputFrozen = freeze;
            var dual = FindFirstObjectByType<Cali.Gameplay.LocalDualHorseInput>();
            if (dual == null)
                return;

            bool online = Cali.Network.CoopSessionStarter.IsOnline;
            dual.enabled = !freeze && !online;
        }

        Button WireBtn(string name, UnityEngine.Events.UnityAction action)
        {
            var btn = FindBtn(name);
            if (btn == null)
                return null;

            FantasyUiFactory.HardenButtonHits(btn.gameObject);
            btn.interactable = true;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(action);
            return btn;
        }

        void EnsureAddSaveButton()
        {
            if (FindBtn("BtnAddSave") != null)
                return;

            var resumeT = FantasyUiFactory.FindDeepChild(root.transform, "BtnResume");
            Transform panel = resumeT != null ? resumeT.parent : root.transform;
            var btn = FantasyUiFactory.CreateFallbackButton(panel, "BtnAddSave", "Add Save Point");
            btn.name = "BtnAddSave";
        }

        void EnsureResetSaveButton()
        {
            if (FindBtn("BtnResetSave") != null)
                return;

            var resumeT = FantasyUiFactory.FindDeepChild(root.transform, "BtnResume");
            Transform panel = resumeT != null ? resumeT.parent : root.transform;
            var btn = FantasyUiFactory.CreateFallbackButton(panel, "BtnResetSave", "Reset Save");
            btn.name = "BtnResetSave";
        }

        void LayoutPauseButtons()
        {
            Button[] order = { _resume, _settings, FindBtn("BtnAddSave"), FindBtn("BtnResetSave"), _quit };
            float top = 200f;
            float step = 92f;
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] == null)
                    continue;
                var rt = order[i].GetComponent<RectTransform>();
                if (rt == null)
                    continue;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, top - i * step);
                rt.sizeDelta = new Vector2(480f, 82f);
            }
        }

        void RaiseButtons()
        {
            Button[] order = { _resume, _settings, _addSave, _resetSave, _quit };
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] != null)
                    order[i].transform.SetAsLastSibling();
            }
        }

        void NeutralizeBlocker()
        {
            var blocker = FantasyUiFactory.FindDeepChild(root.transform, "Blocker");
            if (blocker == null)
                return;

            var steal = blocker.GetComponent<Button>();
            if (steal != null)
                steal.enabled = false;
        }

        void DisableDecorRaycasts()
        {
            string[] names = { "Pop-Up-BG", "Image", "PauseTitle" };
            for (int i = 0; i < names.Length; i++)
            {
                var t = FantasyUiFactory.FindDeepChild(root.transform, names[i]);
                if (t == null)
                    continue;
                var images = t.GetComponentsInChildren<Image>(true);
                for (int im = 0; im < images.Length; im++)
                {
                    if (images[im] != null)
                        images[im].raycastTarget = false;
                }
            }
        }

        static void SetButtonLabel(Button button, string text)
        {
            if (button == null)
                return;
            var tmp = button.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null)
                tmp.text = text;
        }

        Button FindBtn(string name)
        {
            var t = FantasyUiFactory.FindDeepChild(root != null ? root.transform : transform, name);
            return t != null ? t.GetComponent<Button>() : null;
        }

        void OnDestroy()
        {
            if (_paused && !Cali.Network.CoopSessionStarter.IsOnline)
                Time.timeScale = 1f;
            if (_inputFrozen)
                FreezeLocalInput(false);
        }
    }
}
