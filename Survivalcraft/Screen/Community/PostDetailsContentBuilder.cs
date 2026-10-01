using System.Globalization;
using System.Net;
using System.Text;
using Engine;
using Engine.Graphics;
using HtmlAgilityPack;

namespace Game {
    /// <summary>
    /// 帖子/版本详情内容构建器：HtmlAgilityPack 解析出的 DOM 树就是唯一模型，
    /// 本类直接遍历 HtmlNode 生成引擎 widget（不维护第二套中间块模型）。
    /// 富文本降级规则：块级标签切分段落、img 走图片管线、a 降级为 " (href)" 纯文本
    /// （引擎 Label 不可点击）、li 按列表深度缩进并带序号标记。
    /// 另持有已加载图片纹理的生命周期（引擎 Dialog/Widget 树无 Dispose 级联）。
    /// </summary>
    sealed class PostDetailsContentBuilder {
        const float ContentWidth = 828f;

        // 每层列表缩进宽度（标签"li 深度 × 缩进"对应视觉上一档层级）。
        const float ListIndentPerDepth = 22f;

        static readonly CommunityHttpDownloader s_imageDownloader = new(HttpClientProvider.Community);

        readonly StackPanelWidget m_contentPanel;

        readonly string m_baseUrl;

        // Texture2D 持有 GL 纹理句柄（GraphicsResource:IDisposable），框架的 Dialog/Widget 树无 Dispose 级联。
        // 收集每张加载的纹理，重建内容或对话框移除时统一释放，避免每开一次详情累积一个不可回收句柄。
        readonly List<Texture2D> m_loadedTextures = [];

        // URL → 纹理：内容重建（自动翻译等）时复用已下载图片，避免同一张图下载两次。
        // 纹理对象与 m_loadedTextures 同源（后者负责释放），对话框关闭时一并清空。
        readonly Dictionary<string, Texture2D> m_imageTextureCache = new(StringComparer.OrdinalIgnoreCase);

        // 对话框脱离 widget 树后置位。图片下载是异步的：若用户在下载未完成时关闭对话框，
        // UpdateCeases 已把列表清空，晚到的回调若再把纹理加入列表将永不被释放（UpdateCeases 不会再次触发）。
        // 回调据此标志就地释放新纹理，避免 GL 句柄泄漏。
        bool m_texturesReleased;

        // 遍历状态（每次 AppendContent 重置）。
        readonly StringBuilder m_textBuffer = new();
        BlockStyle m_kind = BlockStyle.Paragraph;
        bool m_preserveWhitespace;
        string m_linkHref;
        int m_listDepth;
        bool m_listOrdered;

        int m_listNextNumber;

        // 标记栈：li 的序号/圆点标记不写进文本缓冲（内层 p 等块级标签会把缓冲冲刷成
        // 孤儿标记行），挂在 li 生命期内由首个非空 flush 消费；空 li 或仅嵌套列表的 li 丢弃。
        readonly List<(string Marker, bool Consumed)> m_markerStack = [];
        int m_liDepth;
        float m_listIndent;
        bool m_appendedAny;

        public PostDetailsContentBuilder(StackPanelWidget contentPanel, string baseUrl) {
            m_contentPanel = contentPanel ?? throw new ArgumentNullException(nameof(contentPanel));
            m_baseUrl = baseUrl;
        }

        // 翻译前置检查：是否存在可翻译的可见文本（script/style 与纯图片不计）。
        public static bool HasVisibleText(string html) {
            if (string.IsNullOrWhiteSpace(html)) {
                return false;
            }
            HtmlDocument document = new();
            document.LoadHtml(html);
            return HasVisibleText(document.DocumentNode);
        }

        // 解析 html 并把内容 widget 追加到内容面板。返回 false 表示没有产出任何内容（调用方可补占位文案）。
        public bool AppendContent(string html) {
            m_appendedAny = false;
            m_textBuffer.Clear();
            m_kind = BlockStyle.Paragraph;
            m_preserveWhitespace = false;
            m_linkHref = null;
            m_listDepth = 0;
            m_listOrdered = false;
            m_listNextNumber = 0;
            m_markerStack.Clear();
            m_liDepth = 0;
            m_listIndent = 0f;
            if (string.IsNullOrWhiteSpace(html)) {
                return false;
            }
            HtmlDocument document = new();
            document.LoadHtml(html);
            WalkChildren(document.DocumentNode);
            Flush();
            return m_appendedAny;
        }

