namespace Engine.Animation {
    /// <summary>
    /// 动画模板，定义生物类型的动画特性
    /// </summary>
    public class AnimationTemplate {
        public string Name { get; }
        public Dictionary<string, LayerDefinition> Layers { get; }
        public Dictionary<string, StateTrackDefinition> StateTracks { get; }
        public string[] RequiredBones { get; }

        public AnimationTemplate(string name,
            Dictionary<string, LayerDefinition> layers,
            Dictionary<string, StateTrackDefinition> stateTracks,
            string[] requiredBones = null) {
            Name = name;
            Layers = layers ?? new Dictionary<string, LayerDefinition>();
            StateTracks = stateTracks ?? new Dictionary<string, StateTrackDefinition>();
            RequiredBones = requiredBones ?? new string[0];
        }
    }
}