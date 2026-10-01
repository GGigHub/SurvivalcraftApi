using System.Xml.Linq;

namespace Game {
    // 下载源编辑框（镜像 EditCommunityServerInfoDialog）：内置条目只读可克隆（m_toAdd=true，
    // 克隆件自动另起用户可见名避免占用内置保留内部名），自建条目原地改；删除仅限未改动条目。
    // 名称是下载源身份（队列/设置按名恢复），确定时查重；主机留空 = 默认线路语义（不改写 URL）。
    public sealed class EditDownloadSourceInfoDialog : Dialog {
        public SelectDownloadSourceDialog m_parentDialog;
        public CommunityDownloadSourceManager.Info m_info;
        public bool m_changed;
        public bool m_toAdd; //false: edit, true: add

        public TextBoxWidget m_nameTextBox;
        public TextBoxWidget m_hostTextBox;
        public ButtonWidget m_okButton;
        public ButtonWidget m_removeButton;
        public ButtonWidget m_cancelButton;

        public EditDownloadSourceInfoDialog(SelectDownloadSourceDialog parentDialog, CommunityDownloadSourceManager.Info info = null) {
            m_parentDialog = parentDialog;
            if (info == null) {
                m_info = new CommunityDownloadSourceManager.Info(string.Empty, string.Empty);
                m_changed = true;
                m_toAdd = true;
            }
            else if (CommunityDownloadSourceManager.IsDefaultInfo(info)) {
                m_info = info.Clone();
                m_changed = true;
                m_toAdd = true;
                m_info.Name =
                    $"{CommunityDownloadSourceManager.GetDisplayName(info)} ({LanguageControl.Get("ContentWidgets", "ViewGameLogDialog", "1")})";
            }
            else {
                m_info = info;
                m_changed = false;
                m_toAdd = false;
            }
            if (string.IsNullOrEmpty(m_info.Name)) {
                m_info.Name =
                    $"{LanguageControl.Get("EditDownloadSourceInfoDialog", "NewDownloadSource")} ({LanguageControl.Get("ContentWidgets", "ViewGameLogDialog", "1")})";
            }
            XElement node = ContentManager.Get<XElement>("Dialogs/EditDownloadSourceInfoDialog");
            LoadContents(this, node);
            m_nameTextBox = Children.Find<TextBoxWidget>("EditDownloadSourceInfoDialog.Name");
            m_hostTextBox = Children.Find<TextBoxWidget>("EditDownloadSourceInfoDialog.Host");
            m_okButton = Children.Find<ButtonWidget>("EditDownloadSourceInfoDialog.Ok");
            m_removeButton = Children.Find<ButtonWidget>("EditDownloadSourceInfoDialog.Remove");
            m_cancelButton = Children.Find<ButtonWidget>("EditDownloadSourceInfoDialog.Cancel");
            m_nameTextBox.Text = m_info.Name;
            m_hostTextBox.Text = m_info.Host;
        }

        public override void Update() {
            if (m_nameTextBox.Text != m_info.Name) {
                m_changed = true;
            }
            if (m_hostTextBox.Text != m_info.Host) {
                m_changed = true;
            }
            m_removeButton.IsEnabled = !m_changed;
            if (m_okButton.IsClicked) {
                if (m_changed) {
                    string name = m_nameTextBox.Text.Trim();
                    // 空名与重名一并拒绝：名称是 FindByName 的身份键
                    if (name.Length == 0
                        || CommunityDownloadSourceManager.NameExists(name, m_toAdd ? null : m_info)) {
                        DialogsManager.ShowDialog(
                            null,
                            new MessageDialog(
                                LanguageControl.Error,
                                LanguageControl.Get("EditDownloadSourceInfoDialog", "DuplicateName"),
                                LanguageControl.Ok,
                                null,
                                null
                            )
                        );
                        return;
                    }
                    m_info.Name = name;
                    m_info.Host = m_hostTextBox.Text.Trim();
                    if (m_toAdd) {
                        m_parentDialog.AddSource(m_info);
                    }
                    else {
                        m_parentDialog.UpdateList();
                        m_parentDialog.m_listPanel.SelectedItem = m_info;
                    }
                }
                DialogsManager.HideDialog(this);
            }
            if (m_removeButton.IsClicked
                && !m_changed) {
                m_parentDialog.RemoveSource(m_info);
                DialogsManager.HideDialog(this);
            }
            if (Input.Back
                || Input.Cancel
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value))
                || m_cancelButton.IsClicked) {
                DialogsManager.HideDialog(this);
            }
        }
    }
}