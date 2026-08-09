using Engine;
using Engine.Graphics;
using Engine.Media;

namespace Game.IContentReader {
    /// <summary>
    ///     位图字体读取器<br/>
    ///     可使用此工具生成 https://github.com/XiaofengdiZhu/SC-Chinese
    /// </summary>
    public class BitmapFontReader : IContentReader {
        public override string Type => "Engine.Media.BitmapFont";
        public override string[] DefaultSuffix => ["lst", "astc", "astcsrgb", "webp", "png"];

        public override object Get(ContentInfo[] contents) {
            ContentInfo[] glyphMatches = contents
                .Where(content => content.ContentSuffix == ".lst")
                .ToArray();
            if (glyphMatches.Length != 1) {
                throw new Exception(
                    $"Bitmap font requires exactly one .lst resource, found {glyphMatches.Length}.");
            }
            ContentInfo glyphs = glyphMatches[0];
            ContentInfo[] textures = contents
                .Where(content => content.ContentSuffix != ".lst")
                .ToArray();
            ContentInfo[] sameSourceTextures = textures
                .Where(content => string.Equals(
                    content.SourcePackageName,
                    glyphs.SourcePackageName,
                    StringComparison.Ordinal))
                .ToArray();
            ContentInfo texture;
            if (sameSourceTextures.Length == 1) {
                texture = sameSourceTextures[0];
            }
            else if (sameSourceTextures.Length == 0 && textures.Length == 1) {
                texture = textures[0];
            }
            else {
                throw new Exception(
                    $"Bitmap font texture is ambiguous for source '{glyphs.SourcePackageName}': "
                    + $"{sameSourceTextures.Length} same-source candidates, {textures.Length} total candidates.");
            }
            return BitmapFont.Initialize(ContentManager.Get<Texture2D>(texture.ContentPath, texture.ContentSuffix), glyphs.Duplicate(), Vector2.Zero);
        }
    }
}
