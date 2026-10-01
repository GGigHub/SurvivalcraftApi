using System.Xml.Linq;

namespace Game {
    sealed class CommunityFiltersDialog : Dialog {
        internal struct State {
            public ExternalContentType FileType;
            public CommunityOrderType OrderType;
            public bool OnlyCurrentVersion;
            public CommunitySection Section;
            public bool IgnoreFileType;
            public bool IgnoreOrder;
            public bool IgnoreVersion;
            public bool IgnoreSection;
        }

        readonly Action<State> m_submit;
        readonly Action<State> m_saveDefault;
        readonly BevelledButtonWidget m_fileTypeButton;
        readonly BevelledButtonWidget m_orderButton;
        readonly BevelledButtonWidget m_versionButton;
        readonly BevelledButtonWidget m_sectionButton;
        readonly CheckboxWidget m_ignoreFileTypeCheckbox;
        readonly CheckboxWidget m_ignoreOrderCheckbox;
        readonly CheckboxWidget m_ignoreVersionCheckbox;
        readonly CheckboxWidget m_ignoreSectionCheckbox;
        readonly BevelledButtonWidget m_saveDefaultButton;
        readonly BevelledButtonWidget m_confirmButton;
        readonly BevelledButtonWidget m_cancelButton;
        ExternalContentType m_fileType;
        CommunityOrderType m_orderType;
        bool m_onlyCurrentVersion;
        CommunitySection m_section;
        bool m_defaultsSaved;

        public CommunityFiltersDialog(State state, Action<State> saveDefault, Action<State> submit) {
            m_fileType = state.FileType;
            m_orderType = state.OrderType;
            m_onlyCurrentVersion = state.OnlyCurrentVersion;
            m_section = state.Section;
            m_saveDefault = saveDefault;
            m_submit = submit;
            XElement node = ContentManager.Get<XElement>("Dialogs/CommunityFiltersDialog");
            LoadContents(this, node);
            m_fileTypeButton = Children.Find<BevelledButtonWidget>("FiltersDialog.FileTypeButton");
            m_orderButton = Children.Find<BevelledButtonWidget>("FiltersDialog.OrderButton");
            m_versionButton = Children.Find<BevelledButtonWidget>("FiltersDialog.VersionButton");
            m_sectionButton = Children.Find<BevelledButtonWidget>("FiltersDialog.SectionButton");
            m_ignoreFileTypeCheckbox = Children.Find<CheckboxWidget>("FiltersDialog.IgnoreFileType");
            m_ignoreOrderCheckbox = Children.Find<CheckboxWidget>("FiltersDialog.IgnoreOrder");
            m_ignoreVersionCheckbox = Children.Find<CheckboxWidget>("FiltersDialog.IgnoreVersion");
            m_ignoreSectionCheckbox = Children.Find<CheckboxWidget>("FiltersDialog.IgnoreSection");
            m_saveDefaultButton = Children.Find<BevelledButtonWidget>("FiltersDialog.SaveDefaultButton");
            m_confirmButton = Children.Find<BevelledButtonWidget>("FiltersDialog.ConfirmButton");
            m_cancelButton = Children.Find<BevelledButtonWidget>("FiltersDialog.CancelButton");
            m_confirmButton.Text = LanguageControl.Ok;
            m_cancelButton.Text = LanguageControl.Cancel;
            m_ignoreFileTypeCheckbox.IsChecked = state.IgnoreFileType;
            m_ignoreOrderCheckbox.IsChecked = state.IgnoreOrder;
            m_ignoreVersionCheckbox.IsChecked = state.IgnoreVersion;
            m_ignoreSectionCheckbox.IsChecked = state.IgnoreSection;
            UpdateOptionButtons();
        }

