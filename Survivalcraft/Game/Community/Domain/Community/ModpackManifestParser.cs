using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Engine;

namespace Game {
    sealed class ModpackManifestParser {
        // ZIP 内嵌条目大小上限：恶意/异常巨大条目 CopyTo 内存会耗尽（对比下载防护同量级）。
        const long MaxEmbeddedEntryBytes = 256L * 1024 * 1024;

        // 嵌入文件累计解压内存预算：多条中等条目叠加同样会耗尽内存。
        const long MaxTotalEmbeddedBytes = 512L * 1024 * 1024;

        // manifest 未声明 Icon 时的嵌入图标探测序：命名与优先级同游戏 scmod 约定统一
        // （icon.webp 优先、icon.png 兜底）。已发布数据文件用大写 Icon.*，保留为次级兜底，
        // 老包不丢图标；此前实现按 zip 条目序取首个命中，png/webp 并存时优先级不确定。
        static readonly string[] EmbeddedIconCandidates = ["icon.webp", "icon.png", "Icon.webp", "Icon.png"];

        // 依候选序返回存储的原样键：embeddedFiles 是大小写不敏感字典，
        // 不能把候选拼写直接当结果——图标加载方按嵌入时的原键取数据。
        static string FindEmbeddedIcon(IReadOnlyDictionary<string, byte[]> embeddedFiles) {
            foreach (string candidate in EmbeddedIconCandidates) {
                string stored = embeddedFiles.Keys.FirstOrDefault(key => string.Equals(key, candidate, StringComparison.Ordinal));
                if (stored != null) {
                    return stored;
                }
            }
            return null;
        }

        readonly JsonSerializerOptions m_jsonOptions = new() {
            ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, PropertyNameCaseInsensitive = true
        };

