using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Engine;

namespace Game
{
    /// <summary>
    /// 整合包详情对话框：字段行 + 条目清单 + 五按钮（启停/补装/删除/上传/关闭）。
    /// 启停与补装动作转发给宿主屏（链路逻辑唯一归属 ModpacksManageContentScreen），
    /// fileId 补全复用宿主屏的 TaskSlot，打开期间轮询宿主屏消费结果重绑清单行。
    /// </summary>
    public sealed class ModpackDetailsDialog : Dialog
    {
        public const string fName = "ModpackDetailsDialog";

        private readonly ModpacksManageContentScreen m_screen;
        private readonly LocalResourceItem m_item;
        private readonly LocalModpackRecord m_record;

        private readonly LabelWidget m_titleLabel;
        private readonly LabelWidget m_downloadStatusLabel;
        private readonly LabelWidget m_itemsListValueLabel;
        private readonly BevelledButtonWidget m_toggleEnabledButton;
        private readonly BevelledButtonWidget m_downloadMissingButton;
        // 上一帧的下载忙/文件信息加载态：失败等路径不产生任何回调，按钮文本靠边沿轮询恢复。
        private bool m_lastBusy;
        private bool m_lastLoading;
        private readonly BevelledButtonWidget m_deleteButton;
        private readonly BevelledButtonWidget m_uploadButton;
        private readonly BevelledButtonWidget m_closeButton;

        // 宿主屏为 internal，构造器收窄为 internal（CS0051）；类保持 public 与既有对话框一致。
        internal ModpackDetailsDialog(ModpacksManageContentScreen screen, LocalResourceItem item, LocalModpackRecord record)
        {
            m_screen = screen;
            m_item = item;
            m_record = record;
            XElement node = ContentManager.Get<XElement>("Dialogs/ModpackDetailsDialog");
            LoadContents(this, node);
            m_titleLabel = Children.Find<LabelWidget>("ModpackDetailsDialog.Title");
            StackPanelWidget contentPanel = Children.Find<StackPanelWidget>("ModpackDetailsDialog.Content");
            m_toggleEnabledButton = Children.Find<BevelledButtonWidget>("ModpackDetailsDialog.ToggleEnabled");
            m_downloadMissingButton = Children.Find<BevelledButtonWidget>("ModpackDetailsDialog.DownloadMissing");
            m_deleteButton = Children.Find<BevelledButtonWidget>("ModpackDetailsDialog.Delete");
            m_uploadButton = Children.Find<BevelledButtonWidget>("ModpackDetailsDialog.Upload");
            m_closeButton = Children.Find<BevelledButtonWidget>("ModpackDetailsDialog.Close");

            m_titleLabel.Text = m_item?.DisplayName ?? m_item?.Name ?? "-";

            // fileId 补全先行应用缓存，再对缺名条目复用宿主屏的补全任务槽。
            m_screen.ApplyCachedFileInfo(m_record);

            Vector2 margin = new Vector2(0f, 4f);
            ModpackManifest manifest = m_record?.Manifest;
            void AddField(string key, string value, bool wordWrap = false)
            {
                contentPanel.Children.Add(new LabelWidget
                {
                    Text = $"{LanguageControl.Get(fName, key)}{(string.IsNullOrWhiteSpace(value) ? LanguageControl.None : value)}",
                    WordWrap = wordWrap,
                    Margin = margin
                });
            }

            AddField("1", m_record?.Version);
            AddField("2", manifest?.ApiVersion);
            AddField("3", manifest?.Author);
            contentPanel.Children.Add(new LabelWidget
            {
                Text = $"{LanguageControl.Get(fName, "4")}{ModpacksManageContentScreen.TryGetFileSizeText(m_item?.LocalFilePath)}",
                Margin = margin
            });
            AddField("5", manifest?.Description ?? m_record?.Description, wordWrap: true);

            string homepage = !string.IsNullOrWhiteSpace(manifest?.Homepage) ? manifest.Homepage : m_record?.SourceUrl;
            if (string.IsNullOrWhiteSpace(homepage))
            {
                contentPanel.Children.Add(new LabelWidget
                {
                    Text = $"{LanguageControl.Get(fName, "6")}{LanguageControl.None}",
                    Margin = margin
                });
            }
            else
            {
                contentPanel.Children.Add(new StackPanelWidget
                {
                    Direction = LayoutDirection.Horizontal,
                    Children =
                    {
                        new LabelWidget { Text = LanguageControl.Get(fName, "6") },
                        new LinkWidget { Text = homepage, Url = homepage, Color = new Color(64, 192, 64) }
                    },
                    Margin = margin
                });
            }

            AddField("7", Storage.GetFileName(m_item?.LocalFilePath ?? string.Empty));
            AddField("8", m_record?.PackageName);

            (int downloadedCount, int totalCount) = ModpackPresenceRules.GetDownloadCounts(m_record, m_screen.EnsurePresenceSnapshot());
            m_downloadStatusLabel = new LabelWidget
            {
                Text = string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get(fName, "9"),
                    downloadedCount,
                    totalCount),
                Margin = margin
            };
            contentPanel.Children.Add(m_downloadStatusLabel);

            contentPanel.Children.Add(new LabelWidget
            {
                Text = LanguageControl.Get(fName, "ItemsList"),
                Margin = margin
            });
            m_itemsListValueLabel = new LabelWidget { WordWrap = true };
            contentPanel.Children.Add(m_itemsListValueLabel);

            RefreshDownloadStatusRow();
            m_itemsListValueLabel.Text = BuildItemsListText();

