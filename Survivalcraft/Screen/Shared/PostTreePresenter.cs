using System;
using System.Collections.Generic;
using System.Globalization;
using Engine;
using Engine.Graphics;

namespace Game
{
    /// <summary>
    /// 单棵帖子树的渲染 presenter：节点创建、行组件刷新、分页状态机与按钮/跳页控件。
    /// 每棵树一个实例；树、状态标签、分页控件由构造传入。图标缓存/下载为进程级共享（PostIconLoader）。
    /// 行组件刷新时机：数据变化走 MarkDirty；行组件被引擎重建走 CommunityTreeViewWidget.RowWidgetsRebuilt。
    /// 生命周期：ctor 订阅进程级图标事件，宿主屏被换语言整表重建替换时经
    /// CommunityScreen.Dispose → Detach 确定性退订，无孤儿 handler。
    /// </summary>
    internal sealed class PostTreePresenter
    {
        private readonly CommunityTreeViewWidget m_tree;
        private readonly LabelWidget m_statusLabel;
        private readonly BevelledButtonWidget m_prevPageButton;
        private readonly BevelledButtonWidget m_nextPageButton;
        private readonly TextBoxWidget m_pageJumpBox;
        private readonly BevelledButtonWidget m_pageJumpButton;
        private readonly Func<ExternalContentType, string> m_fileTypeNameProvider;
        private readonly Func<PostVersionItem, PostFileItem> m_singleVersionFileProvider;
        private readonly Func<PostVersionItem, bool> m_showCompatibilityProvider;
        private readonly TaskSlot<PostDetailLoadResult> m_postDetailSlot;

        private bool m_postNodeWidgetsDirty = true;
        private TreeViewNode m_pendingPostDetailNode;
        private CommunityTreeViewWidget m_pendingPostDetailTree;

        /// <summary>占位子节点 Tag：表示"版本列表未随列表下发，点击展开时按 postId 拉取"。</summary>
        private const string LazyTag = "lazy";

        public PostTreePresenter(
            CommunityTreeViewWidget tree,
            LabelWidget statusLabel,
            BevelledButtonWidget prevPageButton,
            BevelledButtonWidget nextPageButton,
            TextBoxWidget pageJumpBox,
            BevelledButtonWidget pageJumpButton,
            Func<ExternalContentType, string> fileTypeNameProvider,
            Func<PostVersionItem, PostFileItem> singleVersionFileProvider,
            Func<PostVersionItem, bool> showCompatibilityProvider,
            TaskSlot<PostDetailLoadResult> postDetailSlot)
        {
            m_tree = tree;
            m_statusLabel = statusLabel;
            m_prevPageButton = prevPageButton;
            m_nextPageButton = nextPageButton;
            m_pageJumpBox = pageJumpBox;
            m_pageJumpButton = pageJumpButton;
            m_fileTypeNameProvider = fileTypeNameProvider;
            m_singleVersionFileProvider = singleVersionFileProvider;
            m_showCompatibilityProvider = showCompatibilityProvider;
            m_postDetailSlot = postDetailSlot;
            if (m_tree != null)
            {
                m_tree.OnNodeClicked = HandleTreeNodeClicked;
                m_tree.RowWidgetsRebuilt += HandleRowWidgetsRebuilt;
            }
            PostIconLoader.IconUpdated += HandleIconUpdated;
            PostIconLoader.IconCleared += HandleIconCleared;
        }

        /// <summary>退订进程级图标事件；幂等。之后 presenter 及其控件图不再被静态事件钉住。</summary>
        public void Detach()
        {
            PostIconLoader.IconUpdated -= HandleIconUpdated;
            PostIconLoader.IconCleared -= HandleIconCleared;
        }

        public LabelWidget StatusLabel => m_statusLabel;

        public int Page { get; set; } = 1;

        public int TotalPages { get; set; } = 1;

        public void AddRoot(TreeViewNode node)
        {
            m_tree?.AddRoot(node);
        }

        public void Clear(bool disposeIcons)
        {
            if (m_tree != null)
            {
                m_tree.Clear(false);
            }
            if (disposeIcons)
            {
                PostIconLoader.DisposeRetired();
            }
        }

        /// <summary>节点数据变化（列表加载/安装状态/筛选刷新）后请求重刷行组件。</summary>
        public void MarkDirty()
        {
            m_postNodeWidgetsDirty = true;
        }

