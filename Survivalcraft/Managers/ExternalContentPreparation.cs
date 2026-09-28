using System.Collections.Generic;
using System.Globalization;

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
        /// <summary>直链 zip 包链路旗标：包体已下载到内存（ArchiveData），落盘由主线程覆盖确认后执行，此字段不再表示「已落盘」。</summary>
        public bool ArchiveSaved { get; set; }
        /// <summary>直链 zip 整合包的包体字节：准备任务只下到内存，落盘改由主线程在覆盖确认后执行（SaveArchive）。</summary>
        public byte[] ArchiveData { get; set; }
        public ModpackManifest Manifest { get; set; }
        public IReadOnlyDictionary<string, byte[]> EmbeddedFiles { get; set; }
        public byte[] OriginalData { get; set; }

        public bool RequiresOverwriteConfirmation =>
            !string.IsNullOrWhiteSpace(ExistingEntryName)
            || !string.IsNullOrWhiteSpace(ExistingModPackageName);

        /// <summary>
        /// 覆盖确认框是否提供"导入为副本"：仅家具包——引擎重名时自动另起新名保留旧包；
        /// 材质/皮肤是原地覆盖、Mod 抛同名异常，均无安全的副本语义。
        /// </summary>
        public bool CanRenameOnOverwrite => Type == ExternalContentType.FurniturePack;

        /// <summary>覆盖确认框文案：资源类型 + 已存在的本地项显示名回退链。</summary>
        public string GetOverwriteConfirmationMessage()
        {
            string resourceTypeName = ExternalContentManager.GetContentTypeDisplayName(Type);
            string existingName = ExistingDisplayName ?? ExistingEntryName ?? FileName;
            return string.Format(
                CultureInfo.InvariantCulture,
                LanguageControl.Get("ScbbsPlusRemoteImportManager", "LocalAlreadyExistsOverwriteIt"),
                resourceTypeName,
                existingName);
        }
    }
}
