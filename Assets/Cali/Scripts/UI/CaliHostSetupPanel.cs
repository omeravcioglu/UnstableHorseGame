using Cali.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>
    /// Host lobby setup: region, lobby name, optional password.
    /// </summary>
    public class CaliHostSetupPanel : MonoBehaviour
    {
        public GameObject root;

        GameObject _canvasRoot;
        GraphicRaycaster _raycaster;
        CanvasGroup _canvasGroup;
        bool _bound;

        TMP_InputField _lobbyName;
        TMP_InputField _password;
        TMP_Dropdown _region;
        TMP_Text _status;
        Button _create;
        Button _back;

        public bool IsOpen =>
            _canvasRoot != null && _canvasRoot.activeSelf && root != null && root.activeSelf;

        public void Bind(GameObject hostRoot)
        {
            _canvasRoot = hostRoot != null ? hostRoot : gameObject;
            var content = _canvasRoot.transform.Find("Content");
            root = content != null ? content.gameObject : _canvasRoot;

            _raycaster = _canvasRoot.GetComponent<GraphicRaycaster>();
            _canvasGroup = _canvasRoot.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = _canvasRoot.AddComponent<CanvasGroup>();

            _lobbyName = FindInput("LobbyName");
            _password = FindInput("Password");
            _region = FindDropdown("RegionDropdown");
            var statusT = FantasyUiFactory.FindDeepChild(root.transform, "StatusLabel");
            _status = statusT != null ? statusT.GetComponent<TMP_Text>() : null;
            _create = FindBtn("BtnCreate");
            _back = FindBtn("BtnBack");

            if (_lobbyName != null && string.IsNullOrWhiteSpace(_lobbyName.text))
                _lobbyName.text = "MyLobby";

            if (_create != null)
            {
                _create.onClick.RemoveAllListeners();
                _create.onClick.AddListener(OnCreate);
            }

            if (_back != null)
            {
                _back.onClick.RemoveAllListeners();
                _back.onClick.AddListener(OnBackClicked);
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

        /// <summary>Director: activate for fade-in without forcing alpha 1.</summary>
        public void NotifyOpenedByDirector()
        {
            if (!_bound)
                Bind(_canvasRoot != null ? _canvasRoot : gameObject);
            SetVisible(true, preserveAlpha: true);
            SetStatus("");
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void NotifyClosedByDirector()
        {
            // Keep GameObject active for director fade; final hide is director's job.
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
        }

        void Start()
        {
            // Do NOT call SetVisible(false) here. If the panel was bound while inactive,
            // Start runs on the first Show() and would immediately close it (needs 2 clicks).
            if (!_bound)
                Bind(gameObject);
        }

        public void Show()
        {
            if (!_bound)
                Bind(_canvasRoot != null ? _canvasRoot : gameObject);
            SetVisible(true, preserveAlpha: false);
            SetStatus("");
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Hide()
        {
            SetVisible(false, preserveAlpha: false);
        }

        void SetVisible(bool visible, bool preserveAlpha = false)
        {
            if (_canvasRoot == null)
                _canvasRoot = gameObject;

            // Enable parent canvas first so activating Content works.
            if (_canvasRoot != null && visible)
                _canvasRoot.SetActive(true);

            if (root != null)
                root.SetActive(visible);

            if (_canvasRoot != null && !visible)
                _canvasRoot.SetActive(false);

            if (visible)
            {
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

        void OnCreate()
        {
            string name = _lobbyName != null ? _lobbyName.text : "";
            if (string.IsNullOrWhiteSpace(name))
            {
                SetStatus("Enter a room code.");
                return;
            }

            string region = "";
            if (_region != null && _region.value >= 0 && _region.value < CaliPhotonRegions.Options.Length)
                region = CaliPhotonRegions.Options[_region.value].Code;

            if (string.IsNullOrEmpty(region))
            {
                SetStatus("Pick a fixed region (not Best) so friends can join by code.");
                return;
            }

            string password = _password != null ? _password.text : "";
            CaliPendingSession.SetHost(region, name.Trim(), password);
            CaliScreenFader.LoadScene(CaliMainMenuController.LobbySceneName);
        }

        void SetStatus(string msg)
        {
            if (_status != null)
                _status.text = msg ?? "";
        }

        TMP_InputField FindInput(string rowName)
        {
            var row = FantasyUiFactory.FindDeepChild(root.transform, rowName);
            return row != null ? row.GetComponentInChildren<TMP_InputField>(true) : null;
        }

        TMP_Dropdown FindDropdown(string rowName)
        {
            var row = FantasyUiFactory.FindDeepChild(root.transform, rowName);
            return row != null ? row.GetComponentInChildren<TMP_Dropdown>(true) : null;
        }

        Button FindBtn(string name)
        {
            var t = FantasyUiFactory.FindDeepChild(root.transform, name);
            return t != null ? t.GetComponentInChildren<Button>(true) : null;
        }
    }
}
