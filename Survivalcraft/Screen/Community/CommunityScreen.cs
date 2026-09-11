using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using Engine;

namespace Game
{
    internal enum CommunitySection
    {
        All = 1,
        Trending = 2,
        NewActivity = 3
    }

    internal partial class CommunityScreen : CommunityScreenBase
    {
        // UI 控件
        public BevelledButtonWidget m_downloadButton;
        public BevelledButtonWidget m_prevPageButton;
        public BevelledButtonWidget m_nextPageButton;
        public TextBoxWidget m_pageJumpBox;
        public BevelledButtonWidget m_pageJumpButton;
        public BevelledButtonWidget m_filterButton;
        public BevelledButtonWidget m_orderButton;
        public TextBoxWidget m_searchBox;
        public LabelWidget m_searchHintLabel;
        public BevelledButtonWidget m_detailsButton;
        public BevelledButtonWidget m_moreButton;

        // 社区内容列表
        public CommunityTreeViewWidget m_postsTree;
        public ButtonWidget m_topBarBackButton;
        // 社区筛选状态
        private CommunitySection m_section = CommunitySection.All;
        private CommunityFileType m_selectedFileType = CommunityFileType.Mod;
        private CommunityOrderType m_orderType = CommunityOrderType.Hot;
        private bool m_onlyCurrentVersion = true;
        private bool m_ignoreFileTypeFilter;
        private bool m_ignoreOrderFilter;
        private bool m_ignoreVersionFilter;
        private bool m_ignoreSectionFilter;
        private CommunitySection m_defaultSection = CommunitySection.All;
        private CommunityFileType m_defaultSelectedFileType = CommunityFileType.Mod;
        private CommunityOrderType m_defaultOrderType = CommunityOrderType.Hot;
        private bool m_defaultOnlyCurrentVersion = true;
        private bool m_defaultIgnoreFileTypeFilter;
        private bool m_defaultIgnoreOrderFilter;
        private bool m_defaultIgnoreVersionFilter;
        private bool m_defaultIgnoreSectionFilter;
        private string m_searchTitle = string.Empty;
        // 搜索防抖：输入停止 SearchDebounceSeconds 后才重建左面板并发请求（0 表示无待触发）。
        private double m_searchDebounceUntil;
        private readonly TaskSlot<PostsLoadResult> m_postsLoadSlot = new(LanguageControl.Get("ScbbsPlusTaskSlot", "PostsList"));
        private readonly TaskSlot<PostDetailLoadResult> m_postDetailSlot = new(LanguageControl.Get("ScbbsPlusTaskSlot", "PostNodeDetails"));
        private readonly TaskSlot<PostDetailsDialogLoadResult> m_postDialogSlot = new(LanguageControl.Get("ScbbsPlusTaskSlot", "PostDetailsDialog"));
        private readonly TaskSlot<FeaturedLoadResult> m_featuredSlot = new(LanguageControl.Get("ScbbsPlusTaskSlot", "Featured"));
        private FeaturedLoadResult m_featuredResult;
        private bool m_firstDownloadSelfReviewPromptShown;
        private bool m_selfReviewPromptActive;
        private bool m_hasDeferredInitialPanelRefresh;
        private int m_deferredInitialPanelRefreshFrames;
        private bool m_hasDeferredEnterSideWork;
        private readonly PostTreePresenter m_postsPresenter;
        // 本地模组状态（评价缓存、待处理动作、社区发布状态、更新详情、待加载模组扫描）见 LocalModStateStore。
        private Dictionary<string, LocalModReviewEntry> m_localModReviews => LocalModStateStore.LocalModReviews;
        // 版本目录缓存见 VersionCatalogCache。

        // 认证会话（登录/登出/token 失效）见 AuthSessionManager，屏幕经 LoginStateChanged 订阅刷新界面。

        // 生产单例接线见各域类自身（AuthSessionManager.Instance / CommunityManager.ApiClient 等）。
        private string CurrentApiBaseUrl => AuthSessionManager.Instance.ApiBaseUrl;

        private DownloadSourceOption ResolveEffectiveDownloadSource()
        {
            // 枚举成员可非连续，未定义值不会等于任何具名成员，模式无须兜底；
            // 源读取兜底防手改 Settings.xml。
            return SettingsManager.CommunityDownloadSourceSelectionMode == DownloadSourceSelectionMode.CustomDefault
                ? NormalizeSource(SettingsManager.CommunityDefaultDownloadSource)
                : DownloadSourceOption.DefaultCn;
        }

        private static DownloadSourceOption NormalizeSource(DownloadSourceOption value)
        {
            return Enum.IsDefined(value) ? value : DownloadSourceOption.DefaultCn;
        }

