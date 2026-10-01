using System.Xml.Linq;

namespace Game {
    public enum ImportOverwriteAction {
        Overwrite,
        Rename,
        Cancel
    }

    // 本地已存在同名资源时的选择对话框。MessageDialog 只支持两按钮，故自定义三按钮：
    // 覆盖（强制替换，走备份回滚）、重命名（按游戏原生导入、保留旧项）、取消。
    // 模组类型无法"重命名"（同名 scmod 导入会抛 FileAlreadyExists），构造时 canRename=false 只显示覆盖/取消。
    public sealed class CommunityImportOverwriteDialog : Dialog {
        readonly ButtonWidget m_overwriteButton;
        readonly ButtonWidget m_renameButton;
        readonly ButtonWidget m_cancelButton;
        readonly Action<ImportOverwriteAction> m_handler;

        public CommunityImportOverwriteDialog(string message, bool canRename, Action<ImportOverwriteAction> handler) {
            m_handler = handler;
            XElement node = ContentManager.Get<XElement>("Dialogs/CommunityImportOverwriteDialog");
            LoadContents(this, node);
            Children.Find<LabelWidget>("ImportOverwriteDialog.Message").Text = message ?? string.Empty;
            m_overwriteButton = Children.Find<ButtonWidget>("ImportOverwriteDialog.OverwriteButton");
            m_renameButton = Children.Find<ButtonWidget>("ImportOverwriteDialog.RenameButton");
            m_cancelButton = Children.Find<ButtonWidget>("ImportOverwriteDialog.CancelButton");
            m_cancelButton.Text = LanguageControl.Cancel;
            m_renameButton.IsVisible = canRename;
        }

        public override void Update() {
            if (Input.Back
                || Input.Cancel
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value))
                || m_cancelButton.IsClicked) {
                Complete(ImportOverwriteAction.Cancel);
            }
            else if (m_overwriteButton.IsClicked) {
                Complete(ImportOverwriteAction.Overwrite);
            }
            else if (m_renameButton.IsClicked) {
                Complete(ImportOverwriteAction.Rename);
            }
        }

        void Complete(ImportOverwriteAction action) {
            DialogsManager.HideDialog(this);
            m_handler?.Invoke(action);
        }
    }
}