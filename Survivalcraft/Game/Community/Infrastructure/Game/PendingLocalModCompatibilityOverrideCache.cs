using System;
using System.Collections.Generic;
using System.Threading;
using Engine;

namespace Game
{
    internal sealed class PendingLocalModCompatibilityOverrideCache
    {
        private const int CacheCapacity = 1024;
        private readonly Lock m_sync = new();
        private readonly Dictionary<string, string> m_versions = new(StringComparer.OrdinalIgnoreCase);
        // 插入序队列：容量上限内的伪 LRU 淘汰（已卸载模组的包名不再无限累积）。
        private readonly Queue<string> m_order = new();

        public void Remember(string packageName, string fileName, string version)
        {
            string key = BuildKey(packageName, fileName);
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            lock (m_sync)
            {
                // 已存在的键只更新值不入队，避免同键重复入队导致弹出时误删新条目。
                if (m_versions.ContainsKey(key))
                {
                    m_versions[key] = ExternalVersionParser.Normalize(version);
                    return;
                }
                m_versions[key] = ExternalVersionParser.Normalize(version);
                m_order.Enqueue(key);
                while (m_order.Count > CacheCapacity)
                {
                    m_versions.Remove(m_order.Dequeue());
                }
            }
        }

        public bool Matches(string packageName, string filePathOrName, string version)
        {
            string key = BuildKey(packageName, filePathOrName);
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            string storedVersion;
            lock (m_sync)
            {
                if (!m_versions.TryGetValue(key, out storedVersion))
                {
                    return false;
                }
            }

            string currentVersion = ExternalVersionParser.Normalize(version);
            return string.IsNullOrWhiteSpace(storedVersion)
                || string.IsNullOrWhiteSpace(currentVersion)
                || string.Equals(storedVersion, currentVersion, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildKey(string packageName, string filePathOrName)
        {
            if (!string.IsNullOrWhiteSpace(packageName))
            {
                return packageName.Trim();
            }

            string fileName = Storage.GetFileNameWithoutExtensionBCL(Storage.GetFileName(filePathOrName ?? string.Empty));
            return string.IsNullOrWhiteSpace(fileName) ? string.Empty : fileName.Trim();
        }
    }
}
