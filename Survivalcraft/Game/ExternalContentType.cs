namespace Game {
    public enum ExternalContentType {
        Unknown = 0,
        Directory = 1,
        World = 2,
        BlocksTexture = 3,
        CharacterSkin = 4,
        FurniturePack = 5,
        Mod = 6,
        [Obsolete]
        ModList = 7,
        // 以下成员由社区域引入：Modpack/NetMod 参与本地筛选与匹配，Image/Other 仅承载服务器帖子类型展示；
        // 原生内容链路不会产生这些值，勿重排已有 0–7（多处按 int 持久化）。
        Modpack = 8,
        NetMod = 9,
        Image = 10,
        Other = 11
    }
}