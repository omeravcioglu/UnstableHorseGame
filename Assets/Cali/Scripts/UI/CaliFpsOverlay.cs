using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>Unscaled FPS readout, top-right. Does not steal clicks.</summary>
    [DefaultExecutionOrder(1000)]
    public class CaliFpsOverlay : MonoBehaviour
    {
        static CaliFpsOverlay _instance;

        TMP_Text _label;
        float _window;
        int _frames;
        float _fps;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => Ensure();

        public static void Ensure()
        {
            if (_instance != null)
                return;

            var existing = FindFirstObjectByType<CaliFpsOverlay>(FindObjectsInactive.Include);
            if (existing != null)
            {
                _instance = existing;
                if (!existing.gameObject.activeSelf)
                    existing.gameObject.SetActive(true);
                return;
            }

            var canvas = FantasyUiFactory.CreateCanvas("CaliFpsOverlay", 5000);
            Object.DontDestroyOnLoad(canvas.gameObject);
            canvas.GetComponent<GraphicRaycaster>().enabled = false;

            var overlay = canvas.gameObject.AddComponent<CaliFpsOverlay>();
            overlay.Build(canvas.transform);
            _instance = overlay;
        }

        void Awake()
        {
            _instance = this;
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        void Build(Transform parent)
        {
            var go = new GameObject("FpsLabel", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _label = go.AddComponent<TextMeshProUGUI>();
            _label.text = "— FPS";
            _label.fontSize = 36f;
            _label.alignment = TextAlignmentOptions.TopRight;
            _label.color = new Color(0.92f, 0.9f, 0.82f, 0.92f);
            _label.raycastTarget = false;
            _label.fontStyle = FontStyles.Bold;
            _label.outlineWidth = 0.18f;
            _label.outlineColor = new Color(0f, 0f, 0f, 0.7f);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-48f, -28f);
            rt.sizeDelta = new Vector2(280f, 56f);
        }

        void Update()
        {
            if (_label == null)
                return;

            _window += Time.unscaledDeltaTime;
            _frames++;
            if (_window < 0.25f)
                return;

            _fps = _frames / _window;
            _window = 0f;
            _frames = 0;
            _label.text = Mathf.RoundToInt(_fps).ToString() + " FPS";
        }
    }
}
