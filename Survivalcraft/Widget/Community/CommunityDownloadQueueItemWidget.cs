using System.Xml.Linq;
using Engine;

namespace Game {
    public class CommunityDownloadQueueItemWidget : CanvasWidget {
        public readonly RectangleWidget m_backgroundWidget;
        public readonly LabelWidget m_titleWidget;
        public readonly LabelWidget m_detailWidget;

        public string Title {
            get => m_titleWidget.Text;
            set => m_titleWidget.Text = value;
        }

        public string Detail {
            get => m_detailWidget.Text;
            set => m_detailWidget.Text = value;
        }

        public void ApplySelection(bool isSelected) {
            m_backgroundWidget.FillColor = isSelected ? new Color(42, 82, 36, 220) : new Color(18, 18, 18, 210);
            m_backgroundWidget.OutlineColor = isSelected ? new Color(255, 230, 140, 210) : new Color(72, 72, 72, 180);
            m_titleWidget.Color = isSelected ? new Color(255, 244, 188) : Color.White;
            m_detailWidget.Color = isSelected ? new Color(220, 236, 188) : new Color(178, 178, 178);
        }

        public CommunityDownloadQueueItemWidget() {
            XElement node = ContentManager.Get<XElement>("Widgets/CommunityDownloadQueueItemWidget");
            LoadContents(this, node);
            m_backgroundWidget = Children.Find<RectangleWidget>("DownloadQueueItemWidget.Background");
            m_titleWidget = Children.Find<LabelWidget>("DownloadQueueItemWidget.Title");
            m_detailWidget = Children.Find<LabelWidget>("DownloadQueueItemWidget.Detail");
        }
    }
}