using System;
using System.Collections.Generic;
using System.Linq;
using Engine;

namespace Game
{
    /// <summary>
    /// Adapts the game's loaded-mod and storage state for pending-mod queries:
    /// 磁盘递归枚举 .scmod（排除已加载路径）→ 待重启项投影 → 包集合汇总。
    /// 无实例状态，静态域类（对齐游戏本体风格）。
    /// </summary>
    internal static class PendingLocalModScanner
    {
        public static List<LocalResourceItem> Scan(
            Func<PendingLocalModInfo, bool> isIncompatible,
            Func<string, string, string, bool> hasCompatibilityOverride)
        {
            return ScanPendingMods(
                GetLoadedPaths(),
                PendingLocalModMetadataReader.Read,
                isIncompatible,
                hasCompatibilityOverride);
        }

        public static PendingModPackageSets GetPackageSets(
            Func<PendingLocalModInfo, bool> isIncompatible,
            Func<string, string, string, bool> hasCompatibilityOverride)
        {
            PendingModPackageSets result = new();
            foreach (LocalResourceItem item in Scan(isIncompatible, hasCompatibilityOverride))
            {
                PendingLocalModInfo info = item?.Tag as PendingLocalModInfo;
                string packageName = !string.IsNullOrWhiteSpace(info?.PackageName)
                    ? info.PackageName.Trim()
                    : Storage.GetFileNameWithoutExtensionBCL(Storage.GetFileName(item?.LocalFilePath ?? string.Empty));
                if (string.IsNullOrWhiteSpace(packageName))
                {
                    continue;
                }

                HashSet<string> target = item.IsIncompatibleWithCurrentGame
                    ? result.IncompatiblePackages
                    : result.PendingRestartPackages;
                target.Add(packageName);
                if (!string.IsNullOrWhiteSpace(info?.Version))
                {
                    target.Add(ModDependencyAvailabilityRules.MakePackageVersionKey(packageName, info.Version));
                }
            }
            return result;
        }

        public static List<LocalResourceItem> ScanPendingMods(
            IReadOnlySet<string> loadedPaths,
            Func<string, PendingLocalModInfo> readInfo,
            Func<PendingLocalModInfo, bool> isIncompatible,
            Func<string, string, string, bool> hasCompatibilityOverride)
        {
            List<LocalResourceItem> items = [];
            if (!Storage.DirectoryExists(ModsManager.ModsPath))
            {
                return items;
            }

            foreach (string path in EnumerateScmodFiles(ModsManager.ModsPath))
            {
                string normalizedPath = PendingLocalModMetadataReader.NormalizeStoragePath(path);
                if (loadedPaths?.Contains(normalizedPath) == true)
                {
                    continue;
                }

                PendingLocalModInfo info = readInfo?.Invoke(path);
                string fileName = Storage.GetFileName(path);
                bool incompatible = isIncompatible?.Invoke(info) == true;
                if (hasCompatibilityOverride?.Invoke(info?.PackageName, path, info?.Version) == true)
                {
                    incompatible = false;
                }

                items.Add(new LocalResourceItem
                {
                    Type = ExternalContentType.Mod,
                    Name = string.IsNullOrWhiteSpace(info?.PackageName) ? Storage.GetFileNameWithoutExtensionBCL(fileName) : info.PackageName,
                    DisplayName = string.IsNullOrWhiteSpace(info?.Name) ? Storage.GetFileNameWithoutExtensionBCL(fileName) : info.Name,
                    CreationTime = TryGetLastWriteTime(path),
                    LocalFilePath = path,
                    PackageName = info?.PackageName,
                    Version = info?.Version,
                    RequiresRestartToLoad = true,
                    IsIncompatibleWithCurrentGame = incompatible,
                    Tag = info
                });
            }
            return items;
        }

        public static IEnumerable<string> EnumerateScmodFiles(string path)
        {
            foreach (string fileName in Storage.ListFileNames(path))
            {
                if (string.Equals(Storage.GetExtensionBCL(fileName), ".scmod", StringComparison.OrdinalIgnoreCase))
                {
                    yield return Storage.CombinePaths(path, fileName);
                }
            }

            foreach (string directoryName in Storage.ListDirectoryNames(path))
            {
                foreach (string child in EnumerateScmodFiles(Storage.CombinePaths(path, directoryName)))
                {
                    yield return child;
                }
            }
        }

        private static DateTime TryGetLastWriteTime(string path)
        {
            try
            {
                // 引擎返回 Kind=Utc（GetLastWriteTimeUtc）；兜底必须同为 Utc，
                // 否则 OrderPendingMods 按 tick 排序时两来源错位一个时区偏移。
                return Storage.GetFileLastWriteTime(path);
            }
            catch
            {
                return DateTime.UtcNow;
            }
        }

        private static HashSet<string> GetLoadedPaths()
        {
            // Scan 由 LocalMods.Pending.cs 的 Task.Run 在后台线程执行，而 ModListAll
            // 在主线程列表重建（排序时 Clear+AddRange）期间不可并发枚举，故枚举封送
            // 主线程同步执行；主线程侧只轮询 IsCompleted 后读结果，无死锁。
            HashSet<string> loadedPaths = null;
            Dispatcher.Dispatch(() =>
            {
                loadedPaths = ModsManager.ModListAll
                    .Where(entity => entity?.ModFilePath != null)
                    .Select(entity => PendingLocalModMetadataReader.NormalizeStoragePath(entity.ModFilePath))
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }, waitUntilCompleted: true);
            return loadedPaths;
        }
    }

    internal sealed class PendingModPackageSets
    {
        public HashSet<string> PendingRestartPackages { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> IncompatiblePackages { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
