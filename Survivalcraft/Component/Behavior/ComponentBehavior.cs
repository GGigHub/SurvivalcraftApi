using GameEntitySystem;
using System;

namespace Game {
    public abstract class ComponentBehavior : Component {
        private bool m_isActive;

        public abstract float ImportanceLevel { get; }

        public virtual bool IsActive {
            get => m_isActive;
            set {
                if (NetworkManager.IsServerAuthoritativeCreatureBody(Entity?.FindComponent<ComponentBody>())) {
                    return;
                }
                m_isActive = value;
            }
        }

        public virtual string DebugInfo => string.Empty;
    }
}
