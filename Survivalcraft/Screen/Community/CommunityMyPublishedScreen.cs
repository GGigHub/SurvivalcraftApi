using System.Globalization;
using System.Xml.Linq;
using Engine;

namespace Game {
    /// <summary>
    /// 我的发布屏（终端屏）：本账号发布的资源列表。不能进一步打开其他屏，只能左上角返回/返回键返回。
    /// 未登录进入时弹登录框；登录取消则直接返回上一屏。
    /// </summary>
    class CommunityMyPublishedScreen : CommunityScreenBase {
        // UI 控件
        public CommunityTreeViewWidget m_postsTree;
        public LabelWidget m_postsStatusLabel;
        public ButtonWidget m_topBarBackButton;
        public BevelledButtonWidget m_typeButton;
        public BevelledButtonWidget m_prevPageButton;
        public BevelledButtonWidget m_nextPageButton;
        public TextBoxWidget m_pageJumpBox;
        public BevelledButtonWidget m_pageJumpButton;
        public BevelledButtonWidget m_shareLinkButton;
        public BevelledButtonWidget m_downloadButton;
        public BevelledButtonWidget m_unpublishedButton;

        readonly PostTreePresenter m_postsPresenter;
        ExternalContentType m_selectedFileType = ExternalContentType.Mod;
        readonly TaskSlot<PostsLoadResult> m_postsLoadSlot = new(LanguageControl.Get("TaskSlot", "PostsList"));
        readonly TaskSlot<UploadFilesLoadResult> m_uploadsLoadSlot = new(LanguageControl.Get("TaskSlot", "PostsList"));
        readonly TaskSlot<PostDetailLoadResult> m_postDetailSlot = new(LanguageControl.Get("TaskSlot", "PostNodeDetails"));

        bool m_pendingLoginOnEnter;

        // 只看未发布开关：开=upload/list 平铺未发布文件；关=帖子树（全部类别）。
        bool m_showUnpublished;

        public CommunityMyPublishedScreen() {
            // 下载/整合包管线见 DownloadFlowManager（ModsManager/Community/Downloads）。
            // 界面回调（状态提示/下载源/重启返回）是共享单例上的单槽委托，三屏 ctor 都赋会被
            // 后注册者覆盖，统一放 Enter/Leave 配对（社区/本地屏有自己的回调值，此处用默认值）。
            XElement node = ContentManager.Get<XElement>("Screens/CommunityMyPublishedScreen");
            LoadContents(this, node);
            m_topBarBackButton = Children.Find<ButtonWidget>("TopBar.Back");
            // LocalTypeOptionsPanel/PaginationPanel 仅验证 XML 结构存在，无字段。
            m_postsStatusLabel = Children.Find<LabelWidget>("CommunityMyPublishedScreen.PostsStatusLabel");
            m_postsTree = Children.Find<CommunityTreeViewWidget>("CommunityMyPublishedScreen.PostsTree");
            m_downloadButton = Children.Find<BevelledButtonWidget>("CommunityMyPublishedScreen.DownloadButton");
            m_shareLinkButton = Children.Find<BevelledButtonWidget>("CommunityMyPublishedScreen.ShareLinkButton");
            Children.Find<StackPanelWidget>("CommunityMyPublishedScreen.PaginationPanel");
            m_prevPageButton = Children.Find<BevelledButtonWidget>("CommunityMyPublishedScreen.PrevPageButton");
            m_nextPageButton = Children.Find<BevelledButtonWidget>("CommunityMyPublishedScreen.NextPageButton");
            m_pageJumpBox = Children.Find<TextBoxWidget>("CommunityMyPublishedScreen.PageJumpBox");
            m_pageJumpButton = Children.Find<BevelledButtonWidget>("CommunityMyPublishedScreen.PageJumpButton");
            Children.Find<StackPanelWidget>("CommunityMyPublishedScreen.LocalTypeOptionsPanel");
            m_typeButton = Children.Find<BevelledButtonWidget>("CommunityMyPublishedScreen.TypeButton");
            m_unpublishedButton = Children.Find<BevelledButtonWidget>("CommunityMyPublishedScreen.UnpublishedButton");
            m_postsPresenter = new PostTreePresenter(
                m_postsTree,
                m_postsStatusLabel,
                m_prevPageButton,
                m_nextPageButton,
                m_pageJumpBox,
                m_pageJumpButton,
                item => LanguageControl.GetEnumDisplayName(item),
                PostTreePresenter.GetSingleVersionFile,
                PostTreePresenter.ShouldShowCompatibilityForVersion,
                m_postDetailSlot
            );
            m_downloadButton.IsEnabled = false;
        }

