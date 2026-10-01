namespace Game {
    // 下载源选择模式曾是三值枚举（Default/ManualSelect/CustomDefault），Default 删除后仅剩
    // 手动选择与否的二元选择，收编为 SettingsManager 布尔属性
    // CommunityDownloadSourceManualSelectEnabled；下载源本体见 CommunityDownloadSourceManager。

    public class CommunityFileDownloadResult {
        public string FileName { get; set; }
        public string ImportedName { get; set; }
        public ExternalContentType Type { get; set; }
        public int PostId { get; set; }
        public bool RequiresRestart { get; set; }
        public bool Paused { get; set; }
        public bool Canceled { get; set; }
        public bool DownloadFailed { get; set; }
        public bool HideProgressDialog { get; set; }
        public string Error { get; set; }

        /// <summary>
        /// 导入目标已存在冲突时的既有文件路径（来自引擎 <c>FileAlreadyExistsException.Path</c>）；
        /// null 表示非该类失败。覆盖确认流程以此类型化字段判定，不嗅探错误文本。
        /// </summary>
        public string ExistingFilePath { get; set; }
    }
}