        // 纯文本段落（空内容占位文案等）。
        public void AppendParagraph(string text) {
            if (!string.IsNullOrWhiteSpace(text)) {
                AppendStyledLabel(m_contentPanel, BlockStyle.Paragraph, text, 0f);
                m_appendedAny = true;
            }
        }

        // widget 级语义原语：供 header 区构建与纯文本渲染直接产出 widget（不经 HTML 序列化再解析）。
        // 与 HTML 解析路径共用 AppendStyledLabel，视觉样式同源。
        public static void AppendHeading(StackPanelWidget panel, string text) {
            if (!string.IsNullOrWhiteSpace(text)) {
                AppendStyledLabel(panel, BlockStyle.Heading2, text, 0f);
            }
        }

        public static void AppendParagraph(StackPanelWidget panel, string text) {
            if (!string.IsNullOrWhiteSpace(text)) {
                AppendStyledLabel(panel, BlockStyle.Paragraph, text, 0f);
            }
        }

        public static void AppendListItem(StackPanelWidget panel, string text) {
            if (!string.IsNullOrWhiteSpace(text)) {
                AppendStyledLabel(panel, BlockStyle.ListItem, text, ListIndentPerDepth);
            }
        }

        // 纯文本（Gitee release notes 等）逐行渲染：行解析在 HtmlTextUtils.SplitPlainTextLines。
        public static void AppendPlainText(StackPanelWidget panel, string text) {
            foreach ((bool isListItem, string line) in HtmlTextUtils.SplitPlainTextLines(text)) {
                if (isListItem) {
                    AppendListItem(panel, "• " + line);
                }
                else {
                    AppendParagraph(panel, line);
                }
            }
        }

        // 对话框关闭（UpdateCeases）时调用：释放纹理句柄并标记已释放，
        // 晚到的图片下载回调据此就地释放新纹理，不再加入列表。
        public void ReleaseTextures() {
            if (m_loadedTextures.Count == 0) {
                m_texturesReleased = true;
                return;
            }
            foreach (Texture2D texture in m_loadedTextures) {
                try {
                    texture?.Dispose();
                }
                catch (Exception ex) {
                    Log.Information(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("PostDetailsContentBuilder", "DisposePostDetailsTextureFailed"),
                            ex.Message
                        )
                    );
                }
            }
            m_loadedTextures.Clear();
            m_imageTextureCache.Clear();
            m_texturesReleased = true;
        }

        static bool HasVisibleText(HtmlNode node) {
            foreach (HtmlNode child in node.ChildNodes) {
                switch (child.NodeType) {
                    case HtmlNodeType.Text:
                        if (!string.IsNullOrWhiteSpace(HtmlEntity.DeEntitize(((HtmlTextNode)child).Text))) {
                            return true;
                        }
                        break;
                    case HtmlNodeType.Comment: break;
                    case HtmlNodeType.Element:
                        if (child.Name?.ToLowerInvariant() is not ("script" or "style")
                            && HasVisibleText(child)) {
                            return true;
                        }
                        break;
                }
            }
            return false;
        }

        void WalkChildren(HtmlNode node) {
            foreach (HtmlNode child in node.ChildNodes) {
                switch (child.NodeType) {
                    case HtmlNodeType.Text: AppendText(((HtmlTextNode)child).Text); break;
                    case HtmlNodeType.Comment: break;
                    case HtmlNodeType.Element: VisitElement(child); break;
                }
            }
        }

