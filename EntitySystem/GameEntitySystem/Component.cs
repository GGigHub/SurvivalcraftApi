using System;
using System.Collections.Generic;
using Engine;
using TemplatesDatabase;

namespace GameEntitySystem {
    public abstract class Component : IDisposable {
        public Entity m_entity;

        public ValuesDictionary m_valuesDictionary;

        public ValuesDictionary ValuesDictionary => m_valuesDictionary;

        public Entity Entity => m_entity;

        public Project Project => m_entity.Project;

        public bool IsAddedToProject => m_entity.IsAddedToProject;

        public virtual IEnumerable<Entity> GetOwnedEntities() => ReadOnlyList<Entity>.Empty;

        protected int StableHash(int salt = 0) {
            unchecked {
                int hash = (int)2166136261;
                hash = (hash ^ salt) * 16777619;
                hash = (hash ^ (m_entity?.Id ?? 0)) * 16777619;
                string componentName = m_valuesDictionary?.DatabaseObject?.Name ?? GetType().FullName;
                for (int i = 0; i < componentName.Length; i++) {
                    hash = (hash ^ componentName[i]) * 16777619;
                }
                return hash & int.MaxValue;
            }
        }

        public Component() { }

        public virtual void OnEntityAdded() { }

        public virtual void OnEntityRemoved() { }

        public virtual void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap) { }

        public virtual void Save(ValuesDictionary valuesDictionary, EntityToIdMap entityToIdMap) { }

        public virtual void Dispose() { }

        public virtual void InheritFromComponent(Component baseComponent) { }

        internal void DisposeInternal() {
            GC.SuppressFinalize(this);
            Dispose();
        }

        public virtual void Initialize(Entity entity, ValuesDictionary valuesDictionary) {
            if (valuesDictionary.DatabaseObject.Type != entity.Project.GameDatabase.MemberComponentTemplateType) {
                throw new InvalidOperationException("ValuesDictionary has invalid type.");
            }
            m_entity = entity;
            m_valuesDictionary = valuesDictionary;
        }
    }
}
