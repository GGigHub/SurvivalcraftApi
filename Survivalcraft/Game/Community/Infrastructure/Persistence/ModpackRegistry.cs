using System.Globalization;
using System.Text;
using System.Text.Json;
using Engine;

namespace Game {
    /// <summary>
    /// Owns the on-disk integration-pack protocol and its parse cache.
    /// </summary>
    sealed class ModpackRegistry {
        const string FolderName = "ModPacks";

        // 注册表只产生/枚举/解析 .zip/.json（BuildManifestPath/EnumerateManifestFiles 同口径）。
        // .modpack/.scpack 不参与顶替清理：它们不在整合包列表中展示，若纳入会基于同基名
        // 组共享的 meta 被误判同源而删掉（审查缺陷 2 复审 F2）。
        static readonly string[] ConflictingExtensions = { ".zip", ".json" };

        readonly Lock m_cacheSync = new();

        // 多步写入（主文件+元数据/删除文件集）串行化，避免与并发读交错出半套记录；
        // 单文件读由原子写保证要么旧要么新。
        readonly Lock m_writeSync = new();
        readonly Dictionary<string, CacheEntry> m_cache = new(StringComparer.OrdinalIgnoreCase);
        readonly string m_directory = Storage.CombinePaths(ModsManager.DocPath, FolderName);
        readonly JsonSerializerOptions m_jsonOptions = new() { WriteIndented = true };
        readonly ModpackManifestParser m_parser = new();

        public static ModpackRegistry Shared { get; } = new();

        public event Action Changed;

        public string DirectoryPath => m_directory;

