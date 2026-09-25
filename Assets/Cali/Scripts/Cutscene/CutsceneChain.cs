using System.Collections.Generic;
using UnityEngine;

namespace Cali.Cutscene
{
    /// <summary>
    /// Visual hanging chain between two transforms. Uses the same chain mesh as gameplay.
    /// Cutscene-only — no physics.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(40)]
    [RequireComponent(typeof(LineRenderer))]
    public class CutsceneChain : MonoBehaviour
    {
        [Tooltip("One end. Leave empty to use the Point A child.")]
        public Transform pointA;

        [Tooltip("Other end. Leave empty to use the Point B child.")]
        public Transform pointB;

        [Tooltip("How much it hangs. 0 is a straight line.")]
        [Range(0f, 1.2f)]
        public float sag = 0.4f;

        [Range(6, 32)]
        public int segments = 16;

        [Header("Chain Mesh")]
        [Tooltip("PretoriusLab Chain prefab. Same one the horse chain uses.")]
        public GameObject linkPrefab;

        [Tooltip("World length of one visual link.")]
        public float linkMeshLength = 0.35f;

        public Vector3 linkScale = Vector3.one;

        [Tooltip("Extra rotation if the mesh does not face Z-forward.")]
        public Vector3 linkEulerOffset = new(90f, 0f, 0f);

        public bool alternateLinkTwist = true;

        public bool hideLineWhenUsingMesh = true;

        [Tooltip("Fallback line thickness if the mesh is missing.")]
        public float width = 0.07f;

        public Color color = new(0.42f, 0.32f, 0.22f, 1f);

        LineRenderer _line;
        Vector3[] _pts;
        Transform _fallbackA;
        Transform _fallbackB;
        Transform _linkRoot;
        Transform[] _links;
        Mesh[] _sliceMeshes;
        Material[] _linkMaterials;
        float _sliceLength;
        GameObject _preparedPrefab;

        void OnEnable()
        {
            TryAssignDefaultLinkPrefab();
            SetupLine();
            Rebuild();
        }

        void OnDisable()
        {
            ClearLinks();
        }

        void LateUpdate()
        {
            Rebuild();
        }

        void OnValidate()
        {
            segments = Mathf.Clamp(segments, 6, 32);
            sag = Mathf.Clamp(sag, 0f, 1.2f);
            width = Mathf.Max(0.01f, width);
            linkMeshLength = Mathf.Max(0.08f, linkMeshLength);
            SetupLine();
        }

        void TryAssignDefaultLinkPrefab()
        {
            if (linkPrefab != null)
                return;
#if UNITY_EDITOR
            linkPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/PretoriusLab/IndustrialProps/Prefabs/Chain.prefab");
#endif
        }

        void SetupLine()
        {
            if (_line == null)
                _line = GetComponent<LineRenderer>();
            if (_line == null)
                return;

            _line.useWorldSpace = true;
            _line.loop = false;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.numCapVertices = 4;
            _line.startWidth = width;
            _line.endWidth = width;
            _line.startColor = color;
            _line.endColor = color;
        }

        Transform ResolveA()
        {
            if (pointA != null)
                return pointA;
            if (_fallbackA == null)
                _fallbackA = transform.Find("Point A");
            return _fallbackA;
        }

        Transform ResolveB()
        {
            if (pointB != null)
                return pointB;
            if (_fallbackB == null)
                _fallbackB = transform.Find("Point B");
            return _fallbackB;
        }

        bool CanSpawnLinks()
        {
            if (linkPrefab == null)
                return false;
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.PrefabUtility.IsPartOfPrefabAsset(gameObject))
                return false;
#endif
            return true;
        }

