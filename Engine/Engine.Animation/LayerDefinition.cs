namespace Engine.Animation {
    /// <summary>
    /// 动画层定义
    /// </summary>
    public class LayerDefinition {
        public int Index { get; }
        public AnimationBlendMode BlendMode { get; }
        public string[] BoneMask { get; }

        public LayerDefinition(int index, AnimationBlendMode blendMode, string[] boneMask = null) {
            Index = index;
            BlendMode = blendMode;
            BoneMask = boneMask;
        }
    }
}