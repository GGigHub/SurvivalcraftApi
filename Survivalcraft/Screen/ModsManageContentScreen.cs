using System.Xml.Linq;
using Engine;
using Game;

public class ModsManageContentScreen : Screen {
    public static string fName = "ModsManageContentScreen";

    public ListPanelWidget m_modsContentList;
    public ButtonWidget m_viewDetailButton;
    public ButtonWidget m_triggerEnableButton;
    public ButtonWidget m_moveUpButton;
    public ButtonWidget m_moveDownButton;
    public ButtonWidget m_openHomepageButton;

    public int m_fixedModsCount;
    public bool m_needRestart;

    public static bool IsFixedMod(ModEntity entity) => entity.IsSystemMod
        || entity is SurvivalCraftModEntity
        || entity is FastDebugModEntity;

    public static int GetVisibleListIndex(ModEntity entity) {
        int visibleIndex = 0;
        foreach (ModEntity modEntity in ModsManager.ModListAll) {
            if (modEntity.IsSystemMod) {
                continue;
            }
            if (ReferenceEquals(modEntity, entity)) {
                return visibleIndex;
            }
            visibleIndex++;
        }
        return -1;
    }

    public static int FindPreviousVisibleIndex(int index) {
        for (int i = index - 1; i >= 0; i--) {
            if (!ModsManager.ModListAll[i].IsSystemMod) {
                return i;
            }
        }
        return -1;
    }

    public static int FindNextVisibleIndex(int index) {
        for (int i = index + 1; i < ModsManager.ModListAll.Count; i++) {
            if (!ModsManager.ModListAll[i].IsSystemMod) {
                return i;
            }
        }
        return -1;
    }

    public static bool IsOldApiVersionMod(ModEntity modEntity, out string description) {
        ModInfo modInfo = modEntity.modInfo;
        if (modInfo == null || string.IsNullOrEmpty(modInfo.ApiVersion)) {
            description = string.Format(LanguageControl.Get(fName, "68"), LanguageControl.Unknown);
            return true;
        }
        if (modInfo.ApiVersion.StartsWith("1.4")
            || modInfo.ApiVersion.StartsWith("1.5")
            || modInfo.ApiVersion.StartsWith("1.6")
            || modInfo.ApiVersion.StartsWith("1.7")) {
            description = string.Format(LanguageControl.Get(fName, "68"), modInfo.ApiVersion);
            return true;
        }
        if (modInfo.ApiVersionRange != null) {
            if (!modInfo.ApiVersionRange.Satisfies(ModsManager.APINuGetVersion)) {
                description = string.Format(LanguageControl.Get(fName, "76"), modInfo.ApiVersion);
                return true;
            }
            if (!modInfo.ApiVersionRange.HasUpperBound
                && modInfo.ApiVersionRange.MinVersion != null
                && modInfo.ApiVersionRange.MinVersion.Major == 1
                && modInfo.ApiVersionRange.MinVersion.Minor <= 7) {
                description = string.Format(LanguageControl.Get(fName, "68"), modInfo.ApiVersion);
                return true;
            }
        }
        description = modEntity.IsDisabled ? $"{LanguageControl.Get("ModDetailsDialog", "8")}{Storage.GetFileName(modEntity.ModFilePath)}" : GetFirstLine(modInfo.Description);
        return false;
    }

    public static string GetFirstLine(string input) {
        if (string.IsNullOrEmpty(input)) {
            return input;
        }
        ReadOnlySpan<char> span = input.AsSpan();
        int index = span.IndexOf('\n');
        return index == -1 ? input : span.Slice(0, index).TrimEnd('\r').ToString();
    }

