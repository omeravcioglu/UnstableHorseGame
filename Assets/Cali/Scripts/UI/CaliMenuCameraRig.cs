using System.Collections;
using UnityEngine;

namespace Cali.UI
{
    public enum CaliMenuState
    {
        Main = 0,
        Host = 1,
        Join = 2,
        Settings = 3,
    }

    /// <summary>
    /// Moves the menu camera between named anchors with eased lerp.
    /// </summary>
    public class CaliMenuCameraRig : MonoBehaviour
    {
        public Camera menuCamera;
        public Transform anchorMain;
        public Transform anchorHost;
        public Transform anchorJoin;
        public Transform anchorSettings;
        public float moveDuration = 1.1f;

        public bool IsMoving { get; private set; }

        public void SnapTo(CaliMenuState state)
        {
            ResolveAnchorsIfNeeded();
            var anchor = GetAnchor(state);
            if (anchor == null || menuCamera == null)
                return;
            menuCamera.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
        }

        public Coroutine MoveTo(CaliMenuState state, MonoBehaviour host)
        {
            return MoveTo(state, host, moveDuration);
        }

        public Coroutine MoveTo(CaliMenuState state, MonoBehaviour host, float duration)
        {
            ResolveAnchorsIfNeeded();
            return host.StartCoroutine(MoveToRoutine(state, duration));
        }

        void ResolveAnchorsIfNeeded()
        {
            if (menuCamera == null)
                menuCamera = Camera.main;

            if (anchorMain == null) anchorMain = transform.Find("CamAnchor_Main");
            if (anchorHost == null) anchorHost = transform.Find("CamAnchor_Host");
            if (anchorJoin == null) anchorJoin = transform.Find("CamAnchor_Join");
            if (anchorSettings == null) anchorSettings = transform.Find("CamAnchor_Settings");
        }

        IEnumerator MoveToRoutine(CaliMenuState state, float duration)
        {
            var anchor = GetAnchor(state);
            if (anchor == null || menuCamera == null)
                yield break;

            IsMoving = true;
            var camTx = menuCamera.transform;
            Vector3 startPos = camTx.position;
            Quaternion startRot = camTx.rotation;
            Vector3 endPos = anchor.position;
            Quaternion endRot = anchor.rotation;
            float dur = Mathf.Max(0.05f, duration);
            float t = 0f;

            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / dur;
                float e = EaseInOut(Mathf.Clamp01(t));
                camTx.SetPositionAndRotation(
                    Vector3.Lerp(startPos, endPos, e),
                    Quaternion.Slerp(startRot, endRot, e));
                yield return null;
            }

            camTx.SetPositionAndRotation(endPos, endRot);
            IsMoving = false;
        }

        public Transform GetAnchor(CaliMenuState state)
        {
            return state switch
            {
                CaliMenuState.Host => anchorHost,
                CaliMenuState.Join => anchorJoin,
                CaliMenuState.Settings => anchorSettings,
                _ => anchorMain,
            };
        }

        static float EaseInOut(float t)
        {
            return t * t * (3f - 2f * t);
        }
    }
}
