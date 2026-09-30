using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Engine;

namespace Game
{
    internal sealed class ManagedModSet
    {
        public HashSet<string> PackageNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> PackageVersionKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FileKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal static class ModpackModMatcher
    {
        public static bool HasManagedMods(LocalModpackRecord record)
        {
            ManagedModSet managedMods = BuildManagedSet(record);
            return managedMods.PackageNames.Count > 0
                || managedMods.PackageVersionKeys.Count > 0
                || managedMods.FileKeys.Count > 0;
        }

        public static IReadOnlyList<ModpackItem> GetManagedModItems(LocalModpackRecord record)
        {
            if (record?.Manifest?.Items == null || record.Manifest.Items.Length == 0)
            {
                return [];
            }

            return record.Manifest.Items
                .Where(item => item != null
                    && item.IsEnabled
                    && ModpackRules.ResolveContentType(item) == ExternalContentType.Mod)
                .ToArray();
        }

        public static ManagedModSet BuildManagedSet(LocalModpackRecord record)
        {
            return BuildManagedSet(GetManagedModItems(record));
        }

        public static ManagedModSet BuildManagedSet(IEnumerable<ModpackItem> items)
        {
            ManagedModSet result = new();
            foreach (ModpackItem item in items ?? [])
            {
                if (item == null || !item.IsEnabled || ModpackRules.ResolveContentType(item) != ExternalContentType.Mod)
                {
                    continue;
                }

                string fileKey = NormalizeMatchKey(item.Filename);
                if (!string.IsNullOrWhiteSpace(fileKey))
                {
                    result.FileKeys.Add(fileKey);
                }

                string packageName = item.PackageName?.Trim();
                if (string.IsNullOrWhiteSpace(packageName))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.Version))
                {
                    result.PackageNames.Add(packageName);
                }
                else
                {
                    result.PackageVersionKeys.Add(MakePackageVersionKey(packageName, item.Version));
                }
            }
            return result;
        }

        public static bool IsManaged(
            ManagedModSet managedMods,
            string packageName,
            string version,
            string filePath)
        {
            if (managedMods == null)
            {
                return false;
            }

            packageName = packageName?.Trim();
            version = version?.Trim();
            if (!string.IsNullOrWhiteSpace(packageName)
                && ((!string.IsNullOrWhiteSpace(version) && managedMods.PackageVersionKeys.Contains(MakePackageVersionKey(packageName, version)))
                    || managedMods.PackageNames.Contains(packageName)))
            {
                return true;
            }

            string fileKey = NormalizeMatchKey(filePath);
            return !string.IsNullOrWhiteSpace(fileKey) && managedMods.FileKeys.Contains(fileKey);
        }

        public static string NormalizeMatchKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string candidate = text.Trim().Replace('\\', '/');
            try
            {
                // 模组 FileNameRules.GetFileNameWithoutExtension → 本体 Storage.GetFileNameWithoutExtensionBCL（BCL 语义，点文件保留全名）。
                candidate = Storage.GetFileNameWithoutExtensionBCL(candidate);
            }
            catch
            {
                // 清单文件名含非法路径字符时 Storage 解析可能抛：回退为空键（不匹配），
                // 而不是带着未去扩展名的半处理结果继续，保证匹配行为确定。
                return string.Empty;
            }

            StringBuilder builder = new(candidate.Length);
            foreach (char ch in candidate)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
            }
            return builder.ToString();
        }

        private static string MakePackageVersionKey(string packageName, string version)
        {
            return $"{packageName?.Trim()}\n{version?.Trim()}";
        }
    }
}
