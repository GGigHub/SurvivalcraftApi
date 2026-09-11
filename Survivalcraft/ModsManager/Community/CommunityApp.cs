using System;
using System.Globalization;
using System.Xml.Linq;
using Engine;
using GameEntitySystem;

namespace Game
{
    /// <summary>
    /// ScbbsPlus 组合根与生命周期门面：移植进游戏本体后 ModLoader 已删除，
    /// 由游戏原生调用点（LoadingScreen/ScreensManager/GameManager 等效位置）直调本类。
    /// </summary>
    internal static class CommunityApp
    {
        /// <summary>
        /// 进程级初始化：旧模组设置一次性迁移必须最早执行（直写 SettingsManager.Community*
        /// 不触发事件链，且必须早于任何 EnsureLoaded），随后是组合根接线（设置副作用订阅
        /// 先于任何 OnModSettingChanged / EnsureLoaded）、崩溃残留恢复与启动期挂起动作。
        /// </summary>
        public static void Initialize()
        {
            // 迁移前置条件此刻已满足：SettingsManager.Initialize（Program.cs）已写入默认值，
            // ModsManager.Initialize 的 LoadModSettings 已填充 ModSettingsCache。
            // 必须先于 WireCompositionRoot：迁移直写 SettingsManager，不经 ApiBaseUrlChanged（切服登出链）。
            LegacySettingsMigrator.Run();
            WireCompositionRoot();
            try
            {
                TransactionalFileReplacement.RecoverLeftoverBackups(
                    ModsManager.ModsPath,
                    ModsManager.DocPath);
            }
            catch (Exception ex)
            {
                Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusModLoader", "StartupRollbackResidualBackupFailed"), ex.Message));
            }

            // 先于一切业务读取：修复上次进程死于 MoveFile 两步之间时 data:ScbbsPlus 下的状态丢失。
            JsonFileStore.RecoverLeftoverTempFiles(StoragePaths.RootDirectory);
            // ModPacks 主文件/meta 与 JsonFileStore 同款 .tmp 命名，同因写入：Move 阶段死亡时包文件唯一副本成孤儿 .tmp。
            JsonFileStore.RecoverLeftoverTempFiles(ModpackRegistry.Shared.DirectoryPath);

            PendingLocalModActions.Process("startup-early", includeSettingActions: false);
        }

        public static void OnLoadingFinished()
        {
            PendingLocalModActions.Process("startup");
            // 全局帧泵：下载收尾/自动更新/认证轮询改由引擎 Window.Frame 驱动，不再依赖 mod 屏 Update。
            GlobalFramePump.Initialize();
            Log.Information(LanguageControl.Get("ScbbsPlusModLoader", "GameLoaded"));
        }

        public static void OnScreenEntered(Screen screen, object[] screenParameters)
        {
            if (screen is MainMenuScreen)
            {
                bool shouldRestoreScbbsPlusAfterRestart = ModSettingsState.HasPendingRestartReturnTarget();
                Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusModLoader", "EnteredMainMenuRestoreFlag"), shouldRestoreScbbsPlusAfterRestart));
                if (shouldRestoreScbbsPlusAfterRestart)
                {
                    Log.Information(LanguageControl.Get("ScbbsPlusModLoader", "RequestingSwitchToPreRestartScreen"));
                    if (!ModSettingsState.TryGetRestartReturnTarget(out string targetScreenName))
                    {
                        return;
                    }

                    Screen targetScreen = ScreensManager.FindScreen<Screen>(targetScreenName);
                    if (targetScreen == null)
                    {
                        Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusModLoader", "RestartReturnScreenNotRegistered"), targetScreenName));
                        return;
                    }

                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusModLoader", "AutoSwitchFromMainMenuToScreen"), targetScreenName));
                    ScreensManager.SwitchScreen(targetScreen);
                    ModSettingsState.ClearRestartReturnTarget();
                }
            }
        }

        public static void OnScreenLeaved(Screen screen)
        {
            GlobalToastManager.DetachFromScreen(screen);
        }

        public static void OnModSettingChanged(string[] idPath, object value)
        {
            CommunitySettingsManager.Instance.ApplySettingChange(idPath, value);
        }

        public static void OnProjectLoaded(Project project)
        {
            PlayedTimeTracker.StartProject(project);
        }

        public static void OnProjectDisposed()
        {
            PlayedTimeTracker.StopProject();
        }

        public static void OnProjectXmlSaved(XElement xElement)
        {
            PlayedTimeTracker.OnProjectXmlSaved();
        }

        /// <summary>
        /// 组合根显式接线：传输反馈提供者与设置副作用订阅。各域单例自身静态化后
        /// （TransferManager/SettingsManager.Instance 等），接线时机由本类显式控制，
        /// 不再有"首次触碰某个共享服务即隐式产生注入"的隐藏副作用。
        /// </summary>
        private static void WireCompositionRoot()
        {
            // 传输反馈的登录态回退：延迟求值（仅真正上报时解析），接线前匿名上报。
            TransferManager.ConfigureFeedbackProviders(
                () => AuthSessionManager.Instance.CurrentToken,
                () => AuthSessionManager.Instance.ApiBaseUrl);
            WireSettingsSideEffects();
        }

        /// <summary>
        /// 设置副作用接线：组合根（本类）订阅设置事件并驱动 Auth 与切服登出链，
        /// CommunitySettingsManager 不反向依赖 Presentation/Integration。
        /// 必须在任何 OnModSettingChanged / EnsureLoaded 之前订阅（Initialize 最早）。
        /// </summary>
        private static void WireSettingsSideEffects()
        {
            CommunitySettingsManager settings = CommunitySettingsManager.Instance;
            settings.ApiBaseUrlChanged += apiBaseUrl =>
            {
                // 切换服务器即登出旧会话。顺序关键：BeginLogout 在赋新值之前捕获旧
                // ApiBaseUrl，登出请求须发往旧服务器（新服务器上没有该会话）。
                AuthSessionManager auth = AuthSessionManager.Instance;
                if (auth.IsLoggedIn)
                {
                    auth.BeginLogout();
                }
                auth.ApiBaseUrl = apiBaseUrl;
            };
        }
    }
}
