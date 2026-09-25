using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Cali.Combat
{
    /// <summary>
    /// Turns a posed enemy skeleton into a ragdoll. Humanoids use Avatar bones;
    /// wolves fall back to Malbers bone names on the same mesh.
    /// </summary>
    public static class EnemyDeathRagdoll
    {
        const float FreezeAfter = 12f;
        const float MaxLaunch = 12f;
        const float MinLaunch = 3.5f;

        static readonly string[] HipsNames = { "Pelvis", "Hips", "hips", "pelvis" };
        static readonly string[] SpineNames = { "Spine1", "Spine", "Chest", "spine" };
        static readonly string[] HeadNames = { "Head", "head" };
        static readonly string[] LUpperArmNames = { "L UpperArm", "LeftArm", "arm_L" };
        static readonly string[] LLowerArmNames = { "L Forearm", "LeftForeArm", "forearm_L" };
        static readonly string[] RUpperArmNames = { "R UpperArm", "RightArm", "arm_R" };
        static readonly string[] RLowerArmNames = { "R Forearm", "RightForeArm", "forearm_R" };
        static readonly string[] LUpperLegNames = { "L Thigh", "LeftUpLeg", "thigh_L" };
        static readonly string[] LLowerLegNames = { "L Calf", "LeftLeg", "calf_L" };
        static readonly string[] LFootNames = { "L Foot", "LeftFoot", "foot_L" };
        static readonly string[] RUpperLegNames = { "R Thigh", "RightUpLeg", "thigh_R" };
        static readonly string[] RLowerLegNames = { "R Calf", "RightLeg", "calf_R" };
        static readonly string[] RFootNames = { "R Foot", "RightFoot", "foot_R" };

        public static bool TryActivate(
            MonoBehaviour host,
            Animator animator,
            Vector3 hitPoint,
            Vector3 hitVelocity)
        {
            if (animator == null)
                return false;

            var hips = Find(animator, HumanBodyBones.Hips, HipsNames);
            if (hips == null)
                return false;

            var spine = Find(animator, HumanBodyBones.Chest, SpineNames)
                        ?? Find(animator, HumanBodyBones.Spine, SpineNames);
            var head = Find(animator, HumanBodyBones.Head, HeadNames);
            var lArm = Find(animator, HumanBodyBones.LeftUpperArm, LUpperArmNames);
            var lFore = Find(animator, HumanBodyBones.LeftLowerArm, LLowerArmNames);
            var rArm = Find(animator, HumanBodyBones.RightUpperArm, RUpperArmNames);
            var rFore = Find(animator, HumanBodyBones.RightLowerArm, RLowerArmNames);
            var lThigh = Find(animator, HumanBodyBones.LeftUpperLeg, LUpperLegNames);
            var lCalf = Find(animator, HumanBodyBones.LeftLowerLeg, LLowerLegNames);
            var lFoot = Find(animator, HumanBodyBones.LeftFoot, LFootNames);
            var rThigh = Find(animator, HumanBodyBones.RightUpperLeg, RUpperLegNames);
            var rCalf = Find(animator, HumanBodyBones.RightLowerLeg, RLowerLegNames);
            var rFoot = Find(animator, HumanBodyBones.RightFoot, RFootNames);

            animator.enabled = false;
            animator.updateMode = AnimatorUpdateMode.Normal;

            var bodies = new List<Rigidbody>(13);
            var hipsRb = AddPart(hips, spine ?? head, null, 8f, 0.14f, 0f, 0f, 0f, bodies);
            if (hipsRb == null)
                return false;

            AddPart(spine, head, hips, 6.5f, 0.16f, -20f, 20f, 20f, bodies);
            AddPart(head, null, spine ?? hips, 2.8f, 0.12f, -40f, 25f, 25f, bodies);

            AddPart(lThigh, lCalf, hips, 5f, 0.1f, -20f, 70f, 30f, bodies);
            AddPart(lCalf, lFoot, lThigh, 3f, 0.08f, -80f, 0f, 8f, bodies);
            AddPart(lFoot, null, lCalf, 1.2f, 0.06f, -20f, 20f, 15f, bodies);

            AddPart(rThigh, rCalf, hips, 5f, 0.1f, -20f, 70f, 30f, bodies);
            AddPart(rCalf, rFoot, rThigh, 3f, 0.08f, -80f, 0f, 8f, bodies);
            AddPart(rFoot, null, rCalf, 1.2f, 0.06f, -20f, 20f, 15f, bodies);

            AddPart(lArm, lFore, spine ?? hips, 2.2f, 0.07f, -70f, 10f, 50f, bodies);
            AddPart(lFore, null, lArm, 1.4f, 0.06f, -90f, 0f, 8f, bodies);
            AddPart(rArm, rFore, spine ?? hips, 2.2f, 0.07f, -70f, 10f, 50f, bodies);
            AddPart(rFore, null, rArm, 1.4f, 0.06f, -90f, 0f, 8f, bodies);

            for (int i = 0; i < bodies.Count; i++)
            {
                var rb = bodies[i];
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.constraints = RigidbodyConstraints.None;
                rb.detectCollisions = true;
            }

            ApplyLaunch(bodies, hipsRb, hitPoint, hitVelocity);

            var marker = host != null
                ? host.GetComponent<ChainRagdoll>() ?? host.gameObject.AddComponent<ChainRagdoll>()
                : animator.GetComponentInParent<ChainRagdoll>();
            if (marker == null)
                marker = animator.gameObject.AddComponent<ChainRagdoll>();
            marker.SetBodies(bodies);

            var ragdollCols = new Collider[bodies.Count];
            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i] != null)
                    ragdollCols[i] = bodies[i].GetComponent<Collider>();
            }

            ChainKillableEnemy.IgnoreHorsesAndPushables(ragdollCols);

            var skins = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skins.Length; i++)
            {
                if (skins[i] != null)
                    skins[i].updateWhenOffscreen = true;
            }

            if (host != null && host.isActiveAndEnabled)
                host.StartCoroutine(FreezeAfterDelay(bodies, FreezeAfter));

            return true;
        }

        static void ApplyLaunch(
            List<Rigidbody> bodies,
            Rigidbody hips,
            Vector3 hitPoint,
            Vector3 hitVelocity)
        {
            Vector3 launch = hitVelocity;
            if (launch.sqrMagnitude < 0.01f)
                launch = Vector3.forward;

            float mag = Mathf.Clamp(launch.magnitude * 0.28f, MinLaunch, MaxLaunch);
            launch = launch.normalized * mag + Vector3.up * 2.4f;

            Rigidbody nearest = hips;
            float best = float.PositiveInfinity;
            for (int i = 0; i < bodies.Count; i++)
            {
                var rb = bodies[i];
                float d = (rb.worldCenterOfMass - hitPoint).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = rb;
                }
            }

            hips.AddForce(launch * 0.55f, ForceMode.VelocityChange);
            nearest.AddForceAtPosition(launch, hitPoint, ForceMode.VelocityChange);
        }

        static IEnumerator FreezeAfterDelay(List<Rigidbody> bodies, float delay)
        {
            yield return new WaitForSeconds(delay);

            for (int i = 0; i < bodies.Count; i++)
            {
                var rb = bodies[i];
                if (rb == null)
                    continue;

                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
                rb.detectCollisions = true;
            }
        }

        static Rigidbody AddPart(
            Transform bone,
            Transform child,
            Transform parent,
            float mass,
            float radius,
            float twistMin,
            float twistMax,
            float swing,
            List<Rigidbody> bodies)
        {
            if (bone == null)
                return null;

            ClearPhysics(bone);

            Vector3 worldEnd;
            if (child != null)
            {
                worldEnd = child.position;
            }
            else if (parent != null)
            {
                Vector3 along = bone.position - parent.position;
                if (along.sqrMagnitude < 0.0001f)
                    along = bone.up;
                worldEnd = bone.position + along.normalized * 0.22f;
            }
            else
            {
                worldEnd = bone.position + bone.up * 0.28f;
            }

            Vector3 worldDir = worldEnd - bone.position;
            float height = worldDir.magnitude;
            if (height < 0.06f)
            {
                height = 0.14f;
                worldDir = bone.up * height;
                worldEnd = bone.position + worldDir;
            }

            var capsule = bone.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = DominantAxis(bone.InverseTransformDirection(worldDir));
            capsule.height = height;
            capsule.radius = Mathf.Clamp(radius, 0.04f, height * 0.45f);
            capsule.center = bone.InverseTransformPoint(bone.position + worldDir * 0.5f);
            capsule.enabled = true;
            capsule.isTrigger = false;

            int enemyLayer = LayerMask.NameToLayer("Enemy");
            if (enemyLayer >= 0)
                bone.gameObject.layer = enemyLayer;

            var rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.isKinematic = true;
            rb.useGravity = true;
            rb.linearDamping = 0.25f;
            rb.angularDamping = 0.35f;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.solverIterations = 10;
            rb.solverVelocityIterations = 4;
            rb.maxAngularVelocity = 8f;
            bodies.Add(rb);

            if (parent != null)
            {
                var parentRb = parent.GetComponent<Rigidbody>();
                if (parentRb != null)
                {
                    var joint = bone.gameObject.AddComponent<CharacterJoint>();
                    joint.connectedBody = parentRb;
                    joint.autoConfigureConnectedAnchor = true;
                    joint.enableProjection = true;
                    joint.enablePreprocessing = true;
                    joint.projectionDistance = 0.1f;
                    joint.projectionAngle = 20f;
                    joint.enableCollision = false;

                    Vector3 axis = bone.InverseTransformDirection(worldDir.normalized);
                    if (axis.sqrMagnitude < 0.01f)
                        axis = Vector3.right;
                    joint.axis = axis.normalized;

                    Vector3 swingAxis = Vector3.Cross(joint.axis, Vector3.up);
                    if (swingAxis.sqrMagnitude < 0.01f)
                        swingAxis = Vector3.Cross(joint.axis, Vector3.forward);
                    joint.swingAxis = swingAxis.normalized;

                    joint.lowTwistLimit = new SoftJointLimit { limit = twistMin };
                    joint.highTwistLimit = new SoftJointLimit { limit = twistMax };
                    joint.swing1Limit = new SoftJointLimit { limit = swing };
                    joint.swing2Limit = new SoftJointLimit { limit = swing };
                }
            }

            return rb;
        }

        static void ClearPhysics(Transform bone)
        {
            var joints = bone.GetComponents<Joint>();
            for (int i = 0; i < joints.Length; i++)
                UnityEngine.Object.DestroyImmediate(joints[i]);

            var cols = bone.GetComponents<Collider>();
            for (int i = 0; i < cols.Length; i++)
                UnityEngine.Object.DestroyImmediate(cols[i]);

            var rbs = bone.GetComponents<Rigidbody>();
            for (int i = 0; i < rbs.Length; i++)
                UnityEngine.Object.DestroyImmediate(rbs[i]);
        }

        static int DominantAxis(Vector3 localDir)
        {
            Vector3 a = new Vector3(Mathf.Abs(localDir.x), Mathf.Abs(localDir.y), Mathf.Abs(localDir.z));
            if (a.x >= a.y && a.x >= a.z)
                return 0;
            if (a.y >= a.x && a.y >= a.z)
                return 1;
            return 2;
        }

        static Transform Find(Animator animator, HumanBodyBones human, string[] names)
        {
            if (animator.isHuman)
            {
                var t = animator.GetBoneTransform(human);
                if (t != null)
                    return t;
            }

            for (int i = 0; i < names.Length; i++)
            {
                var t = FindRecursive(animator.transform, names[i]);
                if (t != null)
                    return t;
            }

            return null;
        }

        static Transform FindRecursive(Transform root, string name)
        {
            if (NameMatches(root.name, name))
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindRecursive(root.GetChild(i), name);
                if (found != null)
                    return found;
            }

            return null;
        }

        static bool NameMatches(string boneName, string query)
        {
            if (string.Equals(boneName, query, StringComparison.OrdinalIgnoreCase))
                return true;

            if (boneName.Length <= query.Length)
                return false;

            if (!boneName.EndsWith(query, StringComparison.OrdinalIgnoreCase))
                return false;

            char sep = boneName[boneName.Length - query.Length - 1];
            return sep == '_' || sep == ':' || sep == '.' || sep == ' ';
        }
    }
}