        void VisitElement(HtmlNode element) {
            string tagName = element.Name?.ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrEmpty(tagName)) {
                return;
            }
            if (tagName is "script" or "style") {
                return;
            }
            if (tagName == "img") {
                Flush();
                AddImageBlock(
                    HtmlEntity.DeEntitize(element.GetAttributeValue("src", string.Empty)),
                    HtmlEntity.DeEntitize(element.GetAttributeValue("alt", string.Empty))
                );
                return;
            }
            if (tagName == "br") {
                m_textBuffer.Append('\n');
                return;
            }
            if (tagName == "a") {
                m_linkHref = HtmlEntity.DeEntitize(element.GetAttributeValue("href", string.Empty));
                WalkChildren(element);
                if (!string.IsNullOrWhiteSpace(m_linkHref)) {
                    m_textBuffer.Append(" (").Append(m_linkHref).Append(')');
                }
                m_linkHref = null;
                return;
            }
            if (tagName is "ul" or "ol") {
                bool prevOrdered = m_listOrdered;
                int prevNextNumber = m_listNextNumber;
                Flush();
                m_listDepth++;
                m_listOrdered = tagName == "ol";
                m_listNextNumber = m_listOrdered ? ParseListStart(element) : 0;
                WalkChildren(element);
                Flush();
                m_listDepth--;
                m_listOrdered = prevOrdered;
                m_listNextNumber = prevNextNumber;
                return;
            }
            if (tagName == "li") {
                Flush();
                m_markerStack.Add((HtmlTextUtils.GetListMarker(m_listOrdered, m_listNextNumber, m_listDepth), false));
                if (m_listOrdered) {
                    m_listNextNumber++;
                }
                float prevIndent = m_listIndent;
                m_listIndent = ListIndentPerDepth * m_listDepth;
                m_liDepth++;
                WalkChildren(element);
                Flush();
                m_liDepth--;
                m_listIndent = prevIndent;
                // 未被消费的标记直接丢弃：空 li、仅嵌套列表的 li 不产生孤儿标记行。
                m_markerStack.RemoveAt(m_markerStack.Count - 1);
                return;
            }
            if (IsBlockTag(tagName)
                || IsHeadingTag(tagName)
                || tagName == "blockquote"
                || tagName == "pre") {
                Flush();
                m_kind = GetStyleForTag(tagName);
                m_preserveWhitespace = tagName == "pre";
                WalkChildren(element);
                Flush();
                m_kind = BlockStyle.Paragraph;
                m_preserveWhitespace = false;
                return;
            }

