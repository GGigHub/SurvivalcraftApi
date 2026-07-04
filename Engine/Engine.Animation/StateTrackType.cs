namespace Engine.Animation {
    /// <summary>
    /// 状态轨道类型
    /// </summary>
    public enum StateTrackType {
        /// <summary>
        /// 枚举状态：Walk/Trot/Canter
        /// </summary>
        Enum,

        /// <summary>
        /// 布尔状态：IsFlying
        /// </summary>
        Bool,

        /// <summary>
        /// 浮点参数：DeathPhase (0.0 - 1.0)
        /// </summary>
        Float
    }
}