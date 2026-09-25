using Cali.Combat;
using Cali.Gameplay;
using MalbersAnimations.Controller;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>
    /// Binds dual horse health bars + avatar slots + XP readout.
    /// </summary>
    public class CaliGameplayHud : MonoBehaviour
    {
        public static CaliGameplayHud Instance { get; private set; }

        public GameObject root;
        public HorseHealth healthA;
        public HorseHealth healthB;
        public HorseIdentity identityA;
        public HorseIdentity identityB;

        Image _fillA;
        Image _fillB;
        Slider _sliderA;
        Slider _sliderB;
        Image _portraitA;
        Image _portraitB;
        TMP_Text _nameA;
        TMP_Text _nameB;
        TMP_Text _xpLabel;
        GameObject _highlightA;
        GameObject _highlightB;
        CaliAlterHorsePanel _alterPanel;

        public static bool HudOwnsXpDisplay => Instance != null && Instance.isActiveAndEnabled;

        public void Bind(GameObject hudRoot)
        {
            root = hudRoot;
            CacheUi();
            ResolveHorses();
            WireAvatars();
            RefreshAll();
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            Unhook(healthA);
            Unhook(healthB);
            if (identityA != null) identityA.Changed -= OnIdentityChanged;
            if (identityB != null) identityB.Changed -= OnIdentityChanged;
            if (PlayerXp.Instance != null)
                PlayerXp.Instance.Changed -= OnXpChanged;
        }

        void Start()
        {
            if (root == null)
                root = gameObject;
            if (_fillA == null && _sliderA == null)
                Bind(root);

            PlayerXp.EnsureExists();
            if (PlayerXp.Instance != null)
            {
                PlayerXp.Instance.Changed -= OnXpChanged;
                PlayerXp.Instance.Changed += OnXpChanged;
                OnXpChanged(PlayerXp.Instance.Xp, PlayerXp.Instance.Level);
            }

            _alterPanel = FindFirstObjectByType<CaliAlterHorsePanel>(FindObjectsInactive.Include);
        }

        void Update()
        {
            UpdateLocalHighlight();
        }

        void CacheUi()
        {
            var barA = FantasyUiFactory.FindDeepChild(root.transform, "HealthBarA")
                       ?? FantasyUiFactory.FindDeepChild(root.transform, "HeartA");
            var barB = FantasyUiFactory.FindDeepChild(root.transform, "HealthBarB")
                       ?? FantasyUiFactory.FindDeepChild(root.transform, "HeartB");
            _fillA = FantasyUiFactory.FindFillImage(barA != null ? barA.gameObject : null);
            _fillB = FantasyUiFactory.FindFillImage(barB != null ? barB.gameObject : null);
            _sliderA = barA != null ? barA.GetComponentInChildren<Slider>(true) : null;
            _sliderB = barB != null ? barB.GetComponentInChildren<Slider>(true) : null;

            var avatarA = FantasyUiFactory.FindDeepChild(root.transform, "AvatarA");
            var avatarB = FantasyUiFactory.FindDeepChild(root.transform, "AvatarB");
            _portraitA = FantasyUiFactory.FindAvatarIcon(avatarA != null ? avatarA.gameObject : null);
            _portraitB = FantasyUiFactory.FindAvatarIcon(avatarB != null ? avatarB.gameObject : null);
            _nameA = avatarA != null ? avatarA.GetComponentInChildren<TMP_Text>(true) : null;
            _nameB = avatarB != null ? avatarB.GetComponentInChildren<TMP_Text>(true) : null;

            var xp = FantasyUiFactory.FindDeepChild(root.transform, "XpLabel");
            _xpLabel = xp != null ? xp.GetComponent<TMP_Text>() : null;

            var hA = FantasyUiFactory.FindDeepChild(root.transform, "LocalSeatHighlightA");
            var hB = FantasyUiFactory.FindDeepChild(root.transform, "LocalSeatHighlightB");
            _highlightA = hA != null ? hA.gameObject : null;
            _highlightB = hB != null ? hB.gameObject : null;
        }

        void ResolveHorses()
        {
            var dual = FindFirstObjectByType<LocalDualHorseInput>();
            MAnimal animalA = dual != null ? dual.horseA : null;
            MAnimal animalB = dual != null ? dual.horseB : null;

            if (animalA == null)
                animalA = FindAnimal("Horse Realistic");
            if (animalB == null)
                animalB = FindAnimal("Horse Unicorn");

            healthA = EnsureHealth(animalA);
            healthB = EnsureHealth(animalB);
            identityA = EnsureIdentity(animalA, HorseIdentity.KeyA, "Ashmane");
            identityB = EnsureIdentity(animalB, HorseIdentity.KeyB, "Starfall");

            Hook(healthA, OnHealthA);
            Hook(healthB, OnHealthB);
            if (identityA != null)
            {
                identityA.Changed -= OnIdentityChanged;
                identityA.Changed += OnIdentityChanged;
            }

            if (identityB != null)
            {
                identityB.Changed -= OnIdentityChanged;
                identityB.Changed += OnIdentityChanged;
            }
        }

        static MAnimal FindAnimal(string nameContains)
        {
            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var a in animals)
            {
                if (a == null) continue;
                if (a.name.Contains(nameContains) && !a.name.Contains("Pegasus"))
                    return a;
            }

            return null;
        }

        static HorseHealth EnsureHealth(MAnimal animal)
        {
            if (animal == null) return null;
            var h = animal.GetComponent<HorseHealth>();
            if (h == null)
                h = animal.gameObject.AddComponent<HorseHealth>();
            return h;
        }

        static HorseIdentity EnsureIdentity(MAnimal animal, string key, string defaultName)
        {
            if (animal == null) return null;
            var id = animal.GetComponent<HorseIdentity>();
            if (id == null)
                id = animal.gameObject.AddComponent<HorseIdentity>();
            id.horseKey = key;
            if (string.IsNullOrEmpty(id.displayName) || id.displayName == "Horse")
                id.displayName = defaultName;
            if (id.portrait == null)
                id.portrait = FantasyUiFactory.LoadPortrait(key == HorseIdentity.KeyB ? "hero_03" : "hero_02");
            id.Load();
            return id;
        }

        void WireAvatars()
        {
            var avatarA = FantasyUiFactory.FindDeepChild(root.transform, "AvatarA");
            var avatarB = FantasyUiFactory.FindDeepChild(root.transform, "AvatarB");
            WireAvatarButton(avatarA, identityA);
            WireAvatarButton(avatarB, identityB);
        }

        void WireAvatarButton(Transform avatar, HorseIdentity identity)
        {
            if (avatar == null || identity == null) return;
            var btn = avatar.GetComponent<Button>();
            if (btn == null)
                btn = avatar.gameObject.AddComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OpenAlter(identity));
        }

        void OpenAlter(HorseIdentity identity)
        {
            if (_alterPanel == null)
                _alterPanel = FindFirstObjectByType<CaliAlterHorsePanel>(FindObjectsInactive.Include);
            if (_alterPanel != null)
                _alterPanel.Open(identity);
        }

        void Hook(HorseHealth h, System.Action<HorseHealth> handler)
        {
            if (h == null) return;
            h.Changed -= handler;
            h.Changed += handler;
        }

        void Unhook(HorseHealth h)
        {
            if (h == null) return;
            h.Changed -= OnHealthA;
            h.Changed -= OnHealthB;
        }

        void OnHealthA(HorseHealth h) => ApplyHealth(_fillA, _sliderA, h);
        void OnHealthB(HorseHealth h) => ApplyHealth(_fillB, _sliderB, h);

        void OnIdentityChanged(HorseIdentity _) => RefreshIdentities();

        void OnXpChanged(int xp, int level)
        {
            if (_xpLabel == null) return;
            int toNext = PlayerXp.Instance != null ? PlayerXp.Instance.XpToNextLevel : 50;
            _xpLabel.text = $"Level {level}   XP {xp}/{toNext}";
        }

        void RefreshAll()
        {
            ApplyHealth(_fillA, _sliderA, healthA);
            ApplyHealth(_fillB, _sliderB, healthB);
            RefreshIdentities();
            if (PlayerXp.Instance != null)
                OnXpChanged(PlayerXp.Instance.Xp, PlayerXp.Instance.Level);
        }

        void RefreshIdentities()
        {
            ApplyIdentity(_portraitA, _nameA, identityA);
            ApplyIdentity(_portraitB, _nameB, identityB);
        }

        static void ApplyHealth(Image fill, Slider slider, HorseHealth health)
        {
            float n = health != null ? health.Normalized : 1f;
            if (slider != null)
                slider.value = n;
            if (fill != null)
                fill.fillAmount = n;
        }

        static void ApplyIdentity(Image portrait, TMP_Text name, HorseIdentity id)
        {
            if (id == null) return;
            if (name != null)
                name.text = id.displayName;
            if (portrait != null && id.portrait != null)
                portrait.sprite = id.portrait;
        }

        void UpdateLocalHighlight()
        {
            bool online = Cali.Network.CoopSessionStarter.IsOnline;
            int seat = 0;
            if (online)
            {
                var runner = Cali.Network.CoopSessionStarter.Runner;
                seat = Cali.Network.CoopInput.GetLocalSeat(runner);
            }

            // Offline: show both without exclusive highlight. Online: highlight local seat.
            if (_highlightA != null)
                _highlightA.SetActive(online && seat == 0);
            if (_highlightB != null)
                _highlightB.SetActive(online && seat == 1);
        }
    }
}
