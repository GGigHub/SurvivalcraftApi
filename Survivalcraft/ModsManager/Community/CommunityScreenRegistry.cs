using System;
using System.Collections.Generic;
using System.Globalization;
using Engine;

namespace Game
{
    /// <summary>
    /// 屏批两屏（社区/More）统一注册（屏名清单见 CommunityScreenNames；Local/MyPublished 后续轮加回）。
    /// 屏幕实例只 new 一次（幂等）；引擎换语言时用 Activator.CreateInstance 整表
    /// 重建 m_screens 且不调用旧实例 Leave，旧实例连同 ctor 订阅的静态事件一起成为
    /// 僵尸——故静态事件 handler 须以 IsOwningScreenLive 自检，发现归属屏被替换即退订。
    /// </summary>
    internal static class CommunityScreenRegistry
    {
        private static readonly (string Name, Func<Screen> Factory)[] s_screenRegistrations =
        [
            (CommunityScreenNames.Community, static () => new CommunityScreen()),
            (CommunityScreenNames.More, static () => new CommunityMoreScreen())
        ];

        /// <summary>
        /// 控件归属屏是否仍是 ScreensManager 当前注册的实例。真实语义是"控件挂在某个
        /// 已注册 Screen 的子树下"——不要拿非 Screen 挂载点的控件来问（如对话框控件，
        /// 其解析路径都会落到某个已注册屏，当前没问题；直接挂在引擎 RootWidget 下的
        /// 散控件会判 false）。换语言整表重建后旧实例返回 false，静态事件 handler 据此
        /// 退订自救。上溯止步于 ScreensManager.RootWidget 的直接子级：屏在显示链上时其
        /// ParentWidget 就是引擎 RootWidget，继续上溯会把存活屏误判成孤儿。休眠屏
        /// （不在显示链）ParentWidget 为空，root 即屏本身。
        /// </summary>
        public static bool IsOwningScreenLive(Widget widget)
        {
            if (widget == null)
            {
                return false;
            }

            Widget root = widget;
            while (root.ParentWidget != null && root.ParentWidget != ScreensManager.RootWidget)
            {
                root = root.ParentWidget;
            }
            return root is Screen screen && ScreensManager.m_screens.ContainsValue(screen);
        }

        /// <summary>
        /// 幂等注册屏批两屏；任何一屏构造失败记日志并继续处理其他屏幕。
        /// 社区屏注册时附带远程导入主机注册。
        /// </summary>
        public static void EnsureAllScreensRegistered(string source)
        {
            foreach ((string name, Func<Screen> factory) in s_screenRegistrations)
            {
                EnsureScreenRegistered(source, name, factory);
            }
        }

        private static void EnsureScreenRegistered(string source, string screenName, Func<Screen> factory)
        {
            Screen screen = ScreensManager.FindScreen<Screen>(screenName);
            if (screen == null)
            {
                try
                {
                    screen = factory();
                    ScreensManager.AddScreen(screenName, screen);
                    Log.Information(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusScreenRegistry", "ScreenRegistered"), screenName, source));
                }
                catch (Exception e)
                {
                    Log.Error(string.Format(CultureInfo.InvariantCulture, LanguageControl.Get("ScbbsPlusScreenRegistry", "RegisterScreenFailed"), screenName, source, e));
                    return;
                }
            }

            if (string.Equals(screenName, CommunityScreenNames.Community, StringComparison.Ordinal)
                && screen is CommunityScreen communityScreen)
            {
                RemoteImportManager.RegisterHost(communityScreen);
            }
        }
    }
}
