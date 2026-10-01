namespace Game {
    partial class CommunityScreen {
        void LoadSettings() {
            ApplyLoadedDefaultFilters();
        }

        void SaveDefaultFilters(CommunityFiltersDialog.State state) {
            m_defaultSelectedFileType = state.FileType;
            m_defaultOrderType = state.OrderType;
            m_defaultOnlyCurrentVersion = state.OnlyCurrentVersion;
            m_defaultSection = NormalizeSection((int)state.Section);
            m_defaultIgnoreFileTypeFilter = state.IgnoreFileType;
            m_defaultIgnoreOrderFilter = state.IgnoreOrder;
            m_defaultIgnoreVersionFilter = state.IgnoreVersion;
            m_defaultIgnoreSectionFilter = state.IgnoreSection;
            CommunityModSettingsState.SetCommunityDefaults(
                m_defaultSelectedFileType,
                m_defaultOrderType,
                m_defaultOnlyCurrentVersion,
                (int)m_defaultSection
            );
        }

        void ApplyLoadedDefaultFilters() {
            m_defaultSelectedFileType = CommunityModSettingsState.CommunityFileType;
            m_defaultOrderType = CommunityModSettingsState.CommunityOrderType;
            m_defaultOnlyCurrentVersion = CommunityModSettingsState.CommunityOnlyCurrentVersion;
            m_defaultSection = NormalizeSection(CommunityModSettingsState.CommunitySection);
            m_defaultIgnoreFileTypeFilter = false;
            m_defaultIgnoreOrderFilter = false;
            m_defaultIgnoreVersionFilter = false;
            m_defaultIgnoreSectionFilter = false;
            m_selectedFileType = m_defaultSelectedFileType;
            m_orderType = m_defaultOrderType;
            m_onlyCurrentVersion = m_defaultOnlyCurrentVersion;
            m_section = m_defaultSection;
            m_ignoreFileTypeFilter = m_defaultIgnoreFileTypeFilter;
            m_ignoreOrderFilter = m_defaultIgnoreOrderFilter;
            m_ignoreVersionFilter = m_defaultIgnoreVersionFilter;
            m_ignoreSectionFilter = m_defaultIgnoreSectionFilter;
            m_postsPresenter.Page = 1;
        }

        static CommunitySection NormalizeSection(int section) {
            return section switch {
                2 => CommunitySection.Trending,
                3 => CommunitySection.NewActivity,
                _ => CommunitySection.All
            };
        }
    }
}