        public override void Enter(object[] parameters) {
            // 屏实例全局复用：每次进入都从第一页开始，防上次会话的页码残留。
            m_postsPresenter.Page = 1;
            // 屏实例全局复用：防上次会话开关残留，同 Page 重置语义。
            m_showUnpublished = false;
            // 游戏内 CommunityModSettingsState 以 MyPublishedFileType 单属性承载本屏类型（无按屏名
            // TryGet 版式），恒有值，直接恢复即可。
            SetCurrentMyPublishedFileType(CommunityModSettingsState.MyPublishedFileType);
            InstallDownloadFlowCallbacks(_ => { }, () => false, () => CommunityDownloadSourceManager.DefaultCnInfo);
            DownloadFlowManager.Instance.LoadQueue();
            m_pendingLoginOnEnter = false;
            // 本屏 Update 轮询前的会话恢复：直奔本屏（不经过社区/More 屏）时只有这里
            // 能把本地凭据装进 IsLoggedIn，否则已登录也会被下面的检查弹登录框。
            AuthSessionManager.Instance.EnsureStoredAuthLoaded();
            if (!AuthSessionManager.Instance.IsLoggedIn) {
                m_pendingLoginOnEnter = true;
                AuthSessionManager.Instance.OpenAuthEntry();
                return;
            }
            LoadMyPublishedPosts();
        }

        public override void Leave() {
            m_postsLoadSlot.Invalidate();
            m_uploadsLoadSlot.Invalidate();
            m_postDetailSlot.Invalidate();
            ReleaseDownloadFlowCallbacks(false);
            m_pendingLoginOnEnter = false;
        }

        protected override void OnLoginStateChanged(AuthSessionManager.AuthChangeKind kind) {
            // 登录成功刷新列表，登录取消返回上一屏（终端屏语义）。
            switch (kind) {
                case AuthSessionManager.AuthChangeKind.LoggedIn:
                    if (m_pendingLoginOnEnter) {
                        m_pendingLoginOnEnter = false;
                        LoadMyPublishedPosts();
                    }
                    break;
                case AuthSessionManager.AuthChangeKind.LoginCanceled:
                    if (m_pendingLoginOnEnter) {
                        m_pendingLoginOnEnter = false;
                        ScreensManager.GoBack();
                    }
                    break;
            }
        }

