using System;
using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Simple horse hit points for HUD bars and spear damage.
    /// </summary>
    public class HorseHealth : MonoBehaviour
    {
        [Min(1f)] public float maxHealth = 100f;
        [SerializeField] float currentHealth = 100f;

        public float CurrentHealth => currentHealth;
        public float Normalized => maxHealth > 0.01f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;
        public bool IsDead => currentHealth <= 0f;
        public int HitCount { get; private set; }

        public event Action<HorseHealth, float, float> Damaged;
        public event Action<HorseHealth> Died;
        public event Action<HorseHealth> Changed;

        void Awake()
        {
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        }

        public void SetMaxHealth(float max, bool fill = true)
        {
            maxHealth = Mathf.Max(1f, max);
            if (fill)
                currentHealth = maxHealth;
            else
                currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
            Changed?.Invoke(this);
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f)
                return;
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            Changed?.Invoke(this);
        }

        public void TakeDamage(float amount)
        {
            if (amount <= 0f || IsDead)
                return;

            float before = currentHealth;
            currentHealth = Mathf.Max(0f, currentHealth - amount);
            HitCount++;
            Damaged?.Invoke(this, before - currentHealth, currentHealth);
            Changed?.Invoke(this);
            CaliPlayerHurtFx.Notify(this);

            if (currentHealth <= 0f)
            {
                Died?.Invoke(this);
                Cali.Gameplay.CoopDeadZone.OnHorseDied();
            }
        }
    }
}
