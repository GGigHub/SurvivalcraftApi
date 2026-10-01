using Engine;

namespace Game {
    /// <summary>
    /// 全局每帧泵：挂引擎 <c>Window.Frame</c>（主线程每帧,BeforeFrame/AfterFrame 之间），
    /// 驱动不依赖特定屏幕的跨屏轮询——下载队列收尾、自动更新检查、认证会话轮询。
    /// 收尾逻辑（弹窗/事件/队列推进）此前只由四个 mod 屏的 Update() 调用，
    /// 用户切回原生屏后离屏完成的收尾会停摆到下次进入 mod 屏。
    /// 弹窗/Toast 均自适应当前屏（DialogsManager/CommunityToastManager 以 CurrentScreen 为宿主），
    /// 离屏收尾时通知落在当前所在屏上。
    /// </summary>
    static class CommunityFramePump {
        /// <summary>由 ModLoader.OnLoadingFinished 调用；先摘后挂，任何重启路径下都恰好一个订阅。</summary>
        internal static void Initialize() {
            Window.Frame -= OnFrame;
            Window.Frame += OnFrame;
        }

        static void OnFrame() {
            // 加载阶段（LoadingScreen）不泵：此时恢复的队列项会在死屏上弹进度对话框
            // （ShowDialog 的宿主解析为 CurrentScreen=LoadingScreen，切换后不可见且无法关闭）。
            if (ScreensManager.CurrentScreen is LoadingScreen) {
                return;
            }

            // 轮询异常由各 Update 内部兜底：引擎 Frame?.Invoke() 无 try/catch，
            // 冒泡即杀进程；各自兜底使一个轮询故障不拖累同帧其他轮询。
            AuthSessionManager.Instance.Update();
            DownloadFlowManager.Instance.Update();
        }
    }
}