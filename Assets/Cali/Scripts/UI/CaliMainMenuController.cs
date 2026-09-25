using UnityEngine;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>
    /// Title / main menu: Host, Join, Settings, Quit (online-only).
    /// Host/Join/Settings go through <see cref="CaliMenuFlowDirector"/> for cinematic transitions.
    /// </summary>
    public class CaliMainMenuController : MonoBehaviour
    {
        public const string GameSceneName = "MC_Demo_Day";
        public const string SinglePlayerSceneName = "Singleplayer";
        public const string MenuSceneName = "CaliMainMenu";
        public const string LobbySceneName = "CaliLobby";
        public const string SplashSceneName = "Splash";

        Button _play;
        Button _host;
        Button _join;
        Button _settings;
        Button _quit;
        CaliSettingsMenu _settingsMenu;
        CaliHostSetupPanel _hostPanel;
        CaliJoinBrowserPanel _joinPanel;

        void Awake()
        {
            FantasyUiFactory.EnsureEventSystem();
            EnsureOverlayPanels();
            WireButtons();
        }

        void Start()
        {
            FantasyUiFactory.EnsureEventSystem();
            EnsureOverlayPanels();
            WireButtons();
        }

        void EnsureOverlayPanels()
        {
            _settingsMenu = FindFirstObjectByType<CaliSettingsMenu>(FindObjectsInactive.Include);
            if (_settingsMenu == null)
            {
                var settingsRoot = FantasyUiFactory.BuildSettingsRoot();
                var c = settingsRoot.AddComponent<CaliSettingsMenu>();
                c.Bind(settingsRoot);
                _settingsMenu = c;
            }

            _hostPanel = FindFirstObjectByType<CaliHostSetupPanel>(FindObjectsInactive.Include);
            if (_hostPanel == null)
            {
                var hostRoot = FantasyUiFactory.BuildHostSetupPanel();
                var c = hostRoot.AddComponent<CaliHostSetupPanel>();
                c.Bind(hostRoot);
                _hostPanel = c;
            }

            _joinPanel = FindFirstObjectByType<CaliJoinBrowserPanel>(FindObjectsInactive.Include);
            if (_joinPanel == null)
            {
                var joinRoot = FantasyUiFactory.BuildJoinBrowserPanel();
                var c = joinRoot.AddComponent<CaliJoinBrowserPanel>();
                c.Bind(joinRoot);
                _joinPanel = c;
            }
        }

        void WireButtons()
        {
            EnsurePlayButton();

            _play = FindButton("BtnPlay");
            _host = FindButton("BtnHost");
            _join = FindButton("BtnJoin");
            _settings = FindButton("BtnSettings");
            _quit = FindButton("BtnQuit");

            BindClick(_play, OnPlay);
            BindClick(_host, OnHost);
            BindClick(_join, OnJoin);
            BindClick(_settings, OnSettings);
            BindClick(_quit, OnQuit);
            LayoutMenuButtons();
        }

        static void BindClick(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null)
                return;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        Button FindButton(string name)
        {
            var t = FantasyUiFactory.FindDeepChild(transform, name);
            if (t == null)
                t = GameObject.Find(name)?.transform;
            if (t == null)
                return null;
            return t.GetComponent<Button>() ?? t.GetComponentInChildren<Button>(true);
        }

        void EnsurePlayButton()
        {
            if (FindButton("BtnPlay") != null)
                return;

            var host = FindButton("BtnHost");
            if (host == null)
                return;

            var clone = Object.Instantiate(host.gameObject, host.transform.parent);
            clone.name = "BtnPlay";
            clone.SetActive(true);
            FantasyUiFactory.SetButtonLabel(clone, "Play");
            clone.transform.SetAsFirstSibling();
        }

        void LayoutMenuButtons()
        {
            var play = FindButton("BtnPlay");
            var host = FindButton("BtnHost");
            if (play == null || host == null)
                return;

            var playRt = play.GetComponent<RectTransform>();
            var hostRt = host.GetComponent<RectTransform>();
            if (playRt == null || hostRt == null)
                return;

            playRt.anchorMin = hostRt.anchorMin;
            playRt.anchorMax = hostRt.anchorMax;
            playRt.pivot = hostRt.pivot;
            playRt.sizeDelta = hostRt.sizeDelta;
            playRt.localScale = hostRt.localScale;

            float x = hostRt.anchoredPosition.x;
            float y = hostRt.anchoredPosition.y;
            float step = 90f;
            var join = FindButton("BtnJoin");
            if (join != null)
            {
                var joinRt = join.GetComponent<RectTransform>();
                if (joinRt != null)
                    step = Mathf.Abs(joinRt.anchoredPosition.y - hostRt.anchoredPosition.y);
                if (step < 40f)
                    step = 90f;
            }

            Button[] order = { play, host, FindButton("BtnJoin"), FindButton("BtnSettings"), FindButton("BtnQuit") };
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] == null)
                    continue;
                var rt = order[i].GetComponent<RectTransform>();
                if (rt == null)
                    continue;
                rt.anchoredPosition = new Vector2(x, y - i * step);
            }

            var parent = hostRt.parent as RectTransform;
            if (parent != null)
            {
                var size = parent.sizeDelta;
                size.y = Mathf.Max(size.y, step * 5f + 40f);
                parent.sizeDelta = size;
            }
        }

        void OnPlay()
        {
            CaliScreenFader.LoadScene(SinglePlayerSceneName);
        }

        void OnHost()
        {
            EnsureOverlayPanels();
            if (CaliMenuFlowDirector.Instance != null)
            {
                CaliMenuFlowDirector.Instance.GoToHost();
                return;
            }

            if (_joinPanel != null && _joinPanel.IsOpen)
                _joinPanel.Hide();
            if (_settingsMenu != null && _settingsMenu.IsOpen)
                _settingsMenu.Hide();
            if (_hostPanel != null)
                _hostPanel.Show();
            else
                Debug.LogWarning("[CaliMainMenu] Host panel missing.");
        }

        void OnJoin()
        {
            EnsureOverlayPanels();
            if (CaliMenuFlowDirector.Instance != null)
            {
                CaliMenuFlowDirector.Instance.GoToJoin();
                return;
            }

            if (_hostPanel != null && _hostPanel.IsOpen)
                _hostPanel.Hide();
            if (_settingsMenu != null && _settingsMenu.IsOpen)
                _settingsMenu.Hide();
            if (_joinPanel != null)
                _joinPanel.Show();
            else
                Debug.LogWarning("[CaliMainMenu] Join panel missing.");
        }

        void OnSettings()
        {
            EnsureOverlayPanels();
            if (CaliMenuFlowDirector.Instance != null)
            {
                CaliMenuFlowDirector.Instance.GoToSettings();
                return;
            }

            if (_hostPanel != null && _hostPanel.IsOpen)
                _hostPanel.Hide();
            if (_joinPanel != null && _joinPanel.IsOpen)
                _joinPanel.Hide();
            if (_settingsMenu != null)
                _settingsMenu.Show();
        }

        void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
