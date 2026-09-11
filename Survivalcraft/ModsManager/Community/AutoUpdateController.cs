using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game
{
    public static class AutoUpdateController
    {
        // 设置可由游戏侧回调（Changed 事件）与帧循环读写，volatile 保证跨线程可见。
        private volatile static Task<UpdateCheckResult> s_updateCheckTask;
        private volatile static bool s_settingsLoaded;
        private volatile static bool s_enabled = true;
        private volatile static bool s_started;
        private volatile static int s_requestId;
        private static readonly Lock s_startLock = new();

        /// <summary>
        /// 检查执行口：默认走 CommunityManager.CheckSelfUpdateAsync（组合根静态客户端）；
        /// 测试注入 HTTP 桩客户端构造的检查委托。
        /// </summary>
        internal static Func<string, string, int, Task<UpdateCheckResult>> CheckProvider { get; set; }
            = (apiBaseUrl, currentVersion, requestId) => CommunityManager.CheckSelfUpdateAsync(
                CommunityManager.ApiClient,
                CommunityManager.ModCatalog,
                LocalModPostResolver.Instance,
                apiBaseUrl,
                currentVersion,
                requestId);

        /// <summary>
        /// 当前版本提供器：测试注入固定版本（测试 host 无 scbbsplus 模组实体，
        /// 真实枚举恒返回 null 会跳过检查）；生产默认读游戏模组列表。
        /// </summary>
        internal static Func<string> VersionProvider { get; set; } = CommunityVersionProvider.GetCurrentVersion;

        /// <summary>
        /// 最近一次被帧循环消费的检查结果（含被丢弃的结果）。供测试断言消费路径，
        /// 生产代码不读取。
        /// </summary>
        internal static UpdateCheckResult LastConsumedResult { get; private set; }

        /// <summary>
        /// 最近一次成功检查的三态比较结论（无更新细分：已最新/本地更新/无法比较）。
        /// 供测试断言分支，生产代码不读取。
        /// </summary>
        internal static UpdateComparisonState? LastConsumedComparison { get; private set; }

        /// <summary>
        /// 发现更新时的展示回调（主报告 #4 归位）：本类留 Integration 只管检查调度，
        /// 弹窗等 UI 副作用经此回调交 Presentation 侧（ScbbsPlusAutoUpdatePresenter）
        /// 安装，组合根（ModLoader.OnLoadingFinished，先于帧泵首帧）接线。
        /// 未安装则按"无 UI 可展示"丢弃本次发现并记日志；s_started 保持已启动，
        /// 本会话不再自动重查（与"已最新"路径同为一次性检查语义）。
        /// </summary>
        internal static Action<UpdateCheckResult, string, string> UpdateFoundHandler { get; set; }

        public static void SetEnabled(bool enabled)
        {
            s_settingsLoaded = true;
            s_enabled = enabled;
            if (enabled && !s_started)
            {
                StartUpdateCheck();
            }
        }

        /// <summary>
        /// 测试专用：复位全部可变静态状态，替代反射 SetValue 保证 xUnit 并行类间隔离。
        /// </summary>
        internal static void ResetForTests()
        {
            lock (s_startLock)
            {
                s_updateCheckTask = null;
                s_settingsLoaded = false;
                s_enabled = true;
                s_started = false;
                s_requestId = 0;
                CheckProvider = null;
                VersionProvider = CommunityVersionProvider.GetCurrentVersion;
                LastConsumedResult = null;
                LastConsumedComparison = null;
                UpdateFoundHandler = null;
            }
        }

        public static void Update()
        {
            EnsureSettingsLoaded();
            if (!s_enabled)
            {
                return;
            }

            if (!s_started)
            {
                StartUpdateCheck();
            }

            UpdateCheckTask();
        }

        private static void EnsureSettingsLoaded()
        {
            if (s_settingsLoaded)
            {
                return;
            }

            s_settingsLoaded = true;
            try
            {
                CommunitySettingsManager.Instance.EnsureLoaded();
                s_enabled = CommunitySettingsManager.Instance.AutoUpdateCheckEnabled;
            }
            catch (Exception ex)
            {
                s_enabled = true;
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAutoUpdateController", "LoadAutoUpdateSettingsFailed"), ex.Message));
            }
        }

        private static void StartUpdateCheck()
        {
            // 游戏侧 Changed 回调线程与帧循环可能并发进入：锁内 check-then-act 防重复启动。
            lock (s_startLock)
            {
                if (s_updateCheckTask != null)
                {
                    return;
                }

                s_started = true;
                string currentVersion = (VersionProvider ?? CommunityVersionProvider.GetCurrentVersion)();
                if (string.IsNullOrWhiteSpace(currentVersion))
                {
                    // 当前版本不可知（模组未加载/枚举失败）：无法比较，跳过自动检查，
                    // 不伪造版本号制造虚假更新提示。
                    Log.Information(LanguageControl.Get("ScbbsPlusAutoUpdateController", "CurrentVersionUnavailableSkippingCheck"));
                    s_started = false;
                    return;
                }
                int requestId = ++s_requestId;
                Func<string, string, int, Task<UpdateCheckResult>> check = CheckProvider;
                if (check == null)
                {
                    // 检查口被置空：防御性兜底，避免 Task 内 NRE。
                    s_updateCheckTask = Task.FromResult(new UpdateCheckResult
                    {
                        RequestId = requestId,
                        CurrentVersion = currentVersion,
                        Error = LanguageControl.Get("ScbbsPlusCommunityScreen", "Upload", "Update", "CommunityUpdateServiceNotInjected")
                    });
                    return;
                }
                CommunitySettingsManager.Instance.EnsureLoaded();
                string apiBaseUrl = CommunitySettingsManager.Instance.ApiBaseUrl;
                s_updateCheckTask = Task.Run(async () =>
                {
                    try
                    {
                        return await check(apiBaseUrl, currentVersion, requestId);
                    }
                    catch (Exception ex)
                    {
                        return new UpdateCheckResult
                        {
                            RequestId = requestId,
                            CurrentVersion = currentVersion,
                            Error = ex.Message
                        };
                    }
                });
            }
        }

        private static void UpdateCheckTask()
        {
            if (s_updateCheckTask == null || !s_updateCheckTask.IsCompleted)
            {
                return;
            }

            UpdateCheckResult result = s_updateCheckTask.Result;
            s_updateCheckTask = null;
            LastConsumedResult = result;
            if (result == null)
            {
                // 服务实现不应返回 null；即便发生，也保持 s_started 不变，
                // 避免「每帧重启检查」的检查风暴。
                Log.Warning(LanguageControl.Get("ScbbsPlusAutoUpdateController", "UpdateCheckReturnedEmpty"));
                return;
            }
            if (result.RequestId != s_requestId || !s_enabled)
            {
                // 结果被丢弃（过期结果或检查期间用户关闭了自动更新）：复位启动标记，
                // 重新开启后由 SetEnabled/Update 再次触发检查，而不是整会话失效。
                s_started = false;
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAutoUpdateController", "AutoUpdateCheckFailed"), result.Error));
                return;
            }

            string latestVersion = string.IsNullOrWhiteSpace(result.LatestVersion)
                ? ExternalVersionParser.Normalize(result.CommunityVersion?.Version)
                : result.LatestVersion;
            string currentVersion = string.IsNullOrWhiteSpace(result.CurrentVersion) ? "-" : result.CurrentVersion;
            string latestVersionDisplay = string.IsNullOrWhiteSpace(latestVersion) ? "-" : latestVersion;
            UpdateComparisonState comparison = UpdateComparisonEvaluator.Evaluate(
                result.HasUpdate,
                result.CurrentNuGetVersion,
                ExternalVersionParser.ParseOrNull(latestVersion));
            LastConsumedComparison = comparison;
            switch (comparison)
            {
                case UpdateComparisonState.Incomparable:
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAutoUpdateController", "UpdateCheckVersionsIncomparable"), currentVersion, latestVersionDisplay));
                    return;
                case UpdateComparisonState.CurrentVersionNewer:
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAutoUpdateController", "UpdateCheckCurrentVersionNewer"), currentVersion, latestVersionDisplay));
                    return;
                case UpdateComparisonState.UpToDate:
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAutoUpdateController", "UpdateCheckAlreadyUpToDate"), currentVersion, latestVersionDisplay));
                    return;
            }

            string downloadUrl = result.CommunityFile?.Url;
            if (result.CommunityFile == null || string.IsNullOrWhiteSpace(downloadUrl))
            {
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusAutoUpdateController", "UpdateFoundNoScmodFile"), latestVersion));
                return;
            }

            string fileName = CommunityFileRules.GetDownloadFileName(result.CommunityFile);
            Action<UpdateCheckResult, string, string> handler = UpdateFoundHandler;
            if (handler == null)
            {
                Log.Information(LanguageControl.Get("ScbbsPlusAutoUpdateController", "UpdateFoundButPresentationNotReady"));
                return;
            }
            handler(result, latestVersion, fileName);
        }
    }
}
