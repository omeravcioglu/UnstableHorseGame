using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Cali.UI
{
    /// <summary>
    /// Fullscreen video surface that keeps a clip's own shape. The render target is sized
    /// from the source resolution instead of the window, and the image is letterboxed
    /// inside the canvas rather than stretched to fill it.
    /// </summary>
    public sealed class CaliVideoSurface
    {
        /// <summary>Oversized clips scale down proportionally rather than allocating a huge target.</summary>
        const int MaxEdge = 1920;

        readonly VideoPlayer _player;
        readonly RawImage _image;
        readonly AspectRatioFitter _fitter;
        readonly string _textureName;
        RenderTexture _rt;

        public CaliVideoSurface(VideoPlayer player, RawImage image, string textureName)
        {
            _player = player;
            _image = image;
            _textureName = textureName;

            _fitter = image.GetComponent<AspectRatioFitter>();
            if (_fitter == null)
                _fitter = image.gameObject.AddComponent<AspectRatioFitter>();
            _fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            _fitter.aspectRatio = 16f / 9f;
        }

        /// <summary>Sizes the target from import metadata, before the player is prepared.</summary>
        public void BindClip(VideoClip clip)
        {
            if (clip == null)
                return;

            Bind((int)clip.width, (int)clip.height,
                clip.pixelAspectRatioNumerator, clip.pixelAspectRatioDenominator);
        }

        /// <summary>
        /// Re-sizes from what the player actually decoded. This is the authoritative size and
        /// also covers sources with no usable import metadata, such as URLs.
        /// </summary>
        public void BindPrepared()
        {
            if (_player == null)
                return;

            Bind((int)_player.width, (int)_player.height,
                _player.pixelAspectRatioNumerator, _player.pixelAspectRatioDenominator);
        }

        void Bind(int width, int height, uint parNumerator, uint parDenominator)
        {
            if (width <= 0 || height <= 0)
                return;

            // Anamorphic sources store non-square pixels, so display shape is not width/height.
            float par = parNumerator > 0 && parDenominator > 0
                ? parNumerator / (float)parDenominator
                : 1f;
            _fitter.aspectRatio = Mathf.Max(0.01f, width * par / height);

            int w = width;
            int h = height;
            int edge = Mathf.Max(w, h);
            if (edge > MaxEdge)
            {
                float scale = MaxEdge / (float)edge;
                w = Mathf.Max(2, Mathf.RoundToInt(w * scale));
                h = Mathf.Max(2, Mathf.RoundToInt(h * scale));
            }

            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                ReleaseTexture();
                _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = _textureName };
            }

            _player.targetTexture = _rt;
            _image.texture = _rt;
        }

        public void Release()
        {
            if (_player != null)
                _player.targetTexture = null;
            if (_image != null)
                _image.texture = null;
            ReleaseTexture();
        }

        void ReleaseTexture()
        {
            if (_rt == null)
                return;

            _rt.Release();
            Object.Destroy(_rt);
            _rt = null;
        }
    }
}