        public CommunityScreen()
        {
            // 下载/整合包管线见 DownloadFlowManager（Presentation/Controllers），
            // 屏幕只订阅完成事件刷新本地列表。界面回调（状态提示/下载源/重启返回）是共享单例上的
            // 单槽委托，不能在 ctor 赋值——三屏 ctor 都赋会被后注册者覆盖；统一放 Enter/Leave 配对。
            m_selfScoreHandler = new((message, autoHideSeconds) => ShowToastPrompt(message, autoHideSeconds: autoHideSeconds));
            DownloadFlowManager downloadManager = DownloadFlowManager.Instance;
            XElement node = ContentManager.Get<XElement>("Screens/ScbbsPlusCommunityScreen");
            LoadContents(this, node);

            m_topBarBackButton = Children.Find<ButtonWidget>("TopBar.Back");
            m_postsTree = Children.Find<CommunityTreeViewWidget>("CommunityPostsTree");
            // 分页控件必须先 Find 再传给 presenter：presenter 构造时取字段值，
            // 顺序颠倒会把 null 存进 presenter，翻页/跳转永远失效。
            m_prevPageButton = Children.Find<BevelledButtonWidget>("CommunityPrevPageButton");
            m_nextPageButton = Children.Find<BevelledButtonWidget>("CommunityNextPageButton");
            m_pageJumpBox = Children.Find<TextBoxWidget>("CommunityPageJumpBox");
            m_pageJumpButton = Children.Find<BevelledButtonWidget>("CommunityPageJumpButton");
            m_postsPresenter = new PostTreePresenter(
                m_postsTree,
                Children.Find<LabelWidget>("CommunityPostsTitle"),
                m_prevPageButton,
                m_nextPageButton,
                m_pageJumpBox,
                m_pageJumpButton,
                item => EnumDisplayName.GetEnumDisplayName((CommunityFileType)item),
                PostTreePresenter.GetSingleVersionFile,
                PostTreePresenter.ShouldShowCompatibilityForVersion,
                m_postDetailSlot);
            m_downloadButton = Children.Find<BevelledButtonWidget>("CommunityDownloadButton");
            m_detailsButton = Children.Find<BevelledButtonWidget>("CommunityDetailsButton");
            m_filterButton = Children.Find<BevelledButtonWidget>("CommunityFilterButton");
            m_orderButton = Children.Find<BevelledButtonWidget>("CommunityOrderButton");
            m_searchBox = Children.Find<TextBoxWidget>("CommunitySearchBox");
            m_searchHintLabel = Children.Find<LabelWidget>("CommunitySearchHintLabel");
            m_searchBox.TextChanged += _ => HandleSearchTextChanged();

            m_moreButton = Children.Find<BevelledButtonWidget>("CommunityMoreButton");
            LoadSettings();
            LocalModStateStore.EnsureReviewsLoaded();
            LocalModStateStore.EnsurePendingActionsLoaded();
            DownloadFlowManager.Instance.LoadQueue();
            UpdatePaginationButtons();
        }

