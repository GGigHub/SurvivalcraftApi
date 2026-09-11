namespace Game
{
    /// <summary>
    /// 四个 ScbbsPlus 屏的注册名与合法性判定。
    /// Application 层的状态持久化（重启返回目标、按屏记忆筛选类型）需要校验屏名，
    /// 故屏名清单置于 Application；CommunityScreenRegistry（ModsManager/Community）引用本类
    /// 完成注册，避免 Application → Presentation 反向依赖。本类是屏名的唯一权威。
    /// </summary>
    internal static class ScreenNames
    {
        public const string Local = "ScbbsPlusLocal";
        public const string Community = "ScbbsPlusCommunity";
        public const string MyPublished = "ScbbsPlusMyPublished";
        public const string More = "ScbbsPlusMore";

        public static bool IsKnown(string name)
        {
            return name is Local or Community or MyPublished or More;
        }
    }
}
