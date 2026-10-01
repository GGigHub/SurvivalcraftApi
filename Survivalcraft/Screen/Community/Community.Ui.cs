using Engine;

namespace Game {
    partial class CommunityScreen {
        // 社区设置页同时承担“下载队列入口、远程导入入口、账号入口”的聚合职责。
        void HandleMoreButtonClicks() {
            if (m_moreButton.IsClicked) {
                ScreensManager.SwitchScreen("CommunitySettings");
            }
        }

        void HandleFilterClicks() {
            if (!m_filterButton.IsClicked) {
                return;
            }
            CommunityFiltersDialog.State oldState = GetCurrentFiltersState();
            DialogsManager.ShowDialog(
                null,
                new CommunityFiltersDialog(
                    oldState,
                    SaveDefaultFilters,
                    delegate(CommunityFiltersDialog.State state) {
                        if (FiltersStateEquals(state, oldState)) {
                            return;
                        }
                        ApplyFiltersState(state);
                        m_postsPresenter.Page = 1;
                        RefreshPanel();
                    }
                )
            );
        }

        CommunityFiltersDialog.State GetCurrentFiltersState() => new() {
            FileType = m_selectedFileType,
            OrderType = m_orderType,
            OnlyCurrentVersion = m_onlyCurrentVersion,
            Section = m_section,
            IgnoreFileType = m_ignoreFileTypeFilter,
            IgnoreOrder = m_ignoreOrderFilter,
            IgnoreVersion = m_ignoreVersionFilter,
            IgnoreSection = m_ignoreSectionFilter
        };

        void ApplyFiltersState(CommunityFiltersDialog.State state) {
            m_selectedFileType = state.FileType;
            m_orderType = state.OrderType;
            m_onlyCurrentVersion = state.OnlyCurrentVersion;
            m_section = NormalizeSection((int)state.Section);
            // 非 All 版块走独立的推荐资源接口，Order 参数发不出去，是死设置；
            // 重置为默认值，避免切回 All 时残留一个用户早已看不到生效的陈旧排序。
            if (m_section != CommunitySection.All) {
                m_orderType = CommunityOrderType.Hot;
            }
            m_ignoreFileTypeFilter = state.IgnoreFileType;
            m_ignoreOrderFilter = state.IgnoreOrder;
            m_ignoreVersionFilter = state.IgnoreVersion;
            m_ignoreSectionFilter = state.IgnoreSection;
        }

        CommunitySection GetEffectiveSection() =>
            // 搜索词非空 = 全局搜索：强制回帖子列表。原版把搜索生效绑在"忽略分区"开关上
            //（默认关），热门/新动态分区下输入搜索词完全不起作用（精选内容原地不动），
            // 实测即"搜索坏了"。
            HasSearchTitle() ? CommunitySection.All : m_section;

        void LoadCurrentPostsList() {
            LoadCommunityPosts();
        }

        bool HasSearchTitle() => !string.IsNullOrWhiteSpace(m_searchTitle);

        static bool FiltersStateEquals(CommunityFiltersDialog.State state, CommunityFiltersDialog.State right) => state.FileType == right.FileType
            && state.OrderType == right.OrderType
            && state.OnlyCurrentVersion == right.OnlyCurrentVersion
            && state.Section == right.Section
            && state.IgnoreFileType == right.IgnoreFileType
            && state.IgnoreOrder == right.IgnoreOrder
            && state.IgnoreVersion == right.IgnoreVersion
            && state.IgnoreSection == right.IgnoreSection;

        void HandleOrderClicks() {
            if (!m_orderButton.IsClicked) {
                return;
            }
            CommunityOrderType oldOrderType = m_orderType;
            DialogsManager.ShowDialog(
                null,
                new ListSelectionDialog(
                    LanguageControl.Get("FiltersDialog", "SelectSortOrder"),
                    new[] { CommunityOrderType.Latest, CommunityOrderType.Oldest, CommunityOrderType.Updated, CommunityOrderType.Hot },
                    60f,
                    item => LanguageControl.GetEnumDisplayName((CommunityOrderType)item),
                    delegate(object item) {
                        CommunityOrderType newOrderType = (CommunityOrderType)item;
                        if (newOrderType == oldOrderType) {
                            return;
                        }
                        m_orderType = newOrderType;
                        m_postsPresenter.Page = 1;
                        UpdateOrderButtonState();
                        LoadCommunityPosts();
                    }
                )
            );
        }

        const double SearchDebounceSeconds = 0.4;

        void HandleSearchTextChanged() {
            string newSearchTitle = m_searchBox?.Text?.Trim() ?? string.Empty;
            if (string.Equals(newSearchTitle, m_searchTitle, StringComparison.Ordinal)) {
                return;
            }
            m_searchTitle = newSearchTitle;
            m_postsPresenter.Page = 1;
            // 防抖：输入期间只更新标题，停止输入 400ms 后由 Update 触发一次重建+请求，
            // 不再每键完整重建左面板并立即发 HTTP（请求风暴）。输入期间列表保持不清空。
            m_searchDebounceUntil = Time.RealTime + SearchDebounceSeconds;
        }

        void UpdateSearchDebounce() {
            if (m_searchDebounceUntil > 0
                && Time.RealTime >= m_searchDebounceUntil) {
                m_searchDebounceUntil = 0;
                RefreshPanel();
            }
        }

        void UpdateSearchHint() {
            m_searchHintLabel.IsVisible = string.IsNullOrEmpty(m_searchBox.Text) && !m_searchBox.HasFocus;
        }

        void HandlePaginationClicks() {
            if (m_postsPresenter.HandlePaginationClicks()) {
                LoadCurrentPostsList();
            }
        }

        // 按钮状态跟实际请求走：Order 参数只在有效版块为 All 时发出（/post/list），
        // Trending/NewActivity 走独立推荐接口，不传 Order，按钮禁用并显示「不可用」。
        void UpdateOrderButtonState() {
            bool orderEffective = GetEffectiveSection() == CommunitySection.All;
            m_orderButton.IsEnabled = orderEffective;
            m_orderButton.Text = orderEffective ? LanguageControl.GetEnumDisplayName(m_orderType) : LanguageControl.Unavailable;
        }

        void UpdatePaginationButtons() {
            // loadBusy 语义：true = 正在加载（禁用按钮）。之前误写成 == null（空闲时反而禁用），
            // 列表加载完成后 task 置 null，按钮被永久锁死。
            m_postsPresenter.UpdatePaginationButtons(() => m_postsLoadSlot.IsRunning);
        }

        internal static string GetVersionFilterName(bool onlyCurrentVersion) => onlyCurrentVersion
            ? LanguageControl.Get("CommunityScreen", "CurrentVersion")
            : LanguageControl.Get("CommunityScreen", "AllVersions");

        // 认证会话状态变化的界面副作用；会话本体由 AuthSessionManager 管理。
        protected override void OnLoginStateChanged(AuthSessionManager.AuthChangeKind kind) {
            if (kind == AuthSessionManager.AuthChangeKind.LoggedOut
                || kind == AuthSessionManager.AuthChangeKind.SessionExpired) {
                // 会话清空后列表数据不再可信，回到加载态重新拉取社区列表。
                RefreshPanel();
                UpdatePaginationButtons();
            }
        }
    }
}