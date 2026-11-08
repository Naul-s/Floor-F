using UnityEngine;

namespace FloorF.Rendering
{
    /// <summary>
    /// Shows the low-res world RenderTexture on a Screen Space - Camera canvas, aspect-fit (letterboxed) and
    /// point-sampled. This is what a uGUI RawImage would do; it drives the engine's CanvasRenderer directly because
    /// FloorF.Rendering does not reference UnityEngine.UI (asmdef owned by the orchestrator).
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    sealed class PixelScreen : MonoBehaviour
    {
        Texture texture;
        int sourceWidth = RenderingStyle.PixelWidth;
        int sourceHeight = RenderingStyle.PixelHeight;
        CanvasRenderer canvasRenderer;
        Mesh mesh;
        Rect lastRect;
        bool dirty = true;

        /// <summary>Screen-space rect (inside the canvas) the image covers after aspect fit. For tests / debugging.</summary>
        public Rect DisplayRect { get; private set; }
        public Texture Texture => texture;
        Material material;
        /// <summary>Material drawing the target (null = the default UI material; PixelGlow sets its composite).</summary>
        public Material Material => material;

        public void SetMaterial(Material m)
        {
            if (material == m) return;
            material = m;
            dirty = true;
            Rebuild();
        }

        public void SetTexture(Texture tex, int width, int height)
        {
            texture = tex;
            sourceWidth = Mathf.Max(1, width);
            sourceHeight = Mathf.Max(1, height);
            dirty = true;
            Rebuild();
        }

        void Awake()
        {
            canvasRenderer = GetComponent<CanvasRenderer>();
            mesh = new Mesh { name = "PixelScreen" };
            mesh.MarkDynamic();
        }

        void OnRectTransformDimensionsChange() => dirty = true;

        void LateUpdate()
        {
            var r = ((RectTransform)transform).rect;
            if (r != lastRect) dirty = true;
            if (dirty) Rebuild();
        }

        void Rebuild()
        {
            if (canvasRenderer == null || mesh == null) return;
            var r = ((RectTransform)transform).rect;
            lastRect = r;
            dirty = false;
            if (r.width <= 0f || r.height <= 0f) return;

            float scale = Mathf.Min(r.width / sourceWidth, r.height / sourceHeight);
            float w = sourceWidth * scale, h = sourceHeight * scale;
            var display = new Rect(r.center.x - w * 0.5f, r.center.y - h * 0.5f, w, h);
            DisplayRect = display;

            mesh.Clear();
            mesh.SetVertices(new[]
            {
                new Vector3(display.xMin, display.yMin), new Vector3(display.xMin, display.yMax),
                new Vector3(display.xMax, display.yMax), new Vector3(display.xMax, display.yMin),
            });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) });
            mesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
            mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);

            canvasRenderer.SetMesh(mesh);
            canvasRenderer.materialCount = 1;
            canvasRenderer.SetMaterial(material != null ? material : Canvas.GetDefaultCanvasMaterial(), 0);
            canvasRenderer.SetTexture(texture);
            canvasRenderer.SetColor(Color.white);
        }

        void OnDestroy()
        {
            if (canvasRenderer != null) canvasRenderer.Clear();
            if (mesh != null) Destroy(mesh);
        }
    }
}
