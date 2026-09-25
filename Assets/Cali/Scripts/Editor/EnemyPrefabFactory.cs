#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Cali.Combat;
using Cali.Gameplay;
using MalbersAnimations.Controller;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cali.EditorTools
{
    public static class EnemyPrefabFactory
    {
        const string PrefabDir = "Assets/Cali/Prefabs/Combat";
        const string ResourcesDir = "Assets/Cali/Resources/Combat";
        const string WolfSource = "Assets/Malbers Animations/Animal Controller/Wolf Lite/Wolf Lite AI Enemy.prefab";
        const string HumanSource = "Assets/Malbers Animations/Animal Controller/Human/Steve AI Enemy.prefab";
        const string BloodSplash = "Assets/RVFX/BloodEffectsPack/1_URP/Blood/Splash/Blood_Splash_01_URP.prefab";
        const string HumanAnimController = "Assets/Cali/Art/CaliEnemyHumanoid.controller";
        const string HumanAnimControllerRes = "Assets/Cali/Resources/Combat/CaliEnemyHumanoid.controller";

        [MenuItem("Cali/Simplify Humanoid Enemies")]
        public static void SimplifyHumanoidEnemies()
        {
            CreatePrefabsOnly();
            Debug.Log("[Cali] Humanoid enemies use idle/walk/attack/death only (Malbers stripped).");
        }

        [MenuItem("Cali/Setup Chain Kill Enemies")]
        public static void SetupAll()
        {
            CreatePrefabsOnly();

            var blood = AssetDatabase.LoadAssetAtPath<GameObject>(BloodSplash);
            var xpOrb = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/XpOrb.prefab");
            var wolf = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/CaliEnemy_Wolf.prefab");
            var human = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/CaliEnemy_Humanoid.prefab");

            EnsureBootstrap(wolf, human, xpOrb, blood);
            PlaceSampleEnemies(wolf, human, blood, xpOrb);
            WireSoftChain();
            PlayerXp.EnsureExists();

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log("[Cali] Chain kill enemies ready in active scene. Blood: Blood_Splash_01_URP.");
        }

        [MenuItem("Cali/Setup Chain Kill Enemies In MC_Demo_Day")]
        public static void SetupInMcDemoDay()
        {
            const string scenePath =
                "Assets/The_Modular_Medieval_Castle/Scenes/MC_Demo_Day.unity";
            if (!File.Exists(scenePath))
            {
                Debug.LogError($"[Cali] Scene not found: {scenePath}");
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(scenePath);
                SetupAll();
                EditorSceneManager.SaveOpenScenes();
                Debug.Log("[Cali] MC_Demo_Day chain-kill setup saved.");
            }
        }

        [MenuItem("Cali/Preview Blood Burst At Scene View")]
        public static void PreviewBloodBurst()
        {
            Vector3 pos = SceneView.lastActiveSceneView != null
                ? SceneView.lastActiveSceneView.pivot
                : Vector3.zero;
            EnemyDeathFx.SpawnSimpleUrpBlood(pos, Vector3.forward + Vector3.up);
            EnemyDeathFx.SpawnBloodDecal(pos);
            Debug.Log("[Cali] Spawned blood burst + ground decal at Scene View pivot (best in Play Mode).");
        }

        [MenuItem("Cali/Preview Blood Decal At Scene View")]
        public static void PreviewBloodDecal()
        {
            Vector3 pos = SceneView.lastActiveSceneView != null
                ? SceneView.lastActiveSceneView.pivot
                : Vector3.zero;
            EnemyDeathFx.SpawnBloodDecal(pos);
            Debug.Log("[Cali] Spawned blood ground decal at Scene View pivot (best in Play Mode).");
        }

        [InitializeOnLoadMethod]
        static void AutoCreatePrefabsWhenMissing()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;

                try
                {
                    var existingWolf = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/CaliEnemy_Wolf.prefab");
                    bool needsMalbersArt = existingWolf == null ||
                                           existingWolf.GetComponentInChildren<MAnimal>(true) == null;
                    bool resourcesReady = File.Exists($"{ResourcesDir}/CaliEnemy_Wolf.prefab") &&
                                          File.Exists($"{ResourcesDir}/XpOrb.prefab");

                    var existingHuman = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/CaliEnemy_Humanoid.prefab");
                    bool humanStillMalbers = existingHuman != null &&
                                             existingHuman.GetComponentInChildren<MAnimal>(true) != null;

                    if (resourcesReady && !needsMalbersArt && !humanStillMalbers)
                        return;

                    if ((needsMalbersArt || humanStillMalbers) && AssetDatabase.LoadAssetAtPath<GameObject>(WolfSource) != null)
                    {
                        CreatePrefabsOnly();
                        Debug.Log("[Cali] Created/upgraded chain-kill enemy prefabs (simple humanoid, Malbers wolf).");
                    }
                    else if (!resourcesReady)
                    {
                        CreatePrefabsOnly();
                        Debug.Log("[Cali] Auto-created chain-kill enemy prefabs under Assets/Cali/Resources/Combat.");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[Cali] Auto enemy prefab create skipped: {e.Message}");
                }
            };
        }

        public static void CreatePrefabsOnly()
        {
            Directory.CreateDirectory(PrefabDir);
            Directory.CreateDirectory(ResourcesDir);
            Directory.CreateDirectory("Assets/Cali/Art");
            EnsureHumanoidAnimator();

            var blood = AssetDatabase.LoadAssetAtPath<GameObject>(BloodSplash);
            var xpOrb = CreateXpOrbPrefab();
            var wolf = CreateEnemyPrefab("CaliEnemy_Wolf", WolfSource, EnemyKind.Wolf, blood, xpOrb);
            var human = CreateEnemyPrefab("CaliEnemy_Humanoid", HumanSource, EnemyKind.Humanoid, blood, xpOrb);
            var meshSwap = CreateMeshSwapEnemyPrefab(blood, xpOrb);

            CopyToResources(wolf, "CaliEnemy_Wolf.prefab");
            CopyToResources(human, "CaliEnemy_Humanoid.prefab");
            CopyToResources(meshSwap, "CaliEnemy_MeshSwap.prefab");
            CopyToResources(xpOrb, "XpOrb.prefab");
            if (File.Exists(HumanAnimController) && !File.Exists(HumanAnimControllerRes))
                AssetDatabase.CopyAsset(HumanAnimController, HumanAnimControllerRes);
            if (blood != null && !File.Exists($"{ResourcesDir}/Blood_Splash_01_URP.prefab"))
                AssetDatabase.CopyAsset(BloodSplash, $"{ResourcesDir}/Blood_Splash_01_URP.prefab");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>Batch entry: opens MC_Demo_Day, then runs setup.</summary>
        public static void SetupAllBatch()
        {
            const string scenePath =
                "Assets/The_Modular_Medieval_Castle/Scenes/MC_Demo_Day.unity";
            if (File.Exists(scenePath.Replace('\\', '/')))
                EditorSceneManager.OpenScene(scenePath);

            SetupAll();
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[Cali] SetupAllBatch finished and scene saved.");
        }

        static GameObject CreateXpOrbPrefab()
        {
            string path = $"{PrefabDir}/XpOrb.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
                return existing;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "XpOrb";
            go.transform.localScale = Vector3.one * 0.28f;
            Object.DestroyImmediate(go.GetComponent<Collider>());

            var rend = go.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")
                                   ?? Shader.Find("Unlit/Color")
                                   ?? Shader.Find("Sprites/Default"));
            mat.color = new Color(0.25f, 1f, 0.35f, 1f);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", new Color(0.25f, 1f, 0.35f, 1f));

            string matPath = "Assets/Cali/Materials/XpOrb_Green.mat";
            Directory.CreateDirectory("Assets/Cali/Materials");
            AssetDatabase.CreateAsset(mat, matPath);
            rend.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.4f, 1f, 0.5f);
            light.intensity = 1.6f;
            light.range = 3.5f;

            go.AddComponent<XpOrb>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject CreateEnemyPrefab(
            string name,
            string sourcePath,
            EnemyKind kind,
            GameObject blood,
            GameObject xpOrb)
        {
            string path = $"{PrefabDir}/{name}.prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                Debug.LogWarning($"[Cali] Missing source prefab: {sourcePath}. Creating capsule placeholder.");
                return CreateCapsuleEnemy(name, kind, blood, xpOrb, path);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.name = name;
            ApplyCaliEnemySetup(instance, kind, blood, xpOrb);

            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        static GameObject CreateCapsuleEnemy(
            string name,
            EnemyKind kind,
            GameObject blood,
            GameObject xpOrb,
            string path)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.localScale = kind == EnemyKind.Wolf
                ? new Vector3(0.7f, 0.45f, 1.1f)
                : new Vector3(0.55f, 1f, 0.55f);

            ApplyCaliEnemySetup(go, kind, blood, xpOrb);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        public static void ApplyCaliEnemySetup(GameObject root, EnemyKind kind, GameObject blood, GameObject xpOrb)
        {
            if (root == null)
                return;

            DisableAiBehaviours(root);

            var enemy = root.GetComponent<ChainKillableEnemy>();
            if (enemy == null)
                enemy = root.AddComponent<ChainKillableEnemy>();
            enemy.kind = kind;
            enemy.bloodPrefab = blood;
            enemy.xpOrbPrefab = xpOrb;
            enemy.xpReward = kind == EnemyKind.Wolf ? 20 : 25;
            enemy.orbCount = 4;
            enemy.useRandomPackFx = true;
            enemy.spawnBloodDecal = true;
            enemy.collapseDuration = 0.45f;
            enemy.deathAnimatorTrigger = "Death";
            enemy.useRagdollDeath = true;
            enemy.detectRadius = ChainKillableEnemy.DefaultDetectRadius(kind);

            if (root.GetComponent<EnemyChaseBrain>() == null)
                root.AddComponent<EnemyChaseBrain>();

            if (kind == EnemyKind.Humanoid)
            {
                if (root.GetComponent<EnemySpearThrower>() == null)
                    root.AddComponent<EnemySpearThrower>();
                if (root.GetComponent<EnemyTalkVoice>() == null)
                    root.AddComponent<EnemyTalkVoice>();
                StripMalbersFromHumanoid(root);
            }
            else
            {
                var animal = root.GetComponent<MAnimal>();
                if (animal != null)
                {
                    animal.DisablePosition = false;
                    animal.DisableRotation = false;
                    if (animal.RB != null)
                    {
                        animal.RB.isKinematic = false;
                        animal.RB.useGravity = true;
                    }
                }

                EnsurePhysics(root, animal);
            }

            SetLayerRecursive(root, LayerMask.NameToLayer("Enemy"));
        }

        static void EnsurePhysics(GameObject root, MAnimal animal)
        {
            if (root.GetComponentsInChildren<Collider>(true).Length == 0)
            {
                var col = root.AddComponent<CapsuleCollider>();
                Bounds b = EncapsulateRenderers(root);
                if (b.size.sqrMagnitude < 0.0001f)
                {
                    col.height = 2f;
                    col.radius = 0.4f;
                    col.center = new Vector3(0f, 1f, 0f);
                }
                else
                {
                    Vector3 localCenter = root.transform.InverseTransformPoint(b.center);
                    Vector3 size = b.size;
                    col.direction = 1;
                    col.height = Mathf.Max(0.5f, size.y);
                    col.radius = Mathf.Max(0.1f, Mathf.Max(size.x, size.z) * 0.5f);
                    col.center = localCenter;
                }
            }

            bool hasRb = root.GetComponent<Rigidbody>() != null || (animal != null && animal.RB != null);
            if (!hasRb)
            {
                var rb = root.AddComponent<Rigidbody>();
                rb.mass = 10f;
                rb.useGravity = true;
                rb.isKinematic = false;
                rb.constraints = RigidbodyConstraints.FreezeRotation;
            }
        }

        static Bounds EncapsulateRenderers(GameObject root)
        {
            var rends = root.GetComponentsInChildren<Renderer>(true);
            Bounds b = new Bounds(root.transform.position, Vector3.zero);
            bool any = false;
            for (int i = 0; i < rends.Length; i++)
            {
                if (rends[i] == null)
                    continue;
                if (!any)
                {
                    b = rends[i].bounds;
                    any = true;
                }
                else
                    b.Encapsulate(rends[i].bounds);
            }

            return any ? b : new Bounds(root.transform.position, Vector3.zero);
        }

        public static GameObject CreateEnemyFromCharacter(
            GameObject sourcePrefab,
            EnemyKind kind,
            string outputFolder,
            string outputName,
            bool copyToResources)
        {
            if (sourcePrefab == null)
            {
                Debug.LogError("[Cali] Source prefab is empty.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(outputFolder))
                outputFolder = PrefabDir;
            if (string.IsNullOrWhiteSpace(outputName))
                outputName = "CaliEnemy_" + sourcePrefab.name;

            outputName = SanitizeFileName(outputName);
            Directory.CreateDirectory(outputFolder);

            var blood = AssetDatabase.LoadAssetAtPath<GameObject>(BloodSplash);
            var xpOrb = CreateXpOrbPrefab();

            string path = UniquePrefabPath(outputFolder, outputName);
            GameObject instance;

            if (kind == EnemyKind.Humanoid)
            {
                instance = InstantiateHumanoidTemplate();
                if (instance == null)
                {
                    Debug.LogError("[Cali] Could not load CaliEnemy_Humanoid / Steve template.");
                    return null;
                }

                instance.name = Path.GetFileNameWithoutExtension(path);
                ApplyCaliEnemySetup(instance, kind, blood, xpOrb);
                ApplyHumanoidLookFromSource(instance, sourcePrefab);
            }
            else
            {
                instance = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
                if (instance == null)
                    instance = Object.Instantiate(sourcePrefab);
                instance.name = Path.GetFileNameWithoutExtension(path);
                ApplyCaliEnemySetup(instance, kind, blood, xpOrb);
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);

            if (copyToResources && prefab != null)
            {
                Directory.CreateDirectory(ResourcesDir);
                CopyToResources(prefab, Path.GetFileName(path));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return prefab;
        }

        static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        static string UniquePrefabPath(string folder, string name)
        {
            string path = $"{folder}/{name}.prefab";
            int n = 2;
            while (File.Exists(path))
            {
                path = $"{folder}/{name}_{n}.prefab";
                n++;
            }

            return path;
        }

        static GameObject InstantiateHumanoidTemplate()
        {
            var template = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/CaliEnemy_Humanoid.prefab");
            if (template == null)
                template = AssetDatabase.LoadAssetAtPath<GameObject>($"{ResourcesDir}/CaliEnemy_Humanoid.prefab");
            if (template == null)
                template = AssetDatabase.LoadAssetAtPath<GameObject>(HumanSource);

            if (template == null)
                return null;

            var instance = PrefabUtility.InstantiatePrefab(template) as GameObject;
            return instance != null ? instance : Object.Instantiate(template);
        }

        static void ApplyHumanoidLookFromSource(GameObject templateRoot, GameObject sourcePrefab)
        {
            if (templateRoot == null || sourcePrefab == null)
                return;

            var rootAnim = templateRoot.GetComponentInChildren<Animator>();
            var body = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
            if (body == null)
                body = Object.Instantiate(sourcePrefab);

            body.name = "Body";
            body.transform.SetParent(templateRoot.transform, false);
            body.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            body.transform.localScale = Vector3.one;

            var srcAnim = body.GetComponentInChildren<Animator>(true);
            if (rootAnim != null)
            {
                if (srcAnim != null && srcAnim.avatar != null)
                    rootAnim.avatar = srcAnim.avatar;

                var templateController = rootAnim.runtimeAnimatorController;
                if (templateController == null)
                {
                    templateController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                        "Assets/Malbers Animations/Common/Human Anims/AC Human v5.controller");
                    rootAnim.runtimeAnimatorController = templateController;
                }

                rootAnim.applyRootMotion = true;
                rootAnim.updateMode = AnimatorUpdateMode.Fixed;
                rootAnim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            }

            StripNestedCharacterSystems(body);

            var skin = templateRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skin.Length; i++)
            {
                if (skin[i] == null)
                    continue;
                if (skin[i].transform == body.transform || skin[i].transform.IsChildOf(body.transform))
                    continue;
                skin[i].enabled = false;
            }
        }

        static void StripNestedCharacterSystems(GameObject body)
        {
            var destroy = new List<Object>();

            foreach (var anim in body.GetComponentsInChildren<Animator>(true))
                anim.enabled = false;

            foreach (var rb in body.GetComponentsInChildren<Rigidbody>(true))
                destroy.Add(rb);
            foreach (var cc in body.GetComponentsInChildren<CharacterController>(true))
                destroy.Add(cc);
            foreach (var e in body.GetComponentsInChildren<ChainKillableEnemy>(true))
                destroy.Add(e);
            foreach (var e in body.GetComponentsInChildren<EnemyChaseBrain>(true))
                destroy.Add(e);
            foreach (var e in body.GetComponentsInChildren<EnemySpearThrower>(true))
                destroy.Add(e);
            foreach (var e in body.GetComponentsInChildren<MAnimal>(true))
                destroy.Add(e);

            foreach (var col in body.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            for (int i = 0; i < destroy.Count; i++)
            {
                if (destroy[i] != null)
                    Object.DestroyImmediate(destroy[i]);
            }
        }

        [MenuItem("Cali/Create Mesh-Swap Enemy Prefab")]
        public static void CreateMeshSwapEnemyMenu()
        {
            Directory.CreateDirectory(PrefabDir);
            Directory.CreateDirectory(ResourcesDir);
            var blood = AssetDatabase.LoadAssetAtPath<GameObject>(BloodSplash);
            var xpOrb = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/XpOrb.prefab");
            var prefab = CreateMeshSwapEnemyPrefab(blood, xpOrb);
            CopyToResources(prefab, "CaliEnemy_MeshSwap.prefab");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (prefab != null)
            {
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
            }

            Debug.Log("[Cali] Mesh-swap enemy prefab: Assets/Cali/Prefabs/Combat/CaliEnemy_MeshSwap.prefab — assign Body Mesh on EnemyBodyMesh.");
        }

        static GameObject CreateMeshSwapEnemyPrefab(GameObject blood, GameObject xpOrb)
        {
            string path = $"{PrefabDir}/CaliEnemy_MeshSwap.prefab";
            var go = new GameObject("CaliEnemy_MeshSwap");

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 10f;
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;

            var col = go.AddComponent<CapsuleCollider>();
            col.height = 2f;
            col.radius = 0.5f;
            col.center = new Vector3(0f, 1f, 0f);
            col.direction = 1;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = new Vector3(0f, 1f, 0f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            var filter = visual.GetComponent<MeshFilter>();
            var rend = visual.GetComponent<MeshRenderer>();
            var litMat = AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Lit.mat");
            if (rend != null && litMat != null)
                rend.sharedMaterial = litMat;

            var enemy = go.AddComponent<ChainKillableEnemy>();
            enemy.kind = EnemyKind.Wolf;
            enemy.bloodPrefab = blood;
            enemy.xpOrbPrefab = xpOrb;
            enemy.xpReward = 20;
            enemy.orbCount = 4;

            go.AddComponent<EnemyChaseBrain>();

            var body = go.AddComponent<EnemyBodyMesh>();
            body.bodyFilter = filter;
            body.bodyMesh = filter != null ? filter.sharedMesh : null;
            body.visualOffset = new Vector3(0f, 1f, 0f);
            body.visualScale = Vector3.one;
            body.fitCollider = true;
            body.Apply();

            SetLayerRecursive(go, LayerMask.NameToLayer("Enemy"));

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static RuntimeAnimatorController EnsureHumanoidAnimator()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(HumanAnimController);
            if (existing != null)
            {
                EnsureDeathStateSpeed(existing, 1.7f);
                CopyHumanoidAnimatorToResources();
                return existing;
            }

            Directory.CreateDirectory("Assets/Cali/Art");
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(HumanAnimController);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Death", AnimatorControllerParameterType.Trigger);

            var idle = LoadClip("Assets/Malbers Animations/Common/Human Anims/Locomotion/Idle.anim");
            var walk = LoadClip("Assets/Malbers Animations/Common/Human Anims/Locomotion/Walk.anim");
            var attack = LoadNamedClip(
                "Assets/Malbers Animations/Common/Human Anims/Weapons/Spear/H_Spear_Attack_Right.fbx",
                "H_Spear_Attack_Right");
            var death = LoadClip("Assets/Malbers Animations/Common/Human Anims/Deaths/H_Death2.anim");

            var root = ctrl.layers[0].stateMachine;
            var idleState = root.AddState("Idle");
            idleState.motion = idle;
            var walkState = root.AddState("Walk");
            walkState.motion = walk;
            var attackState = root.AddState("Attack");
            attackState.motion = attack;
            var deathState = root.AddState("Death");
            deathState.motion = death;
            deathState.speed = 1.7f;
            root.defaultState = idleState;

            var toWalk = idleState.AddTransition(walkState);
            toWalk.hasExitTime = false;
            toWalk.duration = 0.12f;
            toWalk.AddCondition(AnimatorConditionMode.Greater, 0.15f, "Speed");

            var toIdle = walkState.AddTransition(idleState);
            toIdle.hasExitTime = false;
            toIdle.duration = 0.12f;
            toIdle.AddCondition(AnimatorConditionMode.Less, 0.15f, "Speed");

            var atkIdle = idleState.AddTransition(attackState);
            atkIdle.hasExitTime = false;
            atkIdle.duration = 0.08f;
            atkIdle.AddCondition(AnimatorConditionMode.If, 0f, "Attack");

            var atkWalk = walkState.AddTransition(attackState);
            atkWalk.hasExitTime = false;
            atkWalk.duration = 0.08f;
            atkWalk.AddCondition(AnimatorConditionMode.If, 0f, "Attack");

            var atkDone = attackState.AddTransition(idleState);
            atkDone.hasExitTime = true;
            atkDone.exitTime = 0.9f;
            atkDone.duration = 0.1f;

            var die = root.AddAnyStateTransition(deathState);
            die.hasExitTime = false;
            die.duration = 0.08f;
            die.AddCondition(AnimatorConditionMode.If, 0f, "Death");

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
            CopyHumanoidAnimatorToResources();
            return ctrl;
        }

        static void EnsureDeathStateSpeed(AnimatorController ctrl, float speed)
        {
            if (ctrl == null)
                return;

            for (int i = 0; i < ctrl.layers.Length; i++)
            {
                var sm = ctrl.layers[i].stateMachine;
                if (sm == null)
                    continue;

                var states = sm.states;
                for (int s = 0; s < states.Length; s++)
                {
                    var state = states[s].state;
                    if (state == null || state.name != "Death")
                        continue;

                    if (!Mathf.Approximately(state.speed, speed))
                    {
                        state.speed = speed;
                        EditorUtility.SetDirty(ctrl);
                    }

                    return;
                }
            }
        }

        static void CopyHumanoidAnimatorToResources()
        {
            Directory.CreateDirectory(ResourcesDir);
            if (!File.Exists(HumanAnimController))
                return;

            if (File.Exists(HumanAnimControllerRes))
                AssetDatabase.DeleteAsset(HumanAnimControllerRes);

            AssetDatabase.CopyAsset(HumanAnimController, HumanAnimControllerRes);
        }

        static AnimationClip LoadClip(string path)
        {
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        }

        static AnimationClip LoadNamedClip(string assetPath, string clipName)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is AnimationClip clip &&
                    clip.name == clipName &&
                    !clip.name.StartsWith("__preview"))
                    return clip;
            }

            return AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
        }

        static void StripMalbersFromHumanoid(GameObject root)
        {
            if (root == null)
                return;

            var controller = EnsureHumanoidAnimator();
            var anim = root.GetComponentInChildren<Animator>(true);
            if (anim != null)
            {
                anim.applyRootMotion = false;
                anim.updateMode = AnimatorUpdateMode.Normal;
                anim.cullingMode = AnimatorCullingMode.CullCompletely;
                anim.runtimeAnimatorController = controller;
                anim.enabled = true;
            }

            var destroy = new List<Component>();
            var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var mb = behaviours[i];
                if (mb == null)
                    continue;
                if (mb is ChainKillableEnemy || mb is EnemyChaseBrain ||
                    mb is EnemySpearThrower || mb is EnemyTalkVoice)
                    continue;

                string ns = mb.GetType().Namespace ?? "";
                string n = mb.GetType().Name;
                if (ns.StartsWith("Malbers") ||
                    n == "MAnimal" ||
                    n.Contains("AnimalAI") ||
                    n.Contains("AIControl") ||
                    n.Contains("AIBrain") ||
                    n == "LookAt" ||
                    n == "LookAtCamera")
                    destroy.Add(mb);
            }

            foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                destroy.Add(cam);
            foreach (var cc in root.GetComponentsInChildren<CharacterController>(true))
                destroy.Add(cc);

            foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
            {
                if (rb != null && rb.gameObject != root)
                    destroy.Add(rb);
            }

            for (int i = 0; i < destroy.Count; i++)
            {
                if (destroy[i] != null)
                    Object.DestroyImmediate(destroy[i]);
            }

            if (anim != null)
            {
                foreach (var extra in root.GetComponentsInChildren<Animator>(true))
                {
                    if (extra != anim)
                        extra.enabled = false;
                }
            }

            foreach (var col in root.GetComponentsInChildren<Collider>(true))
            {
                if (col.gameObject != root)
                    col.enabled = false;
            }

            var capsule = root.GetComponent<CapsuleCollider>();
            if (capsule == null)
                capsule = root.AddComponent<CapsuleCollider>();
            capsule.enabled = true;
            capsule.isTrigger = false;
            capsule.direction = 1;
            capsule.height = 2f;
            capsule.radius = 0.35f;
            capsule.center = new Vector3(0f, 1f, 0f);

            var rootRb = root.GetComponent<Rigidbody>();
            if (rootRb == null)
                rootRb = root.AddComponent<Rigidbody>();
            rootRb.isKinematic = true;
            rootRb.useGravity = false;
            rootRb.constraints = RigidbodyConstraints.FreezeRotation;
            rootRb.interpolation = RigidbodyInterpolation.None;
            rootRb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rootRb.mass = 10f;
        }

        static void DisableAiBehaviours(GameObject root)
        {
            foreach (var b in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (b == null)
                    continue;
                string n = b.GetType().Name;
                if (n.Contains("AnimalAI") || n.Contains("AIControl") || n.Contains("AIBrain") || n.Contains("MAnimalAI"))
                    b.enabled = false;
            }
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            if (layer < 0)
                return;
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        static void CopyToResources(GameObject prefab, string fileName)
        {
            if (prefab == null)
                return;

            string src = AssetDatabase.GetAssetPath(prefab);
            string dst = $"{ResourcesDir}/{fileName}";
            AssetDatabase.CopyAsset(src, dst);
        }

        static void EnsureBootstrap(GameObject wolf, GameObject human, GameObject xpOrb, GameObject blood)
        {
            var boot = Object.FindFirstObjectByType<ChainKillEncounterBootstrap>();
            if (boot == null)
            {
                var go = new GameObject("<<<Chain Kill Encounter>>>");
                boot = go.AddComponent<ChainKillEncounterBootstrap>();
                Undo.RegisterCreatedObjectUndo(go, "Create Chain Kill Encounter");
            }

            boot.wolfPrefab = wolf;
            boot.humanoidPrefab = human;
            boot.xpOrbPrefab = xpOrb;
            boot.bloodPrefab = blood;
            EditorUtility.SetDirty(boot);
        }

        static void PlaceSampleEnemies(GameObject wolf, GameObject human, GameObject blood, GameObject xpOrb)
        {
            if (Object.FindObjectsByType<ChainKillableEnemy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length > 0)
                return;

            var chain = Object.FindFirstObjectByType<SoftHorseChain>();
            Vector3 center = Vector3.zero;
            if (chain != null && chain.horseA != null)
                center = chain.horseA.transform.position;
            else
            {
                var animals = Object.FindObjectsByType<MAnimal>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var a in animals)
                {
                    if (a.name.Contains("Horse Realistic") && !a.name.Contains("Pegasus"))
                    {
                        center = a.transform.position;
                        break;
                    }
                }
            }

            int id = 100;
            SpawnOne(wolf, EnemyKind.Wolf, center + new Vector3(6f, 0f, 4f), ref id, blood, xpOrb);
            SpawnOne(wolf, EnemyKind.Wolf, center + new Vector3(-5f, 0f, 7f), ref id, blood, xpOrb);
            SpawnOne(human, EnemyKind.Humanoid, center + new Vector3(8f, 0f, -3f), ref id, blood, xpOrb);
            SpawnOne(human, EnemyKind.Humanoid, center + new Vector3(-7f, 0f, -5f), ref id, blood, xpOrb);
        }

        static void SpawnOne(
            GameObject prefab,
            EnemyKind kind,
            Vector3 pos,
            ref int id,
            GameObject blood,
            GameObject xpOrb)
        {
            if (prefab == null)
                return;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = pos;
            go.name = $"{prefab.name}_{id}";

            var enemy = go.GetComponent<ChainKillableEnemy>();
            if (enemy != null)
            {
                enemy.enemyId = id;
                enemy.kind = kind;
                if (enemy.bloodPrefab == null)
                    enemy.bloodPrefab = blood;
                if (enemy.xpOrbPrefab == null)
                    enemy.xpOrbPrefab = xpOrb;
            }

            id++;
            Undo.RegisterCreatedObjectUndo(go, "Place Enemy");
        }

        static void WireSoftChain()
        {
            var chain = Object.FindFirstObjectByType<SoftHorseChain>();
            if (chain == null)
                return;

            int layer = LayerMask.NameToLayer("Enemy");
            if (layer >= 0)
                chain.enemyHitMask = 1 << layer;
            EditorUtility.SetDirty(chain);
        }
    }
}
#endif
