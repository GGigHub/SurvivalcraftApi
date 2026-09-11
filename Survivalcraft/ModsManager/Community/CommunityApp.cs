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
        /// 进程级初始化：组合根接线（设置副作用订阅先于任何设置写入）、
        /// 崩溃残留恢复与启动期挂起动作。
        /// </summary>
        public static void Initialize()
        {
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
        /// 设置副作用接线：组合根订阅包装属性事件驱动切服。per-URL 会话下切服
        /// 不再登出——只更新会话指针并激活目标服会话；同值赋值由 OnServerSwitched
        /// 内部比较 no-op。订阅后回读一次现值完成初始同步（置指针；实际读盘在
        /// GlobalFramePump 首帧 EnsureStoredAuthLoaded 的 legacy 迁移之后）。
        /// </summary>
        private static void WireSettingsSideEffects()
        {
            CommunitySettingsManager.ApiBaseUrlChanged += apiBaseUrl => AuthSessionManager.Instance.OnServerSwitched(apiBaseUrl);
            AuthSessionManager.Instance.OnServerSwitched(CommunitySettingsManager.ApiBaseUrl);
        }
    }
}
