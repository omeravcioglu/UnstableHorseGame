using System.Collections;
using Cali.Audio;
using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Simple flying spear. Hits horses for damage only (no knockback / yaw).
    /// Sticks in the horse for a few seconds, then fades.
    /// </summary>
    public class SpearProjectile : MonoBehaviour
    {
        public float life = 5f;
        public float hitForce = 0f;
        public float hitForceAccel = 0f;
        [Tooltip("Health removed from HorseHealth on hit.")]
        public float damage = 4f;
        public float stickSeconds = 4.5f;
        public float fadeSeconds = 0.4f;
        [Tooltip("How far the tip sits inside the horse. Shaft stays outside.")]
        public float tipEmbed = 0.18f;
        [Tooltip("Client copies of host throws. Visual only — no horse damage.")]
        public bool visualOnly;

        Vector3 _velocity;
        Transform _thrower;
        bool _launched;
        bool _stuck;
        float _spawnTime;

        public void Launch(Vector3 direction, float speed, Transform thrower)
        {
            _velocity = direction.normalized * speed;
            _thrower = thrower;
            _launched = true;
            _stuck = false;
            _spawnTime = Time.time;
            transform.rotation = Quaternion.LookRotation(direction.normalized);
            UseTipColliderOnly();
        }

        void Update()
        {
            if (!_launched || _stuck)
                return;

            if (Time.time - _spawnTime > life)
            {
                Destroy(gameObject);
                return;
            }

            transform.position += _velocity * Time.deltaTime;
            if (_velocity.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(_velocity.normalized);
        }

        void OnTriggerEnter(Collider other)
        {
            if (!_launched || _stuck || other == null)
                return;

            if (_thrower != null && (other.transform == _thrower || other.transform.IsChildOf(_thrower)))
                return;

            if (other.GetComponentInParent<ChainKillableEnemy>() != null)
                return;

            var animal = other.GetComponentInParent<MAnimal>();
            if (animal != null)
            {
                if (!visualOnly)
                {
                    var health = animal.GetComponentInParent<HorseHealth>()
                                 ?? animal.GetComponent<HorseHealth>();
                    if (health == null)
                        health = animal.gameObject.AddComponent<HorseHealth>();
                    health.TakeDamage(damage);
                    CaliGameplayAudio.PlayAll(CaliSoundIds.HorseHit);
                }

                StickIn(other, animal != null ? animal.transform : other.transform);
                return;
            }

            if (!other.isTrigger)
                Destroy(gameObject);
        }

        void StickIn(Collider hit, Transform host)
        {
            _stuck = true;
            _launched = false;
            _velocity = Vector3.zero;

            var cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    cols[i].enabled = false;
            }

            PlaceTipAtHit(hit, host);
            if (host != null)
                transform.SetParent(host, true);

            StartCoroutine(HoldThenFade());
        }

        void PlaceTipAtHit(Collider hit, Transform host)
        {
            float tipAlong = MeasureTipAlongForward();
            Vector3 tipWorld = transform.TransformPoint(Vector3.forward * tipAlong);
            Vector3 contact = hit != null ? hit.ClosestPoint(tipWorld) : transform.position;

            Vector3 inward = transform.forward;
            if (host != null)
            {
                Vector3 toCenter = (host.position + Vector3.up * 0.7f) - contact;
                if (toCenter.sqrMagnitude > 0.01f)
                    inward = toCenter.normalized;
            }

            Vector3 desiredTip = contact + inward * Mathf.Max(0.04f, tipEmbed);
            transform.position += desiredTip - tipWorld;
        }

        void UseTipColliderOnly()
        {
            var cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    cols[i].enabled = false;
            }

            float tipAlong = MeasureTipAlongForward();
            var tip = GetComponent<SphereCollider>();
            if (tip == null)
                tip = gameObject.AddComponent<SphereCollider>();
            tip.enabled = true;
            tip.isTrigger = true;
            tip.radius = 0.07f;
            tip.center = new Vector3(0f, 0f, tipAlong);
        }

        float MeasureTipAlongForward()
        {
            var rends = GetComponentsInChildren<Renderer>(true);
            float maxZ = 0.45f;
            bool any = false;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null)
                    continue;
                var b = rends[i].bounds;
                Vector3 c = b.center;
                Vector3 e = b.extents;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 local = transform.InverseTransformPoint(
                        c + new Vector3(e.x * x, e.y * y, e.z * z));
                    if (!any || local.z > maxZ)
                    {
                        maxZ = local.z;
                        any = true;
                    }
                }
            }

            return Mathf.Max(0.2f, maxZ);
        }

        IEnumerator HoldThenFade()
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, stickSeconds));

            float dur = Mathf.Max(0.05f, fadeSeconds);
            float t = 0f;
            Vector3 from = transform.localScale;
            var rends = GetComponentsInChildren<Renderer>(true);
            Color[] start = new Color[rends.Length];
            for (int i = 0; i < rends.Length; i++)
            {
                start[i] = Color.white;
                if (rends[i] != null && rends[i].material != null && rends[i].material.HasProperty("_Color"))
                    start[i] = rends[i].material.color;
            }

            while (t < dur)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / dur);
                float fade = 1f - u;
                transform.localScale = from * fade;
                for (int i = 0; i < rends.Length; i++)
                {
                    if (rends[i] == null || rends[i].material == null)
                        continue;
                    if (rends[i].material.HasProperty("_Color"))
                    {
                        var c = start[i];
                        c.a *= fade;
                        rends[i].material.color = c;
                    }
                }

                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
