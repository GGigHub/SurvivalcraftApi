namespace Game
{
    /// <summary>
    /// 模组自身身份的唯一权威定义：包名、展示名与自更新地址。
    /// 此前 "scbbsplus" 在 8 处独立硬编码、gitee 地址钉在屏幕类上——漏改任意一处
    /// 即"是否本 mod"判定失效、SelfReview 误认自己、自更新指旧址。改包名/多包名时
    /// 与 modinfo.json 的 PackageName 一起同步。
    /// </summary>
    internal static class ModIdentity
    {
        /// <summary>与 modinfo.json 的 PackageName 同步维护。</summary>
        public const string PackageName = "scbbsplus";

        /// <summary>UI 展示名（评测提示/上传目标/更新对话框标题回退）。</summary>
        public const string DisplayName = "ScbbsPlus";

        public const string ReleaseApiUrl = "https://gitee.com/api/v5/repos/suancaixianyu/scbbs-plus-mod/releases/latest";
        public const string ProjectPageUrl = "https://gitee.com/suancaixianyu/scbbs-plus-mod";
        public const string ReleasePageUrl = "https://gitee.com/suancaixianyu/scbbs-plus-mod/releases";
    }
}
