using GameEntitySystem;
using System;

namespace Game {
    public abstract class ComponentBehavior : Component {
        public Func<bool> IsNetworkMode;

        public abstract float ImportanceLevel { get; }

        public virtual bool IsActive { get; set; }

        public virtual string DebugInfo => string.Empty;
    }
}