        public void SaveManifest(ModpackManifest manifest,
            string sourceUrl,
            string sourceFileName,
            byte[] sourceData = null,
            bool overwriteConflicting = false) {
            if (manifest == null
                || manifest.Items == null
                || manifest.Items.Length == 0) {
                return;
            }
            lock (m_writeSync) {
                EnsureReady();
                LocalModpackRecord record = new() {
                    Name = manifest.Name,
                    PackageName = manifest.PackageName,
                    Version = manifest.Version,
                    Description = manifest.Description,
                    SourceUrl = sourceUrl,
                    SourceFileName = sourceFileName,
                    ImportedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    Manifest = manifest
                };
                string manifestPath = BuildManifestPath(record);
                // overwriteConflicting：调用方已弹覆盖确认，跳过异源改名直接顶替同名；
                // 未确认时维持原语义（同名顶替/异源改名 "pack (2)"）。
                if (!overwriteConflicting) {
                    manifestPath = ResolveNonConflictingTargetPath(manifestPath, sourceUrl);
                }
                if (sourceData != null
                    && sourceData.Length > 0) {
                    WriteBytes(manifestPath, sourceData);
                    WriteMetadata(manifestPath, record);
                }
                else {
                    WriteBytes(manifestPath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, m_jsonOptions)));
                }
                // 写入成功后再清理同基名旧扩展名残留：先删后写时若写入失败，旧记录已被删且新文件未落地。
                DeleteConflictingFiles(manifestPath);
                InvalidateAndNotify();
            }
        }

        public void SaveArchive(byte[] sourceData, string sourceUrl, string sourceFileName, bool overwriteConflicting = false) {
            if (sourceData == null
                || sourceData.Length == 0) {
                throw new InvalidOperationException(LanguageControl.Get("ModpackRegistry", "DownloadedZipPackIsEmpty"));
            }
            lock (m_writeSync) {
                EnsureReady();
                string fileName = Storage.GetFileName(sourceFileName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(fileName)) {
                    fileName = "pack.zip";
                }
                // 与 FindConflictingPackPath/BuildManifestPath 同用 BuildTargetPath：
                // 覆盖确认检查的命中路径必须与实际落盘路径一致，否则「确认覆盖却另写新名」。
                // 入口已按 .zip/.json 分叉，归一化只影响大小写与极端文件名清洗。
                string archivePath = BuildTargetPath(fileName);
                // overwriteConflicting：调用方已弹覆盖确认，跳过异源改名直接顶替同名（与 SaveManifest 同口径）。
                if (!overwriteConflicting) {
                    archivePath = ResolveNonConflictingTargetPath(archivePath, sourceUrl);
                }
                WriteBytes(archivePath, sourceData);
                WriteMetadata(
                    archivePath,
                    new LocalModpackRecord {
                        SourceUrl = sourceUrl, SourceFileName = fileName, ImportedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                    }
                );
                // 写入成功后再清理同基名旧扩展名残留（先删后写时若写盘失败，用户既有整合包被删、新文件未落地）。
                DeleteConflictingFiles(archivePath);
                InvalidateAndNotify();
            }
        }

        public LocalResourceItem[] ReadResources() {
            List<LocalResourceItem> items = [];
            EnsureReady();
            foreach (string manifestPath in EnumerateManifestFiles()) {
                try {
                    LocalModpackRecord record = ReadRecord(manifestPath, out string parseError);
                    if (record?.Manifest?.Items == null
                        || record.Manifest.Items.Length == 0) {
                        Log.Warning(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                LanguageControl.Get("ModpackRegistry", "ModpackRecordCorrupted"),
                                manifestPath,
                                parseError ?? LanguageControl.Get("ModpackRegistry", "NoValidItemsRead")
                            )
                        );
                        continue;
                    }

                    // 三个来源（解析值/文件时间/兜底）统一 Kind=Utc：下方按 CreationTime tick
                    // 直接排序，跨来源 Kind 不一致会错位一个时区偏移（2026-09-09 评审 🔵）。
                    // 显示端统一 ToLocalTime()，不受影响。
                    DateTime importedAt = TryParseImportedAt(record.ImportedAt) ?? TryGetFileTime(manifestPath) ?? DateTime.UtcNow;
                    string displayName = !string.IsNullOrWhiteSpace(record.Name) ? record.Name :
                        !string.IsNullOrWhiteSpace(record.PackageName) ? record.PackageName : Storage.GetFileNameWithoutExtensionBCL(manifestPath);
                    items.Add(
                        new LocalResourceItem {
                            Type = ExternalContentType.Unknown,
                            Name = !string.IsNullOrWhiteSpace(record.PackageName)
                                ? record.PackageName
                                : Storage.GetFileNameWithoutExtensionBCL(manifestPath),
                            DisplayName = displayName,
                            CreationTime = importedAt,
                            LocalFilePath = manifestPath,
                            Version = record.Version,
                            Tag = record
                        }
                    );
                }
                catch (Exception ex) {
                    Log.Warning(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("ModpackRegistry", "ReadModpackRecordFailed"),
                            manifestPath,
                            ex.Message
                        )
                    );
                }
            }
            return items.OrderByDescending(item => item.CreationTime).ThenBy(item => item.DisplayName, StringComparer.InvariantCulture).ToArray();
        }

        public IEnumerable<string> EnumerateManifestFiles() {
            EnsureReady();
            foreach (string fileName in Storage.ListFileNames(m_directory)) {
                string path = Storage.CombinePaths(m_directory, fileName);
                string extension = Storage.GetExtensionBCL(fileName);
                if ((string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
                    && !path.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase)) {
                    yield return path;
                }
            }
        }

        public LocalModpackRecord ReadRecord(string manifestPath) => ReadRecord(manifestPath, out _);

        public LocalModpackRecord ReadRecord(string manifestPath, out string error) {
            string cacheKey = NormalizePath(manifestPath);
            DateTime manifestTime = TryGetFileTime(manifestPath) ?? DateTime.MinValue;
            long manifestSize = TryGetFileSize(manifestPath) ?? -1L;
            string metadataPath = GetMetadataPath(manifestPath);
            DateTime metadataTime = TryGetFileTime(metadataPath) ?? DateTime.MinValue;
            // 解析与缓存写入在锁内完成：并发首读时后到线程锁内二次命中，不重复解析读盘。
            lock (m_cacheSync) {
                if (!string.IsNullOrWhiteSpace(cacheKey)
                    && m_cache.TryGetValue(cacheKey, out CacheEntry cached)
                    && cached.ManifestTime == manifestTime
                    && cached.ManifestSize == manifestSize
                    && cached.MetadataTime == metadataTime
                    // 文件时间不可得（MinValue）时时间戳+大小无法防同 tick 同大小替换：
                    // 加内容哈希校验，防误命中旧解析结果。
                    && (manifestTime != DateTime.MinValue
                        || string.Equals(cached.ContentHash, ComputeContentHash(manifestPath), StringComparison.Ordinal))) {
                    error = cached.Error;
                    return cached.Record;
                }
                error = null;
                ModpackManifestLoadResult parsed = m_parser.Parse(0, ReadBytes(manifestPath), null, Storage.GetFileName(manifestPath));
                if (parsed?.Manifest?.Items == null
                    || parsed.Manifest.Items.Length == 0) {
                    error = parsed?.Error ?? LanguageControl.Get("ModpackManifestParser", "ManifestHasNoValidItems");
                    Cache(
                        cacheKey,
                        manifestPath,
                        manifestTime,
                        manifestSize,
                        metadataTime,
                        null,
                        error
                    );
                    return null;
                }
                LocalModpackFileMetadata metadata = ReadMetadata(manifestPath);
                ModpackRules.TryGetEmbeddedFile(parsed.EmbeddedFiles, parsed.Manifest.Icon, out byte[] iconData);
                LocalModpackRecord record = new() {
                    Name = parsed.Manifest.Name,
                    PackageName = parsed.Manifest.PackageName,
                    Version = parsed.Manifest.Version,
                    Description = parsed.Manifest.Description,
                    SourceUrl = metadata?.SourceUrl,
                    SourceFileName = metadata?.SourceFileName ?? Storage.GetFileName(manifestPath),
                    ImportedAt = metadata?.ImportedAt,
                    Manifest = parsed.Manifest,
                    IconData = iconData,
                    EmbeddedFiles = parsed.EmbeddedFiles
                };
                Cache(
                    cacheKey,
                    manifestPath,
                    manifestTime,
                    manifestSize,
                    metadataTime,
                    record,
                    null
                );
                return record;
            }
        }

        public string GetMetadataPath(string manifestPath) {
            string directory = Storage.GetDirectoryName(manifestPath);
            string baseName = Storage.GetFileNameWithoutExtensionBCL(manifestPath);
            return Storage.CombinePaths(string.IsNullOrWhiteSpace(directory) ? m_directory : directory, $"{baseName}.meta.json");
        }

        public void DeleteFileSet(string manifestPath) {
            if (string.IsNullOrWhiteSpace(manifestPath)) {
                return;
            }
            lock (m_writeSync) {
                if (Storage.FileExists(manifestPath)) {
                    Storage.DeleteFile(manifestPath);
                }
                string metadataPath = GetMetadataPath(manifestPath);
                if (Storage.FileExists(metadataPath)) {
                    Storage.DeleteFile(metadataPath);
                }
                InvalidateAndNotify();
            }
        }

        // 仅作图标缓存键（自产自比，Kind 无关）。勿用作 CreationTime 来源——CreationTime 契约为 Kind=Utc。
        public static DateTime? TryGetExistingFileLastWriteTime(string filePath) {
            try {
                if (Storage.FileExists(filePath)) {
                    return Storage.GetFileLastWriteTime(filePath).ToLocalTime();
                }
                return null;
            }
            catch {
                return null;
            }
        }

        public void EnsureReady() {
            if (!Storage.DirectoryExists(m_directory)) {
                Storage.CreateDirectory(m_directory);
            }
        }

        public byte[] ReadBytes(string path) {
            using Stream stream = Storage.OpenFile(path, OpenFileMode.Read);
            using MemoryStream memory = new();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        public void WriteBytes(string path, byte[] data) {
            string directory = GetParentDirectory(path);
            if (!string.IsNullOrWhiteSpace(directory)
                && !Storage.DirectoryExists(directory)) {
                Storage.CreateDirectory(directory);
            }
            // 先写同目录临时文件再替换，崩溃中断最多留 .tmp 残留，不产生截断的正式文件。
            // Storage.MoveFile 先 Delete(dest) 再 Move：Move 阶段失败时正式文件可能已被删，
            // 必须保留 .tmp 供恢复，只在正式文件仍在（写入阶段失败）时才清理临时文件。
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (Stream stream = Storage.OpenFile(temporaryPath, OpenFileMode.Create)) {
                    stream.Write(data, 0, data.Length);
                }
                Storage.MoveFile(temporaryPath, path);
            }
            catch {
                if (Storage.FileExists(path)) {
                    Storage.DeleteFile(temporaryPath);
                }
                throw;
            }
        }

        public string GetParentDirectory(string filePath) {
            string normalized = (filePath ?? string.Empty).TrimEnd('/', '\\');
            int separatorIndex = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf('\\'));
            if (separatorIndex < 0) {
                return string.Empty;
            }
            int length = separatorIndex == 2 && normalized.Length > 1 && normalized[1] == ':' ? separatorIndex + 1 : separatorIndex;
            string directory = normalized.Substring(0, length);
            return string.Equals(directory, normalized, StringComparison.OrdinalIgnoreCase) ? string.Empty : directory;
        }

        /// <summary>
        /// 解析本次导入的落盘路径。同基名其他扩展名主文件可能是另一条注册表记录：
        /// 以双方 meta 的 SourceUrl 判定同源，双方一致才视为同一包重新导入
        /// （由 DeleteConflictingFiles 顶替）；异源或无证据时新导入改名落盘
        /// （"pack (2).json"），不得静默删除已有包。
        /// 必须在写元数据之前调用——同基名共享 meta 路径，后写会覆盖前者的溯源信息。
        /// </summary>
        string ResolveNonConflictingTargetPath(string targetPath, string sourceUrl) {
            string directory = Storage.GetDirectoryName(targetPath);
            string originalBaseName = Storage.GetFileNameWithoutExtensionBCL(targetPath);
            if (string.IsNullOrWhiteSpace(directory)
                || string.IsNullOrWhiteSpace(originalBaseName)) {
                return targetPath;
            }
            string baseName = originalBaseName;
            string extension = Storage.GetExtensionBCL(targetPath);
            string originalTargetPath = targetPath;
            for (int attempt = 2;; attempt++) {
                bool conflictsWithDifferentSource = false;
                foreach (string candidateExtension in ConflictingExtensions) {
                    string candidatePath = Storage.CombinePaths(directory, $"{baseName}{candidateExtension}");
                    if (!Storage.FileExists(candidatePath)) {
                        continue;
                    }
                    // 原始目标路径自身的同名已存在 = 同名重新导入的顶替语义，不做来源判定；
                    // 改名分配出的新名不在豁免之列——"(2)" 可能已是第三条记录的落盘名，
                    // 无条件覆盖恰是本方法要杜绝的丢失（审查缺陷 2 复审 F1）。
                    if (string.Equals(candidatePath, originalTargetPath, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                    if (!IsSameSource(candidatePath, sourceUrl)) {
                        conflictsWithDifferentSource = true;
                        break;
                    }
                }
                if (!conflictsWithDifferentSource) {
                    return targetPath;
                }
                // 改名后重查一轮：新基名也可能与更早记录撞名。
                targetPath = Storage.CombinePaths(directory, $"{originalBaseName} ({attempt}){extension}");
                baseName = $"{originalBaseName} ({attempt})";
            }
        }

        bool IsSameSource(string existingPath, string sourceUrl) {
            // 双方 SourceUrl 均非空且一致才认定同源。无元数据/无来源（旧版导入、
            // 手工放入）无证据，按异源保守处理——宁可留重复副本，不冒删用户数据的险。
            if (string.IsNullOrWhiteSpace(sourceUrl)) {
                return false;
            }
            try {
                string metadataPath = GetMetadataPath(existingPath);
                if (!Storage.FileExists(metadataPath)) {
                    return false;
                }
                LocalModpackFileMetadata metadata = JsonSerializer.Deserialize<LocalModpackFileMetadata>(
                    Storage.ReadAllText(metadataPath, Encoding.UTF8),
                    m_jsonOptions
                );
                return !string.IsNullOrWhiteSpace(metadata?.SourceUrl)
                    && string.Equals(metadata.SourceUrl, sourceUrl, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) {
                Log.Warning(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("ModpackRegistry", "CompareModpackSourceFailed"),
                        existingPath,
                        ex.Message
                    )
                );
                return false;
            }
        }

        void DeleteConflictingFiles(string manifestPath) {
            string directory = Storage.GetDirectoryName(manifestPath);
            string baseName = Storage.GetFileNameWithoutExtensionBCL(manifestPath);
            if (string.IsNullOrWhiteSpace(directory)
                || string.IsNullOrWhiteSpace(baseName)) {
                return;
            }
            // 只清理同基名其他扩展名的主文件残留。四个候选经 GetMetadataPath 解析到的都是
            // 同一个 {baseName}.meta.json——即目标自身刚写入的元数据，绝不能删（否则
            // SaveManifest/SaveArchive 的写元数据步骤会被本方法立即作废，SourceUrl/ImportedAt 恒空）。
            foreach (string extension in ConflictingExtensions) {
                string candidatePath = Storage.CombinePaths(directory, $"{baseName}{extension}");
                if (Storage.FileExists(candidatePath)
                    && !string.Equals(candidatePath, manifestPath, StringComparison.OrdinalIgnoreCase)) {
                    Storage.DeleteFile(candidatePath);
                }
            }
        }

        void WriteMetadata(string manifestPath, LocalModpackRecord record) {
            LocalModpackFileMetadata metadata = new() {
                SourceUrl = record?.SourceUrl, SourceFileName = record?.SourceFileName, ImportedAt = record?.ImportedAt
            };
            WriteBytes(GetMetadataPath(manifestPath), Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata, m_jsonOptions)));
        }

        LocalModpackFileMetadata ReadMetadata(string manifestPath) {
            try {
                string path = GetMetadataPath(manifestPath);
                if (!Storage.FileExists(path)) {
                    return null;
                }
                return JsonSerializer.Deserialize<LocalModpackFileMetadata>(Storage.ReadAllText(path, Encoding.UTF8), m_jsonOptions);
            }
            catch (Exception ex) {
                Log.Warning(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("ModpackRegistry", "ReadModpackRecordMetadataFailed"),
                        manifestPath,
                        ex.Message
                    )
                );
                return null;
            }
        }

        void Cache(string key,
            string manifestPath,
            DateTime manifestTime,
            long manifestSize,
            DateTime metadataTime,
            LocalModpackRecord record,
            string error) {
            if (string.IsNullOrWhiteSpace(key)) {
                return;
            }
            lock (m_cacheSync) {
                m_cache[key] = new CacheEntry {
                    ManifestTime = manifestTime,
                    ManifestSize = manifestSize,
                    MetadataTime = metadataTime,
                    ContentHash = manifestTime == DateTime.MinValue ? ComputeContentHash(manifestPath) : null,
                    Record = record,
                    Error = error
                };
            }
        }

        // MinValue 时间戳场景的缓存校验用：轻量内容指纹（长度 + FNV-1a 64 位），
        // 足够区分同 tick 同大小替换，不引入加密哈希依赖。
        static string ComputeContentHash(string manifestPath) {
            try {
                using Stream stream = Storage.OpenFile(manifestPath, OpenFileMode.Read);
                ulong hash = 14695981039346656037UL;
                long length = 0;
                int read;
                byte[] buffer = new byte[8192];
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
                    length += read;
                    for (int i = 0; i < read; i++) {
                        hash ^= buffer[i];
                        hash *= 1099511628211UL;
                    }
                }
                return $"{length}:{hash:X16}";
            }
            catch {
                return null;
            }
        }

        void InvalidateAndNotify() {
            lock (m_cacheSync) {
                m_cache.Clear();
            }
            Changed?.Invoke();
        }

        static string NormalizePath(string path) => string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/').Trim();

        static DateTime? TryGetFileTime(string path) {
            try {
                // 引擎返回 Kind=Utc（GetLastWriteTimeUtc），保持原样供 CreationTime 排序可比；
                // 缓存校验（ReadRecord）两侧同源取值，Kind 归一无影响。
                return Storage.FileExists(path) ? Storage.GetFileLastWriteTime(path) : null;
            }
            catch {
                return null;
            }
        }

        static long? TryGetFileSize(string path) {
            try {
                if (!Storage.FileExists(path)) {
                    return null;
                }
                using Stream stream = Storage.OpenFile(path, OpenFileMode.Read);
                return stream.Length;
            }
            catch {
                return null;
            }
        }

        // 归一为 Kind=Utc：带 Z 后缀（本代码库写入格式）解析即 Utc 原样；带时区偏移先转
        // 本地钟面（Kind=Local）再转回 UTC 同一时刻；无时区（Kind=Unspecified）按本地
        // 钟面时间解释后转 UTC——显示端 ToLocalTime() 可还原钟面值，历史数据无需迁移。
        static DateTime? TryParseImportedAt(string text) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime time) ? time.ToUniversalTime() : null;

        sealed class CacheEntry {
            public DateTime ManifestTime { get; set; }
            public long ManifestSize { get; set; }

            public DateTime MetadataTime { get; set; }

            // 文件时间不可得（MinValue）时的内容指纹：防同 tick 同大小替换误命中旧解析。
            public string ContentHash { get; set; }
            public LocalModpackRecord Record { get; set; }
            public string Error { get; set; }
        }

        sealed class LocalModpackFileMetadata {
            public string SourceUrl { get; set; }
            public string SourceFileName { get; set; }
            public string ImportedAt { get; set; }
        }

        /// <summary>
        /// 判定本次导入是否与既有整合包同名（同基名的 .zip/.json 任一已存在）。
        /// 返回命中的既有文件路径（供覆盖确认框展示），无同名返回 null。
        /// 口径与 ResolveNonConflictingTargetPath 的"原始目标基名即重新导入"一致，
        /// 不含历史导入改名分配出的 "pack (2)" 这类非原始基名。
        /// </summary>
        public string FindConflictingPackPath(string sourceFileName) {
            string targetPath = BuildTargetPath(sourceFileName);
            string directory = Storage.GetDirectoryName(targetPath);
            string baseName = Storage.GetFileNameWithoutExtensionBCL(targetPath);
            if (string.IsNullOrWhiteSpace(directory)
                || string.IsNullOrWhiteSpace(baseName)) {
                return null;
            }
            foreach (string candidateExtension in ConflictingExtensions) {
                string candidatePath = Storage.CombinePaths(directory, baseName + candidateExtension);
                if (Storage.FileExists(candidatePath)) {
                    return candidatePath;
                }
            }
            return null;
        }

        string BuildManifestPath(LocalModpackRecord record) => BuildTargetPath(record?.SourceFileName);

        string BuildTargetPath(string sourceFileName) {
            string fileName = Storage.GetFileName(sourceFileName ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(fileName)) {
                string baseName = Storage.SanitizeFileNameSegment(Storage.GetFileNameWithoutExtensionBCL(fileName), "pack");
                string extension = Storage.GetExtensionBCL(fileName);
                if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase)) {
                    extension = ".json";
                }
                return Storage.CombinePaths(m_directory, $"{baseName}{extension.ToLowerInvariant()}");
            }
            return Storage.CombinePaths(m_directory, "pack.json");
        }
    }
}