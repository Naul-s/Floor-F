using System;
using FloorF.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FloorF.Rendering
{
    /// <summary>
    /// GDD 8 / ARCHITECTURE "Rendering pipeline":
    ///   world camera (the scene's "Main Camera", orthographic) -> 640x360 RenderTexture (Point, no MSAA, no HDR)
    ///   -> UI camera "[Rendering] UI Camera" with a Screen Space - Camera canvas (sorting -100) showing it aspect-fit.
    /// HUD canvases go on the same UI camera with sortingOrder >= 0 (they then draw at native resolution on top).
    /// </summary>
    sealed class PixelPipeline : IDisposable
    {
        public RenderTexture Target { get; private set; }
        public Camera WorldCamera { get; private set; }
        public Camera UiCamera { get; private set; }
        public Canvas ScreenCanvas { get; private set; }
        public PixelScreen Screen { get; private set; }
        /// <summary>Low-res glow on emissive pixels (PixelGlow.DefaultOn; Enabled toggles it at runtime).</summary>
        public PixelGlow Glow { get; private set; }
        bool createdWorldCamera;

        public static int UiLayer => PresentationContract.UiLayer;

        public static PixelPipeline Create(Transform parent, Camera sceneCamera)
        {
            var p = new PixelPipeline();
            p.Target = new RenderTexture(new RenderTextureDescriptor(RenderingStyle.PixelWidth, RenderingStyle.PixelHeight, RenderTextureFormat.ARGB32, 24)
            {
                msaaSamples = 1,
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
                useMipMap = false,
                autoGenerateMips = false,
            })
            {
                name = "FloorF PixelTarget",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
            };
            p.Target.Create();

            if (sceneCamera == null)
            {
                var go = new GameObject("World Camera");
                go.transform.SetParent(parent, false);
                sceneCamera = go.AddComponent<Camera>();
                p.createdWorldCamera = true;
            }
            p.WorldCamera = sceneCamera;
            ConfigureWorldCamera(sceneCamera, p.Target);

            p.UiCamera = CreateUiCamera(parent);
            p.ScreenCanvas = CreateCanvas(parent, p.UiCamera);
            var screenGo = new GameObject("PixelScreen", typeof(RectTransform), typeof(CanvasRenderer));
            screenGo.layer = UiLayer;
            var rt = (RectTransform)screenGo.transform;
            rt.SetParent(p.ScreenCanvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            p.Screen = screenGo.AddComponent<PixelScreen>();
            p.Screen.SetTexture(p.Target, RenderingStyle.PixelWidth, RenderingStyle.PixelHeight);
            p.Glow = new PixelGlow(p.Target, p.WorldCamera, p.Screen);
            return p;
        }

        static void ConfigureWorldCamera(Camera cam, RenderTexture target)
        {
            cam.orthographic = true;
            cam.targetTexture = target;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            cam.allowDynamicResolution = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderingStyle.Background;
            cam.cullingMask = ~(1 << UiLayer);
            cam.depth = -10f;
            cam.aspect = (float)RenderingStyle.PixelWidth / RenderingStyle.PixelHeight;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = true;
        }

        static Camera CreateUiCamera(Transform parent)
        {
            var go = new GameObject(RenderingNames.UiCamera);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(new Vector3(0f, -500f, 0f), Quaternion.identity); // away from the world
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 1 << UiLayer;
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 50f;
            cam.depth = 0f;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = false;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.requiresColorOption = CameraOverrideOption.Off;
            return cam;
        }

        static Canvas CreateCanvas(Transform parent, Camera uiCamera)
        {
            var go = new GameObject(RenderingNames.ScreenCanvas, typeof(RectTransform));
            go.layer = UiLayer;
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = uiCamera;
            canvas.planeDistance = RenderingStyle.ScreenCanvasPlaneDistance;
            canvas.sortingOrder = RenderingStyle.ScreenCanvasSortingOrder;
            canvas.pixelPerfect = false;
            return canvas;
        }

        public void ApplyFrame(CameraFrame frame)
        {
            if (WorldCamera == null) return;
            WorldCamera.transform.SetPositionAndRotation(frame.Position, frame.Rotation);
            WorldCamera.orthographicSize = frame.OrthographicSize;
            WorldCamera.nearClipPlane = 0.3f;
            WorldCamera.farClipPlane = RenderingStyle.CameraDistance + 80f;
            // oblique orthographic projection (see CameraFrame); culling and WorldToViewportPoint use it as well
            WorldCamera.projectionMatrix = CameraFraming.ProjectionMatrix(frame, (float)RenderingStyle.PixelWidth / RenderingStyle.PixelHeight,
                WorldCamera.nearClipPlane, WorldCamera.farClipPlane);
            Frame = frame;
        }

        /// <summary>The frame applied last (screen-plane shear + pixel grid).</summary>
        public CameraFrame Frame { get; private set; }

        public void Dispose()
        {
            Glow?.Dispose();
            Glow = null;
            if (WorldCamera != null)
            {
                if (WorldCamera.targetTexture == Target) WorldCamera.targetTexture = null;
                WorldCamera.ResetProjectionMatrix();
                if (createdWorldCamera) Object.Destroy(WorldCamera.gameObject);
            }
            if (Target != null)
            {
                Target.Release();
                Object.Destroy(Target);
            }
            Target = null;
        }
    }
}