    public ModsManageContentScreen() {
        m_fixedModsCount = ModsManager.ModListAll.TakeWhile(IsFixedMod).Count();
        XElement node = ContentManager.Get<XElement>("Screens/ModsManageContentScreen");
        LoadContents(this, node);
        m_modsContentList = Children.Find<ListPanelWidget>("ModsContentList");
        Children.Find<LabelWidget>("TopBar.Label").Text = LanguageControl.Get(fName, "1");
        m_viewDetailButton = Children.Find<ButtonWidget>("ViewDetailButton");
        m_triggerEnableButton = Children.Find<BevelledButtonWidget>("TriggerEnableButton");
        m_moveUpButton = Children.Find<BevelledButtonWidget>("MoveUpButton");
        m_moveDownButton = Children.Find<BevelledButtonWidget>("MoveDownButton");
        m_openHomepageButton = Children.Find<BevelledButtonWidget>("OpenHomepageButton");
        m_viewDetailButton.Text = LanguageControl.Get(fName, "80");
        m_triggerEnableButton.Text = LanguageControl.Get(fName, "18");
        m_openHomepageButton.Text = LanguageControl.Get(fName, "77");
        m_modsContentList.ItemWidgetFactory = item => {
            if (item is not ModEntity entity) {
                return null;
            }
            GetTriggerAndTitle(entity, out string title, out Color titleColor);
            ModsManageContentItemWidget result = new() { Title = title, IsDisabled = entity.IsDisabled, TitleColor = titleColor };
            if (IsOldApiVersionMod(entity, out string description)) {
                result.TitleColor = Color.Red;
                if (string.IsNullOrEmpty(entity.ModFilePath)) {
                    result.IsInformationVisible = false;
                }
                else {
                    result.Information = Storage.GetFileName(entity.ModFilePath);
                }
            }
            else {
                result.Information = string.Format(
                    LanguageControl.Get(fName, "79"),
                    entity.modInfo!.Version,
                    entity.modInfo.ApiVersion,
                    entity.modInfo.Author,
                    DataSizeFormatter.Format(entity.Size)
                );
            }
            if (entity.Icon != null) {
                result.Icon = entity.Icon;
            }
            result.Description = description;
            return result;
        };
        m_modsContentList.ItemClicked += item => {
            if (item is not ModEntity entity) {
                return;
            }
            if (ReferenceEquals(entity, m_modsContentList.SelectedItem)) {
                DialogsManager.ShowDialog(null, new ModDetailsDialog(this, entity));
            }
            else {
                m_viewDetailButton.IsEnabled = true;
                m_triggerEnableButton.IsEnabled = !entity.IsSystemMod
                    && (entity.IsDisabled
                        ? entity.DisableReason == ModDisableReason.Manually
                        : entity.modInfo.PackageName is not "survivalcraft" and not "fastdebug");
                m_triggerEnableButton.Text = LanguageControl.Get(fName, GetTrigger(entity) ? "19" : "18");
                m_openHomepageButton.IsEnabled = true;
                UpdateMoveUpAndDownEnable(ModsManager.ModListAll.IndexOf(entity));
            }
        };
        m_modsContentList.SelectionChanged += () => {
            if (m_modsContentList.SelectedItem is not ModEntity) {
                m_viewDetailButton.IsEnabled = false;
                m_triggerEnableButton.IsEnabled = false;
                m_triggerEnableButton.Text = LanguageControl.Get(fName, "18");
                m_moveUpButton.IsEnabled = false;
                m_moveDownButton.IsEnabled = false;
                m_openHomepageButton.IsEnabled = false;
            }
        };
    }

    public override void Enter(object[] parameters) {
        m_modsContentList.AddItems(ModsManager.ModListAll.Where(entity => !entity.IsSystemMod).ToList());
    }

    public override void Leave() {
        m_modsContentList.ClearItems();
        m_viewDetailButton.IsEnabled = false;
        m_triggerEnableButton.IsEnabled = false;
        m_openHomepageButton.IsEnabled = false;
    }

    public override void Update() {
        if (m_modsContentList.SelectedItem is ModEntity entity) {
            if (m_viewDetailButton.IsClicked) {
                DialogsManager.ShowDialog(null, new ModDetailsDialog(this, entity));
            }
            if (m_triggerEnableButton.IsClicked) {
                TriggerEnable(entity);
            }
            if (m_moveUpButton.IsClicked) {
                MoveUp(entity);
            }
            if (m_moveDownButton.IsClicked) {
                MoveDown(entity);
            }
            if (m_openHomepageButton.IsClicked) {
                if (string.IsNullOrEmpty(entity.modInfo?.Link)) {
                    DialogsManager.ShowDialog(
                        null,
                        new MessageDialog(LanguageControl.Error, LanguageControl.Get(fName, 78), LanguageControl.Ok, null, null)
                    );
                }
                else {
                    WebBrowserManager.LaunchBrowser(entity.modInfo.Link);
                }
            }
        }
        if (Input.Back
            || Input.Cancel
            || Children.Find<ButtonWidget>("TopBar.Back").IsClicked) {
            if (m_needRestart) {
                List<string> array = [];
                foreach (ModEntity entity1 in ModsManager.ModListAll) {
                    if (!string.IsNullOrEmpty(entity1.LoadAfter)) {
                        array.Add(entity1.modInfo.PackageName);
                        array.Add(entity1.LoadAfter);
                    }
                }
                SettingsManager.ModLoadAfters = string.Join(';', array);
                DialogsManager.ShowDialog(
                    null,
                    new MessageDialog(
                        LanguageControl.Get(fName, 4),
                        LanguageControl.Get(fName, 38),
                        LanguageControl.Get(fName, 39),
                        LanguageControl.Get(fName, 31),
                        delegate (MessageDialogButton result) {
                            SettingsManager.SaveSettings();
                            if (result == MessageDialogButton.Button1) {
                                Window.Restart();
                            }
                            if (result == MessageDialogButton.Button2) {
                                ScreensManager.SwitchScreen("Content");
                            }
                        }
                    )
                );
            }
            else {
                ScreensManager.SwitchScreen("Content");
            }
        }
    }

