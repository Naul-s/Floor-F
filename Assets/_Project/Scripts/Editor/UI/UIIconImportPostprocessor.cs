using UnityEditor;

namespace FloorF.EditorTools.UI
{
    /// <summary>
    /// Destination icons (Resources/Icons/*.png, read by FloorF.UI.UIIcons via Resources.Load&lt;Sprite&gt;) must import
    /// as single 2D sprites. The Material Symbols metas from the asset import carry textureShape 2 (Cube), so Unity
    /// produced Cubemaps and every icon load returned null. This forces the shape/type for the import only (the .meta
    /// itself is not rewritten - HANDOFF: orchestrator re-saves them with ConfigureThirdPartyImporters + textureShape).
    /// </summary>
    sealed class UIIconImportPostprocessor : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Icons/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureShape = TextureImporterShape.Texture2D;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
        }
    }
}
