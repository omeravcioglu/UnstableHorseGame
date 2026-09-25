using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// Hitting the side of a wall or box should block the horse. It should not slide
    /// around the face or hop up it.
    /// </summary>
    [DisallowMultipleComponent]
    public class HorseColliderStop : MonoBehaviour
    {
        const float WallUpDot = 0.22f;

        MAnimal _animal;
        Rigidbody _rb;

        public static void Ensure(MAnimal animal)
        {
            if (animal == null)
                return;
            if (animal.GetComponent<HorseColliderStop>() == null)
                animal.gameObject.AddComponent<HorseColliderStop>();
        }

        void Awake()
        {
            _animal = GetComponent<MAnimal>();
            _rb = GetComponent<Rigidbody>();
        }

        void OnCollisionEnter(Collision collision) => StopOn(collision);

        void OnCollisionStay(Collision collision) => StopOn(collision);

        void StopOn(Collision collision)
        {
            if (_rb == null || _rb.isKinematic)
                return;
            if (collision == null || collision.contactCount == 0)
                return;
            if (HorseJump.IsAirborne(_animal) || !HorseJump.HasFooting(_animal))
                return;

            Vector3 up = _animal != null ? _animal.UpVector : Vector3.up;
            Vector3 wall = Vector3.zero;
            bool wallHit = false;
            int count = collision.contactCount;
            for (int i = 0; i < count; i++)
            {
                ContactPoint contact = collision.GetContact(i);
                if (Vector3.Dot(contact.normal, up) >= WallUpDot)
                    continue;
                wall += contact.normal;
                wallHit = true;
            }

            if (!wallHit)
                return;

            wall.Normalize();
            Vector3 v = _rb.linearVelocity;

            Vector3 slide = Vector3.ProjectOnPlane(v, wall);
            slide = Vector3.ProjectOnPlane(slide, up);
            v -= slide;
            _rb.linearVelocity = v;

            if (_animal == null)
                return;

            Vector3 add = _animal.AdditivePosition;
            Vector3 addSlide = Vector3.ProjectOnPlane(add, wall);
            addSlide = Vector3.ProjectOnPlane(addSlide, up);
            _animal.AdditivePosition = add - addSlide;
        }
    }
}
