using TMPro;
using UnityEngine;

namespace Cali.Gameplay
{
    /// <summary>
    /// Billboard over the stall door. Visible once batch 2 has unlocked the latch,
    /// hidden when the mini-game starts or the door is already open.
    /// </summary>
    public class LatchDoorMarker : MonoBehaviour
    {
        const float Height = 3.4f;

        Transform _door;
        TextMeshPro _label;
        Transform _diamond;
        bool _visible;

        public static LatchDoorMarker Attach(Transform door)
        {
            var go = new GameObject("LatchDoorMarker");
            var marker = go.AddComponent<LatchDoorMarker>();
            marker._door = door;
            marker.Build();
            marker.SetVisible(false);
            return marker;
        }

        public void SetVisible(bool on)
        {
            _visible = on;
            if (gameObject.activeSelf != on)
                gameObject.SetActive(on);
        }

        void Build()
        {
            _diamond = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            _diamond.name = "Diamond";
            _diamond.SetParent(transform, false);
            _diamond.localScale = new Vector3(0.35f, 0.35f, 0.08f);
            _diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
            Object.Destroy(_diamond.GetComponent<Collider>());

            var rend = _diamond.GetComponent<MeshRenderer>();
            rend.sharedMaterial = MakeMat(new Color(1f, 0.82f, 0.22f, 1f));
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            _label = textGo.AddComponent<TextMeshPro>();
            _label.text = "UNLATCH";
            _label.fontSize = 4.5f;
            _label.alignment = TextAlignmentOptions.Center;
            _label.color = new Color(1f, 0.93f, 0.65f, 1f);
            _label.fontStyle = FontStyles.Bold;
            _label.outlineWidth = 0.18f;
            _label.outlineColor = new Color(0.15f, 0.08f, 0.02f, 1f);
            var tr = _label.rectTransform;
            tr.sizeDelta = new Vector2(4.5f, 1.2f);
        }

        void LateUpdate()
        {
            if (!_visible)
                return;

            Vector3 pos = _door != null
                ? _door.position + Vector3.up * Height
                : transform.position;
            transform.position = pos;

            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 toCam = transform.position - cam.transform.position;
                if (toCam.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(toCam, Vector3.up);
            }

            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 4.2f);
            if (_diamond != null)
                _diamond.localScale = new Vector3(0.35f, 0.35f, 0.08f) * pulse;
        }

        static Material MakeMat(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            return mat;
        }
    }
}
