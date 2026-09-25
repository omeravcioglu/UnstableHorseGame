using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>
    /// Full-screen "Level Cleared" overlay shown after the latch-door camera cue finishes.
    /// </summary>
    public class LevelClearedHud : MonoBehaviour
    {
        public static LevelClearedHud Instance { get; private set; }
        public static bool IsOpen { get; private set; }

        Canvas _canvas;
        GameObject _root;
        Button _continue;

        public static LevelClearedHud Ensure()
        {
            if (Instance != null)
                return Instance;

            var canvas = FantasyUiFactory.CreateCanvas("LevelClearedHud", 130);
            var hud = canvas.gameObject.AddComponent<LevelClearedHud>();
            hud.Build(canvas);
            Instance = hud;
            return hud;
        }

        public static void Show()
        {
            Ensure().Open();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (IsOpen && Instance == null)
                IsOpen = false;
        }

        void Build(Canvas canvas)
        {
            _canvas = canvas;
            FantasyUiFactory.EnsureEventSystem();

            _root = new GameObject("Content", typeof(RectTransform));
            _root.transform.SetParent(canvas.transform, false);
            var rootRt = _root.GetComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            var blocker = new GameObject("Blocker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            blocker.transform.SetParent(_root.transform, false);
            var blockerRt = blocker.GetComponent<RectTransform>();
            blockerRt.anchorMin = Vector2.zero;
            blockerRt.anchorMax = Vector2.one;
            blockerRt.offsetMin = Vector2.zero;
            blockerRt.offsetMax = Vector2.zero;
            var blockerImg = blocker.GetComponent<Image>();
            blockerImg.color = new Color(0f, 0f, 0f, 0.62f);
            blockerImg.raycastTarget = true;

            var panel = FantasyUiFactory.InstantiateResource(FantasyUiPaths.PopUp, _root.transform);
            if (panel == null)
            {
                panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                panel.transform.SetParent(_root.transform, false);
                panel.GetComponent<Image>().color = new Color(0.12f, 0.1f, 0.16f, 0.96f);
            }

            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.anchoredPosition = Vector2.zero;
            panelRt.sizeDelta = new Vector2(1100f, 520f);

            var title = FantasyUiFactory.EnsureLabel(panel.transform, "Title", "Level Cleared", 92f);
            title.fontStyle = FontStyles.Bold;
            title.color = new Color(1f, 0.9f, 0.55f, 1f);
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0.5f, 0.5f);
            titleRt.anchorMax = new Vector2(0.5f, 0.5f);
            titleRt.pivot = new Vector2(0.5f, 0.5f);
            titleRt.anchoredPosition = new Vector2(0f, 90f);
            titleRt.sizeDelta = new Vector2(980f, 140f);

            var sub = FantasyUiFactory.EnsureLabel(panel.transform, "Subtitle", "The stall is open.", 36f);
            sub.color = new Color(0.92f, 0.88f, 0.78f, 1f);
            var subRt = sub.rectTransform;
            subRt.anchorMin = new Vector2(0.5f, 0.5f);
            subRt.anchorMax = new Vector2(0.5f, 0.5f);
            subRt.pivot = new Vector2(0.5f, 0.5f);
            subRt.anchoredPosition = new Vector2(0f, -10f);
            subRt.sizeDelta = new Vector2(900f, 60f);

            var btnGo = FantasyUiFactory.InstantiateResource(FantasyUiPaths.ButtonOrange, panel.transform)
                        ?? FantasyUiFactory.CreateFallbackButton(panel.transform, "BtnContinue", "Continue");
            btnGo.name = "BtnContinue";
            FantasyUiFactory.SetButtonLabel(btnGo, "Continue");
            FantasyUiFactory.HardenButtonHits(btnGo);
            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0.5f, 0.5f);
            btnRt.anchorMax = new Vector2(0.5f, 0.5f);
            btnRt.pivot = new Vector2(0.5f, 0.5f);
            btnRt.anchoredPosition = new Vector2(0f, -150f);
            btnRt.sizeDelta = new Vector2(420f, 90f);
            _continue = btnGo.GetComponent<Button>() ?? btnGo.GetComponentInChildren<Button>(true);
            if (_continue != null)
                _continue.onClick.AddListener(Close);

            _root.SetActive(false);
        }

        void Update()
        {
            if (!IsOpen)
                return;

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame
                               || kb.numpadEnterKey.wasPressedThisFrame))
                Close();
        }

        void Open()
        {
            if (_root != null)
                _root.SetActive(true);
            if (_canvas != null)
                _canvas.enabled = true;
            IsOpen = true;
        }

        public void Close()
        {
            if (_root != null)
                _root.SetActive(false);
            IsOpen = false;
        }
    }
}
