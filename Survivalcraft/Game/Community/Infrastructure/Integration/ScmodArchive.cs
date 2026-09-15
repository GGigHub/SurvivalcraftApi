using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace Game
{
    /// <summary>scmod 归档结构失败的分类：类型承载判定，不再字符串嗅探。</summary>
    internal enum ScmodArchiveFailureKind
    {
        /// <summary>数据不含可识别的 zip 归档（含中央目录损坏——曾以英文 ZipArchive 消息逃逸）。</summary>
        NotAZipArchive,
        /// <summary>归档可读但根目录缺少 modinfo.json。</summary>
        MissingModInfo
    }

    /// <summary>scmod 归档结构无效。消息仍是本地化展示文案（兜底直显），分类判定只看类型。</summary>
    internal sealed class InvalidScmodArchiveException : InvalidOperationException
    {
        public ScmodArchiveFailureKind Kind { get; }

        public InvalidScmodArchiveException(ScmodArchiveFailureKind kind, Exception innerException = null)
            : base(BuildMessage(kind), innerException)
        {
            Kind = kind;
        }

        private static string BuildMessage(ScmodArchiveFailureKind kind)
        {
            return kind == ScmodArchiveFailureKind.MissingModInfo
                ? LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "ModinfoMissingInPackage")
                : LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "FileNotScmodArchive");
        }
    }

    internal static class ScmodArchive
    {
        // 游戏内置弱加密的两种魔数前缀的最小长度。短于此长度的数据不可能携带有效密文，
        // 跳过解码以免 GetDecipherStream 越界访问，交由后续 zip 头扫描给出可识别错误。
        internal static readonly int MinEncryptedHeaderLength = Math.Max(
            Encoding.UTF8.GetByteCount(ModsManager.HeadingCode),
            Encoding.UTF8.GetByteCount(ModsManager.HeadingCode2));

        // 解密输出按输入数组引用缓存：同一 scmod 数据读 modinfo.json 与图标等多次解析时
        // 不重复整体解密。ConditionalWeakTable 弱引用键，输入数组被 GC 后条目自动回收。
        // 契约：返回的数组由缓存共享，调用方不得原地修改（当前全部调用方只读）。
        private static readonly ConditionalWeakTable<byte[], byte[]> s_decipheredCache = new();

        public static string ImportExternalContentSyncNormalized(Stream stream, ExternalContentType type, string fileName)
        {
            if (type != ExternalContentType.Mod)
            {
                return ExternalContentManager.ImportExternalContentSync(stream, type, fileName);
            }

            using MemoryStream normalizedStream = new(ReadDecodedScmodArchiveData(stream), writable: false);
            return ExternalContentManager.ImportExternalContentSync(normalizedStream, type, fileName);
        }

        public static byte[] ReadStreamData(Stream stream)
        {
            if (!stream.CanSeek)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "UpdatePackageDataUnreadable"));
            }

            long originalPosition = stream.Position;
            try
            {
                stream.Position = 0;
                using MemoryStream dataStream = new();
                stream.CopyTo(dataStream);
                return dataStream.ToArray();
            }
            finally
            {
                stream.Position = originalPosition;
            }
        }

        /// <summary>归档结构无效时的统一展示文案（原 GetFriendlyScmodArchiveError 的归一目标）。</summary>
        public static string InvalidArchiveFriendlyMessage => LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "DownloadedArchiveCorrupt");

        public static byte[] NormalizeScmodArchiveData(Stream stream)
        {
            return NormalizeScmodArchiveData(ReadStreamData(stream));
        }

        public static byte[] NormalizeScmodArchiveData(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "UpdatePackageDataIsEmpty"));
            }

            // 统一解码游戏内置弱加密的 scmod（HeadingCode 前缀整体倒序、HeadingCode2 前缀交织）。
            // GetDecipherStream 对无前缀的明文 zip 原样返回，故对普通包无副作用，且重复调用幂等。
            if (data.Length >= MinEncryptedHeaderLength)
            {
                // TryGetValue+Add 非原子：并发归一化同一数据时第二次 Add 会抛 ArgumentException。
                data = s_decipheredCache.GetOrAdd(data, static d => DecipherBytes(d));
            }

            int zipStart = FindScmodZipStart(data);
            if (zipStart < 0)
            {
                throw new InvalidScmodArchiveException(ScmodArchiveFailureKind.NotAZipArchive);
            }
            if (zipStart == 0)
            {
                return data;
            }

            byte[] normalized = new byte[data.Length - zipStart];
            Buffer.BlockCopy(data, zipStart, normalized, 0, normalized.Length);
            return normalized;
        }

        public static ModInfo ReadModInfoFromScmodData(byte[] data)
        {
            if (!TryReadModInfoFromScmodData(data, out ModInfo modInfo))
            {
                throw new InvalidScmodArchiveException(ScmodArchiveFailureKind.MissingModInfo);
            }

            return modInfo;
        }

        public static ModInfo ReadModInfoFromScmodData(Stream stream)
        {
            if (!TryReadModInfoFromScmodData(stream, out ModInfo modInfo))
            {
                throw new InvalidScmodArchiveException(ScmodArchiveFailureKind.MissingModInfo);
            }

            return modInfo;
        }

        public static bool TryReadModInfoFromScmodData(byte[] data, out ModInfo modInfo)
        {
            if (!TryReadRootEntryText(data, "modinfo.json", out string modInfoJson))
            {
                modInfo = null;
                return false;
            }

            modInfo = TryDeserializeModInfo(modInfoJson);
            return true;
        }

        public static bool TryReadModInfoFromScmodData(Stream stream, out ModInfo modInfo)
        {
            if (!TryReadRootEntryText(stream, "modinfo.json", out string modInfoJson))
            {
                modInfo = null;
                return false;
            }

            modInfo = TryDeserializeModInfo(modInfoJson);
            return true;
        }

        public static bool TryReadRootEntryText(Stream stream, string entryName, out string text)
        {
            return TryReadRootEntryText(NormalizeScmodArchiveData(stream), entryName, out text);
        }

        // 引擎 DeserializeJson 对损坏 JSON 抛裸 JsonException，这里归一化为类型化 false。
        private static ModInfo TryDeserializeModInfo(string modInfoJson)
        {
            try
            {
                return ModsManager.DeserializeJson(modInfoJson);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static bool TryReadRootEntryText(byte[] data, string entryName, out string text)
        {
            if (!TryReadRootEntryData(data, entryName, out byte[] entryData))
            {
                text = null;
                return false;
            }

            using MemoryStream entryStream = new(entryData, writable: false);
            using StreamReader reader = new(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
            return true;
        }

        // 根条目（modinfo.json/icon.webp/icon.png）只应是小文件：拒绝超限解压，防恶意 scmod 的压缩炸弹拖垮内存。
        // 解压循环同时复核实际输出，不单信中央目录可伪造的 entry.Length。
        private const int MaxRootEntryDataSize = 4 * 1024 * 1024;

        public static bool TryReadRootEntryData(byte[] data, string entryName, out byte[] entryData)
        {
            if (string.IsNullOrWhiteSpace(entryName)
                || entryName.IndexOf('/') >= 0
                || entryName.IndexOf('\\') >= 0)
            {
                throw new ArgumentException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "EntryNameMustBeInArchiveRoot"), nameof(entryName));
            }

            byte[] normalizedData = NormalizeScmodArchiveData(data);
            using MemoryStream archiveStream = new(normalizedData, writable: false);
            // 中央目录损坏时 ZipArchive 以英文 InvalidDataException 抛出——就地转为
            // 类型化失败，不让英文原文穿越层界、也不再依赖字符串嗅探分类（G2）。
            System.IO.Compression.ZipArchive archive;
            try
            {
                archive = new System.IO.Compression.ZipArchive(
                    archiveStream,
                    ZipArchiveMode.Read,
                    leaveOpen: false);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidScmodArchiveException(ScmodArchiveFailureKind.NotAZipArchive, ex);
            }

            using (archive)
            {
                System.IO.Compression.ZipArchiveEntry entry = archive.Entries.FirstOrDefault(candidate =>
                    string.Equals(candidate.FullName.Replace('\\', '/'), entryName, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    entryData = null;
                    return false;
                }
                if (entry.Length > MaxRootEntryDataSize)
                {
                    entryData = null;
                    return false;
                }

                using Stream entryStream = entry.Open();
                using MemoryStream dataStream = new();
                byte[] buffer = new byte[81920];
                int totalRead = 0;
                int read;
                while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    totalRead += read;
                    if (totalRead > MaxRootEntryDataSize)
                    {
                        entryData = null;
                        return false;
                    }
                    dataStream.Write(buffer, 0, read);
                }
                entryData = dataStream.ToArray();
                return true;
            }
        }

        private static byte[] ReadDecodedScmodArchiveData(Stream stream)
        {
            return NormalizeScmodArchiveData(stream);
        }

        // 复用游戏 ModsManager 的反混淆实现，确保与游戏本体编解码完全一致。
        // 注意 GetDecipherStream 会 Dispose 传入的 stream，因此这里用只读 MemoryStream 包装字节数组。
        private static byte[] DecipherBytes(byte[] data)
        {
            using MemoryStream input = new(data, writable: false);
            using Stream decoded = ModsManager.GetDecipherStream(input);
            using MemoryStream output = new();
            decoded.CopyTo(output);
            return output.ToArray();
        }

        // zip 本地文件头紧跟（解密后的）加密前缀，前缀仅约百字节；64KB 扫描上限余量巨大，
        // 避免对 MB 级归档全量线性扫描。找不到视为无 zip 头，报可识别错误。
        private const int MaxZipStartScanLength = 64 * 1024;

        private static int FindScmodZipStart(byte[] data)
        {
            int scanLength = Math.Min(data.Length, MaxZipStartScanLength);
            for (int i = 0; i <= scanLength - 4; i++)
            {
                if (data[i] == 0x50
                    && data[i + 1] == 0x4B
                    && data[i + 2] == 0x03
                    && data[i + 3] == 0x04)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
