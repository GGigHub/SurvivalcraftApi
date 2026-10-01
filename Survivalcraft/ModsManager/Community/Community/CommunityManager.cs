namespace Game
{
    /// <summary>
    /// 社区内容域静态门面：精选/浏览/详情/评分/版本目录/社区更新等 HTTP 查询的统一入口，
    /// 对齐游戏本体 *Manager 静态域类风格。
    ///
    /// 依赖缝：各方法以 <see cref="CommunityApiClient"/> 为首参（参数穿线），
    /// 生产侧传本类 <see cref="ApiClient"/>，测试传 HTTP 桩客户端。
    /// </summary>
    internal static partial class CommunityManager
    {
        /// <summary>社区 API 生产客户端单例（社区服务组合点解散后的唯一接线）。</summary>
        internal static readonly CommunityApiClient ApiClient = new(HttpClientProvider.Community);

        /// <summary>版本目录查询客户端，依赖 <see cref="ApiClient"/>，声明顺序勿反转。</summary>
        internal static readonly CommunityModCatalogClient ModCatalog = new(ApiClient);
    }
}