            // 未知/行内标签（b、i、span…）：文本继续累积。
            WalkChildren(element);
        }

        void AppendText(string rawText) {
            string text = HtmlEntity.DeEntitize(rawText);
            if (!m_preserveWhitespace) {
                text = HtmlTextUtils.NormalizeInline(text);
            }
            if (string.IsNullOrWhiteSpace(text)) {
                return;
            }
            if (!m_preserveWhitespace
                && m_textBuffer.Length > 0
                && !char.IsWhiteSpace(m_textBuffer[^1])
                && !char.IsWhiteSpace(text[0])) {
                m_textBuffer.Append(' ');
            }
            m_textBuffer.Append(text);
        }

        void Flush() {
            string value = m_textBuffer.ToString();
            value = m_preserveWhitespace ? value.Trim('\n', '\r') : HtmlTextUtils.NormalizeBlock(value);
            m_textBuffer.Clear();
            if (string.IsNullOrWhiteSpace(value)) {
                return;
            }

            // li 生命期内的所有块（含内层 p 等）都按列表项渲染：标记 + 缩进；
            // 标记只挂在首个非空块上，后续续行块保持缩进不再重复标记。
            bool inListItem = m_liDepth > 0;
            string text = value.Trim();
            if (inListItem
                && m_markerStack.Count > 0
                && !m_markerStack[^1].Consumed) {
                text = m_markerStack[^1].Marker + " " + text;
                m_markerStack[^1] = (m_markerStack[^1].Marker, true);
            }
            AppendStyledLabel(m_contentPanel, inListItem ? BlockStyle.ListItem : m_kind, text, inListItem ? m_listIndent : 0f);
            m_appendedAny = true;
        }

        static int ParseListStart(HtmlNode element) => int.TryParse(
                element.GetAttributeValue("start", "1"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int start
            )
            && start > 0
                ? start
                : 1;

        void AddImageBlock(string url, string alt) {
            url = ResolveUrl(url);
            alt = string.IsNullOrWhiteSpace(alt) ? LanguageControl.Get("PostDetailsContentBuilder", "Image") : alt.Trim();
            AddSpacer(m_contentPanel, 10f);
            CanvasWidget frame = new() { Size = new Vector2(ContentWidth, 220f), HorizontalAlignment = WidgetAlignment.Center };
            m_contentPanel.Children.Add(frame);
            RectangleWidget background = new() {
                Size = new Vector2(ContentWidth, 220f),
                FillColor = new Color(8, 8, 8, 220),
                OutlineColor = new Color(120, 120, 120, 180),
                OutlineThickness = 1f
            };
            frame.Children.Add(background);
            RectangleWidget image = new() {
                Size = new Vector2(ContentWidth, 220f), FillColor = Color.White, OutlineThickness = 0f, TextureLinearFilter = true, IsVisible = false
            };
            frame.Children.Add(image);
            LabelWidget status = new() {
                Text = string.IsNullOrWhiteSpace(url)
                    ? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PostDetailsDialog", "FailedToLoad"), alt)
                    : string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PostDetailsDialog", "Loading"), alt),
                HorizontalAlignment = WidgetAlignment.Center,
                VerticalAlignment = WidgetAlignment.Center,
                Color = new Color(170, 170, 170),
                FontScale = 0.78f,
                WordWrap = true
            };
            frame.Children.Add(status);
            if (string.IsNullOrWhiteSpace(url)) {
                return;
            }
            if (m_imageTextureCache.TryGetValue(url, out Texture2D cachedTexture)) {
                // 缓存命中（内容重建，如自动翻译）：复用已下载纹理，不再重复下载。
                ApplyImageTexture(
                    cachedTexture,
                    frame,
                    background,
                    image,
                    status,
                    alt,
                    url
                );
                return;
            }
            _ = LoadImageAsync(url, alt, frame, background, image, status);
        }

        async Task LoadImageAsync(string url, string alt, CanvasWidget frame, RectangleWidget background, RectangleWidget image, LabelWidget status) {
            try {
                using MemoryStream downloaded = await s_imageDownloader.DownloadToMemoryAsync(url, null, CancellationToken.None)
                    .ConfigureAwait(false);
                byte[] data = downloaded.ToArray();
                Dispatcher.Dispatch(
                    delegate {
                        try {
                            if (data.Length == 0) {
                                status.Text = string.Format(
                                    CultureInfo.InvariantCulture,
                                    LanguageControl.Get("PostDetailsDialog", "FailedToLoad"),
                                    alt
                                );
                                return;
                            }
                            using MemoryStream stream = new(data);
                            Texture2D texture = Texture2D.Load(stream);
                            if (m_texturesReleased) {
                                // 对话框已关闭：纹理不会被任何 UpdateCeases 释放，就地回收。
                                try {
                                    texture.Dispose();
                                }
                                catch (Exception disposeEx) {
                                    Log.Information(
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            LanguageControl.Get("PostDetailsContentBuilder", "DisposeLatePostDetailsTextureFailed"),
                                            disposeEx.Message
                                        )
                                    );
                                }
                                return;
                            }
                            if (m_imageTextureCache.TryGetValue(url, out Texture2D existingTexture)) {
                                // 重建期间并发下载了同一 URL：后到者就地释放，复用先到者。
                                try {
                                    texture.Dispose();
                                }
                                catch (Exception disposeEx) {
                                    Log.Information(
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            LanguageControl.Get("PostDetailsContentBuilder", "DisposeDuplicatePostDetailsTextureFailed"),
                                            disposeEx.Message
                                        )
                                    );
                                }
                                texture = existingTexture;
                            }
                            else {
                                m_imageTextureCache[url] = texture;
                                m_loadedTextures.Add(texture);
                            }
                            ApplyImageTexture(
                                texture,
                                frame,
                                background,
                                image,
                                status,
                                alt,
                                url
                            );
                        }
                        catch (Exception ex) {
                            status.Text = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PostDetailsDialog", "FailedToLoad"), alt);
                            Log.Information(
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    LanguageControl.Get("PostDetailsContentBuilder", "DecodePostDetailsImageFailed"),
                                    url,
                                    ex.Message
                                )
                            );
                        }
                    }
                );
            }
            catch (Exception ex) {
                Dispatcher.Dispatch(
                    delegate {
                        status.Text = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PostDetailsDialog", "FailedToLoad"), alt);
                    }
                );
                Log.Information(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("PostDetailsContentBuilder", "RequestPostDetailsImageFailed"),
                        url,
                        ex.Message
                    )
                );
            }
        }

        void ApplyImageTexture(Texture2D texture,
            CanvasWidget frame,
            RectangleWidget background,
            RectangleWidget image,
            LabelWidget status,
            string alt,
            string url) {
            try {
                if (texture == null
                    || texture.Width <= 0
                    || texture.Height <= 0) {
                    status.Text = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PostDetailsDialog", "FailedToLoad"), alt);
                    return;
                }
                Vector2 size = GetImageDisplaySize(texture);
                frame.Size = size;
                background.Size = size;
                image.Size = size;
                image.Subtexture = new Subtexture(texture, Vector2.Zero, Vector2.One);
                image.IsVisible = true;
                status.IsVisible = false;
            }
            catch (Exception ex) {
                status.Text = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PostDetailsDialog", "FailedToLoad"), alt);
                Log.Warning(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("PostDetailsContentBuilder", "DecodePostDetailsImageFailed"),
                        url,
                        ex.Message
                    )
                );
            }
        }

        static void AddSpacer(StackPanelWidget panel, float height) {
            if (height > 0f) {
                panel.Children.Add(new CanvasWidget { Size = new Vector2(0f, height) });
            }
        }

        static void AppendStyledLabel(StackPanelWidget panel, BlockStyle kind, string text, float indent) {
            AddSpacer(panel, GetTopSpacing(kind));
            panel.Children.Add(
                new LabelWidget { Text = text, Color = GetTextColor(kind), FontScale = GetFontScale(kind), WordWrap = true, MarginLeft = indent }
            );
        }

        string ResolveUrl(string url) {
            if (string.IsNullOrWhiteSpace(url)) {
                return string.Empty;
            }
            url = WebUtility.HtmlDecode(url.Trim());
            if (url.StartsWith("//", StringComparison.Ordinal)) {
                return $"https:{url}";
            }
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri absoluteUri)) {
                return absoluteUri.ToString();
            }
            if (!Uri.TryCreate(m_baseUrl, UriKind.Absolute, out Uri baseUri)) {
                return url;
            }
            if (url.StartsWith("/", StringComparison.Ordinal)) {
                return new Uri(new Uri(baseUri.GetLeftPart(UriPartial.Authority)), url).ToString();
            }
            return new Uri(baseUri, url).ToString();
        }

        static Vector2 GetImageDisplaySize(Texture2D texture) {
            if (texture == null
                || texture.Width <= 0
                || texture.Height <= 0) {
                return new Vector2(ContentWidth, 220f);
            }
            float width = texture.Width;
            float height = texture.Height;
            float scale = MathUtils.Min(ContentWidth / width, 360f / height);
            if (scale > 1f) {
                scale = 1f;
            }
            return new Vector2(width * scale, height * scale);
        }

        static bool IsBlockTag(string tagName) => tagName is "p" or "div" or "section" or "article" or "header" or "footer" or "tr" or "ul" or "ol";

        static bool IsHeadingTag(string tagName) => tagName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6";

        static BlockStyle GetStyleForTag(string tagName) {
            return tagName switch {
                "h1" => BlockStyle.Heading1,
                "h2" or "h3" => BlockStyle.Heading2,
                "h4" or "h5" or "h6" => BlockStyle.Heading3,
                "blockquote" => BlockStyle.Quote,
                "pre" => BlockStyle.Pre,
                _ => BlockStyle.Paragraph
            };
        }

        static float GetFontScale(BlockStyle kind) {
            return kind switch {
                BlockStyle.Heading1 => 1.05f,
                BlockStyle.Heading2 => 0.98f,
                BlockStyle.Heading3 => 0.92f,
                BlockStyle.Pre => 0.78f,
                BlockStyle.Quote => 0.82f,
                _ => 0.86f
            };
        }

        static float GetTopSpacing(BlockStyle kind) {
            return kind switch {
                BlockStyle.Heading1 or BlockStyle.Heading2 => 12f,
                BlockStyle.Heading3 => 10f,
                BlockStyle.ListItem => 3f,
                _ => 6f
            };
        }

        static Color GetTextColor(BlockStyle kind) {
            return kind switch {
                BlockStyle.Heading1 or BlockStyle.Heading2 or BlockStyle.Heading3 => new Color(255, 230, 140),
                BlockStyle.Quote => new Color(190, 190, 190),
                BlockStyle.Pre => new Color(210, 230, 210),
                _ => new Color(225, 225, 225)
            };
        }

        // widget 表现样式分类（仅本构建器的视觉映射用，不是数据模型）。
        enum BlockStyle {
            Paragraph,
            Heading1,
            Heading2,
            Heading3,
            ListItem,
            Quote,
            Pre
        }
    }
}