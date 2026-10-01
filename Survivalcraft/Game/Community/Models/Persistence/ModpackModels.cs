using System.Text.Json.Serialization;

namespace Game {
    public class ModpackManifest {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("packageName")] public string PackageName { get; set; }

        [JsonPropertyName("version")] public string Version { get; set; }

        // 展示性可选字段：旧清单无值显示「无」；服务端生成 packinfo.json 时写入。
        [JsonPropertyName("author")] public string Author { get; set; }
        [JsonPropertyName("homepage")] public string Homepage { get; set; }
        [JsonPropertyName("apiVersion")] public string ApiVersion { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
        [JsonPropertyName("icon")] public string Icon { get; set; }
        [JsonPropertyName("items")] public ModpackItem[] Items { get; set; }
    }

    public class ModpackItem {
        [JsonPropertyName("category")] public string Category { get; set; }
        [JsonPropertyName("fileName")] public string Filename { get; set; }
        [JsonPropertyName("url")] public string Url { get; set; }

        [JsonPropertyName("fileId"), JsonConverter(typeof(FlexibleStringJsonConverter))]
        public string FileId { get; set; }

        [JsonPropertyName("packageName")] public string PackageName { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; }
        [JsonPropertyName("targetPath")] public string TargetPath { get; set; }
        [JsonPropertyName("targetDirectory")] public string TargetDirectory { get; set; }
        public bool IsEnabled { get; set; } = true;
        [JsonIgnore] public bool HasExplicitFileName { get; set; }
        [JsonIgnore] public string ManifestFileName { get; set; }
    }

    public class ModpackItemDownloadResult {
        public ModpackItem Item { get; set; }
        public string ImportedName { get; set; }
        public bool Success { get; set; }

        public string Error { get; set; }

        // 本地已有同名内容而跳过（缺陷 1，默认跳过+完成汇总语义）：
        // 不算成功也不算失败，完成提示单独汇总，失败对话框须排除。
        public bool Skipped { get; set; }
    }

    public class ModpackDownloadProgress {
        public int TotalCount { get; set; }
        public int CompletedCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public int SkippedCount { get; set; }
        public List<ModpackItemDownloadResult> Results { get; set; }

        public List<LocalModpackFileStateEntry> CustomFileBackups { get; set; } = [];

        // 顶层异常（ExecuteAsync 入口或 SaveManifest 文件 I/O 抛出）时填充，
        // 使消费端能识别整批失败而不依赖逐项 FailedCount。
        public string Error { get; set; }

        // 请求被取代（用户离开界面/发起新请求）时在首个 await 后置位并 break，
        // 阻止旧请求继续覆盖写入游戏内容；activation 据此不启用不完整整合包。
        public bool Interrupted { get; set; }
    }

    public class ModpackManifestLoadResult {
        public int RequestId { get; set; }
        public ModpackManifest Manifest { get; set; }
        public string FileName { get; set; }
        public string SourceUrl { get; set; }
        public Dictionary<string, byte[]> EmbeddedFiles { get; set; }
        public byte[] OriginalData { get; set; }
        public bool SupportsEmbeddedFiles { get; set; }
        public string Error { get; set; }
    }

    public class LocalModpackRecord {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("packageName")] public string PackageName { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
        [JsonPropertyName("sourceUrl")] public string SourceUrl { get; set; }
        [JsonPropertyName("sourceFileName")] public string SourceFileName { get; set; }
        [JsonPropertyName("importedAt")] public string ImportedAt { get; set; }
        [JsonPropertyName("manifest")] public ModpackManifest Manifest { get; set; }
        [JsonIgnore] public byte[] IconData { get; set; }
        [JsonIgnore] public Dictionary<string, byte[]> EmbeddedFiles { get; set; }
    }

    public class LocalModpackActivationState {
        [JsonPropertyName("recordPath")] public string RecordPath { get; set; }
        [JsonPropertyName("packageName")] public string PackageName { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; }
        [JsonPropertyName("displayName")] public string DisplayName { get; set; }
        [JsonPropertyName("updatedAt")] public string UpdatedAt { get; set; }
        [JsonPropertyName("backupModStates")] public IReadOnlyList<LocalModpackModStateEntry> BackupModStates { get; set; } = [];
        [JsonPropertyName("backupFiles")] public IReadOnlyList<LocalModpackFileStateEntry> BackupFiles { get; set; } = [];
    }

    public class LocalModpackFileStateEntry {
        [JsonPropertyName("targetPath")] public string TargetPath { get; set; }
        [JsonPropertyName("backupPath")] public string BackupPath { get; set; }
        [JsonPropertyName("persistedPath")] public string PersistedPath { get; set; }
        [JsonPropertyName("existedBefore")] public bool ExistedBefore { get; set; }
    }

    public class LocalModpackModStateEntry {
        [JsonPropertyName("filePath")] public string FilePath { get; set; }
        [JsonPropertyName("displayName")] public string DisplayName { get; set; }
        [JsonPropertyName("packageName")] public string PackageName { get; set; }
        [JsonPropertyName("version")] public string Version { get; set; }
        [JsonPropertyName("isDisabled")] public bool IsDisabled { get; set; }
    }
}