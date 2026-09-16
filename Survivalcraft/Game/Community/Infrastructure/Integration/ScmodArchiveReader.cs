using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

    /// <summary>
    /// scmod 归档读取器：把"游戏内置弱加密前缀 + 可选杂质前缀 + zip 归档"统一为普通
    /// ZipArchive 视图，再按需读取根目录条目（modinfo.json / icon.webp / icon.png）。
    /// 只读不改盘——解密仅发生在内存，导入/更新一律透传原始字节（密文落盘，引擎加载期
    /// 自解密，见 ModsManager.GetScmods）。读取入口两个原语：OpenArchive + TryReadRootEntry；
    /// ReadModInfo/TryReadModInfo 是"取 modinfo.json 并反序列化"的组合糖。
    /// </summary>
    internal static class ScmodArchiveReader
    {
        /// <summary>归档结构无效时的统一展示文案（原 GetFriendlyScmodArchiveError 的归一目标）。</summary>
        public static string InvalidArchiveFriendlyMessage => LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "DownloadedArchiveCorrupt");

        /// <summary>
        /// 打开 scmod 数据的归档视图：缓冲整个流，识别并去除游戏内置弱加密前缀
        /// （HeadingCode 前缀整体倒序、HeadingCode2 前缀交织；GetDecipherStream 对明文
        /// 原样透传，重复处理幂等），扫描真实 zip 头并截掉其前的杂质字节。
        /// 结构无效（非 zip / 中央目录损坏）抛 <see cref="InvalidScmodArchiveException"/>。
        /// 返回的 ZipArchive 由本方法创建的内存流支撑，随 using 释放。
        /// </summary>
        public static System.IO.Compression.ZipArchive OpenArchive(Stream stream)
        {
            byte[] data = ReadStreamData(stream);
            if (data.Length == 0)
            {
                throw new InvalidOperationException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "UpdatePackageDataIsEmpty"));
            }
            if (data.Length >= MinEncryptedHeaderLength)
            {
                data = DecipherBytes(data);
            }

            int zipStart = FindScmodZipStart(data);
            if (zipStart < 0)
            {
                throw new InvalidScmodArchiveException(ScmodArchiveFailureKind.NotAZipArchive);
            }

            if (zipStart > 0)
            {
                byte[] normalized = new byte[data.Length - zipStart];
                Buffer.BlockCopy(data, zipStart, normalized, 0, normalized.Length);
                data = normalized;
            }

            // 中央目录损坏时 ZipArchive 以英文 InvalidDataException 抛出——就地转为
            // 类型化失败，不让英文原文穿越层界、也不再依赖字符串嗅探分类（G2）。
            try
            {
                return new System.IO.Compression.ZipArchive(new MemoryStream(data, writable: false), System.IO.Compression.ZipArchiveMode.Read, leaveOpen: false);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidScmodArchiveException(ScmodArchiveFailureKind.NotAZipArchive, ex);
            }
        }

        /// <summary>
        /// 读取归档根目录条目的原始字节。仅接受根条目（含路径分隔符即拒绝）；
        /// 解压上限 + 输出复核双保险，防恶意 scmod 的压缩炸弹拖垮内存（不单信可伪造的
        /// 中央目录 entry.Length）。条目不存在返回 false。
        /// </summary>
        public static bool TryReadRootEntry(System.IO.Compression.ZipArchive archive, string entryName, out byte[] entryData)
        {
            if (string.IsNullOrWhiteSpace(entryName)
                || entryName.IndexOf('/') >= 0
                || entryName.IndexOf('\\') >= 0)
            {
                throw new ArgumentException(LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "EntryNameMustBeInArchiveRoot"), nameof(entryName));
            }

            System.IO.Compression.ZipArchiveEntry entry = archive.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.FullName.Replace('\\', '/'), entryName, StringComparison.OrdinalIgnoreCase));
            if (entry == null || entry.Length > MaxRootEntryDataSize)
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

        /// <summary>读取并反序列化根目录 modinfo.json；条目缺失抛
        /// <see cref="InvalidScmodArchiveException"/>（MissingModInfo），JSON 损坏返回 null。</summary>
        public static ModInfo ReadModInfo(Stream stream)
        {
            if (!TryReadModInfo(stream, out ModInfo modInfo))
            {
                throw new InvalidScmodArchiveException(ScmodArchiveFailureKind.MissingModInfo);
            }
            return modInfo;
        }

        /// <summary>同 <see cref="ReadModInfo"/>，条目缺失改返回 false，不抛。</summary>
        public static bool TryReadModInfo(Stream stream, out ModInfo modInfo)
        {
            using System.IO.Compression.ZipArchive archive = OpenArchive(stream);
            // 与引擎读 modinfo 的历史行为一致：BOM 检测并剥离。
            if (!TryReadRootEntry(archive, "modinfo.json", out byte[] entryData))
            {
                modInfo = null;
                return false;
            }

            using MemoryStream entryStream = new(entryData, writable: false);
            using StreamReader reader = new(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            modInfo = TryDeserializeModInfo(reader.ReadToEnd());
            return true;
        }

        // 引擎 DeserializeJson 对损坏 JSON 抛裸 JsonException，这里归一化为类型化 null。
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

        // 游戏内置弱加密的两种魔数前缀的最小长度。短于此长度的数据不可能携带有效密文，
        // 跳过解码以免 GetDecipherStream 越界访问，交由后续 zip 头扫描给出可识别错误。
        private static readonly int MinEncryptedHeaderLength = Math.Max(
            Encoding.UTF8.GetByteCount(ModsManager.HeadingCode),
            Encoding.UTF8.GetByteCount(ModsManager.HeadingCode2));

        // 根条目（modinfo.json/icon.webp/icon.png）只应是小文件：拒绝超限解压。
        private const int MaxRootEntryDataSize = 4 * 1024 * 1024;

        // zip 本地文件头紧跟（解密后的）加密前缀，前缀仅约百字节；64KB 扫描上限余量巨大，
        // 避免对 MB 级归档全量线性扫描。找不到视为无 zip 头，报可识别错误。
        private const int MaxZipStartScanLength = 64 * 1024;

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

        private static byte[] ReadStreamData(Stream stream)
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
    }
}
