using System.Xml.Linq;

namespace Game {
    public sealed class CommunityMissingDependencyPackageWidget : StackPanelWidget {
        readonly FixedSizePanelWidget m_messagePanel;
        readonly LabelWidget m_messageTextWidget;
        readonly LabelWidget m_detailTextWidget;
        readonly LinkWidget m_linkWidget;

        public CommunityMissingDependencyPackageWidget() {
            XElement node = ContentManager.Get<XElement>("Widgets/CommunityMissingDependencyPackageWidget");
            LoadContents(this, node);
            m_messagePanel = Children.Find<FixedSizePanelWidget>("MissingDependencyPackageWidget.MessagePanel");
            m_messageTextWidget = Children.Find<LabelWidget>("MissingDependencyPackageWidget.MessageText");
            m_detailTextWidget = Children.Find<LabelWidget>("MissingDependencyPackageWidget.DetailText");
            m_linkWidget = Children.Find<LinkWidget>("MissingDependencyPackageWidget.Link");
        }

        internal void Bind(CommunityMissingDependencyPackageDisplayData data) {
            m_messagePanel.IsVisible = data != null;
            m_messageTextWidget.Text = data?.Text ?? string.Empty;
            m_detailTextWidget.Text = data?.DetailText ?? string.Empty;
            // 依赖声明了下载页链接才显示链接行；LinkWidget 点击自带打开浏览器
            m_linkWidget.IsVisible = !string.IsNullOrWhiteSpace(data?.Link);
            m_linkWidget.Text = $"{LanguageControl.Get("MissingDependenciesDialog", "OpenDependencyHomepage")}{data?.Link}";
            m_linkWidget.Url = data?.Link;
        }
    }
}