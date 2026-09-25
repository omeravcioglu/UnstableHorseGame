using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cali.Network;
using Fusion.Matchmaking;
using Fusion.Photon.Realtime;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>
    /// Join browser via Photon Realtime lobby (avoids NetworkRunner.JoinSessionLobby setup NREs).
    /// Lists Fusion ClientServer sessions for the selected region.
    /// </summary>
    public class CaliJoinBrowserPanel : MonoBehaviour
    {
        public GameObject root;

        GameObject _canvasRoot;
        GraphicRaycaster _raycaster;
        CanvasGroup _canvasGroup;
        bool _bound;

        Transform _roomListContent;
        TMP_InputField _roomCode;
        TMP_InputField _password;
        TMP_Dropdown _region;
        TMP_Text _status;
        Button _refresh;
        Button _joinCode;
        Button _join;
        Button _back;

        LobbyBrowseDriver _driver;
        readonly List<RoomInfo> _rooms = new List<RoomInfo>();
        int _selectedIndex = -1;
        bool _busy;
        bool _refreshQueued;
        bool _suppressRegionRefresh;
        string _browseRegion = "";
        string _connectedRegion = "";
        int _refreshGen;

        public bool IsOpen =>
            _canvasRoot != null && _canvasRoot.activeSelf && root != null && root.activeSelf;

        public void Bind(GameObject joinRoot)
        {
            _canvasRoot = joinRoot != null ? joinRoot : gameObject;
            var content = _canvasRoot.transform.Find("Content");
            root = content != null ? content.gameObject : _canvasRoot;

            _raycaster = _canvasRoot.GetComponent<GraphicRaycaster>();
            _canvasGroup = _canvasRoot.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = _canvasRoot.AddComponent<CanvasGroup>();

            var list = FantasyUiFactory.FindDeepChild(root.transform, "RoomList");
            if (list != null)
            {
                var scroll = list.GetComponent<ScrollRect>();
                _roomListContent = scroll != null && scroll.content != null
                    ? scroll.content
                    : FantasyUiFactory.FindDeepChild(list, "Content");
            }

            _roomCode = FindInput("RoomCode");
            _password = FindInput("Password");
            _region = FindDropdown("RegionDropdown");
            var statusT = FantasyUiFactory.FindDeepChild(root.transform, "StatusLabel");
            _status = statusT != null ? statusT.GetComponent<TMP_Text>() : null;
            _refresh = FindBtn("BtnRefresh");
            _joinCode = FindBtn("BtnJoinCode");
            _join = FindBtn("BtnJoinRoom");
            _back = FindBtn("BtnBack");

            if (_refresh != null)
            {
                _refresh.onClick.RemoveAllListeners();
                _refresh.onClick.AddListener(() => { RefreshLobbyAsync(); });
            }

            if (_joinCode != null)
            {
                _joinCode.onClick.RemoveAllListeners();
                _joinCode.onClick.AddListener(OnJoinByCode);
            }

            if (_join != null)
            {
                _join.onClick.RemoveAllListeners();
                _join.onClick.AddListener(OnJoinSelected);
            }

            if (_back != null)
            {
                _back.onClick.RemoveAllListeners();
                _back.onClick.AddListener(OnBackClicked);
            }

            if (_region != null)
            {
                _region.onValueChanged.RemoveAllListeners();
                _region.onValueChanged.AddListener(_ =>
                {
                    if (_suppressRegionRefresh)
                        return;
                    RefreshLobbyAsync();
                });
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

            _suppressRegionRefresh = true;
            SetVisible(true, preserveAlpha: true);
            SetStatus("Enter the host’s exact room code + same region, or browse the list.");
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _suppressRegionRefresh = false;

            _ = RefreshLobbyAsync();
            _ = WarmPingsAndRefreshRowsAsync();
        }

        public void NotifyClosedByDirector()
        {
            _refreshGen++;
            _suppressRegionRefresh = true;
            _refreshQueued = false;
            _ = ShutdownBrowserAsync();
            if (_canvasGroup != null)
            {
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
            _suppressRegionRefresh = false;
        }

        void Start()
        {
            if (!_bound)
                Bind(gameObject);
        }

        void OnDestroy()
        {
            _ = ShutdownBrowserAsync();
        }

        public async void Show()
        {
            if (!_bound)
                Bind(_canvasRoot != null ? _canvasRoot : gameObject);

            _suppressRegionRefresh = true;
            SetVisible(true, preserveAlpha: false);
            SetStatus("Enter the host’s exact room code + same region, or browse the list.");
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _suppressRegionRefresh = false;

            await RefreshLobbyAsync();
            _ = WarmPingsAndRefreshRowsAsync();
        }

        async Task WarmPingsAndRefreshRowsAsync()
        {
            try
            {
                await CaliRegionPingCache.EnsurePingedAsync();
                if (_rooms.Count > 0)
                    RebuildRows();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CaliJoinBrowserPanel] Ping warm-up: {e.Message}");
            }
        }

        public async void Hide()
        {
            _refreshGen++;
            _suppressRegionRefresh = true;
            _refreshQueued = false;
            await ShutdownBrowserAsync();
            SetVisible(false, preserveAlpha: false);
            _suppressRegionRefresh = false;
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

        async Task RefreshLobbyAsync()
        {
            if (_busy)
            {
                _refreshQueued = true;
                return;
            }

            _busy = true;
            try
            {
                do
                {
                    _refreshQueued = false;
                    await RefreshLobbyOnceAsync();
                } while (_refreshQueued);
            }
            finally
            {
                _busy = false;
            }
        }

        async Task RefreshLobbyOnceAsync()
        {
            int gen = ++_refreshGen;
            SetStatus("Connecting to lobby…");
            _selectedIndex = -1;
            ClearRows();
            _rooms.Clear();

            try
            {
                await ShutdownBrowserAsync();

                _browseRegion = "";
                if (_region != null && _region.value >= 0 && _region.value < CaliPhotonRegions.Options.Length)
                    _browseRegion = CaliPhotonRegions.Options[_region.value].Code ?? "";

                if (!PhotonAppSettings.TryGetGlobal(out var global) || global?.AppSettings == null ||
                    string.IsNullOrWhiteSpace(global.AppSettings.AppIdFusion))
                {
                    SetStatus("Photon AppIdFusion missing. Open PhotonAppSettings and paste your Fusion App Id.");
                    return;
                }

                var appSettings = CaliPhotonRegions.BuildAppSettings(_browseRegion);
                if (appSettings == null)
                {
                    SetStatus("Could not build Photon settings.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(appSettings.FixedRegion))
                    appSettings.FixedRegion = null;

                await CaliPhotonCloudGate.RunAsync(async () =>
                {
                    if (gen != _refreshGen)
                        return;

                    var go = new GameObject("<<<Cali Lobby Browser>>>");
                    DontDestroyOnLoad(go);
                    _driver = go.AddComponent<LobbyBrowseDriver>();
                    _driver.OnRoomsUpdated = OnRoomsUpdated;
                    _driver.OnFailed = msg =>
                    {
                        if (gen == _refreshGen)
                            SetStatus($"Lobby failed: {msg}");
                    };

                    RealtimeClient client;
                    try
                    {
                        // Public Fusion helper (RealtimeClientExtensions.SetupForFusion is internal).
                        client = MatchmakingArgumentsExtensions.BuildRealtimeClient(appSettings);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[CaliJoinBrowserPanel] BuildRealtimeClient failed: {e.Message}");
                        client = new RealtimeClient();
                    }

                    _driver.Client = client;
                    client.AddCallbackTarget(_driver);

                    try
                    {
                        await client.ConnectUsingSettingsAsync(appSettings);
                        if (gen != _refreshGen)
                            return;

                        _connectedRegion = client.CurrentRegion ?? _browseRegion ?? "";
                        var lobby = new TypedLobby(CaliPhotonRegions.SharedLobbyName, LobbyType.Default);
                        short code = await client.JoinLobbyAsync(lobby);
                        if (gen != _refreshGen)
                            return;

                        if (code != ErrorCode.Ok)
                        {
                            SetStatus($"Lobby failed: Photon error {code}");
                            await ShutdownBrowserAsync();
                            return;
                        }

                        SetStatus($"In lobby ({_connectedRegion}). Waiting for rooms… Host with the same region.");
                    }
                    catch (Exception e)
                    {
                        if (gen == _refreshGen)
                        {
                            SetStatus($"Lobby failed: {e.Message}");
                            Debug.LogWarning($"[CaliJoinBrowserPanel] {e}");
                        }

                        await ShutdownBrowserAsync();
                    }
                });
            }
            catch (Exception e)
            {
                if (gen == _refreshGen)
                {
                    SetStatus($"Lobby error: {e.Message}");
                    Debug.LogWarning($"[CaliJoinBrowserPanel] {e}");
                }
            }
        }

        void OnRoomsUpdated(List<RoomInfo> roomList)
        {
            _rooms.Clear();
            if (roomList != null)
            {
                foreach (var room in roomList)
                {
                    if (room == null || room.RemovedFromList)
                        continue;
                    if (!room.IsVisible || !room.IsOpen)
                        continue;
                    if (string.IsNullOrEmpty(room.Name))
                        continue;
                    _rooms.Add(room);
                }
            }

            // Stable order for selection indices.
            _rooms.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            RebuildRows();
            SetStatus(_rooms.Count == 0
                ? $"No lobbies in '{_connectedRegion}' yet. Host with the same region."
                : $"Found {_rooms.Count} lobby(s) in '{_connectedRegion}'. Select one and Join.");
        }

        async Task ShutdownBrowserAsync()
        {
            var driver = _driver;
            _driver = null;
            if (driver == null)
                return;

            try
            {
                driver.OnRoomsUpdated = null;
                driver.OnFailed = null;
                var client = driver.Client;
                if (client != null)
                {
                    client.RemoveCallbackTarget(driver);
                    if (client.IsConnected)
                        await client.DisconnectAsync();
                }
            }
            catch
            {
                // ignored
            }

            if (driver != null && driver.gameObject != null)
                Destroy(driver.gameObject);
        }

        void OnJoinByCode()
        {
            string code = _roomCode != null ? _roomCode.text.Trim() : "";
            if (string.IsNullOrEmpty(code))
            {
                SetStatus("Enter the host’s room code (exact lobby name).");
                return;
            }

            string region = "";
            if (_region != null && _region.value >= 0 && _region.value < CaliPhotonRegions.Options.Length)
                region = CaliPhotonRegions.Options[_region.value].Code ?? "";

            if (string.IsNullOrEmpty(region))
            {
                SetStatus("Pick the same fixed region as the host (not Best).");
                return;
            }

            string password = _password != null ? _password.text : "";
            CaliPendingSession.SetClient(region, code, password);
            _ = ShutdownBrowserAsync();
            CaliScreenFader.LoadScene(CaliMainMenuController.LobbySceneName);
        }

        void OnJoinSelected()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _rooms.Count)
            {
                SetStatus("Select a lobby first.");
                return;
            }

            var room = _rooms[_selectedIndex];
            bool locked = RoomHasPassword(room);
            string password = _password != null ? _password.text : "";
            if (locked && string.IsNullOrEmpty(password))
            {
                SetStatus("This lobby needs a password.");
                return;
            }

            string region = !string.IsNullOrEmpty(_connectedRegion) ? _connectedRegion : _browseRegion;
            int slash = region != null ? region.IndexOf('/') : -1;
            if (slash > 0)
                region = region.Substring(0, slash);

            if (string.IsNullOrEmpty(region))
            {
                SetStatus("Pick the same fixed region as the host (not Best).");
                return;
            }

            CaliPendingSession.SetClient(region, room.Name, password);
            _ = ShutdownBrowserAsync();
            CaliScreenFader.LoadScene(CaliMainMenuController.LobbySceneName);
        }

        void RebuildRows()
        {
            ClearRows();
            if (_roomListContent == null)
                return;

            for (int i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];
                string region = string.IsNullOrEmpty(_connectedRegion)
                    ? (_browseRegion.Length == 0 ? "?" : _browseRegion)
                    : _connectedRegion;
                int ping = CaliRegionPingCache.GetPingMs(region);
                string pingText = ping >= 0 ? $"{ping} ms" : "—";
                string players = $"{room.PlayerCount}/{room.MaxPlayers}";
                bool locked = RoomHasPassword(room);
                var row = FantasyUiFactory.CreateRoomRow(_roomListContent, room.Name, players, region, pingText, locked);
                int index = i;
                var btn = row.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.AddListener(() =>
                    {
                        _selectedIndex = index;
                        HighlightSelection();
                    });
                }
            }

            HighlightSelection();
        }

        void HighlightSelection()
        {
            if (_roomListContent == null)
                return;
            for (int i = 0; i < _roomListContent.childCount; i++)
            {
                var img = _roomListContent.GetChild(i).GetComponent<Image>();
                if (img == null) continue;
                img.color = i == _selectedIndex
                    ? new Color(0.45f, 0.35f, 0.18f, 1f)
                    : new Color(0.18f, 0.16f, 0.22f, 1f);
            }
        }

        void ClearRows()
        {
            if (_roomListContent == null)
                return;
            for (int i = _roomListContent.childCount - 1; i >= 0; i--)
                Destroy(_roomListContent.GetChild(i).gameObject);
        }

        static bool RoomHasPassword(RoomInfo room)
        {
            if (room?.CustomProperties == null)
                return false;
            if (!room.CustomProperties.TryGetValue(CaliPhotonRegions.HasPasswordProperty, out object value) || value == null)
                return false;
            if (value is int i)
                return i != 0;
            if (value is bool b)
                return b;
            string s = value.ToString();
            return s == "1" || s == "True" || s == "true";
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

        /// <summary>
        /// Drives RealtimeClient.Service while lobby browsing stays open.
        /// </summary>
        class LobbyBrowseDriver : MonoBehaviour, ILobbyCallbacks, IConnectionCallbacks
        {
            public RealtimeClient Client;
            public Action<List<RoomInfo>> OnRoomsUpdated;
            public Action<string> OnFailed;

            readonly Dictionary<string, RoomInfo> _cache = new Dictionary<string, RoomInfo>();

            void Update()
            {
                Client?.Service();
            }

            public void OnJoinedLobby()
            {
                // Room list may arrive via OnRoomListUpdate immediately after.
            }

            public void OnLeftLobby() { }

            public void OnRoomListUpdate(List<RoomInfo> roomList)
            {
                if (roomList == null)
                    return;

                foreach (var info in roomList)
                {
                    if (info == null || string.IsNullOrEmpty(info.Name))
                        continue;
                    if (info.RemovedFromList)
                        _cache.Remove(info.Name);
                    else
                        _cache[info.Name] = info;
                }

                OnRoomsUpdated?.Invoke(new List<RoomInfo>(_cache.Values));
            }

            public void OnLobbyStatisticsUpdate(List<TypedLobbyInfo> lobbyStatistics) { }

            public void OnConnected() { }
            public void OnConnectedToMaster() { }
            public void OnDisconnected(DisconnectCause cause)
            {
                if (cause != DisconnectCause.DisconnectByClientLogic && cause != DisconnectCause.None)
                    OnFailed?.Invoke(cause.ToString());
            }

            public void OnRegionListReceived(RegionHandler regionHandler) { }
            public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }
            public void OnCustomAuthenticationFailed(string debugMessage) => OnFailed?.Invoke(debugMessage);
        }
    }
}
