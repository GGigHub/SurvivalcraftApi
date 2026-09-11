using System.Text.Json;
using System.Text.Json.Serialization;
using Engine;

namespace Game
{
    internal static class DownloadQueueStore
    {
        // 枚举以字符串序列化（文件可读、未来新增枚举值不破坏旧文件读取）；
        // 允许整数值兼容旧版数字格式文件。
        private static readonly JsonSerializerOptions s_options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(allowIntegerValues: true) }
        };

        public static DownloadQueuePersistedState Load()
        {
            return Storage.FileExists(StoragePaths.DownloadQueueFile)
                ? JsonFileStore.Read<DownloadQueuePersistedState>(StoragePaths.DownloadQueueFile, s_options)
                : null;
        }

        public static void Save(DownloadQueuePersistedState store)
        {
            JsonFileStore.Write(StoragePaths.DownloadQueueFile, store, s_options);
        }
    }
}