        /// <summary>引擎重建行组件后由树回调：新行是模板默认值，立即重刷定制，无 settle 竞态。</summary>
        private void HandleRowWidgetsRebuilt()
        {
            ApplyPostNodeWidgets();
        }

        public void Update()
        {
            if (m_postNodeWidgetsDirty)
            {
                ApplyPostNodeWidgets();
                m_postNodeWidgetsDirty = false;
            }
            if (m_postDetailSlot.TryTake(
                    out PostDetailLoadResult detailResult,
                    error => new PostDetailLoadResult
                    {
                        RequestId = m_postDetailSlot.RequestId,
                        Error = error.Message
                    }))
            {
                ApplyPostDetailResult(detailResult);
            }
        }

        /// <summary>
        /// 树节点点击：刷新行组件，并处理展开懒加载——列表接口可能不内嵌 postVersions，
        /// 此时帖子下是"展开加载文件"占位节点，点击时按 postId 拉帖子详情回填版本子树。
        /// </summary>
        public void HandleTreeNodeClicked(TreeViewNode node)
        {
            MarkDirty();
            if (node?.Tag is PostItem post
                && node.Nodes.Count == 1
                && Equals(node.Nodes[0].Tag, LazyTag))
            {
                LoadPostDetail(node, post);
            }
        }

        /// <summary>列表刷新时丢弃在途的展开加载与挂起节点（旧节点即将随树重建失效）。</summary>
        public void InvalidatePostDetailLoad()
        {
            m_postDetailSlot.Invalidate();
            m_pendingPostDetailNode = null;
            m_pendingPostDetailTree = null;
        }

        private void LoadPostDetail(TreeViewNode targetNode, PostItem post)
        {
            string token = AuthSessionManager.Instance.CurrentToken;
            string apiBaseUrl = AuthSessionManager.Instance.ApiBaseUrl;
            if (!m_postDetailSlot.TryStart(async (requestId, _) =>
            {
                CommunityPostDetailLoad load = await CommunityManager.LoadPostDetailAsync(
                    CommunityManager.ApiClient,
                    apiBaseUrl,
                    token,
                    post.Id);
                return new PostDetailLoadResult
                {
                    RequestId = requestId,
                    Response = load.Response,
                    Error = load.Error
                };
            }, out _))
            {
                return;
            }

            m_pendingPostDetailNode = targetNode;
            m_pendingPostDetailTree = m_tree;
        }

        private void ApplyPostDetailResult(PostDetailLoadResult result)
        {
            TreeViewNode targetNode = m_pendingPostDetailNode;
            CommunityTreeViewWidget targetTree = m_pendingPostDetailTree;
            m_pendingPostDetailNode = null;
            m_pendingPostDetailTree = null;
            if (targetNode == null || targetTree == null)
            {
                return;
            }

            // 节点不拥有纹理所有权：TreeViewNode.Dispose 会 Dispose Icon，而 Icon 指向
            // 进程级共享缓存（PostIconLoader）或引擎内容资产。子节点（占位）是
            // 纯托管对象，GC 回收即可；共享纹理由缓存的退休队列统一释放。
            targetNode.ClearChildren(dispose: false);
            if (result.Response?.Code == 200 && result.Response.Data != null)
            {
                PostItem detail = result.Response.Data;
                if (detail.PostVersions != null)
                {
                    // 整次回填共享一份本地快照（ref-snapshot 约定，同 ApplyPostNodeWidgetsRecursive）。
                    List<LocalResourceItem> snapshot = null;
                    foreach (PostVersionItem version in detail.PostVersions)
                    {
                        if (version == null)
                        {
                            continue;
                        }
                        version.ShowLocalModInstallStatus = (targetNode.Tag as PostItem)?.ShowLocalModInstallStatus == true;
                        TreeViewNode versionNode = CreateVersionNode(version, detail.Id, ref snapshot);
                        if (versionNode != null)
                        {
                            targetNode.AddChild(versionNode);
                        }
                    }
                }
                // 版本缺失/空/元素全 null 均为服务端数据事实，落"无资源"终态；
                // 与下方加载失败路径区分——失败是瞬时的，重挂可重试的懒加载占位。
                if (targetNode.Nodes.Count == 0)
                {
                    targetNode.AddChild(new TreeViewNode(LanguageControl.Get("ScbbsPlusPostTreePresenter", "NoResources"), "") { Selectable = false });
                }
                targetTree.InvalidateRows();
                MarkDirty();
                return;
            }

            // 失败可能是瞬时网络错误：重新挂懒加载占位保持可重试（点击帖子节点再次拉取），
            // 错误信息写进占位行副文本展示，不钉死成"无资源"。
            TreeViewNode failed = new TreeViewNode(
                LanguageControl.Get("ScbbsPlusPostTreePresenter", "ExpandToLoadFiles"),
                string.IsNullOrWhiteSpace(result.Error) ? result.Response?.Msg ?? string.Empty : result.Error)
            {
                Tag = LazyTag,
                Selectable = false
            };
            targetNode.AddChild(failed);
            targetTree.InvalidateRows();
            MarkDirty();
            if (!string.IsNullOrEmpty(result.Error))
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostTreePresenter", "GetPostDetailsFailed"), result.Error));
            }
        }

