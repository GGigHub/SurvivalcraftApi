namespace Engine.Animation {
    /// <summary>
    /// 动画模板配置，用于 JSON 反序列化
    /// </summary>
    public class AnimationTemplateConfig {
        /// <summary>
        /// 模板名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 动画层配置字典（key 为层名）
        /// </summary>
        public Dictionary<string, TemplateLayerConfig> Layers { get; set; }

        /// <summary>
        /// 状态轨道配置字典（key 为轨道名）
        /// </summary>
        public Dictionary<string, TemplateStateTrackConfig> StateTracks { get; set; }

        /// <summary>
        /// 必需骨骼名称列表
        /// </summary>
        public List<string> RequiredBones { get; set; }
    }

    /// <summary>
    /// 模板动画层配置
    /// </summary>
    public class TemplateLayerConfig {
        /// <summary>
        /// 层索引（决定优先级）
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 混合模式：Override 或 Additive
        /// </summary>
        public string BlendMode { get; set; } = "Override";

        /// <summary>
        /// 骨骼遮罩（仅影响指定的骨骼）
        /// </summary>
        public List<string> BoneMask { get; set; }
    }

    /// <summary>
    /// 模板状态轨道配置
    /// </summary>
    public class TemplateStateTrackConfig {
        /// <summary>
        /// 轨道类型：Enum、Bool 或 Float
        /// </summary>
        public string Type { get; set; } = "Float";

        /// <summary>
        /// 默认值
        /// </summary>
        public object DefaultValue { get; set; }

        /// <summary>
        /// 枚举值列表（Type 为 Enum 时使用）
        /// </summary>
        public List<string> EnumValues { get; set; }

        /// <summary>
        /// 最小值（Type 为 Float 时使用）
        /// </summary>
        public float MinValue { get; set; }

        /// <summary>
        /// 最大值（Type 为 Float 时使用）
        /// </summary>
        public float MaxValue { get; set; }
    }
}