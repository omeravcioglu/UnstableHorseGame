using Cali.Gameplay;
using Cali.UI;
using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cali.Network
{
    /// <summary>
    /// Starts a Fusion Shared session without reloading the current scene.
    /// The room creator is Master Client and spawns CoopGameState.
    /// </summary>
    public class CoopSessionStarter : MonoBehaviour, INetworkRunnerCallbacks
    {
        public const string DefaultSessionName = "CaliHorseCoop";

        [Header("Refs")]
        [Tooltip("Prefab with NetworkObject + CoopGameController. Auto-created under Resources if missing.")]
        public NetworkObject coopGameStatePrefab;

        [Header("Config")]
        public string sessionName = DefaultSessionName;
        [Tooltip("Legacy OnGUI Host/Join. Leave off when Fantasy main menu / HUD is used.")]
        public bool showGui = false;

        public static CoopSessionStarter Instance { get; private set; }
        public static bool IsOnline => Instance != null && Instance._runner != null && Instance._runner.IsRunning;
        public static NetworkRunner Runner => Instance != null ? Instance._runner : null;
        public static bool IsStarting => Instance != null && Instance._starting;
        /// <summary>True while connecting or online — blocks offline dual horse input.</summary>
        public static bool BlocksOfflineDualInput =>
            Instance != null && (Instance._starting || (Instance._runner != null && Instance._runner.IsRunning));

        public static event System.Action PlayersChanged;

        public string SessionName => sessionName;
        public string FixedRegion => _fixedRegion;
        public int PlayerCount =>
            _runner != null && _runner.IsRunning ? _runner.SessionInfo.PlayerCount : 0;
        public bool IsHost => IsMaster;
        public bool IsMaster => _runner != null && _runner.IsRunning && _runner.IsSharedModeMasterClient;

        NetworkRunner _runner;
        bool _starting;
        string _status = "Offline — local dual keyboard active";
        bool _menuCursorUnlocked = true;
        string _hostPassword = "";
        string _fixedRegion = "";

        public static CoopSessionStarter EnsurePersistent()
        {
            if (Instance != null)
            {
                DontDestroyOnLoad(Instance.gameObject);
                return Instance;
            }

            var go = new GameObject("CoopSessionStarter");
            DontDestroyOnLoad(go);
            return go.AddComponent<CoopSessionStarter>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Play scene keeps SoftHorseChain + LocalDualHorseInput on this same object.
                // Only strip the leftover starter — never Destroy(gameObject).
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            if (coopGameStatePrefab == null)
                coopGameStatePrefab = Resources.Load<NetworkObject>("CoopGameState");
        }

        void Start()
        {
            // Malbers Game Settings locks the cursor; unlock so Host/Join OnGUI is clickable.
            SetMenuCursor(true);
        }

        void Update()
        {
            // Esc cursor unlock is debug-only; Fantasy pause menu owns Esc when showGui is off.
            if (showGui && Input.GetKeyDown(KeyCode.Escape))
                SetMenuCursor(!_menuCursorUnlocked);

            // Keep unlocked while the menu wants mouse clicks (Game Settings only locks once at Awake).
            if (showGui && _menuCursorUnlocked)
                ApplyUnlockedCursor();

            if (_starting)
                return;

            // Keyboard fallback when cursor is locked / Game view steals clicks (debug OnGUI mode).
            if (!showGui)
                return;

            if (!IsOnline)
            {
                if (Input.GetKeyDown(KeyCode.H))
                    StartHost();
                else if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.C))
                    StartClient();
            }
            else if (Input.GetKeyDown(KeyCode.X))
            {
                Shutdown();
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        static void ApplyUnlockedCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void SetMenuCursor(bool unlocked)
        {
            _menuCursorUnlocked = unlocked;
            if (unlocked)
                ApplyUnlockedCursor();
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public void SetGameplayCursor(bool locked)
        {
            SetMenuCursor(!locked);
        }

        public async void StartHost() => await StartSession(create: true, null, null, null);

        public async void StartClient() => await StartSession(create: false, null, null, null);

        public async void StartHost(string region, string lobbyName, string password) =>
            await StartSession(create: true, region, lobbyName, password);

        public async void StartClient(string region, string lobbyName, string password) =>
            await StartSession(create: false, region, lobbyName, password);

        public async void Shutdown() => await ShutdownAsync();

        public async System.Threading.Tasks.Task ShutdownAsync()
        {
            if (_runner != null)
            {
                await _runner.Shutdown();
                if (_runner != null)
                    Destroy(_runner.gameObject);
                _runner = null;
            }

            _status = "Offline — local dual keyboard active";
            _starting = false;
            _hostPassword = "";
            CoopGameController.KeepPlayingAcrossLoad = false;
            SetMenuCursor(true);
        }

        async System.Threading.Tasks.Task StartSession(bool create, string region, string lobbyName, string password)
        {
            if (_starting || IsOnline)
                return;

            if (coopGameStatePrefab == null)
            {
                coopGameStatePrefab = Resources.Load<NetworkObject>("CoopGameState");
                if (coopGameStatePrefab == null)
                {
                    _status = "Missing CoopGameState prefab. Use menu: Cali → Create Coop Prefab";
                    Debug.LogError(_status);
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(lobbyName))
                sessionName = lobbyName.Trim();
            if (string.IsNullOrWhiteSpace(sessionName))
                sessionName = DefaultSessionName;

            _fixedRegion = region ?? "";
            _hostPassword = create ? (password ?? "") : "";
            string joinPassword = create ? "" : (password ?? "");

            _starting = true;
            _status = create ? "Starting Shared room..." : "Joining Shared room...";
            CoopGameController.KeepPlayingAcrossLoad = false;
            if (create)
            {
                CoopSavePoint.ResetForNewHostSession();
                CaliIntroCutscene.ResetForNewSession();
                NetHorseSpawner.Reset();
            }

            var go = new GameObject(create ? "NetworkRunner (Shared Host)" : "NetworkRunner (Shared Join)");
            go.transform.SetParent(transform, false);
            DontDestroyOnLoad(gameObject);
            _runner = go.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;
            _runner.AddCallbacks(this);

            go.AddComponent<NetworkSceneManagerDefault>();
            go.AddComponent<NetworkObjectProviderDefault>();

            bool hasPwd = create && !string.IsNullOrEmpty(_hostPassword);

            Dictionary<string, SessionProperty> props = null;
            if (create)
            {
                props = new Dictionary<string, SessionProperty>
                {
                    [CaliPhotonRegions.HasPasswordProperty] = hasPwd ? 1 : 0,
                    [CaliPhotonRegions.PasswordHashProperty] = CaliPhotonRegions.PasswordHash(_hostPassword)
                };
            }

            // Only send a token when joining with a password. Empty byte[] crashes Fusion connect.
            byte[] token = !create
                ? CaliPhotonRegions.PasswordToToken(joinPassword)
                : null;

            var args = new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = sessionName,
                PlayerCount = 2,
                SessionProperties = props,
                ConnectionToken = token,
                CustomLobbyName = CaliPhotonRegions.SharedLobbyName,
                CustomPhotonAppSettings = CaliPhotonRegions.BuildAppSettings(_fixedRegion),
            };

            var result = await _runner.StartGame(args);

            _starting = false;

            if (result.Ok)
            {
                if (!create && !PasswordAccepted(joinPassword))
                {
                    _status = "Failed: bad password";
                    Debug.LogWarning("[CoopSessionStarter] Join refused — password does not match room.");
                    await ShutdownAsync();
                    SetMenuCursor(true);
                    return;
                }

                _status = (create ? "Shared room" : "Shared join") + " — session '" + sessionName + "'";
                Debug.Log($"[CoopSessionStarter] {_status} region='{_fixedRegion}' master={_runner.IsSharedModeMasterClient}");
                // Keep cursor free for waiting-room UI; gameplay can re-lock after Start.
                SetMenuCursor(true);
                PlayersChanged?.Invoke();
            }
            else
            {
                _status = $"Failed: {result.ShutdownReason}";
                Debug.LogError($"[CoopSessionStarter] StartGame failed: {result.ShutdownReason}");
                if (_runner != null)
                    Destroy(_runner.gameObject);
                _runner = null;
                _hostPassword = "";
                SetMenuCursor(true);
            }
        }

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"[CoopSessionStarter] PlayerJoined {player} (count={runner.SessionInfo.PlayerCount})");

            if (runner.IsSharedModeMasterClient && player == runner.LocalPlayer && CoopGameController.Instance == null)
                SpawnGameState(runner);

            if (player == runner.LocalPlayer)
                NetHorseSpawner.EnsureLocal(runner);

            PlayersChanged?.Invoke();
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"[CoopSessionStarter] PlayerLeft {player}");
            PlayersChanged?.Invoke();
        }

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            // Nothing to send: in Shared mode each peer simulates the horse it owns, so input
            // never travels to a remote State Authority.
        }

        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            _status = $"Shutdown: {shutdownReason}";
            _runner = null;
            _hostPassword = "";
        }

        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }

        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
        {
            if (runner.IsSharedModeMasterClient && !string.IsNullOrEmpty(_hostPassword))
            {
                if (!CaliPhotonRegions.TokenMatchesPassword(token, _hostPassword))
                {
                    Debug.LogWarning("[CoopSessionStarter] Refused join — bad password.");
                    request.Refuse();
                    return;
                }
            }

            request.Accept();
        }

        bool PasswordAccepted(string joinPassword)
        {
            if (_runner == null || !_runner.IsRunning)
                return false;

            var props = _runner.SessionInfo.Properties;
            if (props == null || !props.TryGetValue(CaliPhotonRegions.PasswordHashProperty, out SessionProperty boxed))
                return true;

            int expected = boxed;
            if (expected == 0)
                return true;
            return CaliPhotonRegions.PasswordHash(joinPassword) == expected;
        }

        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) =>
            _status = $"Connect failed: {reason}";

        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public bool TryLoadGameScene()
        {
            if (_runner == null || !_runner.IsRunning || !_runner.IsSharedModeMasterClient)
                return false;

            int buildIndex = CaliSceneIds.FindBuildIndex(CaliMainMenuController.GameSceneName);
            if (buildIndex < 0)
            {
                Debug.LogError("[CoopSessionStarter] " + CaliMainMenuController.GameSceneName + " is not in Build Settings.");
                return false;
            }

            int index = buildIndex;
            CaliScreenFader.RunAfterFadeOut(() =>
            {
                if (_runner == null || !_runner.IsRunning || !_runner.IsSharedModeMasterClient)
                    return;
                CoopGameController.KeepPlayingAcrossLoad = true;
                _runner.LoadScene(SceneRef.FromIndex(index), LoadSceneMode.Single, LocalPhysicsMode.None, true);
            });
            return true;
        }

        NetworkObject SpawnGameState(NetworkRunner runner)
        {
            if (coopGameStatePrefab == null)
                coopGameStatePrefab = Resources.Load<NetworkObject>("CoopGameState");
            if (coopGameStatePrefab == null || runner == null || !runner.IsSharedModeMasterClient)
                return null;

            var obj = runner.Spawn(coopGameStatePrefab, Vector3.zero, Quaternion.identity, runner.LocalPlayer);
            PersistSpawnedState(obj);
            return obj;
        }

        void PersistSpawnedState(NetworkObject obj)
        {
            if (obj == null)
                return;

            obj.transform.SetParent(transform, true);
            DontDestroyOnLoad(obj.gameObject);
        }

        public void OnSceneLoadDone(NetworkRunner runner)
        {
            CaliScreenFader.NotifyNetworkLoadDone();
            StartCoroutine(PrepareGameplayAfterLoad(runner));
        }

        System.Collections.IEnumerator PrepareGameplayAfterLoad(NetworkRunner runner)
        {
            if (runner != null && runner.IsSharedModeMasterClient && CoopGameController.Instance == null)
                SpawnGameState(runner);

            for (int i = 0; i < 20; i++)
            {
                var coop = CoopGameController.Instance;
                if (coop != null)
                {
                    if (runner != null && runner.IsSharedModeMasterClient && CoopGameController.KeepPlayingAcrossLoad)
                        coop.EnsureMatchPlaying();

                    coop.RebindSceneRefs();
                    NetHorseSpawner.EnsureLocal(runner);
                    if (coop.HasPlayHorses)
                    {
                        Debug.Log("[CoopSessionStarter] Play scene horses + chain rebound.");
                        yield break;
                    }
                }

                yield return null;
            }

            Debug.LogWarning("[CoopSessionStarter] Play scene rebind did not find both horses.");
        }

        public void OnSceneLoadStart(NetworkRunner runner)
        {
            CaliScreenFader.NotifyNetworkLoadStart();
        }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

        void OnGUI()
        {
            if (!showGui)
                return;

            const float w = 300f;
            float x = 12f;
            float y = 12f;
            float h = _menuCursorUnlocked ? 190f : 120f;

            GUI.Box(new Rect(x, y, w, h), "Cali Coop (Fusion Shared)");
            y += 28f;
            GUI.Label(new Rect(x + 10f, y, w - 20f, 40f), _status);
            y += 44f;

            if (!_menuCursorUnlocked)
            {
                GUI.Label(new Rect(x + 10f, y, w - 20f, 40f), "Esc = unlock mouse\nH = Host, J = Join, X = Disconnect");
                return;
            }

            GUI.Label(new Rect(x + 10f, y, w - 20f, 20f), "Keys: H Host · J Join · X Disconnect");
            y += 24f;

            GUI.enabled = !IsOnline && !_starting;
            if (GUI.Button(new Rect(x + 10f, y, 130f, 28f), "Start Host"))
                StartHost();
            if (GUI.Button(new Rect(x + 150f, y, 130f, 28f), "Join Client"))
                StartClient();
            y += 36f;

            GUI.enabled = IsOnline;
            if (GUI.Button(new Rect(x + 10f, y, 270f, 28f), "Disconnect"))
                Shutdown();
            GUI.enabled = true;
        }
    }

    public static class CoopInput
    {
        public static bool ReadJumpHeld()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
                return kb.spaceKey.isPressed;
            return Input.GetKey(KeyCode.Space);
        }

        public static bool ReadJumpHeldB()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
                return kb.enterKey.isPressed || kb.numpadEnterKey.isPressed
                    || kb.rightCtrlKey.isPressed || kb.numpad0Key.isPressed;
            return Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)
                || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.Keypad0);
        }

        /// <summary>Horse A / online local — Q requests or accepts a coop save-point.</summary>
        public static bool ReadSavePressed()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.qKey.wasPressedThisFrame)
                return true;
            return Input.GetKeyDown(KeyCode.Q);
        }

        /// <summary>Horse B offline — P (one keyboard cannot tell two Qs apart).</summary>
        public static bool ReadSavePressedB()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.pKey.wasPressedThisFrame)
                return true;
            return Input.GetKeyDown(KeyCode.P);
        }

        /// <summary>Horse A / online local — Left Shift (Right Shift also accepted).</summary>
        public static bool ReadSprintHeld()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
                return kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        /// <summary>Horse B offline — Right Shift (Left Shift also accepted for solo dual-pad).</summary>
        public static bool ReadSprintHeldB()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
                return kb.rightShiftKey.isPressed || kb.leftShiftKey.isPressed;
            return Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.LeftShift);
        }

        /// <summary>
        /// Seat 0 = Horse Realistic (Shared Master Client). Seat 1 = Horse Unicorn (joiner).
        /// </summary>
        public static int GetLocalSeat(NetworkRunner runner)
        {
            if (runner == null || !runner.IsRunning)
                return 0;

            if (runner.IsSharedModeMasterClient)
                return 0;

            return 1;
        }

        /// <summary>Master Client → seat 0, every other player → seat 1+.</summary>
        public static int GetSeatForPlayer(NetworkRunner runner, PlayerRef player)
        {
            if (runner == null || !runner.IsRunning)
                return 0;

            var master = runner.GetMasterClient();
            if (player == master || (player == runner.LocalPlayer && runner.IsSharedModeMasterClient))
                return 0;

            int seat = 1;
            foreach (var p in runner.ActivePlayers)
            {
                if (p == master)
                    continue;
                if (p == player)
                    return seat;
                seat++;
            }

            return 1;
        }

        public static PlayerRef PlayerForSeat(NetworkRunner runner, int seat)
        {
            if (runner == null || !runner.IsRunning)
                return PlayerRef.None;

            var master = runner.GetMasterClient();
            if (seat <= 0)
                return master;

            foreach (var p in runner.ActivePlayers)
            {
                if (p != master)
                    return p;
            }

            return master;
        }

        /// <summary>Convert stick/WASD into a world XZ direction using the local gameplay camera.</summary>
        public static Vector3 CameraRelativeMove(Vector2 move)
        {
            if (move.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            var camTx = ResolveGameplayCamera();
            Vector3 forward = camTx != null ? camTx.forward : Vector3.forward;
            Vector3 right = camTx != null ? camTx.right : Vector3.right;

            forward.y = 0f;
            right.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;

            forward.Normalize();
            right.Normalize();

            var dir = forward * move.y + right * move.x;
            if (dir.sqrMagnitude > 1f)
                dir.Normalize();
            return dir;
        }

        static Transform ResolveGameplayCamera()
        {
            if (Camera.main != null)
                return Camera.main.transform;

            var cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
            return cam != null ? cam.transform : null;
        }

        public static Vector2 ReadWasd()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                float x = 0f, z = 0f;
                if (kb.aKey.isPressed) x -= 1f;
                if (kb.dKey.isPressed) x += 1f;
                if (kb.sKey.isPressed) z -= 1f;
                if (kb.wKey.isPressed) z += 1f;
                return new Vector2(x, z);
            }

            // Fallback if Input System keyboard is unavailable.
            float ox = 0f, oz = 0f;
            if (Input.GetKey(KeyCode.A)) ox -= 1f;
            if (Input.GetKey(KeyCode.D)) ox += 1f;
            if (Input.GetKey(KeyCode.S)) oz -= 1f;
            if (Input.GetKey(KeyCode.W)) oz += 1f;
            return new Vector2(ox, oz);
        }

        public static Vector2 ReadArrows()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                float x = 0f, z = 0f;
                if (kb.leftArrowKey.isPressed) x -= 1f;
                if (kb.rightArrowKey.isPressed) x += 1f;
                if (kb.downArrowKey.isPressed) z -= 1f;
                if (kb.upArrowKey.isPressed) z += 1f;
                return new Vector2(x, z);
            }

            float ox = 0f, oz = 0f;
            if (Input.GetKey(KeyCode.LeftArrow)) ox -= 1f;
            if (Input.GetKey(KeyCode.RightArrow)) ox += 1f;
            if (Input.GetKey(KeyCode.DownArrow)) oz -= 1f;
            if (Input.GetKey(KeyCode.UpArrow)) oz += 1f;
            return new Vector2(ox, oz);
        }
    }
}