        /// <summary>用加载结果填充树、刷新状态标签与分页状态。</summary>
        public void ApplyPostsResult(PostsLoadResult loadResult, string statusPrefix)
        {
            PostsResponse result = loadResult.Response;
            if (loadResult.VersionCatalog != null)
            {
                // 目录与来源服务器绑定写入（缺陷 6）：切服务器后旧目录不再命中。
                VersionCatalogCache.UpdateCatalog(
                    loadResult.ApiBaseUrl,
                    loadResult.VersionCatalog,
                    loadResult.GameVersionsById ?? VersionCatalogCache.CachedGameVersionsById);
            }
            if (result != null && result.Code == 200 && result.Data?.List != null)
            {
                m_tree.Clear(false);
                // 整表构建共享一份本地快照（ref-snapshot 约定）：N 个帖子 × M 个版本节点只取一次。
                List<LocalResourceItem> snapshot = null;
                foreach (PostItem post in result.Data.List)
                {
                    // 服务端脏数据防线（同 CreatePostNode/TryGetPostScore 的判空约定）。
                    if (post == null)
                    {
                        continue;
                    }
                    post.ShowLocalModInstallStatus = loadResult.Source == ResourceSource.Community
                        && (loadResult.FileType == ExternalContentType.Mod || (loadResult.FileType == ExternalContentType.Unknown && CommunityFileTypes.ToExternal(post.FileType) == ExternalContentType.Mod));
                    m_tree.AddRoot(CreatePostNode(post, ref snapshot));
                }
                // 服务端分页语义按端点漂移：/user/posts 的 total=页数、count=条数；
                // /post/list 无 count、total=条数。count>0 视为权威条数，否则退回
                // total 条数口径（total 谎报再由 ResolveListTotals 兜底）。
                (int total, int totalPages) = ResolveListTotals(
                    result.Data.Count > 0 ? result.Data.Count : result.Data.Total,
                    result.Data.Limit, Page, result.Data.List.Length);
                TotalPages = totalPages;
                SetPageJumpBoxText();
                m_statusLabel.Text = result.Data.List.Length > 0
                    ? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostTreePresenter", "PagePosts"), statusPrefix, Page, TotalPages, total)
                    : string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostTreePresenter", "NoPosts"), statusPrefix);
            }
            else
            {
                TotalPages = MathUtils.Max(Page, 1);
                SetPageJumpBoxText();
                string error = loadResult.Error ?? result?.Msg ?? LanguageControl.Get("ScbbsPlusPostTreePresenter", "NoData");
                ShowLoadFailed(statusPrefix);
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostTreePresenter", "LoadPostFailed"), error));
            }
            MarkDirty();
        }

        /// <summary>
        /// 用"我的上传"平铺文件列表填充树（我的发布屏非 Mod 类别）：每行一个根级文件节点，
        /// 无帖子/版本层级。分页与状态标签语义同 ApplyPostsResult；下载数/图标随行数据
        /// 缺失而不显示（服务端 /upload/list 不下发）。
        /// </summary>
        public void ApplyUploadFilesResult(UploadFilesLoadResult loadResult, string statusPrefix)
        {
            UploadModsResponse result = loadResult.Response;
            if (result != null && result.Code == 200 && result.Data?.List != null)
            {
                m_tree.Clear(false);
                foreach (PostFileItem file in result.Data.List)
                {
                    TreeViewNode fileNode = CreateUploadFileNode(file);
                    if (fileNode != null)
                    {
                        m_tree.AddRoot(fileNode);
                    }
                }
                (int total, int totalPages) = ResolveListTotals(result.Data.Total, result.Data.Limit, Page, result.Data.List.Length);
                TotalPages = totalPages;
                SetPageJumpBoxText();
                m_statusLabel.Text = result.Data.List.Length > 0
                    ? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostTreePresenter", "PageUploads"), statusPrefix, Page, TotalPages, total)
                    : string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostTreePresenter", "NoUploads"), statusPrefix);
            }
            else
            {
                TotalPages = MathUtils.Max(Page, 1);
                SetPageJumpBoxText();
                string error = loadResult.Error ?? result?.Msg ?? LanguageControl.Get("ScbbsPlusPostTreePresenter", "NoData");
                ShowLoadFailed(statusPrefix);
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostTreePresenter", "LoadPostFailed"), error));
            }
            MarkDirty();
        }

