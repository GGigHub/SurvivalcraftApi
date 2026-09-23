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

            m_titleLabel.Text = string.Format(
                CultureInfo.InvariantCulture,
                LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "Info"),
                LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "Modpack"));

            // fileId 补全先行应用缓存，再对缺名条目复用宿主屏的补全任务槽。
            m_screen.ApplyCachedFileInfo(m_record);

            Vector2 margin = new Vector2(0f, 4f);
            string displayName = m_item?.DisplayName ?? m_item?.Name ?? "-";
            contentPanel.Children.Add(new LabelWidget
            {
                Text = $"{LanguageControl.Get(fName, "1")}{displayName}",
                Margin = margin
            });
            contentPanel.Children.Add(new LabelWidget
            {
                Text = $"{LanguageControl.Get(fName, "2")}{TryGetFileSizeText(m_item?.LocalFilePath)}",
                Margin = margin
            });
            string manifestFileName = Storage.GetFileName(m_item?.LocalFilePath ?? string.Empty);
            contentPanel.Children.Add(new LabelWidget
            {
                Text = $"{LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "ManifestFile")}{(string.IsNullOrWhiteSpace(manifestFileName) ? "-" : manifestFileName)}",
                WordWrap = true,
                Margin = margin
            });
            contentPanel.Children.Add(new LabelWidget
            {
                Text = $"{LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "Description")}{(string.IsNullOrWhiteSpace(m_record?.Description) ? LanguageControl.None : m_record.Description)}",
                WordWrap = true,
                Margin = margin
            });
            if (string.IsNullOrWhiteSpace(m_record?.SourceUrl))
            {
                contentPanel.Children.Add(new LabelWidget
                {
                    Text = $"{LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "SourceURL")}{LanguageControl.None}",
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
                        new LabelWidget { Text = LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "SourceURL") },
                        new LinkWidget { Text = m_record.SourceUrl, Url = m_record.SourceUrl, Color = new Color(64, 192, 64) }
                    },
                    Margin = margin
                });
            }
            (int downloadedCount, int totalCount) = ModpackPresenceRules.GetDownloadCounts(m_record, m_screen.EnsurePresenceSnapshot());
            contentPanel.Children.Add(new LabelWidget
            {
                Text = $"{LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "Items")}{totalCount.ToString(CultureInfo.InvariantCulture)}",
                Margin = margin
            });
            m_downloadStatusLabel = new LabelWidget
            {
                Text = $"{LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "DownloadStatus")}",
                Margin = margin
            };
            contentPanel.Children.Add(m_downloadStatusLabel);
            if (!string.IsNullOrWhiteSpace(m_record?.Version))
            {
                contentPanel.Children.Add(new LabelWidget
                {
                    Text = $"{LanguageControl.Get(fName, "3")}{m_record.Version}",
                    Margin = margin
                });
            }
            if (!string.IsNullOrWhiteSpace(m_record?.PackageName))
            {
                contentPanel.Children.Add(new LabelWidget
                {
                    Text = $"{LanguageControl.Get(fName, "4")}{m_record.PackageName}",
                    WordWrap = true,
                    Margin = margin
                });
            }
            contentPanel.Children.Add(new LabelWidget
            {
                Text = LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "ItemsList"),
                Margin = margin
            });
            m_itemsListValueLabel = new LabelWidget { WordWrap = true };
            contentPanel.Children.Add(m_itemsListValueLabel);

            RefreshDownloadStatusRow();
            m_itemsListValueLabel.Text = BuildItemsListText();

            RefreshActionState();
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

        /// <summary>动作按钮启用态与进行中文案（与模组本地详情面板同规则）。</summary>
        private void RefreshActionState()
        {
            bool busy = DownloadFlowManager.Instance.ModpackDownloadBusy;
            bool loading = m_screen.IsModpackFileInfoLoading(m_item);
            bool active = m_screen.IsModpackActive(m_item);
            m_toggleEnabledButton.IsEnabled = !busy;
            m_toggleEnabledButton.Text = active
                ? LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "Close")
                : LanguageControl.Get("ScbbsPlusCommunityScreen", "Core", "LocalResources", "Launch");
            m_downloadMissingButton.IsEnabled = m_screen.GetMissingDownloadItemCount(m_record) > 0 && !busy && !loading;
            m_downloadMissingButton.Text = loading
                ? LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "LoadingFileInfo")
                : busy
                    ? LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "Downloading")
                    : LanguageControl.Get("ScbbsPlusCommon", "Text", "Download");
        }

        /// <summary>下载状态行：已下载 x/y，颜色按齐全度分级，激活中追加激活标记。</summary>
        private void RefreshDownloadStatusRow()
        {
            (int downloadedCount, int totalCount) = ModpackPresenceRules.GetDownloadCounts(m_record, m_screen.EnsurePresenceSnapshot());
            bool isActive = m_screen.IsModpackActive(m_item);
            string statusText = string.Format(
                CultureInfo.InvariantCulture,
                LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "Downloaded"),
                downloadedCount,
                totalCount);
            if (isActive)
            {
                statusText += " | " + LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "Active");
            }
            m_downloadStatusLabel.Text = $"{LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "DownloadStatus")}{statusText}";
            m_downloadStatusLabel.Color = isActive || (downloadedCount >= totalCount && totalCount > 0)
                ? new Color(120, 220, 140)
                : downloadedCount > 0 ? new Color(245, 200, 120) : Color.White;
        }

        private string BuildItemsListText()
        {
            if (m_record?.Manifest?.Items == null || m_record.Manifest.Items.Length == 0)
            {
                return LanguageControl.None;
            }

            ModpackPresenceSnapshot snapshot = m_screen.EnsurePresenceSnapshot();
            List<string> lines = [];
            int index = 0;
            foreach (ModpackItem item in m_record.Manifest.Items)
            {
                if (item == null || !item.IsEnabled)
                {
                    continue;
                }

                bool isMissingFromArchive = ModpackPresenceRules.IsEmbeddedItemMissing(m_record, item);
                bool isDownloaded = !isMissingFromArchive && ModpackPresenceRules.IsItemDownloaded(item, snapshot, m_record);
                string status = isMissingFromArchive
                    ? LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "NotFoundInArchive")
                    : isDownloaded
                        ? LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "DownloadedLabel")
                        : LanguageControl.Get("ScbbsPlusCommunityScreen", "Local", "Modpacks", "Page", "Missing");
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}. [{1}] {2} - {3}",
                    index + 1,
                    GetModpackItemCategoryName(item),
                    GetResolvedItemDisplayName(item, snapshot, isDownloaded),
                    status));
                index++;
            }

            return lines.Count == 0 ? LanguageControl.None : string.Join("\n", lines);
        }

        private string GetResolvedItemDisplayName(ModpackItem item, ModpackPresenceSnapshot snapshot, bool isDownloaded)
        {
            if (isDownloaded && ModpackPresenceRules.TryGetDownloadedFileName(item, snapshot, out string localFileName))
            {
                return localFileName;
            }

            return ModpacksManageContentScreen.GetModpackItemDisplayName(item);
        }

        private static string GetModpackItemCategoryName(ModpackItem item)
        {
            ExternalContentType type = ModpackRules.ResolveContentType(item);
            return type switch
            {
                ExternalContentType.World => EnumDisplayName.GetEnumDisplayName(ExternalContentType.World),
                ExternalContentType.FurniturePack => EnumDisplayName.GetEnumDisplayName(ExternalContentType.FurniturePack),
                ExternalContentType.BlocksTexture => EnumDisplayName.GetEnumDisplayName(ExternalContentType.BlocksTexture),
                ExternalContentType.CharacterSkin => EnumDisplayName.GetEnumDisplayName(ExternalContentType.CharacterSkin),
                ExternalContentType.Mod => EnumDisplayName.GetEnumDisplayName(ExternalContentType.Mod),
                _ => LanguageControl.Get("ScbbsPlusLocalScreen", "Text", "OtherFiles")
            };
        }

        /// <summary>
        /// 包体文件大小直读（列表里的文件必存在，取不到属异常，记警告后显示“-”降级）。
        /// </summary>
        private static string TryGetFileSizeText(string filePath)
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
    }
}
