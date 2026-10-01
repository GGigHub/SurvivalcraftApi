using Engine;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Game {
    public class SelectCommunityServerDialog: Dialog {
        public ListPanelWidget m_listPanel;
        public ButtonWidget m_openButton;
        public ButtonWidget m_addButton;
        public ButtonWidget m_editButton;

        /// <summary>
        /// true = 仅选择不导航（More 屏设置语义）：选择只改当前服务器并关框，留在原屏；
        /// 列表仅中文社区条目。false（默认）= 既有语义：选择即切到对应社区屏，列表含全部条目。
        /// </summary>
        public readonly bool m_selectOnly;

        public SelectCommunityServerDialog(bool selectOnly = false) {
            m_selectOnly = selectOnly;
            XElement node = ContentManager.Get<XElement>("Dialogs/SelectCommunityServerDialog");
            LoadContents(this, node);
            m_listPanel = Children.Find<ListPanelWidget>("SelectCommunityServerDialog.ListPanel");
            m_openButton = Children.Find<ButtonWidget>("SelectCommunityServerDialog.Open");
            m_addButton = Children.Find<ButtonWidget>("SelectCommunityServerDialog.Add");
            m_editButton = Children.Find<ButtonWidget>("SelectCommunityServerDialog.Edit");
            if (selectOnly) {
                m_openButton.Text = LanguageControl.GetContentWidgets("SelectCommunityServerDialog", "5");
            }
            m_listPanel.ItemWidgetFactory = o => {
                if (o is not CommunityServerManager.Info info) {
                    return null;
                }
                LabelWidget labelWidget = new () {
                    HorizontalAlignment = WidgetAlignment.Center,
                    VerticalAlignment = WidgetAlignment.Center
                };
                labelWidget.Text = CommunityServerManager.GetDisplayName(info);
                return labelWidget;
            };
            m_listPanel.ItemClicked = o => {
                if (o == m_listPanel.SelectedItem
                    && o is CommunityServerManager.Info info) {
                    if (selectOnly) {
                        SetCurrentServer(info);
                        DialogsManager.HideDialog(this);
                    }
                    else {
                        OpenServerScreen(info);
                        DialogsManager.HideDialog(this);
                    }
                }
            };
            UpdateServerList();
            // 仅选择模式预置当前服务器，直达目标；导航模式维持无预选的现状
            if (selectOnly) {
                m_listPanel.SelectedItem = CommunityServerManager.CurrentChineseInfo;
            }
        }

        public void UpdateServerList() {
            m_listPanel.ClearItems();
            m_listPanel.AddItems(GetListInfos());
            m_listPanel.ScrollPosition = 0f;
            // 编辑对话框原地改 URL 不换 Info 引用（ChangeChineseInfo 的引用比较拦不住）：
            // 列表落定后按当前 ApiBaseUrl 重校一次会话激活，同值内部 no-op。
            AuthSessionManager.Instance.OnServerSwitched();
        }

        private IEnumerable<CommunityServerManager.Info> GetListInfos() {
            if (!m_selectOnly) {
                return CommunityServerManager.GetAllInfos();
            }
            // More 屏是中文社区栈的设置页：账户行与「当前服务器」行都只消费 CurrentChineseInfo，
            // 列 Original 条目选中后无任何可见反馈，故不列
            List<CommunityServerManager.Info> infos = [];
            if (SettingsManager.CommunityContentMode != CommunityContentMode.Disabled) {
                infos.Add(CommunityServerManager.DefaultChineseInfo);
            }
            infos.AddRange(CommunityServerManager.UserInfos.Where(info => info.Type == CommunityServerManager.Type.Chinese));
            return infos;
        }

        public void AddServer(CommunityServerManager.Info info) {
            CommunityServerManager.UserInfos.Add(info);
            m_listPanel.AddItem(info);
            m_listPanel.SelectedItem = info;
        }

        public void RemoveServer(CommunityServerManager.Info info) {
            CommunityServerManager.RemoveUserInfo(info);
            m_listPanel.RemoveItem(info);
        }

        /// <summary>仅选择模式的确认动作：改当前服务器，不导航。缓存清理与会话切换由 Change*Info 承担。</summary>
        public void SetCurrentServer(CommunityServerManager.Info info) {
            if (info == null) {
                return;
            }
            switch (info.Type) {
                case CommunityServerManager.Type.Original:
                    CommunityServerManager.ChangeOriginalInfo(info);
                    break;
                case CommunityServerManager.Type.Chinese:
                    CommunityServerManager.ChangeChineseInfo(info);
                    break;
            }
        }

        public void OpenServerScreen(CommunityServerManager.Info info) {
            if (info == null) {
                return;
            }
            switch (info.Type) {
                case CommunityServerManager.Type.Original:
                    CommunityServerManager.ChangeOriginalInfo(info);
                    ScreensManager.SwitchScreen("OriginalCommunityContent");
                    break;
                case CommunityServerManager.Type.Chinese:
                    CommunityServerManager.ChangeChineseInfo(info);
                    ScreensManager.SwitchScreen("CommunityContent");
                    break;
            }
        }

        public override void Update() {
            if (m_listPanel.SelectedItem is CommunityServerManager.Info selected) {
                if (m_openButton.IsClicked) {
                    if (m_selectOnly) {
                        SetCurrentServer(selected);
                        DialogsManager.HideDialog(this);
                    }
                    else {
                        OpenServerScreen(selected);
                        DialogsManager.HideDialog(this);
                    }
                }
                if (m_editButton.IsClicked) {
                    DialogsManager.ShowDialog(null, new EditCommunityServerInfoDialog(this, selected));
                }
            }
            if (m_addButton.IsClicked) {
                DialogsManager.ShowDialog(null, new EditCommunityServerInfoDialog(this));
            }
            if (Input.Back
                || Input.Cancel
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value))) {
                DialogsManager.HideDialog(this);
            }
        }
    }
}