        void Rebuild()
        {
            var a = ResolveA();
            var b = ResolveB();
            if (a == null || b == null)
                return;

            int n = Mathf.Clamp(segments, 6, 32);
            if (_pts == null || _pts.Length != n)
                _pts = new Vector3[n];

            FillHang(a.position, b.position, n, _pts);

            if (_line != null)
            {
                _line.positionCount = n;
                _line.SetPositions(_pts);
                _line.startWidth = width;
                _line.endWidth = width;
            }

            bool mesh = CanSpawnLinks();
            if (_line != null)
                _line.enabled = !mesh || !hideLineWhenUsingMesh;

            if (!mesh)
            {
                ClearLinks();
                return;
            }

            EnsureLinks(n - 1);
            PlaceLinks();
        }

        void FillHang(Vector3 a, Vector3 b, int n, Vector3[] dest)
        {
            Vector3 delta = b - a;
            float span = delta.magnitude;
            if (span < 0.0001f)
            {
                for (int i = 0; i < n; i++)
                    dest[i] = a;
                return;
            }

            float drop = sag * span * 0.38f;
            Vector3 down = Vector3.down;
            Vector3 along = delta / span;
            if (Mathf.Abs(Vector3.Dot(along, down)) > 0.98f)
                down = Vector3.forward;

            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                Vector3 p = Vector3.Lerp(a, b, t);
                float hang = 4f * t * (1f - t);
                dest[i] = p + down * (drop * hang);
            }
        }

        void PlaceLinks()
        {
            if (_links == null || _pts == null)
                return;

            float meshLen = Mathf.Max(0.05f, _sliceLength > 0.001f ? _sliceLength : linkMeshLength);
            bool sliced = _sliceMeshes != null && _sliceMeshes.Length > 0;
            Quaternion extra = sliced ? Quaternion.identity : Quaternion.Euler(linkEulerOffset);

            int count = Mathf.Min(_links.Length, _pts.Length - 1);
            for (int i = 0; i < count; i++)
            {
                var link = _links[i];
                if (link == null)
                    continue;

                Vector3 pa = _pts[i];
                Vector3 pb = _pts[i + 1];
                Vector3 dir = pb - pa;
                float len = dir.magnitude;
                if (len < 0.0001f)
                {
                    link.gameObject.SetActive(false);
                    continue;
                }

                link.gameObject.SetActive(true);
                link.position = 0.5f * (pa + pb);
                Quaternion look = Quaternion.LookRotation(dir / len, Vector3.up);
                if (alternateLinkTwist && (i & 1) == 1)
                    look *= Quaternion.Euler(0f, 0f, 90f);
                link.rotation = look * extra;
                float stretch = Mathf.Clamp(len / meshLen, 0.2f, 6f);
                link.localScale = Vector3.Scale(linkScale, new Vector3(1f, 1f, stretch));
            }
        }

        void EnsureLinks(int count)
        {
            PrepareLinkMeshes();

            if (_links != null && _links.Length == count && _linkRoot != null)
                return;

            ClearLinks();

            if (_linkRoot == null)
            {
                var go = new GameObject("ChainLinks");
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(transform, false);
                _linkRoot = go.transform;
            }

            _links = new Transform[count];
            bool sliced = _sliceMeshes != null && _sliceMeshes.Length > 0;
            for (int i = 0; i < count; i++)
            {
                GameObject inst;
                if (sliced)
                {
                    inst = new GameObject("ChainLink_" + i);
                    inst.hideFlags = HideFlags.DontSave;
                    inst.transform.SetParent(_linkRoot, false);
                    var mf = inst.AddComponent<MeshFilter>();
                    mf.sharedMesh = _sliceMeshes[i % _sliceMeshes.Length];
                    var mr = inst.AddComponent<MeshRenderer>();
                    if (_linkMaterials != null && _linkMaterials.Length > 0)
                        mr.sharedMaterials = _linkMaterials;
                }
                else
                {
                    inst = Instantiate(linkPrefab, _linkRoot);
                    inst.name = "ChainLink_" + i;
                    inst.hideFlags = HideFlags.DontSave;
                    inst.SetActive(true);
                    foreach (var col in inst.GetComponentsInChildren<Collider>(true))
                        col.enabled = false;
                }

                _links[i] = inst.transform;
            }
        }

        void ClearLinks()
        {
            if (_links != null)
            {
                for (int i = 0; i < _links.Length; i++)
                {
                    if (_links[i] != null)
                        DestroyLink(_links[i].gameObject);
                }
                _links = null;
            }

            if (_linkRoot != null)
            {
                DestroyLink(_linkRoot.gameObject);
                _linkRoot = null;
            }
        }

        static void DestroyLink(GameObject go)
        {
            if (go == null)
                return;
            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }

        void PrepareLinkMeshes()
        {
            if (linkPrefab == null)
                return;
            if (_sliceMeshes != null && _preparedPrefab == linkPrefab)
                return;

            _sliceMeshes = null;
            _sliceLength = 0f;
            _preparedPrefab = linkPrefab;

            try
            {
                var source = CombinePrefabMesh(linkPrefab, out _linkMaterials);
                if (source == null || source.vertexCount < 3)
                {
                    _sliceMeshes = System.Array.Empty<Mesh>();
                    return;
                }

                Bounds b = source.bounds;
                Vector3 size = b.size;
                int axis = 0;
                if (size.y >= size.x && size.y >= size.z)
                    axis = 1;
                else if (size.z >= size.x && size.z >= size.y)
                    axis = 2;

                float nativeLen = Mathf.Max(0.01f, size[axis]);
                int pieces = 1;
                if (nativeLen > linkMeshLength * 1.4f)
                    pieces = Mathf.Clamp(Mathf.RoundToInt(nativeLen / Mathf.Max(0.08f, linkMeshLength)), 2, 24);

                if (pieces <= 1)
                {
                    _sliceMeshes = System.Array.Empty<Mesh>();
                    _sliceLength = nativeLen;
                    return;
                }

                var slices = SplitMeshAlongAxis(source, axis, pieces);
                var packed = new List<Mesh>(slices.Length);
                for (int i = 0; i < slices.Length; i++)
                {
                    if (slices[i] != null && slices[i].vertexCount >= 3)
                        packed.Add(slices[i]);
                }

                if (packed.Count == 0)
                {
                    _sliceMeshes = System.Array.Empty<Mesh>();
                    _sliceLength = nativeLen;
                    return;
                }

                _sliceMeshes = packed.ToArray();
                _sliceLength = packed[0].bounds.size.z;
                if (_sliceLength < 0.01f)
                    _sliceLength = nativeLen / packed.Count;
            }
            catch (System.Exception)
            {
                _sliceMeshes = System.Array.Empty<Mesh>();
            }
        }

        static Mesh CombinePrefabMesh(GameObject prefab, out Material[] materials)
        {
            materials = null;
            var inst = Instantiate(prefab);
            inst.hideFlags = HideFlags.HideAndDontSave;
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            inst.transform.localScale = Vector3.one;
            inst.SetActive(true);

            var renderers = inst.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length > 0)
                materials = renderers[0].sharedMaterials;

            var filters = inst.GetComponentsInChildren<MeshFilter>(true);
            var packed = new List<CombineInstance>(filters.Length);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i] == null || filters[i].sharedMesh == null)
                    continue;
                packed.Add(new CombineInstance
                {
                    mesh = filters[i].sharedMesh,
                    transform = inst.transform.worldToLocalMatrix * filters[i].transform.localToWorldMatrix
                });
            }

            Mesh mesh = null;
            if (packed.Count > 0)
            {
                mesh = new Mesh { name = prefab.name + "_Combined" };
                mesh.CombineMeshes(packed.ToArray(), true, true);
            }

            DestroyLink(inst);
            return mesh;
        }

        static Mesh[] SplitMeshAlongAxis(Mesh source, int axis, int pieces)
        {
            pieces = Mathf.Max(1, pieces);
            var verts = source.vertices;
            var tris = source.triangles;
            if (verts.Length == 0 || tris.Length < 3)
                return System.Array.Empty<Mesh>();

            var norms = source.normals;
            var uvs = source.uv;
            bool hasUv = uvs != null && uvs.Length == verts.Length;
            bool hasN = norms != null && norms.Length == verts.Length;

            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                float a = verts[i][axis];
                if (a < min) min = a;
                if (a > max) max = a;
            }

            float span = Mathf.Max(0.0001f, max - min);
            var buckets = new List<int>[pieces];
            for (int i = 0; i < pieces; i++)
                buckets[i] = new List<int>(64);

            for (int t = 0; t < tris.Length; t += 3)
            {
                int i0 = tris[t];
                int i1 = tris[t + 1];
                int i2 = tris[t + 2];
                float u0 = (verts[i0][axis] - min) / span;
                float u1 = (verts[i1][axis] - min) / span;
                float u2 = (verts[i2][axis] - min) / span;
                int p0 = Mathf.Clamp(Mathf.FloorToInt(u0 * pieces), 0, pieces - 1);
                int p1 = Mathf.Clamp(Mathf.FloorToInt(u1 * pieces), 0, pieces - 1);
                int p2 = Mathf.Clamp(Mathf.FloorToInt(u2 * pieces), 0, pieces - 1);
                int lo = Mathf.Min(p0, Mathf.Min(p1, p2));
                int hi = Mathf.Max(p0, Mathf.Max(p1, p2));
                for (int p = lo; p <= hi; p++)
                {
                    buckets[p].Add(i0);
                    buckets[p].Add(i1);
                    buckets[p].Add(i2);
                }
            }

            var result = new Mesh[pieces];
            for (int p = 0; p < pieces; p++)
            {
                if (buckets[p].Count < 3)
                    continue;

                var map = new Dictionary<int, int>(buckets[p].Count);
                var nv = new List<Vector3>(buckets[p].Count);
                var nn = hasN ? new List<Vector3>(buckets[p].Count) : null;
                var nu = hasUv ? new List<Vector2>(buckets[p].Count) : null;
                var nt = new List<int>(buckets[p].Count);

                for (int i = 0; i < buckets[p].Count; i++)
                {
                    int old = buckets[p][i];
                    if (!map.TryGetValue(old, out int neu))
                    {
                        neu = nv.Count;
                        map[old] = neu;
                        nv.Add(AlignToForwardZ(verts[old], axis));
                        if (hasN)
                            nn.Add(AlignToForwardZ(norms[old], axis));
                        if (hasUv)
                            nu.Add(uvs[old]);
                    }
                    nt.Add(neu);
                }

                Vector3 center = Vector3.zero;
                for (int i = 0; i < nv.Count; i++)
                    center += nv[i];
                if (nv.Count > 0)
                    center /= nv.Count;
                for (int i = 0; i < nv.Count; i++)
                    nv[i] -= center;

                var mesh = new Mesh { name = source.name + "_Slice" + p };
                mesh.SetVertices(nv);
                mesh.SetTriangles(nt, 0);
                if (hasN)
                    mesh.SetNormals(nn);
                else
                    mesh.RecalculateNormals();
                if (hasUv)
                    mesh.SetUVs(0, nu);
                mesh.RecalculateBounds();
                result[p] = mesh;
            }

            return result;
        }

        static Vector3 AlignToForwardZ(Vector3 v, int axis)
        {
            if (axis == 1)
                return new Vector3(v.x, v.z, v.y);
            if (axis == 0)
                return new Vector3(-v.z, v.y, v.x);
            return v;
        }

        void OnDrawGizmos()
        {
            var a = ResolveA();
            var b = ResolveB();
            if (a == null || b == null)
                return;

            Gizmos.color = new Color(0.85f, 0.65f, 0.25f, 0.9f);
            Gizmos.DrawWireSphere(a.position, 0.08f);
            Gizmos.DrawWireSphere(b.position, 0.08f);
        }
    }
}