        /// <summary>列表加载失败状态行（三处同构收敛：帖子/上传/精选共用同一文案）。</summary>
        public void ShowLoadFailed(string statusPrefix)
        {
            StatusLabel.Text = string.Format(CultureInfo.InvariantCulture,
                LanguageControl.Get("ScbbsPlusPostTreePresenter", "LoadFailed"), statusPrefix);
        }

        public void SetPageJumpBoxText()
        {
            if (m_pageJumpBox != null)
            {
                m_pageJumpBox.Text = Page.ToString();
            }
        }

        /// <summary>分页按钮启用态（可见性与布局由屏幕自理）。listActive 为空/隐藏时禁用。</summary>
        public void UpdatePaginationButtons(Func<bool> loadBusy)
        {
            bool active = (loadBusy == null || !loadBusy());
            if (m_prevPageButton != null)
            {
                m_prevPageButton.IsEnabled = active && Page > 1;
            }
            if (m_nextPageButton != null)
            {
                m_nextPageButton.IsEnabled = active && Page < TotalPages;
            }
            if (m_pageJumpButton != null)
            {
                m_pageJumpButton.IsEnabled = active && TotalPages > 1;
            }
        }

        /// <summary>处理分页按钮/跳页点击。返回是否发生翻页（屏幕据页码变化重新加载列表）。</summary>
        public bool HandlePaginationClicks()
        {
            if (m_prevPageButton != null && m_prevPageButton.IsClicked && Page > 1)
            {
                Page--;
                SetPageJumpBoxText();
                return true;
            }
            if (m_nextPageButton != null && m_nextPageButton.IsClicked && Page < TotalPages)
            {
                Page++;
                SetPageJumpBoxText();
                return true;
            }
            if (m_pageJumpButton != null && m_pageJumpButton.IsClicked)
            {
                int requestedPage;
                if (m_pageJumpBox == null || !int.TryParse(m_pageJumpBox.Text?.Trim(), out requestedPage))
                {
                    SetPageJumpBoxText();
                    return false;
                }
                if (requestedPage < 1)
                {
                    requestedPage = 1;
                }
                else if (requestedPage > TotalPages)
                {
                    requestedPage = TotalPages;
                }
                if (requestedPage == Page)
                {
                    SetPageJumpBoxText();
                    return false;
                }
                Page = requestedPage;
                SetPageJumpBoxText();
                return true;
            }
            return false;
        }

        public TreeViewNode CreatePostNode(PostItem post)
        {
            List<LocalResourceItem> snapshot = null;
            return CreatePostNode(post, ref snapshot);
        }

        /// <summary>ref-snapshot 重载：批量构建（如整表加载）共享一次本地快照，避免每节点重建。</summary>
        public TreeViewNode CreatePostNode(PostItem post, ref List<LocalResourceItem> snapshot)
        {
            string timeText = CommunityTimeFormatter.FormatDate(post.CreatedAt);
            string authorText = string.IsNullOrWhiteSpace(post.Creator?.Nickname) ? LanguageControl.Get("ScbbsPlusPostTreePresenter", "UnknownUser") : post.Creator.Nickname.Trim();
            string subText = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostTreePresenter", "PostSummaryFormat"),
                m_fileTypeNameProvider(CommunityFileTypes.ToExternal(post.FileType)),
                authorText,
                timeText,
                post.DownloadCount,
                GetPostVersionCount(post));
            string displayTitle = GetCommunityPostDisplayTitle(post, ref snapshot);
            Color titleColor = GetCommunityPostTitleColor(post, ref snapshot);
            TreeViewNode node = new TreeViewNode(displayTitle, titleColor, subText, new Color(140, 140, 140))
            {
                Tag = post
            };
            PostIconLoader.ApplyPostNodeIcon(node, post);