        public override void Enter(object[] parameters)
        {
            if (parameters != null
                && parameters.Length > 0
                && parameters[0] is string apiUrl
                && !string.IsNullOrWhiteSpace(apiUrl)
                && string.Equals(ApiUrls.NormalizeBaseUrl(CommunitySettingsManager.ApiBaseUrl), ApiUrls.DefaultBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                // 中文社区入口带入的 API 地址仅作初始值：设置里改过则设置优先。
                // 经包装属性赋值（setter 归一化并触发 ApiBaseUrlChanged → 切服登出链）。
                CommunitySettingsManager.ApiBaseUrl = apiUrl;
            }
            AcquireRemoteImportHost();
            InstallDownloadFlowCallbacks(
                MoreScreen.SetSharedStatus,
                () => SettingsManager.CommunityDownloadSourceSelectionMode,
                ResolveEffectiveDownloadSource);
            PlayedTimeTracker.StartSelfPage();
            UpdateTopBarTitle();
            // 首进先只搭骨架并让出 2 帧给首屏绘制，避免解密凭据/拉列表/读本地资源堵在同一帧造成黑屏。
            PreparePanelForDeferredInitialRefresh();
            m_hasDeferredInitialPanelRefresh = true;
            m_deferredInitialPanelRefreshFrames = 2;
            m_hasDeferredEnterSideWork = true;
            m_postsPresenter.MarkDirty();
        }

        // 页面切换后的刷新入口统一放这里，避免 UI 点击代码直接散落到各个加载方法。
        private void RefreshCurrentPage()
        {
            switch (GetEffectiveSection())
            {
                case CommunitySection.Trending:
                case CommunitySection.NewActivity:
                    if (m_featuredResult == null && !m_featuredSlot.IsRunning)
                    {
                        LoadFeatured();
                        return;
                    }

                    RenderSelectedFeatured();
                    return;
                default:
                    LoadCommunityPosts();
                    return;
            }
        }

        private void PreparePanelForDeferredInitialRefresh()
        {
            UpdateOrderButtonState();
            m_postsLoadSlot.Invalidate();
            m_postsPresenter.InvalidatePostDetailLoad();
            // 屏实例全局复用：每次进入都回第一页，防上次会话的页码残留；新类型页数变少时
            // 按旧页码请求会返回空列表（与我的发布屏 Enter/切类型重置 Page 同理）。
            m_postsPresenter.Page = 1;
            m_postsPresenter.TotalPages = 1;
            m_postsTree.Clear(false);
            PostIconLoader.DisposeRetired();

            switch (GetEffectiveSection())
            {
                case CommunitySection.Trending:
                case CommunitySection.NewActivity:
                    m_postsPresenter.StatusLabel.Text = LanguageControl.Get("ScbbsPlusCommunityScreen", "Main", "FeaturedContentLoading");
                    break;
                default:
                    m_postsPresenter.StatusLabel.Text = string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Main", "CommunityLoadingFormat"), EnumDisplayName.GetEnumDisplayName(m_selectedFileType));
                    break;
            }

            UpdatePaginationButtons();
            UpdateSearchHint();
        }

        private void UpdateDeferredInitialPanelRefresh()
        {
            if (m_hasDeferredInitialPanelRefresh)
            {
                if (m_deferredInitialPanelRefreshFrames > 0)
                {
                    m_deferredInitialPanelRefreshFrames--;
                    return;
                }

                m_hasDeferredInitialPanelRefresh = false;
                if (m_hasDeferredEnterSideWork)
                {
                    m_hasDeferredEnterSideWork = false;
                    LoadFeatured();
                    DownloadFlowManager.Instance.TryShowRestoredDownloadPromptOnEnter();
                }

                RefreshPanel();
                return;
            }

            if (m_hasDeferredEnterSideWork)
            {
                m_hasDeferredEnterSideWork = false;
                LoadFeatured();
                DownloadFlowManager.Instance.TryShowRestoredDownloadPromptOnEnter();
            }
        }

        private void UpdateTopBarTitle()
        {
            LabelWidget topBarLabel = Children.Find<LabelWidget>("TopBar.Label");
            // 原生内置后无本插件 ModEntity，ScbbsPlusVersionProvider.GetCurrentVersion() 恒为
            // null（VersionProvider 随自更新链不移植）：标题恒取无版本形态，版本行收尾批再定。
            topBarLabel.Text = LanguageControl.Get("ScbbsPlusCommunityScreen", "Text", "SCChineseCommunity");
        }

        public override void Leave()
        {
            ReleaseDownloadFlowCallbacks(releaseRemoteImportHost: true);
            InvalidatePageRequestIds();
            PlayedTimeTracker.StopSelfPage();
        }

        private void InvalidatePageRequestIds()
        {
            unchecked
            {
                m_postsLoadSlot.Invalidate();
                m_postDetailSlot.Invalidate();
                m_postDialogSlot.Invalidate();
                m_featuredSlot.Invalidate();
            }
        }

        public override void Update()
        {
            UpdateDeferredInitialPanelRefresh();
            UpdateSearchDebounce();

            if (Input.Back
                || Input.Cancel
                || m_topBarBackButton.IsClicked)
            {
                ScreensManager.GoBack();
            }

            HandleMoreButtonClicks();
            TryShowSelfReviewPromptAfterEnoughUse();

            // Tab 按钮切换
            HandleFilterClicks();
            HandleOrderClicks();
            HandlePaginationClicks();
            UpdatePostDetailsDialogTask();
            UpdateDownloadButtons();
            UpdatePaginationButtons();
            UpdateSearchHint();
            m_postsPresenter.Update();

            if (m_postsLoadSlot.TryTake(
                    out PostsLoadResult postsLoadResult,
                    error => new PostsLoadResult
                    {
                        RequestId = m_postsLoadSlot.RequestId,
                        Error = error.Message
                    }))
            {
                m_postsPresenter.ApplyPostsResult(
                    postsLoadResult,
                    postsLoadResult.Source == ResourceSource.MyPublished
                        ? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Text", "MyPublishedFormat"), EnumDisplayName.GetEnumDisplayName(postsLoadResult.FileType))
                        : string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "Text", "CommunityFormat"), EnumDisplayName.GetEnumDisplayName(postsLoadResult.FileType)));
            }

            if (m_featuredSlot.TryTake(out FeaturedLoadResult loadResult)
                && loadResult != null)
            {
                ApplyFeaturedResult(loadResult);
            }

            // 轮询帖子请求
            UpdateSelfScoreTask();
        }

        override protected void RememberRestartReturnToCurrentPage()
        {
            ModSettingsState.SetRestartReturnTarget(ScreenNames.Community);
            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "RecordRestartReturnScreen"), ScreenNames.Community));
        }

    }
}
