namespace Game
{
    internal interface IModpackItemImporter
    {
        void ImportExternalContent(
            int requestId,
            string itemUrl,
            string fileName,
            ExternalContentType type,
            byte[] data,
            ModpackItem item);

        /// <summary>
        /// 导入整合包数据文件。返回 false = host 缺失、数据无处导入：
        /// 调用方必须按失败计数，不得计成功。
        /// </summary>
        bool ImportDataFile(byte[] data, ModpackItem item);
    }
}
