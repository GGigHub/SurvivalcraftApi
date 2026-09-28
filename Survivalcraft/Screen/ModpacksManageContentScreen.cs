using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Engine;

namespace Game
{
    /// <summary>
    /// 整合包管理屏（自模组 ScbbsPlusLocalScreen.Modpacks.Page.cs 提取为独立屏）：
    /// 列表展示 ModPacks 目录下的整合包记录，底部三钮承担 启用/禁用、补装下载、详情。
    /// 启停激活链与详情对话框分别在 Actions 链与 ModpackDetailsDialog 中实现。
    /// </summary>
    internal sealed class ModpacksManageContentScreen : CommunityScreenBase
    {
        public static string fName = "ModpacksManageContentScreen";

        // 图标缓存跨屏实例共享（屏随换语言重建，静态语义与模组一致）。
        private static readonly ModpackIconLoader s_iconLoader = new();

        private readonly ListPanelWidget m_modpacksList;
        private readonly BevelledButtonWidget m_toggleEnabledButton;
        private readonly BevelledButtonWidget m_downloadMissingButton;
        private readonly BevelledButtonWidget m_viewDetailButton;
        private readonly LabelWidget m_placeholderLabel;

        private readonly ModpackManifestLoadManager m_manifestLoadManager =
            new(() => ModpackRegistry.Shared.ReadResources().ToList());
        // fileId 缓存跨屏实例共享：屏随换语言重建，重建瞬间不应丢失已补全的社区文件信息
        //（与模组 ScbbsPlusModpackFileInfoManager 的 static readonly 单例语义一致）。
        private static readonly ModpackFileInfoManager s_fileInfoManager = new(TransferManager.ModpackSourceResolver);
        private readonly ModpackManager m_modpackManager = new();
        private readonly ModpackPresenceManager m_presenceManager = new();
        private readonly ModpackIconManager m_iconManager = new(s_iconLoader);
        private readonly TaskSlot<ModpackFileInfoLoadResult> m_fileInfoSlot = new("ModpackFileInfoLoad");

        private LocalModpackActivationState m_activeModpackState;
        private LocalResourceItem m_selectedItem;
        private ModpackPresenceSnapshot m_presenceSnapshot;
        // 选中代次：选择变化即递增，fileId 补全结果按代次比对防陈旧回写。
        private int m_selectionVersion;
        private int m_fileInfoSelectionVersion;
        private string m_fileInfoLoadingPath;

        public ModpacksManageContentScreen()
        {
            XElement node = ContentManager.Get<XElement>("Screens/ModpacksManageContentScreen");
            LoadContents(this, node);
            m_modpacksList = Children.Find<ListPanelWidget>("ModpacksList");
            m_toggleEnabledButton = Children.Find<BevelledButtonWidget>("ToggleEnabledButton");
            m_downloadMissingButton = Children.Find<BevelledButtonWidget>("DownloadMissingButton");
            m_viewDetailButton = Children.Find<BevelledButtonWidget>("ViewDetailButton");
            m_placeholderLabel = Children.Find<LabelWidget>("ModpacksList.Placeholder");
            Children.Find<LabelWidget>("TopBar.Label").Text = LanguageControl.Get(fName, "1");
            m_toggleEnabledButton.Text = LanguageControl.Get(fName, "2");
            m_downloadMissingButton.Text = LanguageControl.Get(fName, "3");
            m_viewDetailButton.Text = LanguageControl.Get(fName, "4");
            m_modpacksList.ItemWidgetFactory = MakeModpackItemWidget;
            m_modpacksList.ItemClicked += OnModpackItemClicked;
        }

        public override void Enter(object[] parameters)
        {
            base.Enter(parameters);
            // 进屏即作废在场快照：游戏内容可能在上次离屏后发生变化（brief 失效点：进屏）。
            m_presenceManager.Invalidate();
            m_presenceSnapshot = null;
            LoadActivationState();
            SetDownloadFlowRemoteImportHost(true);
            // 屏实例全局复用：订阅放 Enter 与 Leave 成对，保证每次进入期间都能响应外部整合包变化。
            DownloadFlowManager.Instance.PackDownloadCompleted += OnPackDownloadCompleted;
            ModpackRegistry.Shared.Changed += OnRegistryChanged;
            // 作废清单缓存：离屏期间的帖子下载/链接导入不经过屏内订阅，进屏必须强制重读（spec §4.1 外部导入自动刷新）。
            m_manifestLoadManager.Invalidate();
            LoadModpackList();
        }

        public override void Leave()
        {
            DownloadFlowManager.Instance.PackDownloadCompleted -= OnPackDownloadCompleted;
            ModpackRegistry.Shared.Changed -= OnRegistryChanged;
            m_fileInfoSlot.Invalidate();
            m_fileInfoLoadingPath = null;
            m_selectedItem = null;
            m_modpacksList.ClearItems();
            // 顺序不可换：DisposeRetired 必须在 ClearItems 之后（分离 widget 仍持缓存纹理，
            // 只有确认不再重挂重绘后才能释放退休纹理），新增调用点务必保持此序。
            m_iconManager.Clear();
            s_iconLoader.DisposeRetired();
            // 离开后轮询不再消费，就地回收已完成项；未完成项挂兜底 Dispose，防 Image 滞留到进程退出。
            s_iconLoader.ClearPending();
            ReleaseDownloadFlowCallbacks(releaseRemoteImportHost: false);
        }

