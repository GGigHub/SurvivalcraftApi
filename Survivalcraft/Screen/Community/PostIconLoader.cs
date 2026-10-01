using System.Globalization;
using System.Reflection;
using Engine;
using Engine.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Game {
    /// <summary>
    /// 帖子树图标进程级加载器：共享缓存/在途请求去重/异步下载解码；下载完成后经
    /// IconUpdated/IconCleared 事件广播，各 PostTreePresenter 更新自己的树。
    /// </summary>
    static class PostIconLoader {
        static readonly EvictableTextureCache s_iconCache = new(128, StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> s_iconRequests = new(StringComparer.OrdinalIgnoreCase);

        static readonly FieldInfo s_treeViewNodeSubtextureField = typeof(TreeViewNode).GetField(
            "m_subtexture",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        public static event Action<string, Texture2D> IconUpdated;

        public static event Action<string> IconCleared;

        public static void DisposeRetired() {
            s_iconCache.DisposeRetired();
        }

        public static void ApplyPostNodeIcon(TreeViewNode node, PostItem post) {
            ApplyNodeFileTypeIcon(node, CommunityFileTypes.ToExternal(post.FileType));
            string iconUrl = GetNodeIconUrl(post);
            if (string.IsNullOrEmpty(iconUrl)) {
                return;
            }
            StartNodeIconRequest(node, iconUrl);
        }

        public static void ApplyVersionNodeIcon(TreeViewNode node, PostVersionItem version) {
            string iconUrl = GetNodeIconUrl(version);
            if (string.IsNullOrEmpty(iconUrl)) {
                return;
            }
            StartNodeIconRequest(node, iconUrl);
        }

        public static void ApplyFileNodeIcon(TreeViewNode node, PostFileItem file) {
            string iconUrl = GetNodeIconUrl(file);
            if (string.IsNullOrEmpty(iconUrl)) {
                return;
            }
            StartNodeIconRequest(node, iconUrl);
        }

        public static void UpdateIconsInTree(CommunityTreeViewWidget tree, string iconUrl, Texture2D texture) {
            if (tree == null) {
                return;
            }
            foreach (TreeViewNode root in tree.Nodes) {
                UpdateIconRecursive(root, iconUrl, texture);
            }
            tree.InvalidateRows();
        }

        public static void ClearIconInTree(CommunityTreeViewWidget tree, string iconUrl) {
            if (tree == null) {
                return;
            }
            foreach (TreeViewNode root in tree.Nodes) {
                ClearIconRecursive(root, iconUrl);
            }
            tree.InvalidateRows();
        }

        static string GetPostIconUrl(PostItem post) =>
            // 只认服务器图标（文件/帖子/封面）。不为缺图帖子整包下载 .scmod 提取包内
            // 图标——为列表里一枚缩略图拉取可达数百 MB 的包体不成比例；无图一律占位。
            PostIconRules.GetRemoteIconUrl(post, AuthSessionManager.Instance.ApiBaseUrl);

        /// <summary>按节点 Tag 解析其图标 URL（与建节点时的 Apply*NodeIcon 同一规则），用于事件到达/驱逐时的全树匹配。</summary>
        static string GetNodeIconUrl(object tag) {
            return tag switch {
                PostItem post => GetPostIconUrl(post),
                PostVersionItem version => GetVersionIconUrl(version),
                PostFileItem file => string.IsNullOrWhiteSpace(file?.Icon)
                    ? null
                    : PostIconRules.NormalizeUrl(file.Icon, AuthSessionManager.Instance.ApiBaseUrl),
                _ => null
            };
        }

        static string GetVersionIconUrl(PostVersionItem version) {
            if (version?.Files == null) {
                return null;
            }
            foreach (PostFileItem file in version.Files) {
                if (!string.IsNullOrWhiteSpace(file?.Icon)) {
                    return PostIconRules.NormalizeUrl(file.Icon, AuthSessionManager.Instance.ApiBaseUrl);
                }
            }
            return null;
        }

        // 全树匹配按 Tag 重算 URL：节点数十级、事件低频（图标到达/驱逐），不做节点级备忘。
        static void UpdateIconRecursive(TreeViewNode node, string iconUrl, Texture2D texture) {
            if (node == null) {
                return;
            }
            if (string.Equals(GetNodeIconUrl(node.Tag), iconUrl, StringComparison.OrdinalIgnoreCase)) {
                SetNodeIcon(node, texture);
            }
            foreach (TreeViewNode child in node.Nodes) {
                UpdateIconRecursive(child, iconUrl, texture);
            }
        }

        static void ClearIconRecursive(TreeViewNode node, string iconUrl) {
            if (node == null) {
                return;
            }
            if (string.Equals(GetNodeIconUrl(node.Tag), iconUrl, StringComparison.OrdinalIgnoreCase)) {
                s_treeViewNodeSubtextureField?.SetValue(node, null);
                node.Icon = null;
            }
            foreach (TreeViewNode child in node.Nodes) {
                ClearIconRecursive(child, iconUrl);
            }
        }

        static bool TryApplyCachedNodeIcon(TreeViewNode node, string iconUrl) {
            if (!s_iconCache.TryGetValue(iconUrl, out Texture2D texture)) {
                return false;
            }
            SetNodeIcon(node, texture);
            return true;
        }

        static void StartNodeIconRequest(TreeViewNode node, string iconUrl) {
            if (string.IsNullOrWhiteSpace(iconUrl)) {
                return;
            }
            if (TryApplyCachedNodeIcon(node, iconUrl)) {
                return;
            }
            if (!s_iconRequests.Add(iconUrl)) {
                return;
            }
            _ = LoadNodeIconAsync(iconUrl);
        }

        static async Task LoadNodeIconAsync(string iconUrl) {
            try {
                using MemoryStream downloaded = await TransferManager.CommunityHttpDownloader.DownloadToMemoryAsync(
                        iconUrl,
                        null,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
                byte[] data = downloaded.ToArray();
                if (data.Length <= 0) {
                    Dispatcher.Dispatch(() => s_iconRequests.Remove(iconUrl));
                    return;
                }

                // 远程图标一律是图片：加缩略图后仍限制体积，超大即弃用占位。
                if (data.Length > 512 * 1024) {
                    Log.Information(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("PostIconLoader", "SkipOversizedPostIcon"),
                            iconUrl,
                            data.Length
                        )
                    );
                    Dispatcher.Dispatch(() => s_iconRequests.Remove(iconUrl));
                    return;
                }
                DecodedPostIcon result = await Task.Run(
                        delegate {
                            try {
                                Image<Rgba32> image = PostIconDecoder.Decode(data);
                                return new DecodedPostIcon { Image = image };
                            }
                            catch (Exception ex) {
                                return new DecodedPostIcon { Error = ex.Message };
                            }
                        }
                    )
                    .ConfigureAwait(false);
                Dispatcher.Dispatch(
                    delegate {
                        Texture2D texture = null;
                        try {
                            if (result?.Image != null) {
                                texture = Texture2D.Load(result.Image);
                                bool overwriting = s_iconCache.TryGetValue(iconUrl, out _);
                                List<string> evictedKeys = s_iconCache.Add(iconUrl, texture);
                                if (overwriting) {
                                    // 覆写：旧纹理已进退休队列；先广播摘除旧引用再广播新图标，
                                    // 与容量驱逐同一保证——退休纹理 Dispose 时无存活节点引用。
                                    IconCleared?.Invoke(iconUrl);
                                }
                                IconUpdated?.Invoke(iconUrl, texture);
                                if (evictedKeys != null) {
                                    // 被容量驱逐的图标立即从各树摘除，保证退休纹理后续 Dispose 时无节点引用。
                                    foreach (string evictedKey in evictedKeys) {
                                        IconCleared?.Invoke(evictedKey);
                                    }
                                }
                            }
                            else {
                                Log.Information(
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        LanguageControl.Get("PostIconLoader", "DecodeRemoteIconFailed"),
                                        iconUrl,
                                        result?.Error
                                    )
                                );
                            }
                        }
                        catch (Exception ex) {
                            Log.Information(
                                string.Format(
                                    CultureInfo.InvariantCulture,
                                    LanguageControl.Get("PostIconLoader", "CreatePostIconTextureFailed"),
                                    iconUrl,
                                    ex.Message
                                )
                            );
                        }
                        finally {
                            result?.Image?.Dispose();
                            s_iconRequests.Remove(iconUrl);
                        }
                    }
                );
            }
            catch (Exception ex) {
                Dispatcher.Dispatch(() => s_iconRequests.Remove(iconUrl));
                Log.Information(
                    string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("PostIconLoader", "RequestPostIconFailed"), iconUrl, ex.Message)
                );
            }
        }

        static void ApplyNodeFileTypeIcon(TreeViewNode node, ExternalContentType fileType) {
            if (node == null) {
                return;
            }
            Subtexture iconSubtexture = GetFileTypeIconSubtexture(fileType);
            if (iconSubtexture == null) {
                return;
            }
            SetNodeIcon(node, iconSubtexture);
        }

        public static Subtexture GetFileTypeIconSubtexture(ExternalContentType fileType) {
            return fileType switch {
                ExternalContentType.Image => ContentManager.Get<Subtexture>("Textures/Atlas/PhotoButton"),
                ExternalContentType.Other => ContentManager.Get<Subtexture>("Textures/Atlas/QuestionMarkIcon"),
                ExternalContentType.Modpack => ContentManager.Get<Subtexture>("Textures/Atlas/QuestionMarkIcon"),
                ExternalContentType.NetMod => ContentManager.Get<Subtexture>("Textures/Gui/DefaultModIcon"),
                _ => ExternalContentManager.GetEntryTypeIcon(fileType)
            };
        }

        // IconMargin 用 TreeViewNode 默认值（Vector2.Zero）：历史上传入的 margin 恒为零，已删。
        static void SetNodeIcon(TreeViewNode node, Subtexture subtexture) {
            if (node == null
                || subtexture == null) {
                return;
            }
            s_treeViewNodeSubtextureField?.SetValue(node, CropSubtextureToSquare(subtexture));
            node.Icon = subtexture.Texture;
        }

        static void SetNodeIcon(TreeViewNode node, Texture2D texture) {
            if (node == null
                || texture == null) {
                return;
            }
            SetNodeIcon(node, new Subtexture(texture, Vector2.Zero, Vector2.One));
        }

        // 引擎 UV 约定（Subtexture / TextureAtlasManager.LoadTextureAtlas）：TopLeft/BottomRight
        // 是整张 Texture2D 上的归一化 UV，子矩形像素尺寸 = UV 跨度 × 整图尺寸。居中正方形在
        // 像素空间取中心：min 按子矩形像素尺寸取（按整图取会在真图集子区域上算出越界 UV），
        // 原点按 TL×整图尺寸转像素；全幅（TL=0，BR=1）输入各步与原实现逐位一致。
        // min ≤ 子矩形宽高时 x ≥ TL 且 x+min ≤ BR，UV 天然钳在 TL..BR 内——该前提通常成立
        // （整像素尺寸），非 2 幂图集的浮点误差极端时越界至多半像素，采样器钳边吸收。
        internal static Subtexture CropSubtextureToSquare(Subtexture subtexture) {
            if (subtexture == null) {
                return null;
            }
            float width = subtexture.Texture?.Width ?? 1;
            float height = subtexture.Texture?.Height ?? 1;
            float subWidth = width * (subtexture.BottomRight.X - subtexture.TopLeft.X);
            float subHeight = height * (subtexture.BottomRight.Y - subtexture.TopLeft.Y);
            float min = MathUtils.Max(MathUtils.Min(subWidth, subHeight), 1f);
            float x = subtexture.TopLeft.X * width + (subWidth - min) * 0.5f;
            float y = subtexture.TopLeft.Y * height + (subHeight - min) * 0.5f;
            Vector2 tl = new(x / width, y / height);
            Vector2 br = new((x + min) / width, (y + min) / height);
            return new Subtexture(subtexture.Texture, tl, br);
        }

        sealed class DecodedPostIcon {
            public Image<Rgba32> Image;
            public string Error;
        }
    }
}