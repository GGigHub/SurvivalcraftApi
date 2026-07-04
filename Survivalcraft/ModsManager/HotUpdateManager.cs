using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Engine;
using Game;

namespace Game {
    /// <summary>
    /// 热更新状态
    /// </summary>
    public enum HotUpdateState {
        Idle,
        Checking,
        UpdateAvailable,
        Downloading,
        ReadyToRestart,
        Error
    }

    /// <summary>
    /// 热更新清单数据结构
    /// </summary>
    public class HotUpdateManifest {
        public string Version { get; set; }
        public string ReleaseNotes { get; set; }
        public bool Mandatory { get; set; }
        public string WindowsDllUrl { get; set; }
        public string AndroidDllUrl { get; set; }
        public string Sha256 { get; set; }
        public string WindowsSha256 { get; set; }
        public string AndroidSha256 { get; set; }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(HotUpdateManifest))]
    public partial class HotUpdateManifestJsonContext : JsonSerializerContext {
    }

    public static class HotUpdateManager {
        public const string ManifestUrl = "https://api.trk34.top/hotupdate/manifest.json";
        public const string AndroidUpdateDirectory = "data:updates/current";

        public static HotUpdateState State { get; private set; } = HotUpdateState.Idle;
        public static HotUpdateManifest LatestManifest { get; private set; }
        public static Exception LastError { get; private set; }

        public static float DownloadProgress { get; private set; }

        private static readonly HttpClient s_httpClient = new HttpClient();

        /// <summary>
        /// 检查是否有更新
        /// </summary>
        public static async Task<bool> CheckForUpdatesAsync() {
            try {
                State = HotUpdateState.Checking;
                LastError = null;

                string json = await s_httpClient.GetStringAsync(ManifestUrl);
                LatestManifest = JsonSerializer.Deserialize(json, HotUpdateManifestJsonContext.Default.HotUpdateManifest);

                if (LatestManifest == null) {
                    throw new Exception("解析 manifest.json 失败");
                }

                if (APIUpdateManager.CompareVersion(ModsManager.APIVersionString, LatestManifest.Version) == -1) {
                    State = HotUpdateState.UpdateAvailable;
                    return true;
                }

                State = HotUpdateState.Idle;
                return false;
            }
            catch (Exception ex) {
                LastError = ex;
                State = HotUpdateState.Error;
                Log.Error($"HotUpdate Check Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 下载并安装更新
        /// </summary>
        public static async Task DownloadAndInstallUpdateAsync() {
            if (State != HotUpdateState.UpdateAvailable || LatestManifest == null) {
                return;
            }

            try {
                State = HotUpdateState.Downloading;
                DownloadProgress = 0f;
                LastError = null;

#if ANDROID
                string downloadUrl = LatestManifest.AndroidDllUrl;
                string expectedSha256 = string.IsNullOrEmpty(LatestManifest.AndroidSha256)
                    ? LatestManifest.Sha256
                    : LatestManifest.AndroidSha256;
#else
                string downloadUrl = LatestManifest.WindowsDllUrl;
                string expectedSha256 = string.IsNullOrEmpty(LatestManifest.WindowsSha256)
                    ? LatestManifest.Sha256
                    : LatestManifest.WindowsSha256;
#endif

                if (string.IsNullOrEmpty(downloadUrl)) {
                    throw new Exception("更新清单中未找到当前平台的下载链接");
                }
                if (string.IsNullOrEmpty(expectedSha256)) {
                    throw new Exception("更新清单中未找到当前平台的 SHA256");
                }

                // 准备临时下载路径
                string tempFilePath = GetTemporaryDownloadPath();

                // 开始下载
                using (HttpResponseMessage response = await s_httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead)) {
                    response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;
                    using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                    using (FileStream fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true)) {
                        byte[] buffer = new byte[8192];
                        long totalRead = 0;
                        int read;

                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0) {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;

                            if (totalBytes.HasValue) {
                                DownloadProgress = (float)totalRead / totalBytes.Value;
                            }
                        }
                    }
                }
                VerifySha256(tempFilePath, expectedSha256);

                // 下载完成，进行平台特定的替换逻辑
#if ANDROID
                InstallUpdateAndroid(tempFilePath);
#else
                InstallUpdateWindows(tempFilePath);
#endif
                SettingsManager.PendingHotUpdateReleaseNotes = LatestManifest.ReleaseNotes ?? string.Empty;
                SettingsManager.SaveSettings();
                State = HotUpdateState.ReadyToRestart;
            }
            catch (Exception ex) {
                LastError = ex;
                State = HotUpdateState.Error;
                Log.Error($"HotUpdate Download Error: {ex.Message}");
            }
        }

#if ANDROID
        private static void InstallUpdateAndroid(string tempFilePath) {
            // Android 启动器约定从应用内部目录读取 data:updates/current/Survivalcraft.dll
            string updateDir = AndroidUpdateDirectory;
            if (!Storage.DirectoryExists(updateDir)) {
                Storage.CreateDirectory(updateDir);
            }
            string targetPath = Storage.CombinePaths(updateDir, "Survivalcraft.dll");
            
            if (Storage.FileExists(targetPath)) {
                Storage.DeleteFile(targetPath);
            }
            Storage.MoveFile(tempFilePath, targetPath);
        }
#else
        private static void InstallUpdateWindows(string tempFilePath) {
            // Windows 端运行中 dll 被占用，需要依靠 bat 脚本替换并重启
            string processPath = Process.GetCurrentProcess().MainModule?.FileName;
            string currentDir = Path.GetDirectoryName(processPath);
            if (string.IsNullOrEmpty(currentDir)) return;

            string targetDll = Path.Combine(currentDir, "Survivalcraft.dll");
            string updateBatPath = Path.Combine(currentDir, "update_dll.bat");
            int pid = Process.GetCurrentProcess().Id;

            // 构造 bat 脚本
            string batContent = $@"
@echo off
chcp 65001 >nul
echo 正在结束游戏进程...
taskkill /pid {pid} /f >nul 2>&1
timeout /t 1 /nobreak >nul

echo 正在更新文件...
move /y ""{tempFilePath}"" ""{targetDll}""

echo 正在重启游戏...
start """" ""{processPath}""

del ""%~f0""
";
            File.WriteAllText(updateBatPath, batContent, System.Text.Encoding.UTF8);
        }
#endif

        public static void Restart() {
#if ANDROID
            Window.Restart();
#else
            string processPath = Process.GetCurrentProcess().MainModule?.FileName;
            string currentDir = Path.GetDirectoryName(processPath);
            if (string.IsNullOrEmpty(currentDir)) return;

            string updateBatPath = Path.Combine(currentDir, "update_dll.bat");
            // 执行脚本
            ProcessStartInfo psi = new ProcessStartInfo {
                FileName = updateBatPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
#endif
        }

        private static string GetTemporaryDownloadPath() {
#if ANDROID
            string updateDir = AndroidUpdateDirectory;
            if (!Storage.DirectoryExists(updateDir)) {
                Storage.CreateDirectory(updateDir);
            }
            return Storage.CombinePaths(updateDir, "Survivalcraft.dll.download");
#else
            string processPath = Process.GetCurrentProcess().MainModule?.FileName;
            string currentDir = Path.GetDirectoryName(processPath);
            if (string.IsNullOrEmpty(currentDir)) {
                throw new InvalidOperationException("无法确定当前程序目录");
            }
            return Path.Combine(currentDir, "Survivalcraft.dll.download");
#endif
        }

        private static void VerifySha256(string filePath, string expectedSha256) {
            string normalizedExpected = expectedSha256.Replace(" ", string.Empty).ToLowerInvariant();
            using Stream stream = Storage.OpenFile(filePath, OpenFileMode.Read);
            byte[] hash = SHA256.HashData(stream);
            string actual = Convert.ToHexString(hash).ToLowerInvariant();
            if (!string.Equals(actual, normalizedExpected, StringComparison.OrdinalIgnoreCase)) {
                Storage.DeleteFile(filePath);
                throw new InvalidDataException($"更新文件 SHA256 校验失败，期望 {normalizedExpected}，实际 {actual}");
            }
        }
    }
}
