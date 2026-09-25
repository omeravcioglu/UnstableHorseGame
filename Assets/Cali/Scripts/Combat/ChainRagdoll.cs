using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Marks a dead enemy so the horse chain can fling the ragdoll.
    /// </summary>
    public class ChainRagdoll : MonoBehaviour
    {
        Rigidbody[] _bodies;
        int _sweepFrame = -1;
        float _bestSqr;
        Vector3 _bestVel;
        Vector3 _bestPoint;

        public void SetBodies(System.Collections.Generic.List<Rigidbody> bodies)
        {
            if (bodies == null || bodies.Count == 0)
            {
                _bodies = System.Array.Empty<Rigidbody>();
                return;
            }

            _bodies = bodies.ToArray();
        }

        public void ApplyChainSweep(Vector3 velocity, Vector3 worldPoint)
        {
            float sqr = velocity.sqrMagnitude;
            if (sqr < _bestSqr && _sweepFrame == Time.frameCount)
                return;

            _sweepFrame = Time.frameCount;
            _bestSqr = sqr;
            _bestVel = velocity;
            _bestPoint = worldPoint;
        }

        void LateUpdate()
        {
            if (_bodies == null || _sweepFrame != Time.frameCount)
                return;

            Vector3 vel = _bestVel;
            vel.y = Mathf.Max(Mathf.Abs(vel.y), 3.5f);
            float speed = vel.magnitude;
            if (speed < 0.4f)
                return;

            vel = vel.normalized * Mathf.Clamp(speed * 1.15f, 4f, 16f);

            Rigidbody nearest = null;
            float best = float.PositiveInfinity;
            for (int i = 0; i < _bodies.Length; i++)
            {
                var rb = _bodies[i];
                if (rb == null)
                    continue;

                if (rb.isKinematic)
                {
                    rb.isKinematic = false;
                    rb.detectCollisions = true;
                }

                float d = (rb.worldCenterOfMass - _bestPoint).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = rb;
                }
            }

            if (nearest == null)
                return;

            nearest.AddForceAtPosition(vel, _bestPoint, ForceMode.VelocityChange);
            nearest.AddTorque(Random.onUnitSphere * vel.magnitude * 0.8f, ForceMode.VelocityChange);
            _bestSqr = 0f;
        }
    }
}
