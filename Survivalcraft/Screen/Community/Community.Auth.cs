using Engine;
using System.Globalization;

namespace Game
{
    internal partial class CommunityScreen
    {
        private void LoadSettings()
        {
            m_firstDownloadSelfReviewPromptShown = ModSettingsState.FirstDownloadSelfReviewPromptShown;
            ApplyLoadedDefaultFilters();
        }

        private void SaveSettings()
        {
            ModSettingsState.SetFirstDownloadSelfReviewPromptShown();
        }

        private void SaveDefaultFilters(FiltersDialog.State state)
        {
            m_defaultSelectedFileType = state.FileType;
            m_defaultOrderType = state.OrderType;
            m_defaultOnlyCurrentVersion = state.OnlyCurrentVersion;
            m_defaultSection = NormalizeSection((int)state.Section);
            m_defaultIgnoreFileTypeFilter = state.IgnoreFileType;
            m_defaultIgnoreOrderFilter = state.IgnoreOrder;
            m_defaultIgnoreVersionFilter = state.IgnoreVersion;
            m_defaultIgnoreSectionFilter = state.IgnoreSection;
            ModSettingsState.SetCommunityDefaults(
                m_defaultSelectedFileType,
                m_defaultOrderType,
                m_defaultOnlyCurrentVersion,
                (int)m_defaultSection);
            Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusCommunityScreen", "DefaultFiltersSaved"), m_defaultSelectedFileType, m_defaultOrderType, m_defaultOnlyCurrentVersion, m_defaultSection, m_defaultIgnoreFileTypeFilter, m_defaultIgnoreOrderFilter, m_defaultIgnoreVersionFilter, m_defaultIgnoreSectionFilter));
        }

        private void ApplyLoadedDefaultFilters()
        {
            m_defaultSelectedFileType = ModSettingsState.CommunityFileType;
            m_defaultOrderType = ModSettingsState.CommunityOrderType;
            m_defaultOnlyCurrentVersion = ModSettingsState.CommunityOnlyCurrentVersion;
            m_defaultSection = NormalizeSection(ModSettingsState.CommunitySection);
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

        private static CommunitySection NormalizeSection(int section)
        {
            return section switch
            {
                2 => CommunitySection.Trending,
                3 => CommunitySection.NewActivity,
                _ => CommunitySection.All
            };
        }
    }
}
