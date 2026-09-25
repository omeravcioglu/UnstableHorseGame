using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Shared coop XP / level pool. Host (or offline) owns writes; networked values
    /// live on <see cref="Cali.Network.CoopGameController"/> when online.
    /// </summary>
    public class PlayerXp : MonoBehaviour
    {
        public static PlayerXp Instance { get; private set; }

        [Tooltip("XP required for level 1→2; scales by level.")]
        public int baseXpPerLevel = 50;

        [SerializeField] int _xp;
        [SerializeField] int _level = 1;

        public int Xp => IsOnlineAuthority()
            ? (Cali.Network.CoopGameController.Instance != null
                ? Cali.Network.CoopGameController.Instance.SharedXp
                : _xp)
            : _xp;

        public int Level => IsOnlineAuthority()
            ? (Cali.Network.CoopGameController.Instance != null
                ? Cali.Network.CoopGameController.Instance.SharedLevel
                : _level)
            : _level;

        public int XpToNextLevel => Mathf.Max(1, baseXpPerLevel * Level);

        public event System.Action<int, int> Changed;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public static PlayerXp EnsureExists()
        {
            if (Instance != null)
                return Instance;

            var existing = FindFirstObjectByType<PlayerXp>();
            if (existing != null)
                return existing;

            var go = new GameObject("<<<Player XP>>>");
            return go.AddComponent<PlayerXp>();
        }

        public void AddXp(int amount)
        {
            if (amount <= 0)
                return;

            if (Cali.Network.CoopSessionStarter.IsOnline)
            {
                var runner = Cali.Network.CoopSessionStarter.Runner;
                if (runner != null && !runner.IsSharedModeMasterClient)
                    return;

                var coop = Cali.Network.CoopGameController.Instance;
                if (coop != null)
                {
                    coop.ServerAddXp(amount, baseXpPerLevel);
                    Changed?.Invoke(coop.SharedXp, coop.SharedLevel);
                    return;
                }
            }

            _xp += amount;
            while (_xp >= XpToNextLevel)
            {
                _xp -= XpToNextLevel;
                _level++;
            }

            Changed?.Invoke(_xp, _level);
        }

        public void SetProgress(int xp, int level)
        {
            _xp = Mathf.Max(0, xp);
            _level = Mathf.Max(1, level);
            Changed?.Invoke(_xp, _level);
        }

        public void NotifyChanged()
        {
            Changed?.Invoke(Xp, Level);
        }

        static bool IsOnlineAuthority()
        {
            return Cali.Network.CoopSessionStarter.IsOnline
                   && Cali.Network.CoopGameController.Instance != null;
        }

        [Tooltip("When false, OnGUI XP is hidden (Fantasy HUD owns the readout).")]
        public bool showDebugGui;

        void OnGUI()
        {
            if (!showDebugGui && Cali.UI.CaliGameplayHud.HudOwnsXpDisplay)
                return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold
            };
            style.normal.textColor = new Color(0.35f, 1f, 0.45f, 1f);
            GUI.Label(new Rect(16f, 16f, 320f, 28f), $"Level {Level}   XP {Xp}/{XpToNextLevel}", style);
        }
    }
}
