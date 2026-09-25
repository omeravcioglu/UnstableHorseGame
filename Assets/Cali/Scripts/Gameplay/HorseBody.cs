using MalbersAnimations.Controller;
using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// Puts a horse's rigidbody into the right mode for how it is being driven.
    ///
    /// This is what fixes the visible stutter in the horse animation. The Animator runs in
    /// AnimatePhysics and physics ticks at 50 Hz, so a simulated horse's transform only advances
    /// 50 times a second. Rendering at 60 Hz or more then shows the same pose for some frames and
    /// a jump on others, which reads as jittery animation even though nothing is wrong with the
    /// clips or the network. Rigidbody interpolation is what fills in those in-between frames.
    ///
    /// A proxy is the opposite case: its transform is already written every render frame from the
    /// interpolated network snapshot, so Unity's own interpolation would be a second smoothing
    /// pass on top and lag it behind by a physics step.
    /// </summary>
    public static class HorseBody
    {
        /// <summary>
        /// <paramref name="simulated"/> true when Malbers drives this horse here, false when it is
        /// a proxy whose pose is written from the network.
        /// </summary>
        public static void SetSimulated(MAnimal animal, bool simulated)
        {
            if (animal == null)
                return;

            animal.DisablePosition = !simulated;
            animal.DisableRotation = !simulated;

            var rb = animal.RB;
            if (rb == null)
                return;

            // Only written on an actual change: this is called every frame offline, and touching
            // isKinematic wakes the body and can drop its velocity.
            bool kinematic = !simulated;
            if (rb.isKinematic != kinematic)
            {
                // Has to be cleared while the body is still dynamic. A kinematic body rejects the
                // write, which would leave stale velocity to resume the moment it goes dynamic.
                if (kinematic)
                    rb.linearVelocity = Vector3.zero;

                rb.isKinematic = kinematic;
            }

            var wanted = simulated ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
            if (rb.interpolation != wanted)
                rb.interpolation = wanted;

            if (simulated)
                HorsePhysics.Apply(animal);
            else
                HorsePhysics.ApplyProxy(animal);
        }
    }
}