        public ModpackManifestLoadResult Parse(int requestId, byte[] data, string sourceUrl, string sourceFileName) {
            if (data == null
                || data.Length == 0) {
                return Error(
                    requestId,
                    data,
                    sourceUrl,
                    sourceFileName,
                    LanguageControl.Get("ModpackManifestParser", "TheModpackManifestIsEmptyOrInvalid")
                );
            }
            string sourceExtension = Storage.GetExtensionBCL(sourceFileName ?? string.Empty);
            bool isJsonSource = string.Equals(sourceExtension, ".json", StringComparison.OrdinalIgnoreCase);
            bool isZipSource = string.Equals(sourceExtension, ".zip", StringComparison.OrdinalIgnoreCase);
            if (!isJsonSource
                && !isZipSource) {
                return Error(requestId, data, sourceUrl, sourceFileName, LanguageControl.Get("ModpackManifestParser", "PackOnlySupportsZipOrJson"));
            }
            string manifestError = null;
            string jsonError = null;
            if (isJsonSource
                && TryReadJson(data, out ModpackManifest jsonManifest, out jsonError)
                && IsValidManifest(jsonManifest, out manifestError)
                && IsValidForFormat(jsonManifest, false, null, out manifestError)) {
                ModpackRules.NormalizeManifest(jsonManifest);
                return Success(
                    requestId,
                    data,
                    sourceUrl,
                    sourceFileName,
                    jsonManifest,
                    null,
                    false
                );
            }
            if (isJsonSource) {
                return Error(
                    requestId,
                    data,
                    sourceUrl,
                    sourceFileName,
                    jsonError ?? manifestError ?? LanguageControl.Get("ModpackManifestParser", "TheModpackManifestIsEmptyOrInvalid")
                );
            }
            try {
                using MemoryStream stream = new(data, false);
                using System.IO.Compression.ZipArchive archive = OpenArchive(stream);
                System.IO.Compression.ZipArchiveEntry manifestEntry = archive.Entries.FirstOrDefault(entry => string.Equals(
                        entry.FullName?.Trim().Replace('\\', '/'),
                        "packinfo.json",
                        StringComparison.OrdinalIgnoreCase
                    )
                );
                if (manifestEntry == null) {
                    return Error(
                        requestId,
                        data,
                        sourceUrl,
                        sourceFileName,
                        LanguageControl.Get("ModpackManifestParser", "PackinfoJsonNotFoundInZipRoot")
                    );
                }
                if (manifestEntry.Length > MaxEmbeddedEntryBytes) {
                    return Error(
                        requestId,
                        data,
                        sourceUrl,
                        sourceFileName,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("ModpackManifestParser", "ZipEntryExceedsSizeLimit"),
                            "packinfo.json"
                        )
                    );
                }
                using Stream entryStream = manifestEntry.Open();
                using MemoryStream manifestMemory = new();
                entryStream.CopyTo(manifestMemory);
                if (manifestMemory.Length > MaxEmbeddedEntryBytes) {
                    // 中央目录 entry.Length 可伪造，此处解压后按实际大小复核。嵌入文件循环
                    // 无此复核，由 .NET 读取路径（CrcValidatingReadStream）的 CRC 校验兜底。
                    return Error(
                        requestId,
                        data,
                        sourceUrl,
                        sourceFileName,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("ModpackManifestParser", "ZipEntryExceedsSizeLimit"),
                            "packinfo.json"
                        )
                    );
                }
                string zipManifestError = null;
                if (!TryReadJson(manifestMemory.ToArray(), out ModpackManifest zipManifest, out string zipJsonError)
                    || !IsValidManifest(zipManifest, out zipManifestError)) {
                    return Error(
                        requestId,
                        data,
                        sourceUrl,
                        sourceFileName,
                        zipJsonError ?? zipManifestError ?? LanguageControl.Get("ModpackManifestParser", "TheModpackManifestIsEmptyOrInvalid")
                    );
                }
                Dictionary<string, byte[]> embeddedFiles = new(StringComparer.OrdinalIgnoreCase);
                long totalEmbeddedBytes = 0;
                foreach (System.IO.Compression.ZipArchiveEntry entry in archive.Entries) {
                    string entryPath = NormalizeArchiveEntryPath(entry.FullName);
                    if (string.IsNullOrWhiteSpace(entryPath)
                        || string.Equals(entryPath, "packinfo.json", StringComparison.OrdinalIgnoreCase)
                        || entry.FullName.EndsWith("/", StringComparison.Ordinal)) {
                        continue;
                    }
                    if (entry.Length > MaxEmbeddedEntryBytes) {
                        return Error(
                            requestId,
                            data,
                            sourceUrl,
                            sourceFileName,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                LanguageControl.Get("ModpackManifestParser", "ZipEntryExceedsSizeLimit"),
                                entryPath
                            )
                        );
                    }
                    using Stream fileStream = entry.Open();
                    using MemoryStream fileMemory = new();
                    fileStream.CopyTo(fileMemory);
                    totalEmbeddedBytes += fileMemory.Length;
                    if (totalEmbeddedBytes > MaxTotalEmbeddedBytes) {
                        return Error(
                            requestId,
                            data,
                            sourceUrl,
                            sourceFileName,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                LanguageControl.Get("ModpackManifestParser", "ZipEntryExceedsSizeLimit"),
                                entryPath
                            )
                        );
                    }
                    embeddedFiles[entryPath] = fileMemory.ToArray();
                }
                if (string.IsNullOrWhiteSpace(zipManifest.Icon)) {
                    zipManifest.Icon = FindEmbeddedIcon(embeddedFiles);
                }
                if (!IsValidForFormat(zipManifest, true, embeddedFiles, out string formatError)) {
                    return Error(requestId, data, sourceUrl, sourceFileName, formatError);
                }
                ModpackRules.NormalizeManifest(zipManifest);
                return Success(
                    requestId,
                    data,
                    sourceUrl,
                    sourceFileName,
                    zipManifest,
                    embeddedFiles,
                    true
                );
            }
            catch (Exception ex) {
                return Error(
                    requestId,
                    data,
                    sourceUrl,
                    sourceFileName,
                    manifestError
                    ?? string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ModpackManifestParser", "ZipReadFailed"), ex.Message)
                );
            }
        }

        bool TryReadJson(byte[] data, out ModpackManifest manifest, out string error) {
            manifest = null;
            error = null;
            try {
                string json = Encoding.UTF8.GetString(data ?? []).TrimStart('\uFEFF');
                manifest = JsonSerializer.Deserialize<ModpackManifest>(json, m_jsonOptions);
                return manifest != null;
            }
            catch (Exception ex) {
                error = string.Format(
                    CultureInfo.InvariantCulture,
                    LanguageControl.Get("ModpackManifestParser", "ManifestJsonParseFailed"),
                    ex.Message
                );
                return false;
            }
        }

        static bool IsValidManifest(ModpackManifest manifest, out string error) {
            error = null;
            if (manifest == null) {
                error = LanguageControl.Get("ModpackManifestParser", "TheModpackManifestIsEmptyOrInvalid");
                return false;
            }
            if (string.IsNullOrWhiteSpace(manifest.Name)) {
                error = LanguageControl.Get("ModpackManifestParser", "ManifestMissingFieldName");
                return false;
            }
            if (string.IsNullOrWhiteSpace(manifest.PackageName)) {
                error = LanguageControl.Get("ModpackManifestParser", "ManifestMissingFieldPackageName");
                return false;
            }
            if (string.IsNullOrWhiteSpace(manifest.Version)) {
                error = LanguageControl.Get("ModpackManifestParser", "ManifestMissingFieldVersion");
                return false;
            }
            if (string.IsNullOrWhiteSpace(manifest.Description)) {
                error = LanguageControl.Get("ModpackManifestParser", "ManifestMissingFieldDescription");
                return false;
            }
            if (manifest.Items == null
                || manifest.Items.Length == 0) {
                error = LanguageControl.Get("ModpackManifestParser", "ManifestHasNoValidItems");
                return false;
            }
            if (!IsSupportedIconPath(manifest.Icon)) {
                error = LanguageControl.Get("ModpackManifestParser", "IconOnlySupportsPngOrWebp");
                return false;
            }
            return true;
        }

        static bool IsValidForFormat(ModpackManifest manifest,
            bool supportsEmbeddedFiles,
            IReadOnlyDictionary<string, byte[]> embeddedFiles,
            out string error) {
            error = null;
            if (!supportsEmbeddedFiles
                && !string.IsNullOrWhiteSpace(manifest?.Icon)
                && !ModpackRules.IsHttpUrl(manifest.Icon)) {
                error = LanguageControl.Get("ModpackManifestParser", "JsonPackIconMustBeUrl");
                return false;
            }
            foreach (ModpackItem item in manifest?.Items ?? []) {
                if (!ModpackRules.TryValidateSource(item, supportsEmbeddedFiles, embeddedFiles, out error, supportsEmbeddedFiles)) {
                    return false;
                }
            }
            return true;
        }

        static bool IsSupportedIconPath(string icon) {
            if (string.IsNullOrWhiteSpace(icon)) {
                return true;
            }
            string tailName = ModpackRules.TryGetPathTailName(icon) ?? icon;
            string extension = Storage.GetExtensionBCL(tailName);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase);
        }

        static System.IO.Compression.ZipArchive OpenArchive(Stream stream) {
            Encoding encoding = DetectZipEntryNameEncoding(stream);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return new System.IO.Compression.ZipArchive(
                stream,
                ZipArchiveMode.Read,
                false,
                // 探测不到 UTF-8 标志位时回退 GBK（Win 中文打包工具默认），与旧行为一致。
                encoding ?? Encoding.GetEncoding(936)
            );
        }

        // ZIP 首个 local file header 的通用位标志 bit 11（0x0800）置位表示文件名/注释用 UTF-8。
        // Win 中文工具默认 GBK（位未置），Android/跨平台工具多置 UTF-8。据此选编码，兼容两端。
        // 仅在可回退的流上探测；否则返回 null 回退 GBK。
        static Encoding DetectZipEntryNameEncoding(Stream stream) {
            if (stream == null
                || !stream.CanSeek) {
                return null;
            }
            long rewind = stream.Position;
            try {
                if (stream.Length - rewind < 8) {
                    return null;
                }
                byte[] header = new byte[8];
                int read = stream.Read(header, 0, header.Length);
                if (read < 8) {
                    return null;
                }
                // local file header 签名 "PK\x03\x04"
                if (header[0] != 0x50
                    || header[1] != 0x4B
                    || header[2] != 0x03
                    || header[3] != 0x04) {
                    return null;
                }
                int generalPurposeFlag = header[6] | (header[7] << 8);
                return (generalPurposeFlag & 0x0800) != 0 ? Encoding.UTF8 : null;
            }
            catch {
                return null;
            }
            finally {
                stream.Position = rewind;
            }
        }

        static string NormalizeArchiveEntryPath(string path) {
            string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
            while (normalized.StartsWith("/", StringComparison.Ordinal)) {
                normalized = normalized.Substring(1);
            }
            List<string> segments = [];
            foreach (string rawSegment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)) {
                string segment = rawSegment.Trim();
                if (segment == "..") {
                    return null;
                }
                if (!string.IsNullOrWhiteSpace(segment)
                    && segment != ".") {
                    segments.Add(segment);
                }
            }
            return string.Join("/", segments);
        }

        static ModpackManifestLoadResult Success(int requestId,
            byte[] data,
            string sourceUrl,
            string sourceFileName,
            ModpackManifest manifest,
            Dictionary<string, byte[]> embeddedFiles,
            bool supportsEmbeddedFiles) => new() {
            RequestId = requestId,
            Manifest = manifest,
            FileName = sourceFileName,
            SourceUrl = sourceUrl,
            OriginalData = data,
            EmbeddedFiles = embeddedFiles,
            SupportsEmbeddedFiles = supportsEmbeddedFiles
        };

        static ModpackManifestLoadResult Error(int requestId, byte[] data, string sourceUrl, string sourceFileName, string error) => new() {
            RequestId = requestId, FileName = sourceFileName, SourceUrl = sourceUrl, OriginalData = data, Error = error
        };
    }
}