    public void TriggerEnable(ModEntity entity) {
        if (entity.IsSystemMod) {
            return;
        }
        if (entity.IsDisabled) {
            if (entity.DisableReason == ModDisableReason.Manually) {
                if (ModsManager.DisabledMods.TryGetValue(entity.modInfo!.PackageName, out HashSet<string> versions)) {
                    if (!versions.Remove(entity.modInfo.Version)) {
                        versions.Add(entity.modInfo.Version);
                    }
                }
                else {
                    ModsManager.DisabledMods.Add(entity.modInfo.PackageName, [entity.modInfo.Version]);
                }
                m_needRestart = true;
            }
        }
        else if (entity.modInfo!.PackageName is not "survivalcraft" and not "fastdebug") {
            if (ModsManager.DisabledMods.TryGetValue(entity.modInfo.PackageName, out HashSet<string> versions)) {
                if (!versions.Remove(entity.modInfo.Version)) {
                    versions.Add(entity.modInfo.Version);
                }
            }
            else {
                ModsManager.DisabledMods.Add(entity.modInfo.PackageName, [entity.modInfo.Version]);
            }
            m_needRestart = true;
        }
        if (m_modsContentList.m_widgetsByIndex[m_modsContentList.SelectedIndex!.Value] is ModsManageContentItemWidget itemWidget) {
            m_triggerEnableButton.Text = LanguageControl.Get(fName, GetTriggerAndTitle(entity, out string title, out Color titleColor) ? "19" : "18");
            itemWidget.Title = title;
            itemWidget.TitleColor = titleColor;
        }
    }

    /// <returns>true: mod能被启用，false：mod能被禁用</returns>
    public static bool GetTriggerAndTitle(ModEntity entity, out string title, out Color titleColor) {
        title = entity.modInfo?.Name ?? Storage.GetFileName(entity.ModFilePath);
        titleColor = Color.White;
        if (entity.IsDisabled) {
            if (entity.DisableReason == ModDisableReason.Manually) {
                if (ModsManager.DisabledMods.TryGetValue(entity.modInfo!.PackageName, out HashSet<string> versions1)
                    && versions1.Contains(entity.modInfo.Version)) {
                    title = $"[{LanguageControl.Get(fName, "22")}] {title}";
                    titleColor = Color.Red;
                    return true;
                }
                title = $"[{LanguageControl.Get(fName, "23")} {LanguageControl.Get(fName, "81")}] {title}";
                titleColor = Color.Yellow;
                return false;
            }
            title = $"[{LanguageControl.Get(fName, "22")}] {title}";
            titleColor = Color.Red;
            return true;
        }
        if (entity.modInfo != null
            && ModsManager.DisabledMods.TryGetValue(entity.modInfo!.PackageName, out HashSet<string> versions2)
            && versions2.Contains(entity.modInfo.Version)) {
            title = $"[{LanguageControl.Get(fName, "22")} {LanguageControl.Get(fName, "81")}] {title}";
            titleColor = Color.Yellow;
            return true;
        }
        return false;
    }

