using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// 外部内容导入准备：本地/远程资源通用。承载类型识别、本地冲突检测结果与导入载荷；
    /// 远程链路字段（Url/RequestId）与整合包链路字段（Manifest/EmbeddedFiles 等）按需填充。
    /// </summary>
    public sealed class ExternalContentPreparation
    {
        public int RequestId { get; set; }
        public string Url { get; set; }
        public string FileName { get; set; }
        public ExternalContentType Type { get; set; }
        public byte[] FileData { get; set; }
        public string DisplayName { get; set; }
        public string ExistingEntryName { get; set; }
        public string ExistingEntryFilePath { get; set; }
        public string ExistingDisplayName { get; set; }
        public string ExistingModPackageName { get; set; }
        public string ExistingModPath { get; set; }
        public bool RequiresRestart { get; set; }
        public string Error { get; set; }
        public bool ArchiveSaved { get; set; }
        public ModpackManifest Manifest { get; set; }
        public IReadOnlyDictionary<string, byte[]> EmbeddedFiles { get; set; }
        public byte[] OriginalData { get; set; }

        public bool RequiresOverwriteConfirmation =>
            !string.IsNullOrWhiteSpace(ExistingEntryName)
            || !string.IsNullOrWhiteSpace(ExistingModPackageName);
    }
}
