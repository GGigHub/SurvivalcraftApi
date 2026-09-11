namespace Game
{
    // 屏批 Task 4 依赖拉前的临时文件（对照 IsOwningScreenLive 先例）：
    // FiltersDialog 引用社区屏的两个成员，而 CommunityScreen 全量落地在 Task 6。
    // 两成员自模组 Presentation/Screens/Community/（ScbbsPlusCommunityScreen.cs 的
    // 命名空间级枚举与 Community.Ui.cs 的静态方法）原样抽出、仅去前缀；
    // Task 6 落地 CommunityScreen.cs / Community.Ui.cs 时须删除本文件。
    internal enum CommunitySection
    {
        All = 1,
        Trending = 2,
        NewActivity = 3
    }

    internal partial class CommunityScreen
    {
        internal static string GetVersionFilterName(bool onlyCurrentVersion)
        {
            return onlyCurrentVersion ? LanguageControl.Get("ScbbsPlusCommunityScreen", "Text", "CurrentVersion") : LanguageControl.Get("ScbbsPlusCommunityScreen", "Core", "Ui", "AllVersions");
        }
    }
}