            if (post.PostVersions != null && post.PostVersions.Length > 0)
            {
                foreach (PostVersionItem version in post.PostVersions)
                {
                    if (version == null)
                    {
                        continue;
                    }
                    version.ShowLocalModInstallStatus = post.ShowLocalModInstallStatus;
                    TreeViewNode versionNode = CreateVersionNode(version, post.Id, ref snapshot);
                    if (versionNode != null)
                    {
                        node.AddChild(versionNode);
                    }
                }
                // 数组非空但元素全 null（服务器脏数据）：上面跳过后零子节点，
                // 与空数组一样落占位，避免帖子节点展开为空白（缺陷 5 补审 F2）。
                if (node.Nodes.Count == 0)
                {
                    node.AddChild(new TreeViewNode(LanguageControl.Get("ScbbsPlusPostTreePresenter", "NoResources"), "") { Selectable = false });
                }
            }
            else if (ShouldCreateLazyPlaceholder(post))
            {
                TreeViewNode lazyNode = new TreeViewNode(LanguageControl.Get("ScbbsPlusPostTreePresenter", "ExpandToLoadFiles"), "")
                {
                    Tag = LazyTag,
                    Selectable = false
                };
                node.AddChild(lazyNode);
            }
            else
            {
                node.AddChild(new TreeViewNode(LanguageControl.Get("ScbbsPlusPostTreePresenter", "NoResources"), "") { Selectable = false });
            }
            return node;
        }

        /// <summary>null version 返回 null（服务器 PostVersions 数组可能含 null 元素），调用方跳过。</summary>
        public TreeViewNode CreateVersionNode(PostVersionItem version, int postId = 0)
        {
            List<LocalResourceItem> snapshot = null;
            return CreateVersionNode(version, postId, ref snapshot);
        }

        /// <summary>
        /// ref-snapshot 重载：版本树批量构建（整表加载/详情回填）共享一次本地快照，避免
        /// N 个版本节点各取一次全量快照（单参重载内部每次都触引擎 ModListAll 重建）。
        /// 同一同步构建周期内安装状态不会变，复用安全。
        /// </summary>
        public TreeViewNode CreateVersionNode(PostVersionItem version, int postId, ref List<LocalResourceItem> snapshot)
        {
            if (version == null)
            {
                // version.Files 等解引用接收者，null 条件运算符不保护接收者：
                // 不设防会让服务器脏数据崩掉屏幕 Update()。
                return null;
            }
            if (version.PostId <= 0)
            {
                version.PostId = postId;
            }
            int fileCount = version.Files?.Length ?? 0;
            PostFileItem singleFile = m_singleVersionFileProvider(version);
            if (version.ShowLocalModInstallStatus && snapshot == null)
            {
                // 整次构建只捕获一次本地快照（同 ApplyPostNodeWidgetsRecursive 的批量重载约定）。
                snapshot = LocalModStateStore.GetCommunityLocalModSnapshot();
            }
            bool isInstalledVersion = LocalModStateStore.IsInstalledCommunityModVersion(version, snapshot);
            string title = GetCommunityVersionDisplayTitle(version, isInstalledVersion);
            bool showCompatibility = m_showCompatibilityProvider(version);
            VersionCompatibilityInfo compatibility = showCompatibility
                ? CommunityVersionCompatibilityFormatter.GetVersionCompatibilityInfo(
                    version,
                    VersionCatalogCache.CurrentApiVersion,
                    VersionCatalogCache.CurrentGameVersion,
                    VersionCatalogCache.CachedGameVersionsById,
                    VersionCatalogCache.CachedVersionCatalog)
                : null;
            string subText = showCompatibility
                ? $"{compatibility.StatusText}  {version.Title ?? ""}  {string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostTreePresenter", "Files"), fileCount)}"
                : $"{version.Title ?? ""}  {string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostTreePresenter", "Files"), fileCount)}";
            if (singleFile != null)
            {
                string sizeText = TryGetSizeText(singleFile.Size);
                string timeText = CommunityTimeFormatter.FormatDate(string.IsNullOrWhiteSpace(singleFile.CreatedAt) ? version?.CreatedAt : singleFile.CreatedAt);
                subText = showCompatibility
                    ? $"{compatibility.StatusText}  {sizeText}  {timeText}"
                    : $"{version.Title ?? ""}  {sizeText}  {timeText}";
            }
            Color subTextColor = showCompatibility ? compatibility.Color : new Color(140, 140, 140);
            Color titleColor = isInstalledVersion ? new Color(120, 220, 140) : new Color(255, 230, 140);
            TreeViewNode node = new TreeViewNode(title, titleColor, subText, subTextColor)
            {
                Tag = version
            };
            PostIconLoader.ApplyVersionNodeIcon(node, version);
            if (version.Files != null && singleFile == null)
            {
                foreach (PostFileItem file in version.Files)
                {
                    if (file == null)
                    {
                        // 与 null version 同一脏数据前提：CreateFileNode 解引用 file.Size，
                        // 不设防同样崩帧（缺陷 5 补审 F1）。全部无效时由下方占位兜底。
                        continue;
                    }
                    node.AddChild(CreateFileNode(file, version.PostId));
                }
            }
            if (node.Nodes.Count == 0 && singleFile == null)
            {
                node.AddChild(new TreeViewNode(LanguageControl.Get("ScbbsPlusPostTreePresenter", "NoFiles"), "") { Selectable = false });
            }
            return node;
        }

        public TreeViewNode CreateFileNode(PostFileItem file, int postId = 0)
        {
            if (file != null && file.PostId <= 0)
            {
                file.PostId = postId;
            }
            string sizeText = TryGetSizeText(file.Size);
            string timeText = CommunityTimeFormatter.FormatDate(file.CreatedAt);
            string title = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostTreePresenter", "File"), file.Filename ?? string.Empty);
            TreeViewNode node = new TreeViewNode(title, new Color(180, 255, 180), $"{sizeText}  {string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostTreePresenter", "Downloads"), file.DownloadCount)}  {timeText}", new Color(140, 140, 140))
            {
                Tag = file
            };
            PostIconLoader.ApplyFileNodeIcon(node, file);
            return node;
        }

        /// <summary>
        /// "我的上传"平铺文件节点：标题/大小/时间同文件节点，但不下载数（/upload/list 不下发，
        /// 显示"下载：0"是误导）、不回填 postId（无帖子上下文）。file 为 null 返回 null。
        /// </summary>
        public TreeViewNode CreateUploadFileNode(PostFileItem file)
        {
            if (file == null)
            {
                return null;
            }
            string sizeText = TryGetSizeText(file.Size);
            string timeText = CommunityTimeFormatter.FormatDate(file.CreatedAt);
            string title = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostTreePresenter", "File"), file.Filename ?? string.Empty);
            TreeViewNode node = new TreeViewNode(title, new Color(180, 255, 180), $"{sizeText}  {timeText}", new Color(140, 140, 140))
            {
                Tag = file
            };
            PostIconLoader.ApplyFileNodeIcon(node, file);
            return node;
        }

        private void ApplyPostNodeWidgets()
        {
            if (m_tree == null)
            {
                return;
            }

            List<LocalResourceItem> snapshot = null;
            foreach (TreeViewNode node in m_tree.Nodes)
            {
                ApplyPostNodeWidgetsRecursive(node, ref snapshot);
            }
        }

        private void ApplyPostNodeWidgetsRecursive(TreeViewNode node, ref List<LocalResourceItem> snapshot)
        {
            if (node == null)
            {
                return;
            }

            if (node.LinkedWidget != null)
            {
                RectangleWidget nodeIconWidget = node.LinkedWidget.Children.Find<RectangleWidget>("TreeViewNodeContentItem.Icon", false);
                if (nodeIconWidget != null)
                {
                    nodeIconWidget.Size = new Vector2(48f, 48f);
                }
            }

            if (node.Tag is PostItem post && node.LinkedWidget != null)
            {
                LabelWidget titleLabel = node.LinkedWidget.Children.Find<LabelWidget>("TreeViewNodeContentItem.Text", false);
                LabelWidget detailsLabel = node.LinkedWidget.Children.Find<LabelWidget>("TreeViewNodeContentItem.Details", false);
                StarRatingWidget ratingWidget = node.LinkedWidget.Children.Find<StarRatingWidget>("TreeViewNodeContentItem.Rating", false);
                LabelWidget extraTextLabel = node.LinkedWidget.Children.Find<LabelWidget>("TreeViewNodeContentItem.ExtraText", false);

                string displayTitle = GetCommunityPostDisplayTitle(post, ref snapshot);
                Color titleColor = GetCommunityPostTitleColor(post, ref snapshot);
                node.Text = displayTitle;
                node.TextColor = titleColor;
                if (titleLabel != null)
                {
                    titleLabel.Text = displayTitle;
                    titleLabel.Color = titleColor;
                }

                if (detailsLabel != null)
                {
                    detailsLabel.Text = node.SubText;
                    detailsLabel.Color = new Color(140, 140, 140);
                }

                if (ratingWidget != null)
                {
                    if (TryGetPostScore(post, out double score))
                    {
                        ratingWidget.IsVisible = true;
                        ratingWidget.IsEnabled = false;
                        ratingWidget.StarSize = 24f;
                        ratingWidget.Rating = (float)score;
                        ratingWidget.ForeColor = new Color(255, 192, 0);
                        ratingWidget.BackColor = new Color(96, 96, 96);
                    }
                    else
                    {
                        ratingWidget.IsVisible = false;
                    }
                }

                if (extraTextLabel != null)
                {
                    extraTextLabel.Text = string.Empty;
                    extraTextLabel.Color = new Color(140, 140, 140);
                }
            }
            else if (node.Tag is PostVersionItem version && node.LinkedWidget != null)
            {
                LabelWidget titleLabel = node.LinkedWidget.Children.Find<LabelWidget>("TreeViewNodeContentItem.Text", false);
                LabelWidget extraTextLabel = node.LinkedWidget.Children.Find<LabelWidget>("TreeViewNodeContentItem.ExtraText", false);
                if (version.ShowLocalModInstallStatus && snapshot == null)
                {
                    // 整树刷新一趟只捕获一次本地快照（重建事件在 resize 拖动期间每帧触发）。
                    snapshot = LocalModStateStore.GetCommunityLocalModSnapshot();
                }
                bool isInstalledVersion = LocalModStateStore.IsInstalledCommunityModVersion(version, snapshot);
                string displayTitle = GetCommunityVersionDisplayTitle(version, isInstalledVersion);
                Color titleColor = isInstalledVersion ? new Color(120, 220, 140) : new Color(255, 230, 140);
                node.Text = displayTitle;
                node.TextColor = titleColor;
                if (titleLabel != null)
                {
                    titleLabel.Text = displayTitle;
                    titleLabel.Color = titleColor;
                }
                if (extraTextLabel != null)
                {
                    PostFileItem singleFile = m_singleVersionFileProvider(version);
                    extraTextLabel.Text = string.IsNullOrWhiteSpace(singleFile?.Filename) ? string.Empty : singleFile.Filename.Trim();
                    extraTextLabel.Color = Color.White;
                }
            }

            foreach (TreeViewNode child in node.Nodes)
            {
                ApplyPostNodeWidgetsRecursive(child, ref snapshot);
            }
        }

        private void HandleIconUpdated(string iconUrl, Texture2D texture)
        {
            // 更新节点模型并置引擎脏标记；重建后的行组件经 RowWidgetsRebuilt 重刷尺寸等定制。
            PostIconLoader.UpdateIconsInTree(m_tree, iconUrl, texture);
        }

        private void HandleIconCleared(string iconUrl)
        {
            PostIconLoader.ClearIconInTree(m_tree, iconUrl);
        }

        private static string GetCommunityPostDisplayTitle(PostItem post, ref List<LocalResourceItem> snapshot)
        {
            string title = post?.Title ?? string.Empty;
            if (post?.ShowLocalModInstallStatus == true && snapshot == null)
            {
                snapshot = LocalModStateStore.GetCommunityLocalModSnapshot();
            }
            string status = GetInstallStatusText(LocalModInstallStatus.GetCommunityModInstallState(post, snapshot));
            return string.IsNullOrEmpty(status) ? title : $"{title}  [{status}]";
        }

        private static Color GetCommunityPostTitleColor(PostItem post, ref List<LocalResourceItem> snapshot)
        {
            if (post?.ShowLocalModInstallStatus == true && snapshot == null)
            {
                snapshot = LocalModStateStore.GetCommunityLocalModSnapshot();
            }
            return LocalModInstallStatus.GetCommunityModInstallState(post, snapshot) == LocalModInstallState.None
                ? Color.White
                : new Color(120, 220, 140);
        }

        private static string GetInstallStatusText(LocalModInstallState state)
        {
            return state switch
            {
                LocalModInstallState.UpdateAvailable => LanguageControl.Get("ScbbsPlusPostTreePresenter", "UpdateAvailable"),
                LocalModInstallState.Installed => LanguageControl.Get("ScbbsPlusPostTreePresenter", "Installed"),
                _ => null
            };
        }

        private static string GetCommunityVersionDisplayTitle(PostVersionItem version, bool isInstalledVersion)
        {
            string titleValue = string.IsNullOrEmpty(version?.Version) ? version?.Title : version.Version;
            string title = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPostTreePresenter", "VersionFormat"), titleValue);
            return isInstalledVersion ? $"{title}  [{LanguageControl.Get("ScbbsPlusPostTreePresenter", "CurrentlyInstalled")}]" : title;
        }

        internal static bool TryGetPostScore(PostItem post, out double score)
        {
            score = 0;
            if (post == null)
            {
                return false;
            }

            if (post.Score.HasValue && post.Score.Value > 0 && !double.IsNaN(post.Score.Value) && !double.IsInfinity(post.Score.Value))
            {
                score = MathUtils.Clamp(post.Score.Value, 0, 5);
                return true;
            }

            if (post.PostScores == null || post.PostScores.Length == 0)
            {
                return false;
            }

            int count = 0;
            int total = 0;
            foreach (PostScoreItem item in post.PostScores)
            {
                if (item == null || item.Score < 1 || item.Score > 5)
                {
                    continue;
                }
                total += item.Score;
                count++;
            }

            if (count == 0)
            {
                return false;
            }

            score = (double)total / count;
            return true;
        }

        /// <summary>版本只挂一个文件时返回该文件（下载目标直取版本行），否则 null。社区屏/我的发布屏共用。</summary>
        internal static PostFileItem GetSingleVersionFile(PostVersionItem version)
        {
            return version?.Files?.Length == 1 ? version.Files[0] : null;
        }

        /// <summary>版本行是否显示与当前版本的兼容性：模组类必然显示，其余类型仅当文件里有模组类时显示。社区屏/我的发布屏共用。</summary>
        internal static bool ShouldShowCompatibilityForVersion(PostVersionItem version)
        {
            if (version == null)
            {
                return false;
            }

            if (CommunityFileTypes.IsModLike(version.FileType))
            {
                return true;
            }

            return version.Files != null && Array.Exists(version.Files, file => file != null && CommunityFileTypes.IsModLike(file.Type));
        }

        /// <summary>列表未内嵌版本但服务器报有版本 → 应显示"展开加载文件"占位，点击拉帖子详情填充。</summary>
        internal static bool ShouldCreateLazyPlaceholder(PostItem post)
        {
            return (post?.PostVersions?.Length ?? 0) == 0
                && int.TryParse(post?.PostVersionCount, out int count) && count > 0;
        }

        // 服务器给的版本数；计数缺失但内嵌了版本时按内嵌数兜底（subText 展示用）。
        private static int GetPostVersionCount(PostItem post)
        {
            if (int.TryParse(post?.PostVersionCount, out int count) && count > 0)
            {
                return count;
            }
            return post?.PostVersions?.Length ?? 0;
        }

        private static string TryGetSizeText(string value)
        {
            if (long.TryParse(value, out long size))
            {
                return DataSizeFormatter.Format(size);
            }
            return value ?? "";
        }

        /// <summary>
        /// 服务端列表 total 字段会谎报（/user/posts 实测恒 2，与真实分页脱节）：已加载数
        /// 超过 total 在分页语义上不可能，此时弃用 total，展示退回"已加载到哪算哪"口径，
        /// 满页再放行 Page+1 继续翻页，翻到短页后由已加载数收敛出真实总数与页数。
        /// total 诚实时（总 ≥ 已加载）两值与原 total 口径完全一致。
        /// </summary>
        private static (int Total, int TotalPages) ResolveListTotals(int serverTotal, int limit, int page, int loadedCount)
        {
            int safeLimit = MathUtils.Max(limit, 1);
            int loadedTotal = MathUtils.Max(page - 1, 0) * safeLimit + loadedCount;
            int total = MathUtils.Max(serverTotal, loadedTotal);
            int totalPages = loadedTotal > serverTotal && loadedCount >= safeLimit
                ? MathUtils.Max(page, 1) + 1
                : CalculateTotalPages(total, limit);
            return (total, totalPages);
        }

        private static int CalculateTotalPages(int total, int limit)
        {
            if (limit <= 0)
            {
                return 1;
            }
            return MathUtils.Max((total + limit - 1) / limit, 1);
        }
    }

}
