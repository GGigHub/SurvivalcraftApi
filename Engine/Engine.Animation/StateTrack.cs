namespace Engine.Animation {
    /// <summary>
    /// 状态轨道运行时实例
    /// </summary>
    public class StateTrack {
        public string Name { get; }
        public StateTrackType Type { get; }
        public object Value { get; set; }
        public object DefaultValue { get; }

        // Enum 类型特有
        public string[] EnumValues { get; }

        // Float 类型特有
        public float MinValue { get; }
        public float MaxValue { get; }

        public event Action<StateTrack, object, object> OnStateChanged;

        public StateTrack(string name, StateTrackDefinition definition) {
            Name = name;
            Type = definition.Type;
            DefaultValue = definition.DefaultValue;
            EnumValues = definition.EnumValues;
            MinValue = definition.MinValue;
            MaxValue = definition.MaxValue;
            Value = DefaultValue;
        }

        public void SetValue(object value) {
            if (!Equals(Value, value)) {
                object oldValue = Value;
                Value = value;
                OnStateChanged?.Invoke(this, oldValue, value);
            }
        }
    }
}