    /// <returns>true: mod能被启用，false：mod能被禁用</returns>
    public static bool GetTrigger(ModEntity entity) {
        if (entity.IsDisabled) {
            if (entity.DisableReason == ModDisableReason.Manually) {
                return ModsManager.DisabledMods.TryGetValue(entity.modInfo!.PackageName, out HashSet<string> versions1)
                    && versions1.Contains(entity.modInfo.Version);
            }
            return true;
        }
        return entity.modInfo != null
            && ModsManager.DisabledMods.TryGetValue(entity.modInfo!.PackageName, out HashSet<string> versions2)
            && versions2.Contains(entity.modInfo.Version);
    }

    public void MoveUp(ModEntity entity) {
        int index = ModsManager.ModListAll.IndexOf(entity);
        if (index < 0 || IsFixedMod(entity)) {
            return;
        }
        int upIndex = FindPreviousVisibleIndex(index);
        if (upIndex < 0 || IsFixedMod(ModsManager.ModListAll[upIndex])) {
            return;
        }
        ModEntity upEntity = ModsManager.ModListAll[upIndex];
        if (entity.modInfo.DependencyRanges.ContainsKey(upEntity.modInfo.PackageName)) {
            return;
        }
        int visibleIndex = GetVisibleListIndex(entity);
        int previousIndex = FindPreviousVisibleIndex(upIndex);
        int nextIndex = FindNextVisibleIndex(index);
        m_needRestart = true;
        ModsManager.ModListAll[upIndex] = entity;
        ModsManager.ModListAll[index] = upEntity;
        entity.LoadAfter = previousIndex >= 0 ? ModsManager.ModListAll[previousIndex].modInfo.PackageName : null;
        upEntity.LoadAfter = entity.modInfo.PackageName;
        if (nextIndex >= 0) {
            ModsManager.ModListAll[nextIndex].LoadAfter = upEntity.modInfo.PackageName;
        }
        if (visibleIndex > 0) {
            m_modsContentList.SwapItemsAt(visibleIndex - 1, visibleIndex);
        }
        UpdateMoveUpAndDownEnable(upIndex);
    }

    public void MoveDown(ModEntity entity) {
        int index = ModsManager.ModListAll.IndexOf(entity);
        if (index < 0 || IsFixedMod(entity)) {
            return;
        }
        int downIndex = FindNextVisibleIndex(index);
        if (downIndex < 0 || IsFixedMod(ModsManager.ModListAll[downIndex])) {
            return;
        }
        ModEntity downEntity = ModsManager.ModListAll[downIndex];
        if (entity.modInfo.DependencyRanges.ContainsKey(downEntity.modInfo.PackageName)) {
            return;
        }
        int visibleIndex = GetVisibleListIndex(entity);
        int previousIndex = FindPreviousVisibleIndex(index);
        int nextIndex = FindNextVisibleIndex(downIndex);
        m_needRestart = true;
        ModsManager.ModListAll[downIndex] = entity;
        ModsManager.ModListAll[index] = downEntity;
        entity.LoadAfter = downEntity.modInfo.PackageName;
        downEntity.LoadAfter = previousIndex >= 0 ? ModsManager.ModListAll[previousIndex].modInfo.PackageName : null;
        if (nextIndex >= 0) {
            ModsManager.ModListAll[nextIndex].LoadAfter = entity.modInfo.PackageName;
        }
        if (visibleIndex >= 0) {
            m_modsContentList.SwapItemsAt(visibleIndex, visibleIndex + 1);
        }
        UpdateMoveUpAndDownEnable(downIndex);
    }
    public void UpdateMoveUpAndDownEnable(int index) {
        if (index < 0 || index >= ModsManager.ModListAll.Count || IsFixedMod(ModsManager.ModListAll[index])) {
            m_moveUpButton.IsEnabled = false;
            m_moveDownButton.IsEnabled = false;
            return;
        }
        ModEntity entity = ModsManager.ModListAll[index];
        int upIndex = FindPreviousVisibleIndex(index);
        int downIndex = FindNextVisibleIndex(index);
        m_moveUpButton.IsEnabled = upIndex >= 0
            && !IsFixedMod(ModsManager.ModListAll[upIndex])
            && !entity.modInfo.DependencyRanges.ContainsKey(ModsManager.ModListAll[upIndex].modInfo.PackageName);
        m_moveDownButton.IsEnabled = downIndex >= 0
            && !IsFixedMod(ModsManager.ModListAll[downIndex])
            && !ModsManager.ModListAll[downIndex].modInfo.DependencyRanges.ContainsKey(entity.modInfo.PackageName);
    }
}
