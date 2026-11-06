using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FloorF.Harness
{
    /// <summary>
    /// Renders the current state of all cameras into a PNG. Works in batch-mode PlayMode tests,
    /// where ScreenCapture has no window to read from.
    /// Rule this relies on: UI canvases are "Screen Space - Camera" (Overlay canvases are not captured).
    /// Output dir: $FLOORF_CAPTURE_DIR (set by tools/unity-batch.ps1), else temporaryCachePath/captures.
    /// </summary>
    public static class FrameCapture
    {
        public static string OutputDir
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("FLOORF_CAPTURE_DIR");
                return string.IsNullOrEmpty(env) ? Path.Combine(Application.temporaryCachePath, "captures") : env;
            }
        }

        public static string Capture(string name, int width = 1280, int height = 720)
        {
            Directory.CreateDirectory(OutputDir);
            var path = Path.Combine(OutputDir, name + ".png");

            var cams = Camera.allCameras
                .Where(c => c != null && c.isActiveAndEnabled)
                .Where(c =>
                {
                    var data = c.GetComponent<UniversalAdditionalCameraData>();
                    return data == null || data.renderType != CameraRenderType.Overlay; // overlays render inside their base camera's stack
                })
                .OrderBy(c => c.depth)
                .ToList();

            var final = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            try
            {
                RenderTexture.active = final;
                GL.Clear(true, true, Color.black);
                RenderTexture.active = prevActive;

                // 1) offscreen cameras first (e.g. the low-res pixel world camera), 2) screen cameras into the capture target
                foreach (var cam in cams.Where(c => c.targetTexture != null)) RenderInto(cam, cam.targetTexture);
                foreach (var cam in cams.Where(c => c.targetTexture == null)) RenderInto(cam, final);

                RenderTexture.active = final;
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(final);
            }
            Debug.Log("[FrameCapture] " + path);
            return path;
        }

        static void RenderInto(Camera cam, RenderTexture target)
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);
                return;
            }
            var prev = cam.targetTexture;
            cam.targetTexture = target;
            cam.Render();
            cam.targetTexture = prev;
        }
    }
}
