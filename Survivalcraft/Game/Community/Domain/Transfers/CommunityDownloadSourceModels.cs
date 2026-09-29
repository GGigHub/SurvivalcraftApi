namespace Game
{
    public enum CommunityDownloadSourceSelectionMode
    {
        // 原 Default 成员已删：其语义（恒用原站直连）与 CustomDefault + 默认线路完全重复；
        // 保留 ManualSelect=2/CustomDefault=3 原值，旧 Settings.xml 已存数值不换义
        ManualSelect = 2,
        CustomDefault = 3
    }

    // 下载源已由硬编码枚举改为可配置列表（CommunityDownloadSourceManager.Info，身份即名称）；
    // 原枚举成员名成为内置线路的保留内部名，旧队列/设置文件按名直查无需迁移。

    public class CommunityFileDownloadResult
    {
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
        ///     导入目标已存在冲突时的既有文件路径（来自引擎 <c>FileAlreadyExistsException.Path</c>）；
        ///     null 表示非该类失败。覆盖确认流程以此类型化字段判定，不嗅探错误文本。
        /// </summary>
        public string ExistingFilePath { get; set; }
    }
}
