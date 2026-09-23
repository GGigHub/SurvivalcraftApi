using System;
using System.Collections.Generic;
using System.Linq;
using Engine;

namespace Game
{
    /// <summary>
    /// 整合包子项相对本机的已装判定规则（自本地资源整合同页提取）：
    /// 输入 manifest 条目、整合包记录与 presence 快照，输出判定结果，不产 UI 文案、
    /// 不触引擎资源管理器（ WorldsManager 等）；个别路径工具走 Engine.Storage 静态纯函数。
    /// 快照的捕获与失效由 ModpackPresenceManager 负责。
    /// </summary>
    internal static class ModpackPresenceRules
    {
        /// <summary>启用条目总数与其中已在本机落地的数量（详情页/列表行统计）。</summary>
        public static (int DownloadedCount, int TotalCount) GetDownloadCounts(
            LocalModpackRecord record,
            ModpackPresenceSnapshot snapshot)
        {
            if (record?.Manifest?.Items == null || record.Manifest.Items.Length == 0)
            {
                return (0, 0);
            }

            int totalCount = 0;
            int downloadedCount = 0;
            foreach (ModpackItem item in record.Manifest.Items)
            {
                if (item == null || !item.IsEnabled)
                {
                    continue;
                }

                totalCount++;
                if (IsItemDownloaded(item, snapshot, record))
                {
                    downloadedCount++;
                }
            }

            return (downloadedCount, totalCount);
        }

        public static IReadOnlyList<ModpackItem> GetEmbeddedItems(
            LocalModpackRecord record,
            ModpackPresenceSnapshot snapshot)
        {
            return (record?.Manifest?.Items ?? [])
                .Where(item => item != null && item.IsEnabled && IsEmbeddedItem(record, item))
                // 自定义文件即使已存在也要重新应用；覆盖前会保存快照，关闭整合包时恢复。
                .Where(item => ModpackRules.IsCustomDataFile(item) || !IsItemInstalled(item, snapshot))
                .ToArray();
        }

        public static bool IsItemDownloaded(
            ModpackItem item,
            ModpackPresenceSnapshot snapshot,
            LocalModpackRecord record = null)
        {
            if (item == null)
            {
                return false;
            }

            // ZIP 整合包中的相对路径资源已经随整合包落盘，应当显示为已下载；
            // 真正启用时再从压缩包导入目标目录，避免详情页把内嵌文件误判成网络缺失项。
            if (IsEmbeddedItem(record, item))
            {
                return true;
            }

            return IsItemInstalled(item, snapshot);
        }

        public static bool IsItemInstalled(ModpackItem item, ModpackPresenceSnapshot snapshot)
        {
            if (item == null || snapshot == null)
            {
                return false;
            }

            ExternalContentType contentType = ModpackRules.ResolveContentType(item);
            string fileKey = ModpackModMatcher.NormalizeMatchKey(item.Filename);

            return contentType switch
            {
                ExternalContentType.World => snapshot.WorldKeys.Contains(fileKey),
                ExternalContentType.FurniturePack => snapshot.FurnitureKeys.Contains(fileKey),
                ExternalContentType.BlocksTexture => snapshot.TextureKeys.Contains(fileKey),
                ExternalContentType.CharacterSkin => snapshot.SkinKeys.Contains(fileKey),
                ExternalContentType.Mod => IsModDownloaded(item, snapshot, fileKey),
                _ => ModpackRules.IsCustomDataFile(item) && ModpackDataFiles.IsPresent(item)
            };
        }

        public static bool IsEmbeddedItem(LocalModpackRecord record, ModpackItem item)
        {
            string url = item?.Url?.Trim();
            return !string.IsNullOrWhiteSpace(url)
                && !ModpackRules.IsHttpUrl(url)
                && ModpackRules.TryGetEmbeddedFile(record?.EmbeddedFiles, url, out _);
        }

        public static bool IsEmbeddedItemMissing(LocalModpackRecord record, ModpackItem item)
        {
            string url = item?.Url?.Trim();
            return record?.EmbeddedFiles != null
                && !string.IsNullOrWhiteSpace(url)
                && !ModpackRules.IsHttpUrl(url)
                && string.IsNullOrWhiteSpace(item.FileId)
                && !ModpackRules.TryGetEmbeddedFile(record.EmbeddedFiles, url, out _);
        }

        private static bool IsModDownloaded(ModpackItem item, ModpackPresenceSnapshot snapshot, string fileKey)
        {
            string packageKey = ModpackModMatcher.NormalizeMatchKey(item.PackageName);
            return (!string.IsNullOrWhiteSpace(packageKey) && snapshot.ModPackageKeys.Contains(packageKey))
                || (!string.IsNullOrWhiteSpace(fileKey) && snapshot.ModFileKeys.Contains(fileKey));
        }

        /// <summary>已下载条目的本地落地文件名；未落地返回 false（详情页列表展示回退显示名）。</summary>
        public static bool TryGetDownloadedFileName(
            ModpackItem item,
            ModpackPresenceSnapshot snapshot,
            out string fileName)
        {
            fileName = null;
            if (item == null || snapshot == null)
            {
                return false;
            }

            if (ModpackRules.ResolveContentType(item) == ExternalContentType.Mod)
            {
                string packageKey = ModpackModMatcher.NormalizeMatchKey(item.PackageName);
                if (!string.IsNullOrWhiteSpace(packageKey)
                    && snapshot.ModPackageFileNames.TryGetValue(packageKey, out fileName)
                    && !string.IsNullOrWhiteSpace(fileName))
                {
                    return true;
                }
            }

            string itemFileKey = ModpackModMatcher.NormalizeMatchKey(item.Filename);
            if (!string.IsNullOrWhiteSpace(itemFileKey))
            {
                KeyValuePair<string, string> fileNameMatch = snapshot.LocalFileNames.FirstOrDefault(pair =>
                    string.Equals(pair.Key, itemFileKey, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(fileNameMatch.Value))
                {
                    fileName = fileNameMatch.Value;
                    return true;
                }
            }

            if (ModpackRules.IsCustomDataFile(item) && ModpackDataFiles.IsPresent(item))
            {
                try
                {
                    fileName = Storage.GetFileName(ModpackDataFiles.ResolveTargetPath(item));
                    return !string.IsNullOrWhiteSpace(fileName);
                }
                catch
                {
                    // 探测性质：目标路径解析失败即视为不在场，走默认分支。
                }
            }

            return false;
        }
    }
}
