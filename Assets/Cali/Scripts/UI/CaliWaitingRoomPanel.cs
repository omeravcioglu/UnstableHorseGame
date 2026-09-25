using Cali.Network;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>
    /// In-session waiting room: host waits for a second player, then presses Start.
    /// </summary>
    public class CaliWaitingRoomPanel : MonoBehaviour
    {
        public GameObject root;

        GameObject _canvasRoot;
        GraphicRaycaster _raycaster;
        CanvasGroup _canvasGroup;

        TMP_Text _lobbyLabel;
        TMP_Text _playersLabel;
        TMP_Text _hintLabel;
        Button _start;
        Button _leave;

        bool _bound;
        bool _visible;
        int _lastPhase = -1;

        public bool IsOpen => _visible;

        public void Bind(GameObject waitingRoot)
        {
            _canvasRoot = waitingRoot != null ? waitingRoot : gameObject;
            var content = _canvasRoot.transform.Find("Content");
            root = content != null ? content.gameObject : _canvasRoot;

            _raycaster = _canvasRoot.GetComponent<GraphicRaycaster>();
            _canvasGroup = _canvasRoot.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = _canvasRoot.AddComponent<CanvasGroup>();

            var lobbyT = FantasyUiFactory.FindDeepChild(root.transform, "LobbyLabel");
            _lobbyLabel = lobbyT != null ? lobbyT.GetComponent<TMP_Text>() : null;
            var playersT = FantasyUiFactory.FindDeepChild(root.transform, "PlayersLabel");
            _playersLabel = playersT != null ? playersT.GetComponent<TMP_Text>() : null;
            var hintT = FantasyUiFactory.FindDeepChild(root.transform, "HintLabel");
            _hintLabel = hintT != null ? hintT.GetComponent<TMP_Text>() : null;

            _start = FindBtn("BtnStart");
            _leave = FindBtn("BtnLeave");

            if (_start != null)
            {
                _start.onClick.RemoveAllListeners();
                _start.onClick.AddListener(OnStart);
            }

            if (_leave != null)
            {
                _leave.onClick.RemoveAllListeners();
                _leave.onClick.AddListener(OnLeave);
            }

            _bound = true;
            SetVisible(false);
        }

        void Start()
        {
            if (!_bound)
                Bind(gameObject);

            CoopSessionStarter.PlayersChanged -= OnPlayersChanged;
            CoopSessionStarter.PlayersChanged += OnPlayersChanged;
        }

        void OnDestroy()
        {
            CoopSessionStarter.PlayersChanged -= OnPlayersChanged;
        }

        void Update()
        {
            if (!_bound)
                return;

            var coop = CoopGameController.Instance;
            bool online = CoopSessionStarter.IsOnline;
            bool inLobby = SceneManager.GetActiveScene().name == CaliMainMenuController.LobbySceneName;

            if (!online || coop == null)
            {
                if (inLobby)
                {
                    if (!_visible)
                        SetVisible(true);
                    RefreshLabels();
                }
                else if (_visible)
                {
                    SetVisible(false);
                }

                bool connecting = CoopSessionStarter.BlocksOfflineDualInput;
                SyncHudVisibility(playing: !connecting && !inLobby);
                return;
            }

            int phase = coop.MatchPhase;
            bool waiting = phase == CoopGameController.PhaseWaiting;

            if (waiting != _visible || phase != _lastPhase)
            {
                _lastPhase = phase;
                SetVisible(waiting);
                SyncHudVisibility(playing: phase == CoopGameController.PhasePlaying);

                if (phase == CoopGameController.PhasePlaying)
                    CoopSessionStarter.Instance?.SetGameplayCursor(locked: true);
                else if (phase == CoopGameController.PhaseIntro)
                    CoopSessionStarter.Instance?.SetGameplayCursor(locked: false);
            }

            if (_visible)
                RefreshLabels();
        }

        void OnPlayersChanged()
        {
            if (_visible)
                RefreshLabels();
        }

        void RefreshLabels()
        {
            var starter = CoopSessionStarter.Instance;
            int count = starter != null ? starter.PlayerCount : 0;
            string session = starter != null ? starter.SessionName : "—";
            string region = starter != null ? starter.FixedRegion : "";
            if (string.IsNullOrWhiteSpace(region))
                region = "Best";

            if (_lobbyLabel != null)
                _lobbyLabel.text = $"Room code: {session}  ·  Region: {region}";

            if (_playersLabel != null)
                _playersLabel.text = $"Players: {count} / 2";

            bool isHost = starter != null && starter.IsHost;
            bool canStart = isHost && count >= 1;

            if (_start != null)
            {
                _start.gameObject.SetActive(isHost);
                _start.interactable = canStart;
            }

            if (_hintLabel != null)
            {
                if (!isHost)
                    _hintLabel.text = "Waiting for host to start…";
                else if (count < 2)
                    _hintLabel.text = "Solo: WASD + Arrow keys. Start now, or wait for a friend.";
                else
                    _hintLabel.text = "Both players ready — press Start Match.";
            }
        }

        void OnStart()
        {
            var coop = CoopGameController.Instance;
            if (coop == null)
            {
                SetHint("Game state not ready yet.");
                return;
            }

            if (!coop.RequestStartMatch())
            {
                SetHint("Game state not ready yet.");
                return;
            }

            var starter = CoopSessionStarter.Instance;
            if (starter == null || !starter.TryLoadGameScene())
                SetHint("Could not load the play scene.");
        }

        async void OnLeave()
        {
            SetVisible(false);
            SyncHudVisibility(playing: true);

            if (CoopSessionStarter.Instance != null)
                await CoopSessionStarter.Instance.ShutdownAsync();

            CaliScreenFader.LoadScene(CaliMainMenuController.MenuSceneName);
        }

        void SetHint(string msg)
        {
            if (_hintLabel != null)
                _hintLabel.text = msg ?? "";
        }

        void SetVisible(bool visible)
        {
            _visible = visible;

            if (_canvasRoot == null)
                _canvasRoot = gameObject;

            if (_canvasRoot != null && visible)
                _canvasRoot.SetActive(true);

            if (root != null)
                root.SetActive(visible);

            if (_canvasRoot != null && !visible)
            {
                // Keep controller alive; only hide Content so Update can re-show.
                if (root != null && root != _canvasRoot)
                    _canvasRoot.SetActive(true);
                else
                    _canvasRoot.SetActive(false);
            }

            if (visible)
            {
                if (_raycaster != null)
                    _raycaster.enabled = true;
                if (_canvasGroup != null)
                {
                    _canvasGroup.alpha = 1f;
                    _canvasGroup.interactable = true;
                    _canvasGroup.blocksRaycasts = true;
                }

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                RefreshLabels();
            }
        }

        static void SyncHudVisibility(bool playing)
        {
            var hud = CaliGameplayHud.Instance;
            if (hud == null)
                hud = Object.FindFirstObjectByType<CaliGameplayHud>(FindObjectsInactive.Include);
            if (hud == null)
                return;

            var target = hud.root != null ? hud.root : hud.gameObject;
            if (target != null && target.activeSelf != playing)
                target.SetActive(playing);
        }

        Button FindBtn(string name)
        {
            var t = FantasyUiFactory.FindDeepChild(root.transform, name);
            return t != null ? t.GetComponentInChildren<Button>(true) : null;
        }
    }
}