        public override void Update() {
            if (Input.Back
                || Input.Cancel
                || m_topBarBackButton.IsClicked) {
                ScreensManager.GoBack();
            }
            HandleTypeButtonClick();
            HandleUnpublishedToggle();
            if (m_unpublishedButtonState != (m_showUnpublished ? 1 : 0)) {
                m_unpublishedButtonState = m_showUnpublished ? 1 : 0;
                m_unpublishedButton.IsChecked = m_showUnpublished;
                m_unpublishedButton.BevelColor = m_showUnpublished ? new Color(50, 150, 35) : new Color(181, 172, 154);
                m_unpublishedButton.CenterColor = m_showUnpublished ? new Color(50, 150, 35) : new Color(181, 172, 154);
            }
            HandlePaginationClicks();
            // 翻页按钮可用性随加载状态刷新（同社区屏，经 PostTreePresenter.UpdatePaginationButtons）；
            // 帖子/未发布两路任一在途都禁用，加载中不放开翻页。
            m_postsPresenter.UpdatePaginationButtons(() => m_postsLoadSlot.IsRunning || m_uploadsLoadSlot.IsRunning);
            UpdateDownloadButtons();
            m_postsPresenter.Update();
            if (m_postsLoadSlot.TryTake(
                    out PostsLoadResult loadResult,
                    error => new PostsLoadResult { RequestId = m_postsLoadSlot.RequestId, Error = error.Message }
                )) {
                m_postsPresenter.ApplyPostsResult(
                    loadResult,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("CommunityMyPublishedScreen", "MyPublishedFormat"),
                        LanguageControl.GetEnumDisplayName(loadResult.FileType)
                    )
                );
            }
            if (m_uploadsLoadSlot.TryTake(
                    out UploadFilesLoadResult uploadsResult,
                    error => new UploadFilesLoadResult { RequestId = m_uploadsLoadSlot.RequestId, Error = error.Message }
                )) {
                m_postsPresenter.ApplyUploadFilesResult(
                    uploadsResult,
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("CommunityMyPublishedScreen", "MyUnpublishedFormat"),
                        LanguageControl.GetEnumDisplayName(uploadsResult.FileType)
                    )
                );
            }
        }

        void LoadMyPublishedPosts() {
            m_postsPresenter.Clear(true);
            m_postsLoadSlot.Invalidate();
            m_uploadsLoadSlot.Invalidate();
            m_postsPresenter.InvalidatePostDetailLoad();
            ExternalContentType fileType = m_selectedFileType;
            // 开关开：/upload/list 未发布平铺；关：帖子树（全部类别）。
            if (m_showUnpublished) {
                LoadMyUploads(fileType);
                return;
            }

            // ↓↓ 以下为帖子树路径，全部类别生效 ↓↓
            string token = AuthSessionManager.Instance.CurrentToken;
            int userId = AuthSessionManager.Instance.CurrentUser?.Id ?? 0;
            string apiBaseUrl = AuthSessionManager.Instance.ApiBaseUrl;
            m_postsPresenter.TotalPages = MathUtils.Max(m_postsPresenter.Page, 1);
            m_postsStatusLabel.Text = string.Format(
                CultureInfo.InvariantCulture,
                LanguageControl.Get("CommunityMyPublishedScreen", "LoadingFormat"),
                string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("CommunityMyPublishedScreen", "MyPublishedFormat"),
                    LanguageControl.GetEnumDisplayName(fileType)
                )
            );
            VersionCatalogCache.TryGetCachedVersionCatalog(
                apiBaseUrl,
                out VersionListItem[] cachedCatalog,
                out Dictionary<int, string> cachedVersionMap,
                out _
            );
            bool needVersionCatalog = VersionCatalogCache.ShouldLoadVersionCatalogForFileType(fileType);
            m_postsLoadSlot.Start(async (requestId, _) => {
                    CommunityUserPostsLoad load = await CommunityManager.LoadUserPostsPageAsync(
                        CommunityManager.ApiClient,
                        apiBaseUrl,
                        token,
                        userId,
                        m_postsPresenter.Page,
                        10,
                        CommunityFileTypes.FromExternal(fileType),
                        needVersionCatalog,
                        cachedCatalog,
                        cachedVersionMap
                    );
                    if (!string.IsNullOrEmpty(load.Error)) {
                        Log.Error(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                LanguageControl.Get("CommunityMyPublishedScreen", "LoadMyPublishedFailed"),
                                load.Error
                            )
                        );
                    }
                    return new PostsLoadResult {
                        RequestId = requestId,
                        FileType = fileType,
                        Source = ResourceSource.MyPublished,
                        ApiBaseUrl = apiBaseUrl,
                        Response = load.Response,
                        VersionCatalog = load.VersionCatalog,
                        GameVersionsById = load.GameVersionsById,
                        Error = load.Error
                    };
                }
            );
        }

        // 开关开：/upload/list 未发布平铺文件列表（publishState=2，token 归属"我的上传"），
        // 全类别可用；平铺行无版本/兼容性展示，不加载版本目录。
        void LoadMyUploads(ExternalContentType fileType) {
            string token = AuthSessionManager.Instance.CurrentToken;
            string apiBaseUrl = AuthSessionManager.Instance.ApiBaseUrl;
            m_postsPresenter.TotalPages = MathUtils.Max(m_postsPresenter.Page, 1);
            m_postsStatusLabel.Text = string.Format(
                CultureInfo.InvariantCulture,
                LanguageControl.Get("CommunityMyPublishedScreen", "LoadingFormat"),
                string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("CommunityMyPublishedScreen", "MyUnpublishedFormat"),
                    LanguageControl.GetEnumDisplayName(fileType)
                )
            );
            m_uploadsLoadSlot.Start(async (requestId, _) => {
                    CommunityUploadFilesLoad load = await CommunityManager.LoadMyUploadsAsync(
                        CommunityManager.ApiClient,
                        apiBaseUrl,
                        token,
                        m_postsPresenter.Page,
                        10,
                        CommunityFileTypes.FromExternal(fileType)
                    );
                    if (!string.IsNullOrEmpty(load.Error)) {
                        Log.Error(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                LanguageControl.Get("CommunityMyPublishedScreen", "LoadMyPublishedFailed"),
                                load.Error
                            )
                        );
                    }
                    return new UploadFilesLoadResult { RequestId = requestId, FileType = fileType, Response = load.Response, Error = load.Error };
                }
            );
        }

        // 小屏顶栏摆不下 6 个类型按钮：收进 ListSelectionDialog（ManageUserScreen 同款惯式）。
        void HandleTypeButtonClick() {
            if (m_typeButton is null or { IsClicked: false }) {
                return;
            }
            ExternalContentType[] fileTypeValues = CommunityFileTypes.FilterableValues();
            DialogsManager.ShowDialog(
                null,
                new ListSelectionDialog(
                    LanguageControl.Get("FiltersDialog", "SelectFileType"),
                    fileTypeValues,
                    60f,
                    item => LanguageControl.GetEnumDisplayName((ExternalContentType)item),
                    delegate(object result) {
                        ExternalContentType newFileType = (ExternalContentType)result;
                        if (newFileType == m_selectedFileType) {
                            return;
                        }
                        SetCurrentMyPublishedFileType(newFileType);
                        LoadMyPublishedPosts();
                    }
                )
            );
        }

        // 只看未发布开关：切换即回第一页，语义同 SetCurrentMyPublishedFileType。
        void HandleUnpublishedToggle() {
            if (m_unpublishedButton is null or { IsClicked: false }) {
                return;
            }
            m_showUnpublished = !m_showUnpublished;
            m_postsPresenter.Page = 1;
            LoadMyPublishedPosts();
        }

        void SetCurrentMyPublishedFileType(ExternalContentType fileType) {
            m_selectedFileType = fileType;
            CommunityModSettingsState.SetMyPublishedFileType(fileType);
            // 类型决定页数上限：切换（含 Enter 恢复上次类型）必须回第一页，否则新类型页数
            // 少于旧类型时仍按旧页码请求，返回空列表显示"没有数据"。屏实例全局复用，
            // Page 跨 Enter 存活，与社区屏筛选/搜索重置 Page 同理。
            m_postsPresenter.Page = 1;
        }

        void HandlePaginationClicks() {
            if (m_postsPresenter.HandlePaginationClicks()) {
                LoadMyPublishedPosts();
            }
        }

        // Text/IsEnabled 完全由两个布尔输入决定；签名不变时跳过赋值，
        // 避免每帧 2 次 LanguageControl.Get（params 数组 + JsonNode 查找的堆分配）。
        int m_downloadButtonsState = -1;

        // 只看未发布开关按钮样式哨兵：与 m_showUnpublished 比较，变化才重写按钮样式。
        int m_unpublishedButtonState = -1;

        void UpdateDownloadButtons() {
            // SelectedNode 遮蔽属性含 O(n) 可达性检查，每帧多次调用时存局部变量只走一次。
            TreeViewNode selectedNode = m_postsTree.SelectedNode;
            PostFileItem selectedFile = selectedNode?.Tag as PostFileItem;
            PostVersionItem selectedVersion = selectedNode?.Tag as PostVersionItem;
            PostFileItem downloadTarget = selectedFile ?? PostTreePresenter.GetSingleVersionFile(selectedVersion);
            bool downloadBusy = DownloadFlowManager.Instance.IsBusy;
            bool showShareLink = selectedFile != null && !string.IsNullOrWhiteSpace(selectedFile.Url);
            m_shareLinkButton.IsVisible = showShareLink;
            bool hasDownloadTarget = downloadTarget != null;
            int state = (hasDownloadTarget ? 1 : 0) | (downloadBusy ? 2 : 0);
            if (state != m_downloadButtonsState) {
                m_downloadButtonsState = state;
                m_downloadButton.IsEnabled = hasDownloadTarget && !downloadBusy;
                m_downloadButton.Text = downloadBusy
                    ? LanguageControl.Get("CommunityScreen", "Downloading")
                    : LanguageControl.Get("ExternalContentScreen", "2");
            }
            if (m_downloadButton.IsClicked
                && downloadTarget != null) {
                DownloadFlowManager.Instance.StartDownload(downloadTarget, selectedVersion);
            }
            if (showShareLink && m_shareLinkButton?.IsClicked == true) {
                CopyPublishedFileShareLink(selectedFile);
            }
        }

        static void CopyPublishedFileShareLink(PostFileItem file) {
            string url = file?.Url?.Trim();
            if (string.IsNullOrWhiteSpace(url)) {
                return;
            }
            ClipboardManager.ClipboardString = url;
            CommunityToastManager.Show(LanguageControl.Get("CommunityMyPublishedScreen", "ShareLinkCopiedToClipboard"), autoHideSeconds: 2.0);
        }

        public override void Dispose() {
            // PostIconLoader 静态强引用事件的唯一解绑点随屏生命周期走：
            // 换语言重建 Dispose 本屏时解绑，孤儿 handler 不复存在（同社区屏 Dispose）。
            m_postsPresenter.Detach();
            base.Dispose();
        }
    }
}