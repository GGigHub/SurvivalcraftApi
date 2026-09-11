using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Engine;
using NuGet.Versioning;

namespace Game
{
    /// <summary>
    /// 待加载模组后台扫描：冷启动时把解密、解压和 modinfo 解析移出渲染线程；
    /// 兼容性覆盖（下载前已判定的兼容版本）与版本文本兼容判定也在这里，
    /// 扫描谓词与覆盖查询构成同一判定上下文。
    /// 无 UI 依赖——扫描落地由轮询方逐次消费：UpdatePendingLocalModsScanTask 返回 true 表示本次有新结果。
    /// </summary>
    internal static class PendingLocalModScanState
    {
        private static readonly PendingLocalModCompatibilityOverrideCache s_compatibilityOverrides = new();
        private static Task<List<LocalResourceItem>> s_scanTask;
        private static List<LocalResourceItem> s_cachedPendingLocalMods;
        private static bool s_scanRescanRequested;

        public static List<LocalResourceItem> CachedPendingLocalMods => s_cachedPendingLocalMods;

        public static bool IsScanRunning => s_scanTask != null;

        public static void StartPendingLocalModsScan(bool invalidateCache = true)
        {
            if (invalidateCache)
            {
                s_cachedPendingLocalMods = null;
            }
            if (s_scanTask != null)
            {
                if (invalidateCache)
                {
                    s_scanRescanRequested = true;
                }
                return;
            }

            s_scanTask = StartScan();
        }

        // 返回 true 表示本次调用消费了一次扫描落地（结果已写入 CachedPendingLocalMods）。
        // 一次失效周期内的多次落地（重扫链）逐次返回 true，轮询方每次都据此刷新提示，
        // 避免重启提示计数停留在上一轮扫描结果。
        public static bool UpdatePendingLocalModsScanTask()
        {
            if (s_scanTask == null || !s_scanTask.IsCompleted)
            {
                return false;
            }

            try
            {
                s_cachedPendingLocalMods = s_scanTask.Result ?? [];
            }
            catch (Exception ex)
            {
                Log.Warning(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPendingLocalModScanState", "BackgroundScanFailed"), ex.Message));
                s_cachedPendingLocalMods = [];
            }
            s_scanTask = null;

            if (s_scanRescanRequested)
            {
                s_scanRescanRequested = false;
                s_scanTask = StartScan();
            }

            return true;
        }

        private static Task<List<LocalResourceItem>> StartScan()
        {
            return Task.Run(() => GamePendingLocalMods.Scan(
                IsPendingLocalModIncompatibleWithCurrentGame,
                HasPendingLocalModCompatibilityOverride));
        }

        public static int GetPendingRestartLocalModCount()
        {
            if (s_cachedPendingLocalMods != null)
            {
                return s_cachedPendingLocalMods.Count;
            }

            return GamePendingLocalMods.Scan(
                IsPendingLocalModIncompatibleWithCurrentGame,
                HasPendingLocalModCompatibilityOverride).Count;
        }

        public static bool HasPendingLocalModCompatibilityOverride(string packageName, string filePathOrName, string version)
        {
            return s_compatibilityOverrides.Matches(packageName, filePathOrName, version);
        }

        // 下载前已经判定为兼容的版本，在“已下载但未重启”阶段沿用这份判断，避免重新扫本地包后出现前后提示不一致。
        public static void RememberPendingLocalModCompatibilityOverride(string packageName, string fileName, PostVersionItem version)
        {
            if (version == null)
            {
                return;
            }

            bool isCompatible = ModCompatibility.IsCompatibleWithCurrentVersion(
                version,
                VersionCatalogCache.CurrentApiVersion,
                VersionCatalogCache.CurrentGameVersion,
                VersionCatalogCache.CachedGameVersionsById,
                VersionCatalogCache.CachedVersionCatalog);
            if (!isCompatible)
            {
                return;
            }

            string versionText = !string.IsNullOrWhiteSpace(version.Version)
                ? version.Version
                : version.Files?.FirstOrDefault(file => file != null)?.PackageVersion;
            s_compatibilityOverrides.Remember(packageName, fileName, versionText);
        }

        public static bool IsPendingLocalModIncompatibleWithCurrentGame(PendingLocalModInfo info)
        {
            if (info == null)
            {
                return false;
            }

            return IsVersionTextIncompatible(VersionCatalogCache.CurrentApiVersion, info.ApiVersion);
        }

        public static bool IsVersionTextIncompatible(string currentVersion, string candidateVersion)
        {
            // TryParse 内部已做 Trim+Normalize，无需再包一层。
            if (!ExternalVersionParser.TryParse(currentVersion, out NuGetVersion current)
                || !ExternalVersionParser.TryParse(candidateVersion, out NuGetVersion candidate))
            {
                return false;
            }

            if (current.Major != candidate.Major)
            {
                return true;
            }

            return current.Minor != candidate.Minor
                && !(current.Major == 1
                    && current.Minor >= 9
                    && candidate.Minor >= 8
                    && candidate.Minor <= current.Minor);
        }
    }
}