            RefreshActionState();
            m_lastBusy = DownloadFlowManager.Instance.ModpackDownloadBusy;
            m_lastLoading = m_screen.IsModpackFileInfoLoading(m_item);
            m_screen.StartModpackFileInfoLoad(m_item, m_record);
        }

        public override void Update()
        {
            // 打开期间轮询宿主屏的 fileId 补全结果，有新条目落地即重绑清单行。
            m_screen.TryTakeModpackFileInfoUpdates(m_record, out bool changed);
            if (changed)
            {
                m_itemsListValueLabel.Text = BuildItemsListText();
                RefreshDownloadStatusRow();
                RefreshActionState();
            }

            // 下载任务成败均无回调直达对话框（宿主屏靠 PackDownloadCompleted 事件，但 busy
            // 还含清单拉取任务），边沿轮询 busy/loading 变化恢复按钮文本并同步持有状态行。
            bool busy = DownloadFlowManager.Instance.ModpackDownloadBusy;
            bool loading = m_screen.IsModpackFileInfoLoading(m_item);
            if (busy != m_lastBusy || loading != m_lastLoading)
            {
                m_lastBusy = busy;
                m_lastLoading = loading;
                RefreshDownloadStatusRow();
                m_itemsListValueLabel.Text = BuildItemsListText();
                RefreshActionState();
            }
            if (m_toggleEnabledButton.IsClicked)
            {
                m_screen.ToggleSelectedModpackEnabled();
                RefreshActionState();
            }
            if (m_downloadMissingButton.IsClicked)
            {
                m_screen.DownloadSelectedModpackMissingItems();
                RefreshActionState();
            }
            if (m_deleteButton.IsClicked)
            {
                // 返回 true = 本次点击已被处理（含激活中拒绝分支已弹提示），对话框随之关闭。
                if (m_screen.TryDeleteModpack(m_item, m_record))
                {
                    DialogsManager.HideDialog(this);
                    return;
                }
            }
            if (m_uploadButton.IsClicked)
            {
                WebBrowserManager.LaunchBrowser(ModsManageContentScreen.GetChinesePublishPostUrl());
            }
            if (Input.Back
                || Input.Cancel
                || m_closeButton.IsClicked
                || (Input.Tap.HasValue && !HitTest(Input.Tap.Value)))
            {
                DialogsManager.HideDialog(this);
            }
        }

        /// <summary>动作按钮启用态与进行中文案（启用/禁用取语言静态词，与模组管理屏一致）。</summary>
        private void RefreshActionState()
        {
            bool busy = DownloadFlowManager.Instance.ModpackDownloadBusy;
            bool loading = m_screen.IsModpackFileInfoLoading(m_item);
            bool active = m_screen.IsModpackActive(m_item);
            m_toggleEnabledButton.IsEnabled = !busy;
            m_toggleEnabledButton.Text = active ? LanguageControl.Disable : LanguageControl.Enable;
            m_downloadMissingButton.IsEnabled = m_screen.GetMissingDownloadItemCount(m_record) > 0 && !busy && !loading;
            m_downloadMissingButton.Text = loading
                ? LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "LoadingFileInfo")
                : busy
                    ? LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "Downloading")
                    : LanguageControl.Get("ScbbsPlusCommon", "Text", "Download");
        }

        /// <summary>子项持有状态行：x/x 随 fileId 补全刷新，颜色按齐全度分级，激活中追加激活标记。</summary>
        private void RefreshDownloadStatusRow()
        {
            (int downloadedCount, int totalCount) = ModpackPresenceRules.GetDownloadCounts(m_record, m_screen.EnsurePresenceSnapshot());
            bool isActive = m_screen.IsModpackActive(m_item);
            string statusText = string.Format(
                CultureInfo.InvariantCulture,
                LanguageControl.Get(fName, "9"),
                downloadedCount,
                totalCount);
            if (isActive)
            {
                statusText += " | " + LanguageControl.Get(fName, "Active");
            }
            m_downloadStatusLabel.Text = statusText;
            m_downloadStatusLabel.Color = isActive || (downloadedCount >= totalCount && totalCount > 0)
                ? new Color(120, 220, 140)
                : downloadedCount > 0 ? new Color(245, 200, 120) : Color.White;
        }

        /// <summary>逐子项清单：`- [已存在/内置/需下载][类型] 文件名`（压缩包引用缺失归入需下载）。</summary>
        private string BuildItemsListText()
        {
            if (m_record?.Manifest?.Items == null || m_record.Manifest.Items.Length == 0)
            {
                return LanguageControl.None;
            }

            ModpackPresenceSnapshot snapshot = m_screen.EnsurePresenceSnapshot();
            List<string> lines = [];
            foreach (ModpackItem item in m_record.Manifest.Items)
            {
                if (item == null || !item.IsEnabled)
                {
                    continue;
                }

                bool bundled = ModpackPresenceRules.IsEmbeddedItem(m_record, item);
                bool installed = !bundled && ModpackPresenceRules.IsItemDownloaded(item, snapshot, m_record);
                string state = bundled
                    ? LanguageControl.Get(fName, "StateBundled")
                    : installed
                        ? LanguageControl.Get(fName, "StateExists")
                        : LanguageControl.Get(fName, "StateNeedDownload");
                lines.Add($"- [{state}][{GetModpackItemCategoryName(item)}] {ModpacksManageContentScreen.GetModpackItemDisplayName(item)}");
            }

            return lines.Count == 0 ? LanguageControl.None : string.Join("\n", lines);
        }

        private static string GetModpackItemCategoryName(ModpackItem item)
        {
            ExternalContentType type = ModpackRules.ResolveContentType(item);
            return EnumDisplayName.GetEnumDisplayName(
                CommunityFileRules.MapExternalContentTypeToFileType(type),
                item?.Category);
        }
    }
}
