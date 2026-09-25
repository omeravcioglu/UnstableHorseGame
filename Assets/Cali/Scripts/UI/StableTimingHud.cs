using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>
    /// Dual timing bars for the stable latch mini-game. Built in code, no extra art.
    /// </summary>
    public class StableTimingHud : MonoBehaviour
    {
        public static StableTimingHud Instance { get; private set; }

        Canvas _canvas;
        GameObject _promptRoot;
        TMP_Text _prompt;
        GameObject _barsRoot;
        BarView _barA;
        BarView _barB;
        Sprite _white;

        struct BarView
        {
            public Image track;
            public Image zone;
            public Image marker;
            public Image success;
            public TMP_Text label;
            public string labelPrefix;
        }

        public static StableTimingHud Ensure()
        {
            if (Instance != null)
                return Instance;

            var canvas = FantasyUiFactory.CreateCanvas("StableTimingHud", 85);
            var hud = canvas.gameObject.AddComponent<StableTimingHud>();
            hud.Build(canvas);
            Instance = hud;
            return hud;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Build(Canvas canvas)
        {
            _canvas = canvas;
            _white = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);

            _promptRoot = new GameObject("Prompt", typeof(RectTransform));
            _promptRoot.transform.SetParent(canvas.transform, false);
            var promptRt = _promptRoot.GetComponent<RectTransform>();
            promptRt.anchorMin = new Vector2(0.5f, 0.18f);
            promptRt.anchorMax = new Vector2(0.5f, 0.18f);
            promptRt.pivot = new Vector2(0.5f, 0.5f);
            promptRt.sizeDelta = new Vector2(1400f, 160f);

            _prompt = _promptRoot.AddComponent<TextMeshProUGUI>();
            _prompt.alignment = TextAlignmentOptions.Center;
            _prompt.fontSize = 44f;
            _prompt.color = new Color(1f, 0.95f, 0.75f, 1f);
            _prompt.raycastTarget = false;
            _prompt.fontStyle = FontStyles.Bold;
            _prompt.text = "Press Jump to unlatch";

            _barsRoot = new GameObject("Bars", typeof(RectTransform));
            _barsRoot.transform.SetParent(canvas.transform, false);
            var barsRt = _barsRoot.GetComponent<RectTransform>();
            barsRt.anchorMin = new Vector2(0.5f, 0.58f);
            barsRt.anchorMax = new Vector2(0.5f, 0.58f);
            barsRt.pivot = new Vector2(0.5f, 0.5f);
            barsRt.sizeDelta = new Vector2(1100f, 280f);

            _barA = MakeBar(_barsRoot.transform, "P1 — Space", new Vector2(0f, 70f), new Color(0.35f, 0.75f, 1f));
            _barB = MakeBar(_barsRoot.transform, "P2 — Right Ctrl / Numpad 0", new Vector2(0f, -70f), new Color(0.95f, 0.55f, 0.2f));

            Hide();
        }

        BarView MakeBar(Transform parent, string label, Vector2 pos, Color accent)
        {
            var root = new GameObject(label, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(1000f, 90f);

            var view = new BarView
            {
                track = MakeImage(root.transform, "Track", new Color(0.08f, 0.07f, 0.06f, 0.85f), Vector2.zero, new Vector2(1000f, 36f)),
                zone = MakeImage(root.transform, "Zone", new Color(0.25f, 0.85f, 0.28f, 0.9f), Vector2.zero, new Vector2(120f, 36f)),
                marker = MakeImage(root.transform, "Marker", Color.white, Vector2.zero, new Vector2(8f, 52f)),
                success = MakeImage(root.transform, "Success", new Color(1f, 0.82f, 0.2f, 0.55f), Vector2.zero, new Vector2(1000f, 36f)),
                label = FantasyUiFactory.EnsureLabel(root.transform, "Label", label, 32f),
                labelPrefix = label
            };

            var labelRt = view.label.rectTransform;
            labelRt.anchorMin = new Vector2(0f, 1f);
            labelRt.anchorMax = new Vector2(1f, 1f);
            labelRt.pivot = new Vector2(0.5f, 0f);
            labelRt.anchoredPosition = new Vector2(0f, 8f);
            labelRt.sizeDelta = new Vector2(0f, 36f);
            view.label.color = accent;
            view.label.alignment = TextAlignmentOptions.Center;
            view.success.enabled = false;
            view.marker.color = accent;
            return view;
        }

        Image MakeImage(Transform parent, string name, Color color, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = _white;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public void Hide()
        {
            if (_promptRoot != null)
                _promptRoot.SetActive(false);
            if (_barsRoot != null)
                _barsRoot.SetActive(false);
        }

        public void ShowPrompt(string text)
        {
            if (_barsRoot != null)
                _barsRoot.SetActive(false);
            if (_promptRoot != null)
                _promptRoot.SetActive(true);
            if (_prompt != null)
                _prompt.text = text;
        }

        public void ShowBars()
        {
            if (_promptRoot != null)
                _promptRoot.SetActive(false);
            if (_barsRoot != null)
                _barsRoot.SetActive(true);
        }

        public void Render(
            float markerA,
            float markerB,
            float zoneA,
            float zoneB,
            float zoneWidth,
            int hitsA,
            int hitsB,
            int hitsNeeded)
        {
            ShowBars();
            PaintBar(ref _barA, markerA, zoneA, zoneWidth, hitsA, hitsNeeded);
            PaintBar(ref _barB, markerB, zoneB, zoneWidth, hitsB, hitsNeeded);
        }

        void PaintBar(ref BarView bar, float marker01, float zoneCenter, float zoneWidth, int hits, int hitsNeeded)
        {
            int need = Mathf.Max(1, hitsNeeded);
            int have = Mathf.Clamp(hits, 0, need);
            bool done = have >= need;
            float track = 1000f;
            float half = zoneWidth * 0.5f * track;
            bar.zone.rectTransform.sizeDelta = new Vector2(Mathf.Max(24f, half * 2f), 36f);
            bar.zone.rectTransform.anchoredPosition = new Vector2((zoneCenter - 0.5f) * track, 0f);
            bar.marker.rectTransform.anchoredPosition = new Vector2((Mathf.Clamp01(marker01) - 0.5f) * track, 0f);
            bar.success.enabled = done;
            bar.marker.enabled = !done;
            bar.zone.color = done
                ? new Color(1f, 0.82f, 0.2f, 0.95f)
                : new Color(0.25f, 0.85f, 0.28f, 0.9f);
            if (bar.label != null)
                bar.label.text = $"{bar.labelPrefix}  {have}/{need}";
        }
    }
}