        public override void Dispose()
        {
            // 换语言整表重建只走 Dispose 不走 Leave（CommunityScreenBase 文档：Dispose 是
            // 实例唯一生命周期终点）：单例静态事件在此确定性退订，否则死屏被钉在内存。
            // Leave 已退订时再退订为无操作，Enter/Leave/Dispose 任意顺序配对均安全。
            DownloadFlowManager.Instance.PackDownloadCompleted -= OnPackDownloadCompleted;
            ModpackRegistry.Shared.Changed -= OnRegistryChanged;
            base.Dispose();
        }

        public override void Update()
        {
            if (m_manifestLoadManager.TryTakeCompleted(out ManifestItemsLoadResult manifestResult)
                && manifestResult != null)
            {
                if (!string.IsNullOrEmpty(manifestResult.Error))
                {
                    Log.Warning(string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("ScbbsPlusModpackManifestLoadManager", "ReadModpackListFailed"),
                        manifestResult.Error));
                }
                RefreshModpackList();
            }

            TryTakeModpackFileInfoUpdates(GetSelectedRecord(), out _);
            m_iconManager.Update();

            if (m_selectedItem != null)
            {
                if (m_viewDetailButton.IsClicked)
                {
                    ShowModpackDetails(m_selectedItem);
                }
                if (m_toggleEnabledButton.IsClicked)
                {
                    ToggleSelectedModpackEnabled();
                }
                if (m_downloadMissingButton.IsClicked)
                {
                    DownloadSelectedModpackMissingItems();
                }
            }

            if (Input.Back
                || Input.Cancel
                || Children.Find<ButtonWidget>("TopBar.Back").IsClicked)
            {
                // 无 m_needRestart 语义：重启提示由 RestartConfirmDialog 在启停链内承担。
                ScreensManager.SwitchScreen("Content");
            }
        }

        override protected void OnLoginStateChanged(AuthSessionManager.AuthChangeKind kind)
        {
        }

        override protected bool IsModpackDataFilePresent(ModpackItem item)
        {
            return ModpackDataFiles.IsPresent(item);
        }

        override protected void ImportModpackDataFileSafe(byte[] data, ModpackItem item)
        {
            ModpackDataFiles.Import(data, item);
        }

        private void OnPackDownloadCompleted()
        {
            // 下载管线落地了新记录/新内容：作废清单缓存并延迟重读（Update 轮询取完成结果后上屏）。
            m_manifestLoadManager.Invalidate();
            m_manifestLoadManager.StartDeferredLoad();
        }

        private void OnRegistryChanged()
        {
            // 可能由后台下载任务的 SaveManifest 触发：只做线程安全的缓存作废与延迟重读。
            m_manifestLoadManager.Invalidate();
            m_manifestLoadManager.StartDeferredLoad();
        }

        private void LoadModpackList()
        {
            if (m_manifestLoadManager.HasCachedItems)
            {
                RefreshModpackList();
                return;
            }

            m_modpacksList.ClearItems();
            m_placeholderLabel.IsVisible = true;
            m_placeholderLabel.Text = string.Format(
                CultureInfo.InvariantCulture,
                LanguageControl.Get("ModpacksManageContentScreen", "LocalResourcesReadingModpacksFormat"),
                LanguageControl.Get("ModpacksManageContentScreen", "Modpack"));
            m_manifestLoadManager.StartDeferredLoad();
        }

        private void RefreshModpackList()
        {
            // 清单/在场快照的失效点（导入、下载完成、激活、停用、进屏、注册表变化）
            // 全部经由本方法刷新，在此统一作废并让条目工厂按需重取。
            m_presenceManager.Invalidate();
            m_presenceSnapshot = null;
            m_modpacksList.ClearItems();
            if (m_manifestLoadManager.TryGetCachedItems(out List<LocalResourceItem> items))
            {
                m_modpacksList.AddItems(items);
            }
            // 顺序不可换：DisposeRetired 必须在 ClearItems 之后（分离 widget 仍持缓存纹理，
            // 只有确认不再重挂重绘后才能释放退休纹理），新增调用点务必保持此序。
            m_iconManager.Clear();
            s_iconLoader.DisposeRetired();
            m_placeholderLabel.IsVisible = m_modpacksList.Items.Count == 0;
            if (m_modpacksList.Items.Count == 0)
            {
                // 空态：加载已完成，占位文案从「正在读取」换成无内容
                // （模组原版 LocalResourcesEmptyLabel 同键，经 ContentWidgets 子树解析）。
                m_placeholderLabel.Text = LanguageControl.GetContentWidgets("ScbbsPlusCommunityScreen", "NoContent");
            }
            RestoreSelection();
        }

        /// <summary>
        /// 下载完成回调专用刷新：DownloadFlowManager.Update 里 PackDownloadCompleted 先于
        /// completedAction 同帧触发，OnPackDownloadCompleted 已把清单缓存作废，直接刷新会拿到
        /// 空列表（按路径回查恒 null、选中项被误清）。缓存未命中即同步重读后再上屏。
        /// </summary>
        private void RefreshModpackListWithReload()
        {
            if (!m_manifestLoadManager.HasCachedItems)
            {
                m_manifestLoadManager.GetOrLoadSynchronously();
            }
            RefreshModpackList();
        }

