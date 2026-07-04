namespace Engine.Animation.RootMotion {
    /// <summary>
    /// 位移应用模式
    /// </summary>
    public enum TranslationMode {
        /// <summary>
        /// 不应用位移（默认）
        /// </summary>
        None,

        /// <summary>
        /// 融合当前速度和动画速度
        /// </summary>
        Blend,

        /// <summary>
        /// 每次循环添加冲量
        /// </summary>
        AddImpulse,

        /// <summary>
        /// 直接覆盖速度
        /// </summary>
        Override
    }

    /// <summary>
    /// 融合方式（Blend 模式）
    /// </summary>
    public enum BlendMethod {
        /// <summary>
        /// 平滑阻尼（推荐）
        /// </summary>
        SmoothDamp,

        /// <summary>
        /// 加权平均
        /// </summary>
        WeightedAverage,

        /// <summary>
        /// 弹簧阻尼模型
        /// </summary>
        SpringDamper
    }

    /// <summary>
    /// 冲量计算方式（AddImpulse 模式）
    /// </summary>
    public enum ImpulseMethod {
        /// <summary>
        /// 平均速度 = 总位移 / 动画时长（默认）
        /// </summary>
        Average,

        /// <summary>
        /// 峰值速度 = 动画中最大瞬时速度
        /// </summary>
        Peak,

        /// <summary>
        /// 加权速度 = 考虑速度分布的加权平均
        /// </summary>
        Weighted
    }

    /// <summary>
    /// 缩放应用模式
    /// </summary>
    public enum ScaleMode {
        /// <summary>
        /// 不应用缩放（默认）
        /// </summary>
        None,

        /// <summary>
        /// 覆盖碰撞体尺寸
        /// </summary>
        Override
    }

    /// <summary>
    /// 缩放数据来源
    /// </summary>
    public enum ScaleSource {
        /// <summary>
        /// 从动画根骨骼缩放提取
        /// </summary>
        Animation,

        /// <summary>
        /// 使用配置的固定值
        /// </summary>
        Fixed
    }

    /// <summary>
    /// 位移应用配置
    /// </summary>
    public class TranslationConfig {
        /// <summary>
        /// 位移应用模式
        /// </summary>
        public TranslationMode Mode { get; set; } = TranslationMode.None;

        // Blend 模式参数

        /// <summary>
        /// 融合方式
        /// </summary>
        public BlendMethod BlendMethod { get; set; } = BlendMethod.SmoothDamp;

        /// <summary>
        /// SmoothDamp 平滑时间
        /// </summary>
        public float SmoothTime { get; set; } = 0.3f;

        /// <summary>
        /// WeightedAverage 权重
        /// </summary>
        public float BlendWeight { get; set; } = 0.5f;

        /// <summary>
        /// SpringDamper 刚度
        /// </summary>
        public float SpringStiffness { get; set; } = 100f;

        /// <summary>
        /// SpringDamper 阻尼
        /// </summary>
        public float SpringDamping { get; set; } = 10f;

        // AddImpulse 模式参数

        /// <summary>
        /// 冲量计算方式
        /// </summary>
        public ImpulseMethod ImpulseMethod { get; set; } = ImpulseMethod.Average;

        /// <summary>
        /// 冲量缩放因子
        /// </summary>
        public float ImpulseScale { get; set; } = 1.0f;

        /// <summary>
        /// 冲量触发的绝对动画相位（0-1）
        /// 0.0 = 动画首帧，1.0 = 动画末帧
        /// 默认 -1 表示自动：正播时使用 endPhase，反播时使用 startPhase
        /// 例如跳跃冲量在动画 18.2% 处触发：0.182
        /// </summary>
        public float ImpulsePhase { get; set; } = -1f;

        /// <summary>
        /// 直接指定冲量值（覆盖动画数据）
        /// </summary>
        public Vector3? ImpulseOverride { get; set; }

        // 速度控制

        /// <summary>
        /// 速度遮罩：哪些轴受根运动影响（X, Y, Z 分量，默认全部影响）
        /// </summary>
        public Vector3 VelocityMask { get; set; } = Vector3.One;

        // 安全限制

        /// <summary>
        /// 最大速度限制（米/秒），防止动画数据异常
        /// </summary>
        public float MaxSpeed { get; set; } = 20f;

        /// <summary>
        /// 最大冲量限制（米/秒），仅 AddImpulse 模式
        /// </summary>
        public float MaxImpulse { get; set; } = 10f;
    }

    /// <summary>
    /// 缩放应用配置
    /// </summary>
    public class ScaleConfig {
        /// <summary>
        /// 缩放应用模式
        /// </summary>
        public ScaleMode Mode { get; set; } = ScaleMode.None;

        /// <summary>
        /// 缩放数据来源
        /// </summary>
        public ScaleSource Source { get; set; } = ScaleSource.Animation;

        /// <summary>
        /// 固定缩放值（Fixed 模式）
        /// </summary>
        public Vector3? Value { get; set; }

        /// <summary>
        /// 过渡时长（秒）
        /// </summary>
        public float BlendDuration { get; set; } = 0.2f;

        /// <summary>
        /// 最小缩放限制（默认 0.01）
        /// </summary>
        public Vector3? MinScale { get; set; }
    }

    /// <summary>
    /// 根运动配置
    /// </summary>
    public class RootMotionConfig {
        /// <summary>
        /// 采样运动数据的骨骼名称。为空时使用自动检测（从 RootBone 向下 BFS 查找第一个有动画数据的骨骼）
        /// </summary>
        public string SourceBone { get; set; }

        /// <summary>
        /// 位移应用配置
        /// </summary>
        public TranslationConfig Translation { get; set; } = new();

        /// <summary>
        /// 缩放应用配置
        /// </summary>
        public ScaleConfig Scale { get; set; } = new();
    }
}