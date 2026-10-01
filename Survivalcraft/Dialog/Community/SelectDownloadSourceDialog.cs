using System;
using System.Xml.Linq;
using Engine;

namespace Game
{
    // 下载源列表管理框（镜像 SelectCommunityServerDialog）：内置只读线路 + 自建线路同列展示，
    // 当前默认线路带 ● 标记；点击已选中项或"设为默认"按钮即设为默认，添加/编辑进编辑框。
    public sealed class SelectDownloadSourceDialog : Dialog
    {
        public ListPanelWidget m_listPanel;
        public ButtonWidget m_setDefaultButton;
        public ButtonWidget m_addButton;
        public ButtonWidget m_editButton;

        public SelectDownloadSourceDialog()
        {
            XElement node = ContentManager.Get<XElement>("Dialogs/SelectDownloadSourceDialog");
            LoadContents(this, node);
            m_listPanel = Children.Find<ListPanelWidget>("SelectDownloadSourceDialog.ListPanel");
            m_setDefaultButton = Children.Find<ButtonWidget>("SelectDownloadSourceDialog.SetDefault");
            m_addButton = Children.Find<ButtonWidget>("SelectDownloadSourceDialog.Add");
            m_editButton = Children.Find<ButtonWidget>("SelectDownloadSourceDialog.Edit");
            m_listPanel.ItemWidgetFactory = o =>
            {
                if (o is not CommunityDownloadSourceManager.Info info)
                {
                    return null;
                }
                string marker = info == CommunityDownloadSourceManager.CurrentDefaultInfo ? "● " : string.Empty;
                return new LabelWidget
                {
                    HorizontalAlignment = WidgetAlignment.Center,
                    VerticalAlignment = WidgetAlignment.Center,
                    Text = marker + CommunityDownloadSourceManager.GetDisplayName(info)
                };
            };
            m_listPanel.ItemClicked = o =>
            {
                if (o == m_listPanel.SelectedItem
                    && o is CommunityDownloadSourceManager.Info info)
                {
                    SetDefault(info);
                }
            };
            UpdateList();
        }

        public void UpdateList()
        {
            m_listPanel.ClearItems();
            m_listPanel.AddItems(CommunityDownloadSourceManager.GetAllInfos());
            m_listPanel.ScrollPosition = 0f;
        }

        public void SetDefault(CommunityDownloadSourceManager.Info info)
        {
            CommunityDownloadSourceManager.ChangeDefaultInfo(info);
            // 重建条目刷新 ● 默认标记
            UpdateList();
        }

        public void AddSource(CommunityDownloadSourceManager.Info info)
        {
            CommunityDownloadSourceManager.AddUserInfo(info);
            m_listPanel.AddItem(info);
            m_listPanel.SelectedItem = info;
        }

        public void RemoveSource(CommunityDownloadSourceManager.Info info)
        {
            CommunityDownloadSourceManager.RemoveUserInfo(info);
            m_listPanel.RemoveItem(info);
        }

        public override void Update()
        {
            if (m_listPanel.SelectedItem is CommunityDownloadSourceManager.Info selected)
            {
                if (m_setDefaultButton.IsClicked)
                {
                    SetDefault(selected);
                }
                if (m_editButton.IsClicked)
                {
                    DialogsManager.ShowDialog(null, new EditDownloadSourceInfoDialog(this, selected));
                }
            }
            if (m_addButton.IsClicked)
            {
                DialogsManager.ShowDialog(null, new EditDownloadSourceInfoDialog(this));
            }
            if (Input.Back
                || Input.Cancel
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value)))
            {
                DialogsManager.HideDialog(this);
            }
        }
    }
}
