using System.Xml.Linq;

namespace Game
{
    public sealed class CommunityMissingDependencyPackageHeaderWidget : StackPanelWidget
    {
        private readonly CanvasWidget m_fileGroupSpacer;
        private readonly LabelWidget m_headerTextWidget;

        public CommunityMissingDependencyPackageHeaderWidget()
        {
            XElement node = ContentManager.Get<XElement>("Widgets/ScbbsPlusMissingDependencyPackageHeaderWidget");
            LoadContents(this, node);
            m_fileGroupSpacer = Children.Find<CanvasWidget>("ScbbsPlusMissingDependencyPackageHeaderWidget.FileGroupSpacer");
            m_headerTextWidget = Children.Find<LabelWidget>("ScbbsPlusMissingDependencyPackageHeaderWidget.HeaderText");
        }

        internal void Bind(string text, bool hasFileGroupSpacer)
        {
            m_fileGroupSpacer.IsVisible = hasFileGroupSpacer;
            m_headerTextWidget.Text = text ?? string.Empty;
        }
    }
}