        public override void Update() {
            // Back/Cancel 与下方 Ok 同理：子级 ListSelectionDialog 存活期间全局输入先被列表消费，
            // 本对话框同帧读到会一次按键连关两层（未提交的筛选修改丢失）。
            if (((Input.Back || Input.Cancel) && !HasLiveListSelectionDialog())
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value))
                || m_cancelButton.IsClicked) {
                DialogsManager.HideDialog(this);
                return;
            }
            if (m_fileTypeButton.IsClicked) {
                DialogsManager.ShowDialog(
                    null,
                    new ListSelectionDialog(
                        LanguageControl.Get("FiltersDialog", "SelectFileType"),
                        CommunityFileTypes.FilterableValues(),
                        60f,
                        item => LanguageControl.GetEnumDisplayName((ExternalContentType)item),
                        item => {
                            m_fileType = (ExternalContentType)item;
                            MarkDefaultsChanged();
                        }
                    )
                );
                return;
            }
            if (m_orderButton.IsClicked) {
                CommunityOrderType[] values = [
                    CommunityOrderType.Latest, CommunityOrderType.Oldest, CommunityOrderType.Updated, CommunityOrderType.Hot
                ];
                DialogsManager.ShowDialog(
                    null,
                    new ListSelectionDialog(
                        LanguageControl.Get("FiltersDialog", "SelectSortOrder"),
                        values,
                        60f,
                        item => LanguageControl.GetEnumDisplayName((CommunityOrderType)item),
                        item => {
                            m_orderType = (CommunityOrderType)item;
                            MarkDefaultsChanged();
                        }
                    )
                );
                return;
            }
            if (m_versionButton.IsClicked) {
                bool[] values = [true, false];
                DialogsManager.ShowDialog(
                    null,
                    new ListSelectionDialog(
                        LanguageControl.Get("FiltersDialog", "SelectVersionRange"),
                        values,
                        60f,
                        item => CommunityScreen.GetVersionFilterName((bool)item),
                        item => {
                            m_onlyCurrentVersion = (bool)item;
                            MarkDefaultsChanged();
                        }
                    )
                );
                return;
            }
            if (m_sectionButton.IsClicked) {
                CommunitySection[] values = [CommunitySection.All, CommunitySection.Trending, CommunitySection.NewActivity];
                DialogsManager.ShowDialog(
                    null,
                    new ListSelectionDialog(
                        LanguageControl.Get("FiltersDialog", "SelectSection"),
                        values,
                        60f,
                        item => LanguageControl.GetEnumDisplayName((CommunitySection)item),
                        item => {
                            m_section = (CommunitySection)item;
                            MarkDefaultsChanged();
                        }
                    )
                );
                return;
            }
            if (m_ignoreFileTypeCheckbox.IsClicked
                || m_ignoreOrderCheckbox.IsClicked
                || m_ignoreVersionCheckbox.IsClicked
                || m_ignoreSectionCheckbox.IsClicked) {
                MarkDefaultsChanged();
                return;
            }
            if (m_saveDefaultButton.IsClicked) {
                m_saveDefault?.Invoke(GetState());
                m_defaultsSaved = true;
                UpdateOptionButtons();
                return;
            }

            // Input.Ok 为边缘触发但全层级共享（引擎无模态输入隔离）：子级 ListSelectionDialog 存活期间，
            // 同一 A 键按下帧会被本对话框读到并误提交旧筛选值，故存活期间跳过该分支。
            if ((Input.Ok && !HasLiveListSelectionDialog())
                || m_confirmButton.IsClicked) {
                DialogsManager.HideDialog(this);
                m_submit?.Invoke(GetState());
            }
        }

        // 存活判据不能只查 Dialogs：切屏后 ListSelectionDialog 实例仍留在 Dialogs（挂在死屏上），
        // 会让新屏上的 Ok/Back 被永久抑制，须「在 Dialogs 中且父链可达 RootWidget」。
        static bool HasLiveListSelectionDialog() {
            foreach (Dialog dialog in DialogsManager.Dialogs) {
                if (dialog is ListSelectionDialog
                    && IsWidgetInRootHierarchy(dialog)) {
                    return true;
                }
            }
            return false;
        }

        static bool IsWidgetInRootHierarchy(Widget widget) {
            while (widget != null) {
                if (widget == ScreensManager.RootWidget) {
                    return true;
                }
                widget = widget.ParentWidget;
            }
            return false;
        }

        void MarkDefaultsChanged() {
            m_defaultsSaved = false;
            UpdateOptionButtons();
        }

        void UpdateOptionButtons() {
            m_fileTypeButton.Text = LanguageControl.GetEnumDisplayName(m_fileType);
            m_orderButton.Text = LanguageControl.GetEnumDisplayName(m_orderType);
            m_versionButton.Text = CommunityScreen.GetVersionFilterName(m_onlyCurrentVersion);
            m_sectionButton.Text = LanguageControl.GetEnumDisplayName(m_section);
            m_saveDefaultButton.Text = m_defaultsSaved
                ? LanguageControl.Get("FiltersDialog", "Saved")
                : LanguageControl.Get("FiltersDialog", "SaveDefault");
        }

        State GetState() => new() {
            FileType = m_fileType,
            OrderType = m_orderType,
            OnlyCurrentVersion = m_onlyCurrentVersion,
            Section = m_section,
            IgnoreFileType = m_ignoreFileTypeCheckbox.IsChecked,
            IgnoreOrder = m_ignoreOrderCheckbox.IsChecked,
            IgnoreVersion = m_ignoreVersionCheckbox.IsChecked,
            IgnoreSection = m_ignoreSectionCheckbox.IsChecked
        };
    }
}