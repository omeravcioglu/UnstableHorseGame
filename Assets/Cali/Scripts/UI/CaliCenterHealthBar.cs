using Cali.Combat;
using Cali.Gameplay;
using Cali.Network;
using MalbersAnimations.Controller;
using UnityEngine;
using UnityEngine.UI;

namespace Cali.UI
{
    /// <summary>
    /// Scene-authored center HP bar. Assign Fill / Slider in the Inspector.
    /// Does not spawn or destroy UI.
    /// Offline: combined HP of both horses. Online: local horse only.
    /// </summary>
    public class CaliCenterHealthBar : MonoBehaviour
    {
        [Tooltip("Filled Image whose fillAmount tracks horse HP.")]
        public Image fill;

        [Tooltip("Optional Slider alternative (or in addition) to the fill Image.")]
        public Slider slider;

        [Tooltip("Leave empty to auto-find Horse Realistic.")]
        public HorseHealth healthA;

        [Tooltip("Leave empty to auto-find Horse Unicorn.")]
        public HorseHealth healthB;

        void OnEnable()
        {
            ResolveHorses();
            Hook();
            Refresh();
        }

        void Start()
        {
            ResolveFillIfNeeded();
            ResolveHorses();
            Hook();
            Refresh();
        }

        void OnDisable()
        {
            Unhook();
        }

        void Update()
        {
            if (healthA == null && healthB == null)
            {
                ResolveHorses();
                if (healthA != null || healthB != null)
                {
                    Hook();
                    Refresh();
                }
            }
        }

        void ResolveFillIfNeeded()
        {
            if (fill != null)
                return;

            var images = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null && images[i].gameObject.name == "Fill")
                {
                    fill = images[i];
                    return;
                }
            }
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

            if (healthA == null)
                healthA = EnsureHealth(animalA);
            if (healthB == null)
                healthB = EnsureHealth(animalB);
        }

        static MAnimal FindAnimal(string nameContains)
        {
            var animals = FindObjectsByType<MAnimal>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < animals.Length; i++)
            {
                var a = animals[i];
                if (a == null)
                    continue;
                if (a.name.Contains(nameContains) && !a.name.Contains("Pegasus"))
                    return a;
            }

            return null;
        }

        static HorseHealth EnsureHealth(MAnimal animal)
        {
            if (animal == null)
                return null;
            var h = animal.GetComponent<HorseHealth>();
            if (h == null)
                h = animal.gameObject.AddComponent<HorseHealth>();
            return h;
        }

        void Hook()
        {
            if (healthA != null)
            {
                healthA.Changed -= OnHealthChanged;
                healthA.Changed += OnHealthChanged;
            }

            if (healthB != null)
            {
                healthB.Changed -= OnHealthChanged;
                healthB.Changed += OnHealthChanged;
            }
        }

        void Unhook()
        {
            if (healthA != null)
                healthA.Changed -= OnHealthChanged;
            if (healthB != null)
                healthB.Changed -= OnHealthChanged;
        }

        void OnHealthChanged(HorseHealth _) => Refresh();

        void Refresh()
        {
            float n = ReadNormalized();
            if (fill != null)
                fill.fillAmount = n;
            if (slider != null)
                slider.value = n;
        }

        float ReadNormalized()
        {
            if (CoopSessionStarter.IsOnline)
            {
                int seat = CoopInput.GetLocalSeat(CoopSessionStarter.Runner);
                var h = seat == 0 ? healthA : healthB;
                return h != null ? h.Normalized : 1f;
            }

            float cur = 0f;
            float max = 0f;
            if (healthA != null)
            {
                cur += healthA.CurrentHealth;
                max += healthA.maxHealth;
            }

            if (healthB != null)
            {
                cur += healthB.CurrentHealth;
                max += healthB.maxHealth;
            }

            return max > 0.01f ? Mathf.Clamp01(cur / max) : 1f;
        }
    }
}
