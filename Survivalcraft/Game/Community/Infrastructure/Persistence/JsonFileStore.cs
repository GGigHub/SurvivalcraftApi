using System.Globalization;
using System.Text;
using System.Text.Json;
using Engine;

namespace Game {
    /// <summary>
    /// 统一处理本地 JSON 文件的读写。写入先落到同目录临时文件，再替换目标文件。
    /// </summary>
    static class JsonFileStore {
        static readonly Encoding s_utf8WithoutBom = new UTF8Encoding(false);

        public static T Read<T>(string path, JsonSerializerOptions options = null) {
            try {
                // 文件不存在（首次启动/尚未写入过）是正常状态，
                // 直接走默认值，避免每次启动抛 FileNotFoundException 刷日志。
                if (!Storage.FileExists(path)) {
                    return default;
                }
                string json = Storage.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<T>(json, options);
            }
            catch (Exception ex) {
                // 损坏/截断/权限异常统一回退默认值，调用方均按 null/空集合兜底，避免启动链路崩溃。
                Log.Warning(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        LanguageControl.Get("JsonFileStore", "ReadJsonFileFailedUsingDefault"),
                        path,
                        ex.Message
                    )
                );
                return default;
            }
        }

        public static void Write<T>(string path, T value, JsonSerializerOptions options = null) {
            string directory = Storage.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) {
                Storage.CreateDirectory(directory);
            }
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string json = JsonSerializer.Serialize(value, options);
            Storage.WriteAllText(temporaryPath, json, s_utf8WithoutBom);
            // 本体 Storage 已有 MoveFileSafely（备份-替换-回滚）。残留的失败 .tmp
            // 由 RecoverLeftoverTempFiles 兼容清理（老版本写入仍会产出）。
            Storage.MoveFileSafely(temporaryPath, path);
            TryDeleteFile(temporaryPath);
        }

        // 启动清扫CommunityStoragePaths.RootDirectory下写入中断残留的 *.tmp（MoveFile 为 Delete+Move 两步，非原子）：
        // 正式文件仍在 → .tmp 是替换流程残留，正式文件已是新内容，直接删；
        // 正式文件缺失 → Delete 已执行而 Move 未完成，.tmp 是新数据唯一副本，移回原名。
        // 时间戳在 data: 虚拟存储下不可靠，以正式文件是否存在为唯一分支依据。
        public static void RecoverLeftoverTempFiles(string rootPath) {
            if (string.IsNullOrWhiteSpace(rootPath)) {
                return;
            }
            try {
                if (Storage.DirectoryExists(rootPath)) {
                    RecoverTempFilesInDirectory(rootPath, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                }
            }
            catch (Exception ex) {
                Log.Warning(
                    string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("JsonFileStore", "ScanJsonTempFilesFailed"), rootPath, ex.Message)
                );
            }
        }

        static void RecoverTempFilesInDirectory(string directoryPath, HashSet<string> visitedDirectories) {
            if (!visitedDirectories.Add(directoryPath)) {
                return;
            }
            foreach (string fileName in Storage.ListFileNames(directoryPath).ToArray()) {
                if (!TryParseTempFileName(fileName, out string originalFileName)) {
                    continue;
                }
                string temporaryPath = Storage.CombinePaths(directoryPath, fileName);
                string originalPath = Storage.CombinePaths(directoryPath, originalFileName);
                try {
                    if (Storage.FileExists(originalPath)) {
                        TryDeleteFile(temporaryPath);
                    }
                    else {
                        Storage.MoveFile(temporaryPath, originalPath);
                        Log.Information(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                LanguageControl.Get("JsonFileStore", "RestoredJsonTempFile"),
                                temporaryPath,
                                originalPath
                            )
                        );
                    }
                }
                catch (Exception ex) {
                    Log.Warning(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("JsonFileStore", "RestoreJsonTempFileFailed"),
                            temporaryPath,
                            ex.Message
                        )
                    );
                }
            }
            foreach (string directoryName in Storage.ListDirectoryNames(directoryPath).ToArray()) {
                try {
                    RecoverTempFilesInDirectory(Storage.CombinePaths(directoryPath, directoryName), visitedDirectories);
                }
                catch (Exception ex) {
                    Log.Warning(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            LanguageControl.Get("JsonFileStore", "ScanJsonTempFilesFailed"),
                            directoryName,
                            ex.Message
                        )
                    );
                }
            }
        }

        // 临时名格式 <原文件名>.<32位hex>.tmp，与 Write 的命名对齐；校验 hex 段，
        // 避免误伤其他组件自管的非本类命名临时文件。
        static bool TryParseTempFileName(string fileName, out string originalFileName) {
            originalFileName = null;
            const string suffix = ".tmp";
            const int guidLength = 32;
            if (string.IsNullOrWhiteSpace(fileName)
                || !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
            int hexEnd = fileName.Length - suffix.Length;
            int hexStart = hexEnd - guidLength;
            if (hexStart < 2
                || fileName[hexStart - 1] != '.') {
                return false;
            }
            for (int i = hexStart; i < hexEnd; i++) {
                if (!IsHexChar(fileName[i])) {
                    return false;
                }
            }
            originalFileName = fileName.Substring(0, hexStart - 1);
            return true;
        }

        static bool IsHexChar(char ch) => (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');

        static void TryDeleteFile(string path) {
            try {
                if (Storage.FileExists(path)) {
                    Storage.DeleteFile(path);
                }
            }
            catch (Exception ex) {
                Log.Warning(
                    string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("JsonFileStore", "CleanJsonTempFileFailed"), path, ex.Message)
                );
            }
        }
    }
}