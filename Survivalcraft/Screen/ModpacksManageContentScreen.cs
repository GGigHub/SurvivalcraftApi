using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Engine;

namespace Game
{
    /// <summary>
    /// 整合包管理屏（自模组 ScbbsPlusLocalScreen.Modpacks.Page.cs 提取为独立屏）：
    /// 列表展示 ModPacks 目录下的整合包记录，底部三钮承担 启动/关闭、补装下载、详情。
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
        private readonly ModpackFileInfoManager m_fileInfoManager = new(TransferManager.ModpackSourceResolver);
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
                LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "LocalResourcesReadingModpacksFormat"),
                LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "Modpack"));
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
            RestoreSelection();
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
            string displayName = resourceItem.DisplayName ?? resourceItem.Name ?? LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "Modpack");
            ModsManageContentItemWidget widget = new()
            {
                // 行 XML 自带 DefaultModIcon 占位，Icon 不在此设置，由图标管理器异步补图。
                Title = isActive
                    ? string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "CurrentlyActive"),
                        displayName)
                    : displayName,
                TitleColor = isActive ? new Color(120, 220, 140) : Color.White
            };
            (int downloadedCount, int totalCount) = ModpackPresenceRules.GetDownloadCounts(record, EnsurePresenceSnapshot());
            widget.Information = string.Format(
                CultureInfo.InvariantCulture,
                LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "InfoLine"),
                downloadedCount,
                totalCount,
                string.IsNullOrEmpty(record.Version) ? LanguageControl.None : record.Version,
                resourceItem.CreationTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            if (isActive)
            {
                widget.Information += " | " + LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "Active");
            }
            m_iconManager.StartIconRequests(resourceItem, record, widget.Children.Find<RectangleWidget>("ModsManageContentItem.Icon"));
            return widget;
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
            // Task 9 接管：DialogsManager.ShowDialog(null, new ModpackDetailsDialog(this, resourceItem, GetModpackRecord(resourceItem)));
        }

        // Task 8 接管：启停激活链（EnableSelectedLocalModpack/DisableSelectedLocalModpack 直系移植）。
        private void ToggleSelectedModpackEnabled()
        {
        }

        // Task 8 接管：补装下载链（DownloadSelectedLocalModpackMissingItems 直系移植）。
        private void DownloadSelectedModpackMissingItems()
        {
        }

        /// <summary>未选中 → 三钮禁用；选中 → 详情恒可用（完整启用态规则 Task 8 接管）。</summary>
        private void RefreshActionButtons()
        {
            if (m_selectedItem == null)
            {
                m_toggleEnabledButton.IsEnabled = false;
                m_downloadMissingButton.IsEnabled = false;
                m_viewDetailButton.IsEnabled = false;
                return;
            }

            m_viewDetailButton.IsEnabled = true;
            m_toggleEnabledButton.IsEnabled = true;
            m_downloadMissingButton.IsEnabled = true;
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

        private bool IsModpackActive(LocalResourceItem resourceItem)
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

        private ModpackPresenceSnapshot EnsurePresenceSnapshot()
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
                    m_fileInfoManager.Cache(result.ApiBaseUrl, entry.FileId, entry.File);
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
                    || !m_fileInfoManager.TryGetCached(AuthSessionManager.Instance.ApiBaseUrl, item.FileId, out PostFileItem file))
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
                .Where(fileId => !m_fileInfoManager.TryGetCached(AuthSessionManager.Instance.ApiBaseUrl, fileId, out _))
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
            m_fileInfoSlot.Start((requestId, _) => m_fileInfoManager.LoadAsync(
                new ModpackFileInfoLoadRequest
                {
                    RequestId = requestId,
                    ResourcePath = resourcePath,
                    ApiBaseUrl = apiBaseUrl,
                    Token = token,
                    FileIds = fileIds
                }));
        }

        private bool IsModpackFileInfoLoading(LocalResourceItem resourceItem)
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
