using System.Xml.Linq;

namespace Game
{
    public sealed class CommunityMissingDependencyPackageWidget : StackPanelWidget
    {
        private readonly FixedSizePanelWidget m_messagePanel;
        private readonly LabelWidget m_messageTextWidget;
        private readonly LabelWidget m_detailTextWidget;

        public CommunityMissingDependencyPackageWidget()
        {
            XElement node = ContentManager.Get<XElement>("Widgets/CommunityMissingDependencyPackageWidget");
            LoadContents(this, node);
            m_messagePanel = Children.Find<FixedSizePanelWidget>("MissingDependencyPackageWidget.MessagePanel");
            m_messageTextWidget = Children.Find<LabelWidget>("MissingDependencyPackageWidget.MessageText");
            m_detailTextWidget = Children.Find<LabelWidget>("MissingDependencyPackageWidget.DetailText");
        }

        internal void Bind(CommunityMissingDependencyPackageDisplayData data)
        {
            m_messagePanel.IsVisible = data != null;
            m_messageTextWidget.Text = data?.Text ?? string.Empty;
            m_detailTextWidget.Text = data?.DetailText ?? string.Empty;
        }
    }
}
