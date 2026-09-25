using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>
    /// Rename + portrait pick for a horse identity.
    /// </summary>
    public class CaliAlterHorsePanel : MonoBehaviour
    {
        public GameObject root;

        HorseIdentity _target;
        TMP_InputField _nameInput;
        Transform _portraitRow;
        Sprite _selectedPortrait;
        Button _save;
        Button _cancel;
        Sprite[] _choices;

        public void Bind(GameObject alterRoot)
        {
            var content = alterRoot != null ? alterRoot.transform.Find("Content") : null;
            root = content != null ? content.gameObject : alterRoot;
            _nameInput = root != null ? root.GetComponentInChildren<TMP_InputField>(true) : null;
            var row = root != null ? FantasyUiFactory.FindDeepChild(root.transform, "PortraitRow") : null;
            _portraitRow = row;
            _save = FindBtn("BtnSave");
            _cancel = FindBtn("BtnCancel");

            if (_save != null)
            {
                _save.onClick.RemoveAllListeners();
                _save.onClick.AddListener(Save);
            }

            if (_cancel != null)
            {
                _cancel.onClick.RemoveAllListeners();
                _cancel.onClick.AddListener(Hide);
            }

            var blocker = root != null ? FantasyUiFactory.FindDeepChild(root.transform, "Blocker") : null;
            var blockerBtn = blocker != null ? blocker.GetComponent<Button>() : null;
            if (blockerBtn != null)
            {
                blockerBtn.onClick.RemoveAllListeners();
                blockerBtn.onClick.AddListener(Hide);
            }

            if (root != null)
                root.SetActive(false);
        }

        void Start()
        {
            if (root == null)
                root = gameObject;
            if (_save == null)
                Bind(root);
        }

        public void Open(HorseIdentity identity)
        {
            if (identity == null || root == null)
                return;

            _target = identity;
            if (_nameInput != null)
                _nameInput.text = identity.displayName ?? "";
            _selectedPortrait = identity.portrait;
            BuildPortraitButtons();
            root.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Hide()
        {
            if (root != null)
                root.SetActive(false);
            _target = null;
        }

        void Save()
        {
            if (_target == null)
                return;

            string name = _nameInput != null ? _nameInput.text : _target.displayName;
            _target.Apply(name, _selectedPortrait ?? _target.portrait, true);
            Hide();
        }

        void BuildPortraitButtons()
        {
            if (_portraitRow == null)
                return;

            for (int i = _portraitRow.childCount - 1; i >= 0; i--)
                Destroy(_portraitRow.GetChild(i).gameObject);

            _choices = FantasyUiFactory.LoadPortraitChoices();
            float x = -(_choices.Length - 1) * 0.5f * 160f;
            for (int i = 0; i < _choices.Length; i++)
            {
                var sprite = _choices[i];
                var go = new GameObject($"Portrait_{sprite.name}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                go.transform.SetParent(_portraitRow, false);
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(140f, 140f);
                rt.anchoredPosition = new Vector2(x + i * 160f, 0f);
                var img = go.GetComponent<Image>();
                img.sprite = sprite;
                img.preserveAspect = true;
                var btn = go.GetComponent<Button>();
                var captured = sprite;
                btn.onClick.AddListener(() =>
                {
                    _selectedPortrait = captured;
                    HighlightSelection();
                });
            }

            HighlightSelection();
        }

        void HighlightSelection()
        {
            if (_portraitRow == null) return;
            for (int i = 0; i < _portraitRow.childCount; i++)
            {
                var img = _portraitRow.GetChild(i).GetComponent<Image>();
                if (img == null) continue;
                bool selected = _selectedPortrait != null && img.sprite == _selectedPortrait;
                img.color = selected ? Color.white : new Color(0.7f, 0.7f, 0.7f, 0.85f);
            }
        }

        Button FindBtn(string name)
        {
            var t = FantasyUiFactory.FindDeepChild(root.transform, name);
            return t != null ? t.GetComponent<Button>() : null;
        }
    }
}
