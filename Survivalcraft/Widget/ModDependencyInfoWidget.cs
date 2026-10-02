using System.Xml.Linq;
using Engine;
using Engine.Graphics;

namespace Game {
    /// <summary>
    ///     模组详情对话框的单条依赖块：状态头行（缺失/被禁用带靠右动作按钮）+ 版本行 + 可选链接行
    /// </summary>
    public sealed class ModDependencyInfoWidget : FixedSizePanelWidget {
        internal const string fName = ModDetailsDialog.fName;

        readonly LabelWidget m_headerLabel;
        readonly BevelledButtonWidget m_actionButton;
        readonly LabelWidget m_versionLabel;
        readonly LinkWidget m_linkWidget;

        public ModDependencyInfoWidget() {
            XElement node = ContentManager.Get<XElement>("Widgets/ModDependencyInfoWidget");
            LoadContents(this, node);
            m_headerLabel = Children.Find<LabelWidget>("ModDependencyInfoWidget.Header");
            m_actionButton = Children.Find<BevelledButtonWidget>("ModDependencyInfoWidget.ActionButton");
            m_versionLabel = Children.Find<LabelWidget>("ModDependencyInfoWidget.Version");
            m_linkWidget = Children.Find<LinkWidget>("ModDependencyInfoWidget.Link");
        }

        internal ModDependencyInfo Dependency { get; private set; }

        // 动作语义：true=缺失行「查找」（社区查这一条），false=被禁用行「查看」（下钻该模组详情）
        internal bool IsSearchAction { get; private set; }

        public bool IsActionClicked => m_actionButton.IsVisible && m_actionButton.IsClicked;

        internal void Bind(ModDependencyInfo dependency, ModDependencyAvailability availability) {
            Dependency = dependency;
            IsSearchAction = availability == ModDependencyAvailability.Missing;
            string displayName = string.IsNullOrWhiteSpace(dependency.DisplayName) ? dependency.PackageName : dependency.DisplayName;
            string tag = availability switch {
                ModDependencyAvailability.Loaded => null,
                ModDependencyAvailability.PendingRestart => LanguageControl.Get(fName, "StatePendingRestart"),
                ModDependencyAvailability.Incompatible => LanguageControl.Get(fName, "StateIncompatible"),
                ModDependencyAvailability.Disabled => LanguageControl.Get(fName, "StateDisabled"),
                _ => LanguageControl.Get(fName, "StateMissing")
            };
            bool available = availability is ModDependencyAvailability.Loaded or ModDependencyAvailability.PendingRestart;
            m_headerLabel.Text = tag == null ? $"- {displayName}" : $"- [{tag}] {displayName}";
            m_headerLabel.Color = available ? Color.White : Color.Red;
            bool hasAction = availability is ModDependencyAvailability.Missing or ModDependencyAvailability.Disabled;
            m_actionButton.IsVisible = hasAction;
            // 预留按钮宽度，防止长名称换行钻到按钮底下；保留 XML 里的垂直边距
            m_headerLabel.Margin4 = hasAction ? new Vector4(0f, 4f, 112f, 4f) : new Vector4(0f, 4f, 0f, 4f);
            if (hasAction) {
                m_actionButton.Text = IsSearchAction
                    ? LanguageControl.Get(fName, "SearchDependency")
                    : LanguageControl.Get("ModsManageContentScreen", "30");
            }
            // 版本行：声明中要求的版本范围；无限制显示「任何」
            string versionText = string.IsNullOrWhiteSpace(dependency.VersionRangeString)
                ? LanguageControl.Get(fName, "AnyVersion")
                : dependency.VersionRangeString;
            m_versionLabel.Text = $"    {LanguageControl.Get(fName, "2")}{versionText}";
            m_linkWidget.IsVisible = !string.IsNullOrWhiteSpace(dependency.Link);
            if (m_linkWidget.IsVisible) {
                m_linkWidget.Text = $"    {LanguageControl.Get(fName, "7")}{dependency.Link}";
                m_linkWidget.Url = dependency.Link;
            }
        }
    }
}