        /// <summary>按路径找回既有选中项（列表重建后对象实例已更换）；丢失即清空选中。</summary>
        private void RestoreSelection()
        {
            LocalResourceItem matched = FindModpackByPath(m_selectedItem?.LocalFilePath);
            m_selectedItem = matched;
            if (matched != null)
            {
                m_modpacksList.SelectedItem = matched;
            }
            RefreshActionButtons();
        }

        private Widget MakeModpackItemWidget(object item)
        {
            if (item is not LocalResourceItem resourceItem || resourceItem.Tag is not LocalModpackRecord record)
            {
                return null;
            }

            bool isActive = IsModpackActive(resourceItem);
            string displayName = resourceItem.DisplayName ?? resourceItem.Name ?? LanguageControl.Get("ModpacksManageContentScreen", "Modpack");
            ModsManageContentItemWidget widget = new()
            {
                // 行 XML 自带 DefaultModIcon 占位，Icon 不在此设置，由图标管理器异步补图。
                Title = isActive
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get(fName, "CurrentlyActive"),
                        displayName)
                    : displayName,
                TitleColor = isActive ? new Color(120, 220, 140) : Color.White
            };
            (int downloadedCount, int totalCount) = ModpackPresenceRules.GetDownloadCounts(record, EnsurePresenceSnapshot());
            ModpackManifest manifest = record.Manifest;
            widget.Information = string.Format(
                CultureInfo.InvariantCulture,
                LanguageControl.Get(fName, "6"),
                downloadedCount,
                totalCount,
                string.IsNullOrEmpty(record.Version) ? LanguageControl.None : record.Version,
                string.IsNullOrEmpty(manifest?.ApiVersion) ? LanguageControl.None : manifest.ApiVersion,
                string.IsNullOrEmpty(manifest?.Author) ? LanguageControl.None : manifest.Author,
                TryGetFileSizeText(resourceItem.LocalFilePath));
            widget.Description = string.IsNullOrEmpty(manifest?.Description) ? null : manifest.Description;
            m_iconManager.StartIconRequests(resourceItem, record, widget.Children.Find<RectangleWidget>("ModsManageContentItem.Icon"));
            return widget;
        }

        /// <summary>
        /// 包体文件大小直读（列表里的文件必存在，取不到属异常，记警告后显示“-”降级）。
        /// </summary>
        internal static string TryGetFileSizeText(string filePath)
        {
            try
            {
                using Stream stream = Storage.OpenFile(filePath, OpenFileMode.Read);
                return DataSizeFormatter.Format(stream.Length);
            }
            catch (Exception ex)
            {
                Log.Warning(string.Format(
                    CultureInfo.InvariantCulture,
                    "Modpack file size unavailable: {0} ({1})",
                    filePath,
                    ex.Message));
                return "-";
            }
        }

        private void OnModpackItemClicked(object item)
        {
            if (item is not LocalResourceItem resourceItem || resourceItem.Tag is not LocalModpackRecord)
            {
                return;
            }

            if (ReferenceEquals(resourceItem, m_selectedItem))
            {
                ShowModpackDetails(resourceItem);
                return;
            }

            m_selectedItem = resourceItem;
            m_selectionVersion++;
            RefreshActionButtons();
        }

        private void ShowModpackDetails(LocalResourceItem resourceItem)
        {
            LocalModpackRecord record = GetModpackRecord(resourceItem);
            if (resourceItem == null || record == null)
            {
                return;
            }

            DialogsManager.ShowDialog(null, new ModpackDetailsDialog(this, resourceItem, record));
        }

        /// <summary>启停总入口：激活中 → 停用链，否则 → 启用链。</summary>
        internal void ToggleSelectedModpackEnabled()
        {
            LocalResourceItem resourceItem = m_selectedItem;
            if (!IsModpackResource(resourceItem))
            {
                return;
            }

            if (IsModpackActive(resourceItem))
            {
                DisableSelectedModpack(resourceItem);
            }
            else
            {
                EnableSelectedModpack(resourceItem);
            }
        }

        private void EnableSelectedModpack(LocalResourceItem resourceItem)
        {
            ModpackDecision gateDecision = m_modpackManager.EvaluateEnable(new ModpackDecisionInput
            {
                IsModpack = IsModpackResource(resourceItem),
                IsBusy = DownloadFlowManager.Instance.ModpackDownloadBusy,
                HasOtherActiveModpack = m_modpackManager.HasOtherActiveModpack(
                    GetModpackRecord(resourceItem),
                    resourceItem?.LocalFilePath,
                    m_activeModpackState)
            });
            if (gateDecision.Kind == ModpackDecisionKind.Busy)
            {
                CommunityToastManager.Show(LanguageControl.Get("ModpacksManageContentScreen", "ModpackBusyWait"), autoHideSeconds: 2.5);
                return;
            }
            if (gateDecision.Kind == ModpackDecisionKind.OtherModpackActive)
            {
                CommunityToastManager.Show(
                    LanguageControl.Get(fName, "AnotherModpackActive"),
                    autoHideSeconds: 3.0);
                return;
            }

            LocalModpackRecord record = GetModpackRecord(resourceItem);
            ApplyCachedFileInfo(record);
            StartModpackFileInfoLoad(resourceItem, record);
            ModpackDecision decision = m_modpackManager.EvaluateEnable(new ModpackDecisionInput
            {
                IsModpack = record != null,
                IsFileInfoLoading = IsModpackFileInfoLoading(resourceItem),
                HasManagedMods = ModpackModMatcher.HasManagedMods(record)
            });
            if (decision.Kind == ModpackDecisionKind.FileInfoLoading)
            {
                CommunityToastManager.Show(LanguageControl.Get("ModpacksManageContentScreen", "LoadingFileInfo"), autoHideSeconds: 2.5);
                return;
            }
            if (decision.Kind == ModpackDecisionKind.NoManagedMods)
            {
                CommunityToastManager.Show(
                    LanguageControl.Get(fName, "ThisModpackHasNoManagedModResources"),
                    autoHideSeconds: 3.0);
                return;
            }

            IReadOnlyList<ModpackItem> missingEmbeddedItems = m_modpackManager.GetMissingEmbeddedItems(
                record,
                item => ModpackPresenceRules.IsEmbeddedItemMissing(record, item));
            ModpackPresenceSnapshot presenceSnapshot = EnsurePresenceSnapshot();
            IReadOnlyList<ModpackItem> missingItems = m_modpackManager.GetMissingDownloadItems(
                record,
                item => ModpackPresenceRules.IsEmbeddedItemMissing(record, item),
                item => ModpackPresenceRules.IsItemDownloaded(item, presenceSnapshot, record));
            decision = m_modpackManager.EvaluateEnable(new ModpackDecisionInput
            {
                IsModpack = record != null,
                HasManagedMods = true,
                MissingEmbeddedItems = missingEmbeddedItems,
                MissingDownloadItems = missingItems
            });
            if (decision.Kind == ModpackDecisionKind.MissingEmbeddedItems)
            {
                string missingNames = string.Join("、", missingEmbeddedItems
                    .Take(3)
                    .Select(GetModpackItemDisplayName));
                if (missingEmbeddedItems.Count > 3)
                {
                    missingNames += string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get(fName, "AndMore"),
                        missingEmbeddedItems.Count);
                }
                DialogsManager.ShowDialog(
                    null,
                    new MessageDialog(
                        LanguageControl.Get(fName, "MissingModpackFiles"),
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get(fName, "MissingInArchiveList"),
                            missingNames),
                        LanguageControl.Ok,
                        null,
                        null));
                return;
            }

            if (decision.Kind == ModpackDecisionKind.MissingDownloadItems)
            {
                string displayName = resourceItem.DisplayName ?? resourceItem.Name ?? LanguageControl.Get("ModpacksManageContentScreen", "Modpack");
                DialogsManager.ShowDialog(
                    null,
                    new MessageDialog(
                        LanguageControl.Get(fName, "MissingResources"),
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get(fName, "MissingResourcesDownloadPrompt"),
                            displayName,
                            missingItems.Count),
                        LanguageControl.Get(fName, "DownloadAndLaunch"),
                        LanguageControl.Cancel,
                        button =>
                        {
                            if (button == MessageDialogButton.Button1)
                            {
                                StartMissingItemDownload(resourceItem, record, missingItems, startAfterDownload: true);
                            }
                        }));
                return;
            }

            if (decision.CanActivate)
            {
                ImportEmbeddedItemsAndEnable(resourceItem, record);
            }
        }

        /// <summary>补装下载入口：忙时拒绝，无缺失提示已齐，否则走缺项下载管线（startAfterDownload=false）。</summary>
        internal void DownloadSelectedModpackMissingItems()
        {
            if (DownloadFlowManager.Instance.ModpackDownloadBusy)
            {
                CommunityToastManager.Show(LanguageControl.Get("ModpacksManageContentScreen", "ModpackBusyWait"), autoHideSeconds: 2.5);
                return;
            }

            LocalResourceItem resourceItem = m_selectedItem;
            LocalModpackRecord record = GetModpackRecord(resourceItem);
            ApplyCachedFileInfo(record);
            StartModpackFileInfoLoad(resourceItem, record);
            if (IsModpackFileInfoLoading(resourceItem))
            {
                CommunityToastManager.Show(LanguageControl.Get("ModpacksManageContentScreen", "LoadingFileInfo"), autoHideSeconds: 2.5);
                return;
            }

            ModpackPresenceSnapshot presenceSnapshot = EnsurePresenceSnapshot();
            IReadOnlyList<ModpackItem> missingItems = m_modpackManager.GetMissingDownloadItems(
                record,
                item => ModpackPresenceRules.IsEmbeddedItemMissing(record, item),
                item => ModpackPresenceRules.IsItemDownloaded(item, presenceSnapshot, record));
            if (missingItems.Count == 0)
            {
                CommunityToastManager.Show(
                    LanguageControl.Get(fName, "AllResourcesReady"),
                    autoHideSeconds: 2.5);
                RefreshActionButtons();
                return;
            }

            StartMissingItemDownload(resourceItem, record, missingItems, startAfterDownload: false);
        }

        private void StartMissingItemDownload(
            LocalResourceItem resourceItem,
            LocalModpackRecord record,
            IReadOnlyList<ModpackItem> missingItems,
            bool startAfterDownload)
        {
            if (resourceItem == null || record?.Manifest == null || missingItems == null || missingItems.Count == 0)
            {
                return;
            }

            string recordPath = resourceItem.LocalFilePath;
            bool downloadStarted = DownloadFlowManager.Instance.StartModpackItemsDownload(
                record.Manifest,
                missingItems,
                record.SourceUrl,
                record.SourceFileName,
                embeddedFiles: null,
                saveManifestRecord: false,
                completedAction: progress =>
                {
                    DisableDownloadedMods(progress);
                    RefreshModpackListWithReload();
                    LocalResourceItem latestResourceItem = FindModpackByPath(recordPath) ?? resourceItem;
                    LocalModpackRecord latestRecord = GetModpackRecord(latestResourceItem) ?? record;
                    ApplyCachedFileInfo(latestRecord);
                    if (!startAfterDownload)
                    {
                        // 仅当下载期间用户未改选其他整合包时才切回刚下载的包，避免完成回调覆盖用户选择。
                        if (latestResourceItem != null
                            && m_modpackManager.IsCurrentResource(m_selectedItem?.LocalFilePath, recordPath))
                        {
                            m_selectedItem = latestResourceItem;
                            m_selectionVersion++;
                            m_modpacksList.SelectedItem = latestResourceItem;
                            RefreshActionButtons();
                        }
                        CommunityToastManager.Show(
                            LanguageControl.Get(fName, "MissingModpackResourcesDownloaded"),
                            autoHideSeconds: 3.0);
                        return;
                    }

                    ModpackPresenceSnapshot latestPresenceSnapshot = EnsurePresenceSnapshot();
                    bool hasMissingItems = m_modpackManager.GetMissingDownloadItems(
                        latestRecord,
                        item => ModpackPresenceRules.IsEmbeddedItemMissing(latestRecord, item),
                        item => ModpackPresenceRules.IsItemDownloaded(item, latestPresenceSnapshot, latestRecord)).Count > 0;
                    ModpackDownloadCompletionKind completionKind = m_modpackManager.EvaluateDownloadCompletion(progress, hasMissingItems);
                    if (latestResourceItem == null || completionKind != ModpackDownloadCompletionKind.ReadyToActivate)
                    {
                        bool hasUnavailableResources = completionKind == ModpackDownloadCompletionKind.Failed;
                        if (hasUnavailableResources && latestResourceItem != null)
                        {
                            DialogsManager.ShowDialog(
                                null,
                                new MessageDialog(
                                    LanguageControl.Get(fName, "FailedToDownloadModpackResources"),
                                    string.Format(
                                        CultureInfo.InvariantCulture,
                                        LanguageControl.Get(fName, "UnavailableResourcesIgnorePrompt"),
                                        progress.FailedCount),
                                    LanguageControl.Get(fName, "IgnoreAndLaunch"),
                                    LanguageControl.Cancel,
                                    button =>
                                    {
                                        if (button == MessageDialogButton.Button1)
                                        {
                                            Log.Warning(string.Format(
                                                CultureInfo.InvariantCulture,
                                                LanguageControl.Get("ScbbsPlusModpacks", "UserIgnoredUnavailableModpackResources"),
                                                progress.FailedCount));
                                            ImportEmbeddedItemsAndEnable(latestResourceItem, latestRecord);
                                        }
                                    }));
                            return;
                        }

                        CommunityToastManager.Show(
                            LanguageControl.Get(fName, "StartBlockedMissingResources"),
                            autoHideSeconds: 3.0);
                        return;
                    }

                    ImportEmbeddedItemsAndEnable(latestResourceItem, latestRecord);
                },
                allowStartAfterDownloadFailures: startAfterDownload);
            if (downloadStarted)
            {
                CommunityToastManager.Show(
                    LanguageControl.Get(fName, "DownloadingMissingModpackResources"),
                    autoHideSeconds: 2.5);
            }
            RefreshActionButtons();
        }

        private void ImportEmbeddedItemsAndEnable(LocalResourceItem resourceItem, LocalModpackRecord record)
        {
            IReadOnlyList<ModpackItem> embeddedItems = ModpackPresenceRules.GetEmbeddedItems(record, EnsurePresenceSnapshot());
            if (embeddedItems.Count == 0)
            {
                ApplyModpackEnabledState(resourceItem, record);
                return;
            }

            string recordPath = resourceItem?.LocalFilePath;
            bool downloadStarted = DownloadFlowManager.Instance.StartModpackItemsDownload(
                record.Manifest,
                embeddedItems,
                record.SourceUrl,
                record.SourceFileName,
                record.EmbeddedFiles,
                saveManifestRecord: false,
                customFileBackupPreparer: (item, data, currentProgress) => ModpackDataFiles.PrepareForActivation(
                    data,
                    item,
                    currentProgress.CustomFileBackups,
                    record.PackageName ?? recordPath),
                completedAction: progress =>
                {
                    DisableDownloadedMods(progress);
                    if (progress == null || progress.FailedCount > 0 || !string.IsNullOrEmpty(progress.Error) || progress.Interrupted)
                    {
                        ModpackDataFiles.TryRestoreBackups(progress?.CustomFileBackups, out _);
                        CommunityToastManager.Show(
                            LanguageControl.Get(fName, "ExtractFailedNotStarted"),
                            autoHideSeconds: 3.0);
                        return;
                    }

                    RefreshModpackListWithReload();
                    LocalResourceItem latestResourceItem = FindModpackByPath(recordPath) ?? resourceItem;
                    LocalModpackRecord latestRecord = GetModpackRecord(latestResourceItem) ?? record;
                    ApplyCachedFileInfo(latestRecord);
                    ApplyModpackEnabledState(latestResourceItem, latestRecord, progress.CustomFileBackups);
                },
                allowStartAfterDownloadFailures: false);
            if (downloadStarted)
            {
                CommunityToastManager.Show(
                    LanguageControl.Get(fName, "ExtractingResourcesFromTheModpack"),
                    autoHideSeconds: 2.5);
            }
            RefreshActionButtons();
        }

        private void ApplyModpackEnabledState(
            LocalResourceItem resourceItem,
            LocalModpackRecord record,
            List<LocalModpackFileStateEntry> backupFiles = null)
        {
            if (resourceItem == null || record == null)
            {
                return;
            }

            ModpackOperationResult result = m_modpackManager.Activate(
                new ModpackActivationRequest
                {
                    Record = record,
                    RecordPath = resourceItem.LocalFilePath,
                    DisplayName = resourceItem.DisplayName ?? resourceItem.Name,
                    CurrentModStates = CaptureModStateSnapshot(),
                    BackupFiles = backupFiles ?? []
                },
                clearPendingActions: LocalModPendingActionStore.ClearPendingActions,
                saveSettings: SettingsManager.SaveSettings,
                setDisabledState: PendingLocalModActions.SetManualDisabledState);
            m_activeModpackState = result.State;
            if (result.Kind == ModpackOperationKind.StateSaveFailed)
            {
                Log.Warning(string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("ScbbsPlusModpacks", "SaveModpackEnabledStateFailed"),
                    result.Error));
                ShowModpackFileOperationError("FailedToSaveSettings", "SaveConfigFailed", result.Error);
                return;
            }
            RefreshModpackList();
            PromptRestartForModpackChange(
                LanguageControl.Get(fName, "LaunchModpack"),
                LanguageControl.Get(fName, "EnabledRestartPrompt"));
        }

        private void DisableSelectedModpack(LocalResourceItem resourceItem)
        {
            if (!IsModpackActive(resourceItem) || m_activeModpackState == null)
            {
                CommunityToastManager.Show(
                    LanguageControl.Get(fName, "ThisModpackIsNotCurrentlyActive"),
                    autoHideSeconds: 2.5);
                return;
            }

            LocalModpackRecord record = GetModpackRecord(resourceItem);
            ModpackOperationResult result = m_modpackManager.Deactivate(
                new ModpackDeactivationRequest
                {
                    Record = record,
                    ActiveState = m_activeModpackState,
                    CurrentModStates = CaptureModStateSnapshot()
                },
                clearPendingActions: LocalModPendingActionStore.ClearPendingActions,
                saveSettings: SettingsManager.SaveSettings,
                setDisabledState: PendingLocalModActions.SetManualDisabledState);
            if (result.Kind == ModpackOperationKind.PersistFailed)
            {
                ShowModpackFileOperationError("FailedToSaveSettings", "SaveConfigFailed", result.Error);
                return;
            }
            if (result.Kind == ModpackOperationKind.RestoreFailed)
            {
                ShowModpackFileOperationError("FailedToRestoreFiles", "RestoreFilesFailed", result.Error);
                return;
            }
            if (result.Kind == ModpackOperationKind.StateSaveFailed)
            {
                Log.Warning(string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("ScbbsPlusModpacks", "ClearModpackEnabledStateFailed"),
                    result.Error));
                ShowModpackFileOperationError("FailedToSaveSettings", "SaveConfigFailed", result.Error);
                return;
            }

            m_activeModpackState = null;
            RefreshModpackList();
            PromptRestartForModpackChange(
                LanguageControl.Get(fName, "CloseModpack"),
                LanguageControl.Get(fName, "DisabledRestartPrompt"));
        }

        private void ShowModpackFileOperationError(string titleKey, string messageKey, string error)
        {
            DialogsManager.ShowDialog(
                null,
                new MessageDialog(
                    LanguageControl.Get(fName, titleKey),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get(fName, messageKey),
                        error),
                    LanguageControl.Ok,
                    null,
                    null));
        }

        /// <summary>
        /// 重启确认提示：两回调返回 true 后重启由 RestartConfirmDialog 自己执行（回调内不得再调 Window.Restart）。
        /// </summary>
        private void PromptRestartForModpackChange(string title, string message)
        {
            DialogsManager.ShowDialog(
                null,
                new RestartConfirmDialog(
                    title,
                    message,
                    restartAction: () =>
                    {
                        PendingLocalModActions.Process("integration-pack-restart");
                        return true;
                    },
                    restartToCurrentScreenAction: null));
        }

        /// <summary>
        /// 删除整合包记录。返回 true 表示本次点击已被处理（含拒绝分支），详情对话框据此关闭自身。
        /// </summary>
        internal bool TryDeleteModpack(LocalResourceItem resourceItem, LocalModpackRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(resourceItem?.LocalFilePath))
            {
                return false;
            }

            if (IsModpackActive(resourceItem))
            {
                DialogsManager.ShowDialog(
                    null,
                    new MessageDialog(
                        LanguageControl.Get(fName, "CannotDelete"),
                        LanguageControl.Get(fName, "DisableBeforeDelete"),
                        LanguageControl.Ok,
                        null,
                        null));
                return true;
            }

            try
            {
                ModpackRegistry.Shared.DeleteFileSet(resourceItem.LocalFilePath);
                // DeleteFileSet 内部触发 Changed → 作废清单缓存并延迟重读（Update 轮询后上屏）。
                return true;
            }
            catch (Exception ex)
            {
                DialogsManager.ShowDialog(
                    null,
                    new MessageDialog(
                        LanguageControl.Error,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("ModpacksManageContentScreen", "DeleteFailed"),
                            ex.Message),
                        LanguageControl.Ok,
                        null,
                        null));
                return true;
            }
        }

        /// <summary>清单条目的展示名（缺失对话框逐项列举/详情清单行共用）：文件名 → fileId → 目标路径 → 包名。</summary>
        internal static string GetModpackItemDisplayName(ModpackItem item)
        {
            if (item == null)
            {
                return "-";
            }

            if (!string.IsNullOrWhiteSpace(item.Filename))
            {
                return item.Filename;
            }

            if (!string.IsNullOrWhiteSpace(item.FileId))
            {
                return $"fileId:{item.FileId}";
            }

            if (!string.IsNullOrWhiteSpace(item.TargetDirectory))
            {
                return item.TargetDirectory;
            }

            if (!string.IsNullOrWhiteSpace(item.TargetPath))
            {
                return item.TargetPath;
            }

            return item.PackageName ?? "-";
        }

        // 整合包补装的模组默认保持禁用，启动时临时启用，关闭时才能恢复为禁用状态。
        private void DisableDownloadedMods(ModpackDownloadProgress progress)
        {
            m_modpackManager.DisableDownloadedMods(
                progress,
                LoadLocalMods(),
                LocalResourceIdentity.GetModStoragePath,
                SetModDisabledState,
                SettingsManager.SaveSettings);
        }

        private IReadOnlyList<LocalModpackModStateEntry> CaptureModStateSnapshot()
        {
            return m_modpackManager.CaptureModStateSnapshot(
                LoadLocalMods(),
                LocalResourceIdentity.GetModStoragePath,
                GetEffectiveModDisabledState).ToArray();
        }

        private static bool GetEffectiveModDisabledState(LocalResourceItem resourceItem)
        {
            return resourceItem?.PendingAction switch
            {
                LocalModPendingActionType.Enable => false,
                LocalModPendingActionType.Disable => true,
                LocalModPendingActionType.Delete => true,
                _ => resourceItem?.IsDisabled == true
            };
        }

        private static bool SetModDisabledState(string packageName, string version, bool isDisabled)
        {
            if (string.IsNullOrWhiteSpace(packageName) || string.IsNullOrWhiteSpace(version) || ModpackModMatcher.IsSelfPackage(packageName))
            {
                return false;
            }

            return PendingLocalModActions.SetManualDisabledState(packageName, version, isDisabled);
        }

        // 本地模组列表 = 已加载模组（运行时）+ 待重启模组（磁盘扫描），供模组状态快照/补装禁用取数。
        private static List<LocalResourceItem> LoadLocalMods()
        {
            return LocalResourceCollection.BuildLocalMods(
                LocalResourceCollection.GetPendingRestartLocalMods(),
                LocalModPendingActionStore.LocalModPendingActions);
        }

        /// <summary>选中记录当前缺失（未落地且未内嵌缺失）的启用条目数，供按钮可用性与详情对话框共用。</summary>
        internal int GetMissingDownloadItemCount(LocalModpackRecord record)
        {
            ModpackPresenceSnapshot snapshot = EnsurePresenceSnapshot();
            return m_modpackManager.GetMissingDownloadItems(
                record,
                item => ModpackPresenceRules.IsEmbeddedItemMissing(record, item),
                item => ModpackPresenceRules.IsItemDownloaded(item, snapshot, record)).Count;
        }

        /// <summary>
        /// 底部三钮启用态：未选中 → 三钮禁用；选中 → 详情恒可用，启停钮文本随激活态切换，
        /// 补装钮在缺失数 &gt; 0 且不忙且文件信息未在途时才可用，进行中切换提示文本。
        /// </summary>
        private void RefreshActionButtons()
        {
            LocalResourceItem resourceItem = m_selectedItem;
            LocalModpackRecord record = GetModpackRecord(resourceItem);
            if (resourceItem == null || record == null)
            {
                m_toggleEnabledButton.IsEnabled = false;
                m_downloadMissingButton.IsEnabled = false;
                m_viewDetailButton.IsEnabled = false;
                return;
            }

            bool busy = DownloadFlowManager.Instance.ModpackDownloadBusy;
            bool fileInfoLoading = IsModpackFileInfoLoading(resourceItem);
            m_viewDetailButton.IsEnabled = true;
            m_toggleEnabledButton.IsEnabled = true;
            m_toggleEnabledButton.Text = LanguageControl.Get(fName, IsModpackActive(resourceItem) ? "5" : "2");
            m_downloadMissingButton.IsEnabled = GetMissingDownloadItemCount(record) > 0 && !busy && !fileInfoLoading;
            m_downloadMissingButton.Text = fileInfoLoading
                ? LanguageControl.Get("ModpacksManageContentScreen", "LoadingFileInfo")
                : busy
                    ? LanguageControl.Get("ScbbsPlusCommunityScreen", "Downloading")
                    : LanguageControl.Get(fName, "3");
        }

        private void LoadActivationState()
        {
            ActivationStateLoad load = m_modpackManager.LoadActivationState();
            if (load.Error != null)
            {
                Log.Warning(string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("ScbbsPlusModpacks", "ReadModpackEnabledStateFailed"),
                    load.Error.Message));
            }
            m_activeModpackState = load.State;
        }

        internal bool IsModpackActive(LocalResourceItem resourceItem)
        {
            return m_modpackManager.IsActive(GetModpackRecord(resourceItem), resourceItem?.LocalFilePath, m_activeModpackState);
        }

        private LocalModpackRecord GetSelectedRecord()
        {
            return GetModpackRecord(m_selectedItem);
        }

        private static LocalModpackRecord GetModpackRecord(LocalResourceItem resourceItem)
        {
            return resourceItem?.Tag as LocalModpackRecord;
        }

        private static bool IsModpackResource(LocalResourceItem resourceItem)
        {
            return resourceItem?.Tag is LocalModpackRecord;
        }

        private LocalResourceItem FindModpackByPath(string recordPath)
        {
            string normalizedPath = ModsManager.NormalizeStoragePath(recordPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return null;
            }

            return m_modpacksList.Items
                .OfType<LocalResourceItem>()
                .FirstOrDefault(item => IsModpackResource(item)
                    && string.Equals(ModsManager.NormalizeStoragePath(item.LocalFilePath), normalizedPath, StringComparison.OrdinalIgnoreCase));
        }

        internal ModpackPresenceSnapshot EnsurePresenceSnapshot()
        {
            if (m_presenceSnapshot == null)
            {
                m_presenceSnapshot = m_presenceManager.Capture(LocalResourceCollection.GetPendingRestartLocalMods());
            }
            return m_presenceSnapshot;
        }

        /// <summary>
        /// 泵整合包文件信息补全任务（屏与详情对话框共用唯一消费点）：取到结果即写入
        /// 管理器缓存并应用到当前记录，随后刷新列表信息行；<paramref name="changed"/>
        /// 表示本轮确有新条目落地（详情对话框据此重绑清单行）。
        /// </summary>
        internal void TryTakeModpackFileInfoUpdates(LocalModpackRecord record, out bool changed)
        {
            changed = false;
            if (!m_fileInfoSlot.TryTake(out ModpackFileInfoLoadResult result, ex =>
                {
                    Log.Warning(string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("ScbbsPlusModpacks", "LoadModpackFileInfoFailed"),
                        ex.Message));
                    return null;
                }))
            {
                return;
            }

            m_fileInfoLoadingPath = null;
            if (result == null)
            {
                RefreshActionButtons();
                return;
            }

            foreach (ModpackFileInfoEntry entry in result.Entries ?? [])
            {
                if (entry?.File != null)
                {
                    s_fileInfoManager.Cache(result.ApiBaseUrl, entry.FileId, entry.File);
                }
                else if (!string.IsNullOrWhiteSpace(entry?.Error))
                {
                    Log.Warning(string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("ScbbsPlusModpacks", "GetModpackFileInfoFailed"),
                        entry.FileId,
                        entry.Error));
                }
            }

            ApplyCachedFileInfo(record);
            RefreshModpackList();
            RefreshActionButtons();
            changed = true;
        }

        /// <summary>把缓存中的社区文件信息回填到记录清单条目（fileId 补全文件名/包名/版本）。</summary>
        internal void ApplyCachedFileInfo(LocalModpackRecord record)
        {
            foreach (ModpackItem item in record?.Manifest?.Items ?? [])
            {
                if (item == null || string.IsNullOrWhiteSpace(item.FileId)
                    || !s_fileInfoManager.TryGetCached(AuthSessionManager.Instance.ApiBaseUrl, item.FileId, out PostFileItem file))
                {
                    continue;
                }

                ModpackRules.ApplyItemFileInfo(item, file);
            }
        }

        /// <summary>对未落地且缺文件名的清单条目发起 fileId 补全（同路径在途时复用，不重复起任务）。</summary>
        internal void StartModpackFileInfoLoad(LocalResourceItem resourceItem, LocalModpackRecord record)
        {
            if (resourceItem == null || record?.Manifest?.Items == null)
            {
                return;
            }

            ModpackPresenceSnapshot snapshot = EnsurePresenceSnapshot();
            IReadOnlyList<string> fileIds = record.Manifest.Items
                .Where(item => item != null && item.IsEnabled && !string.IsNullOrWhiteSpace(item.FileId))
                .Where(item => !ModpackPresenceRules.IsItemDownloaded(item, snapshot, record))
                .Select(item => item.FileId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(fileId => !s_fileInfoManager.TryGetCached(AuthSessionManager.Instance.ApiBaseUrl, fileId, out _))
                .ToArray();
            if (fileIds.Count == 0)
            {
                return;
            }

            string resourcePath = ModsManager.NormalizeStoragePath(resourceItem.LocalFilePath);
            if (m_fileInfoSlot.IsRunning
                && m_fileInfoSelectionVersion == m_selectionVersion
                && string.Equals(m_fileInfoLoadingPath, resourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string apiBaseUrl = AuthSessionManager.Instance.ApiBaseUrl;
            string token = AuthSessionManager.Instance.CurrentToken;
            m_fileInfoLoadingPath = resourcePath;
            m_fileInfoSelectionVersion = m_selectionVersion;
            m_fileInfoSlot.Start((requestId, _) => s_fileInfoManager.LoadAsync(
                new ModpackFileInfoLoadRequest
                {
                    RequestId = requestId,
                    ResourcePath = resourcePath,
                    ApiBaseUrl = apiBaseUrl,
                    Token = token,
                    FileIds = fileIds
                }));
        }

        internal bool IsModpackFileInfoLoading(LocalResourceItem resourceItem)
        {
            return m_fileInfoSlot.IsRunning
                && resourceItem != null
                && string.Equals(
                    ModsManager.NormalizeStoragePath(resourceItem.LocalFilePath),
                    m_fileInfoLoadingPath,
                    StringComparison.OrdinalIgnoreCase);
        }
    }
}
