using Cali.Network;
using Cali.UI;
using MalbersAnimations.Controller;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Cali.Combat
{
    /// <summary>
    /// Screen blood overlay on every 4th spear hit (4, 8, 12…).
    /// Online: only the local player's horse. Offline: either horse.
    /// </summary>
    public class CaliPlayerHurtFx : MonoBehaviour
    {
        public const int HitsBeforeOverlay = 4;
        static readonly Color HurtRed = new Color(0.92f, 0.07f, 0.06f, 1f);

        static readonly string[] PrefabResources =
        {
            "UI/BloodScreen_01",
            "UI/BloodScreen_02",
            "UI/BloodScreen_03",
            "UI/BloodScreen_04"
        };

        public static CaliPlayerHurtFx Instance { get; private set; }

        Canvas _canvas;
        GameObject[] _screens;
        Animator[] _anims;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            string n = SceneManager.GetActiveScene().name;
            if (!string.IsNullOrEmpty(n) &&
                (n.IndexOf("Menu", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                 n.IndexOf("Lobby", System.StringComparison.OrdinalIgnoreCase) >= 0))
                return;
            Ensure();
        }

        public static CaliPlayerHurtFx Ensure()
        {
            if (Instance != null)
                return Instance;

            var existing = FindFirstObjectByType<CaliPlayerHurtFx>();
            if (existing != null)
            {
                Instance = existing;
                existing.BuildIfNeeded();
                return existing;
            }

            var go = new GameObject("CaliPlayerHurtFx");
            DontDestroyOnLoad(go);
            return go.AddComponent<CaliPlayerHurtFx>();
        }

        public static void Notify(HorseHealth health)
        {
            if (health == null || health.HitCount <= 0 || health.HitCount % HitsBeforeOverlay != 0)
                return;
            if (!IsLocalView(health))
                return;
            Ensure().Play();
        }

        void Awake()
        {
            Instance = this;
            BuildIfNeeded();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void BuildIfNeeded()
        {
            if (_canvas != null)
                return;

            _canvas = FantasyUiFactory.CreateCanvas("HurtBloodCanvas", 95);
            _canvas.transform.SetParent(transform, false);
            var raycaster = _canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
                raycaster.enabled = false;
            DontDestroyOnLoad(_canvas.gameObject);

            _screens = new GameObject[PrefabResources.Length];
            _anims = new Animator[PrefabResources.Length];
            for (int i = 0; i < PrefabResources.Length; i++)
            {
                var prefab = Resources.Load<GameObject>(PrefabResources[i]);
                if (prefab == null)
                {
                    Debug.LogWarning($"[CaliPlayerHurtFx] Missing Resources/{PrefabResources[i]}.prefab", this);
                    continue;
                }

                var inst = Instantiate(prefab, _canvas.transform, false);
                inst.name = prefab.name;
                var rt = inst.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }

                var images = inst.GetComponentsInChildren<Image>(true);
                for (int im = 0; im < images.Length; im++)
                {
                    if (images[im] == null)
                        continue;
                    images[im].raycastTarget = false;
                    TintHurtRed(images[im]);
                }

                inst.SetActive(false);
                _screens[i] = inst;
                _anims[i] = inst.GetComponent<Animator>() ?? inst.GetComponentInChildren<Animator>(true);
            }
        }

        void Play()
        {
            BuildIfNeeded();
            if (_screens == null)
                return;

            int pick = -1;
            int guard = 0;
            while (guard++ < 8)
            {
                int i = Random.Range(0, _screens.Length);
                if (_screens[i] != null)
                {
                    pick = i;
                    break;
                }
            }

            if (pick < 0)
                return;

            for (int i = 0; i < _screens.Length; i++)
            {
                if (_screens[i] != null)
                    _screens[i].SetActive(i == pick);
            }

            var anim = _anims[pick];
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                anim.enabled = true;
                anim.Play("Activate", 0, 0f);
            }

            var images = _screens[pick].GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                TintHurtRed(images[i]);
        }

        static void TintHurtRed(Image image)
        {
            if (image == null)
                return;
            image.color = HurtRed;
            if (image.material == null)
                return;
            if (!image.material.name.Contains("Instance"))
                image.material = new Material(image.material);
            if (image.material.HasProperty("_Color"))
                image.material.SetColor("_Color", HurtRed);
            if (image.material.HasProperty("_BaseColor"))
                image.material.SetColor("_BaseColor", HurtRed);
        }

        static bool IsLocalView(HorseHealth health)
        {
            var coop = CoopGameController.Instance;
            if (coop == null || coop.Object == null || !coop.Object.IsValid || coop.Runner == null)
                return true;

            var animal = health.GetComponent<MAnimal>() ?? health.GetComponentInParent<MAnimal>();
            if (animal == null)
                return true;

            int seat = CoopInput.GetLocalSeat(coop.Runner);
            bool isA = animal.gameObject.name.Contains("Horse Realistic") &&
                       !animal.gameObject.name.Contains("Pegasus");
            bool isB = animal.gameObject.name.Contains("Horse Unicorn");
            return seat == 0 ? isA : isB;
        }
    }
}
