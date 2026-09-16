using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using Engine;

namespace Game
{
    internal static class PendingLocalModMetadataReader
    {
        private const int CacheCapacity = 512;
        private static readonly Lock s_cacheSync = new();
        private static readonly Dictionary<string, CacheEntry> s_cache = new(StringComparer.OrdinalIgnoreCase);
        // 插入序队列：容量上限内的伪 LRU 淘汰（卸载的 mod 路径不再无限累积）。
        private static readonly Queue<string> s_cacheOrder = new();

        public static PendingLocalModInfo Read(string path)
        {
            string cacheKey = NormalizeStoragePath(path);
            DateTime lastWriteTime = TryGetStorageFileLastWriteTime(path);
            lock (s_cacheSync)
            {
                if (!string.IsNullOrWhiteSpace(cacheKey)
                    && s_cache.TryGetValue(cacheKey, out CacheEntry cached)
                    && cached.LastWriteTime == lastWriteTime)
                {
                    return cached.Info;
                }
            }

            PendingLocalModInfo info = null;
            try
            {
                using Stream stream = Storage.OpenFile(path, OpenFileMode.Read);
                using System.IO.Compression.ZipArchive archive = ScmodArchiveReader.OpenArchive(stream);
                if (!ScmodArchiveReader.TryReadRootEntry(archive, "modinfo.json", out byte[] entryData))
                {
                    Cache(cacheKey, lastWriteTime, null);
                    return null;
                }

                // 与读器同约定：BOM 检测并剥离，避免污染 JSON 反序列化。
                using MemoryStream entryStream = new(entryData, writable: false);
                using StreamReader reader = new(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                info = ParsePendingInfo(reader.ReadToEnd());
            }
            catch (InvalidScmodArchiveException)
            {
                // 非法/过短/非 zip 文件按无效包静默处理（不打日志刷屏）。
                Cache(cacheKey, lastWriteTime, null);
                return null;
            }
            catch (Exception ex)
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusPendingLocalModMetadataReader", "ReadPendingModMetadataFailed"), path, ex.Message));
            }

            Cache(cacheKey, lastWriteTime, info);
            return info;
        }

        public static string NormalizeStoragePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').Trim();
        }

        /// <summary>modinfo.json → 待重启模组元数据投影（依赖范围一并解析）。</summary>
        public static PendingLocalModInfo ParsePendingInfo(string modInfoJson)
        {
            if (string.IsNullOrWhiteSpace(modInfoJson))
            {
                return null;
            }

            ModInfo modInfo = ModsManager.DeserializeJson(modInfoJson);
            return new PendingLocalModInfo
            {
                Name = modInfo.Name,
                Version = modInfo.Version,
                PackageName = modInfo.PackageName,
                Author = modInfo.Author,
                Link = modInfo.Link,
                ApiVersion = modInfo.ApiVersion,
                ScVersion = modInfo.ScVersion,
                Dependencies = ModDependencyParser.GetDependencies(modInfo)
            };
        }

        private static void Cache(string cacheKey, DateTime lastWriteTime, PendingLocalModInfo info)
        {
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                return;
            }

            lock (s_cacheSync)
            {
                // 已存在的键只更新值不入队，避免同键重复入队导致弹出时误删新条目。
                if (s_cache.ContainsKey(cacheKey))
                {
                    s_cache[cacheKey] = new CacheEntry
                    {
                        LastWriteTime = lastWriteTime,
                        Info = info
                    };
                    return;
                }
                s_cache[cacheKey] = new CacheEntry
                {
                    LastWriteTime = lastWriteTime,
                    Info = info
                };
                s_cacheOrder.Enqueue(cacheKey);
                while (s_cacheOrder.Count > CacheCapacity)
                {
                    s_cache.Remove(s_cacheOrder.Dequeue());
                }
            }
        }

        private static DateTime TryGetStorageFileLastWriteTime(string path)
        {
            try
            {
                return Storage.GetFileLastWriteTime(path);
            }
            catch
            {
                return DateTime.Now;
            }
        }

        private sealed class CacheEntry
        {
            public DateTime LastWriteTime { get; set; }

            public PendingLocalModInfo Info { get; set; }
        }
    }
}
