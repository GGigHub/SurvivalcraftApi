using Engine;
using GameEntitySystem;
using System;
using System.Linq;
using TemplatesDatabase;

namespace Game {
    public class ComponentRider : Component, IUpdateable {
        public SubsystemBodies m_subsystemBodies;

        public SubsystemTerrain m_subsystemTerrain;

        public DynamicArray<ComponentBody> m_componentBodies = [];

        public Vector3 m_riderOffset;

        public float m_animationTime;

        public bool m_isAnimating;

        public bool m_isDismounting;

        public Vector3 m_targetPositionOffset;

        public Quaternion m_targetRotationOffset;

        public float m_outOfMountTime;
        
        public float DetectSurroundingMountRange = 2.5f;

        public static event Action<ComponentRider, ComponentMount, bool> OnMounting;

        public static event Action<ComponentRider, bool> OnDismounting;

        public ComponentCreature ComponentCreature { get; set; }

        public ComponentMount Mount {
            get {
                if (ComponentCreature.ComponentBody.ParentBody != null) {
                    return ComponentCreature.ComponentBody.ParentBody.Entity.FindComponent<ComponentMount>();
                }
                return null;
            }
        }

        /// <summary>
        /// The mount represented by the latest authoritative transition. During a
        /// dismount animation the physical parent is retained briefly, while the
        /// authoritative relationship is already unmounted.
        /// </summary>
        public ComponentMount AuthoritativeMount =>
            m_isAnimating && m_isDismounting ? null : Mount;

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public virtual ComponentMount FindNearestMount() {
            Vector2 point = new(ComponentCreature.ComponentBody.Position.X, ComponentCreature.ComponentBody.Position.Z);
            m_componentBodies.Clear();
            m_subsystemBodies.FindBodiesAroundPoint(point, DetectSurroundingMountRange, m_componentBodies);
            float num = 0f;
            ComponentMount result = null;
            foreach (ComponentMount item in from b in m_componentBodies
                                            select b.Entity.FindComponent<ComponentMount>()
                into m
                                            where m != null && m.Entity != Entity
                                            select m) {
                float allowToStartRange = item.MountAllowToStartRange;
                float num2 = ScoreMount(item, allowToStartRange);
                if (num2 > num) {
                    num = num2;
                    result = item;
                }
            }
            return result;
        }

        public virtual void StartMounting(ComponentMount componentMount, bool fromNetwork = false) {
            if (!m_isAnimating
                && Mount == null
                && componentMount != null) {
                if (NetworkManager.IsClientRunning && !fromNetwork) {
                    OnMounting?.Invoke(this, componentMount, true);
                    return;
                }
                if (ApplyAuthoritativeMountState(componentMount, animate: true)
                    && !fromNetwork) {
                    OnMounting?.Invoke(this, componentMount, false);
                }
            }
        }

        public virtual void StartDismounting(bool fromNetwork = false) {
            if (!m_isAnimating
                && Mount != null) {
                if (NetworkManager.IsClientRunning && !fromNetwork) {
                    OnDismounting?.Invoke(this, true);
                    return;
                }
                if (ApplyAuthoritativeMountState(null, animate: true)
                    && !fromNetwork) {
                    OnDismounting?.Invoke(this, false);
                }
            }
        }

        /// <summary>
        /// Reconciles the physical parent link with an authoritative mount state.
        /// This method never raises network request/status events and is therefore
        /// safe for packet application, late-join baselines and server fixtures.
        /// </summary>
        public virtual bool ApplyAuthoritativeMountState(
            ComponentMount componentMount,
            bool animate) {
            ComponentBody body = ComponentCreature.ComponentBody;
            ComponentMount currentMount = Mount;
            if (componentMount != null) {
                if (componentMount.Entity == Entity
                    || componentMount.ComponentBody == null
                    || componentMount.ComponentBody.Entity?.Project != Entity.Project) {
                    return false;
                }
                if (currentMount == componentMount) {
                    if (m_isAnimating && m_isDismounting) {
                        m_isAnimating = false;
                        m_isDismounting = false;
                        m_animationTime = 0f;
                        m_targetPositionOffset = componentMount.MountOffset + m_riderOffset;
                        m_targetRotationOffset = Quaternion.Identity;
                        body.ParentBodyPositionOffset = m_targetPositionOffset;
                        body.ParentBodyRotationOffset = m_targetRotationOffset;
                    }
                    return true;
                }
                if (body.ParentBody != null) {
                    body.ParentBody = null;
                }
                m_animationTime = 0f;
                m_isDismounting = false;
                body.ParentBody = componentMount.ComponentBody;
                m_targetPositionOffset = componentMount.MountOffset + m_riderOffset;
                m_targetRotationOffset = Quaternion.Identity;
                if (animate) {
                    m_isAnimating = true;
                    body.ParentBodyPositionOffset = Vector3.Transform(
                        body.Position - componentMount.ComponentBody.Position,
                        Quaternion.Conjugate(componentMount.ComponentBody.Rotation));
                    body.ParentBodyRotationOffset =
                        Quaternion.Conjugate(componentMount.ComponentBody.Rotation)
                        * body.Rotation;
                }
                else {
                    m_isAnimating = false;
                    body.ParentBodyPositionOffset = m_targetPositionOffset;
                    body.ParentBodyRotationOffset = m_targetRotationOffset;
                    m_outOfMountTime = 0f;
                }
                ComponentCreature.ComponentLocomotion.IsCreativeFlyEnabled = false;
                return true;
            }

            if (currentMount == null) {
                m_isAnimating = false;
                m_isDismounting = false;
                m_animationTime = 0f;
                return true;
            }
            if (!animate) {
                body.Velocity = currentMount.ComponentBody.Velocity;
                body.ParentBody = null;
                m_isAnimating = false;
                m_isDismounting = false;
                m_animationTime = 0f;
                m_outOfMountTime = 0f;
                return true;
            }

            float x = CalculateDismountOffset(currentMount);
            m_isAnimating = true;
            m_animationTime = 0f;
            m_isDismounting = true;
            m_targetPositionOffset = currentMount.MountOffset
                + m_riderOffset
                + new Vector3(x, currentMount.DismountOffset.Y, currentMount.DismountOffset.Z);
            m_targetRotationOffset = Quaternion.CreateFromAxisAngle(
                Vector3.UnitY,
                MathF.Sign(x) * MathUtils.DegToRad(60f));
            return true;
        }

        private float CalculateDismountOffset(ComponentMount componentMount) {
            if (componentMount.DismountOffset.X <= 0f) {
                return 0f;
            }
            float distance = componentMount.DismountOffset.X + 0.5f;
            Vector3 center = 0.5f * (ComponentCreature.ComponentBody.BoundingBox.Min
                + ComponentCreature.ComponentBody.BoundingBox.Max);
            TerrainRaycastResult? left = m_subsystemTerrain.Raycast(
                center,
                center - distance * ComponentCreature.ComponentBody.Matrix.Right,
                false,
                true,
                null);
            TerrainRaycastResult? right = m_subsystemTerrain.Raycast(
                center,
                center + distance * ComponentCreature.ComponentBody.Matrix.Right,
                false,
                true,
                null);
            return !left.HasValue ? -componentMount.DismountOffset.X
                : !right.HasValue ? componentMount.DismountOffset.X
                : left.Value.Distance <= right.Value.Distance
                    ? MathUtils.Min(right.Value.Distance, componentMount.DismountOffset.X)
                    : -MathUtils.Min(left.Value.Distance, componentMount.DismountOffset.X);
        }

        public virtual void Update(float dt) {
            if (m_isAnimating) {
                float f = 8f * dt;
                ComponentBody componentBody = ComponentCreature.ComponentBody;
                componentBody.ParentBodyPositionOffset = Vector3.Lerp(componentBody.ParentBodyPositionOffset, m_targetPositionOffset, f);
                componentBody.ParentBodyRotationOffset = Quaternion.Slerp(componentBody.ParentBodyRotationOffset, m_targetRotationOffset, f);
                m_animationTime += dt;
                if (Vector3.DistanceSquared(componentBody.ParentBodyPositionOffset, m_targetPositionOffset) < 0.0100000007f
                    || m_animationTime > 0.75f) {
                    m_isAnimating = false;
                    if (m_isDismounting) {
                        if (componentBody.ParentBody != null) {
                            componentBody.Velocity = componentBody.ParentBody.Velocity;
                            componentBody.ParentBody = null;
                        }
                        m_isDismounting = false;
                    }
                    else {
                        componentBody.ParentBodyPositionOffset = m_targetPositionOffset;
                        componentBody.ParentBodyRotationOffset = m_targetRotationOffset;
                        m_outOfMountTime = 0f;
                    }
                }
            }
            ComponentMount mount = Mount;
            if (mount != null
                && !m_isAnimating) {
                ComponentBody componentBody2 = ComponentCreature.ComponentBody;
                ComponentBody parentBody = ComponentCreature.ComponentBody.ParentBody;
                if (Vector3.DistanceSquared(
                        parentBody.Position + Vector3.Transform(componentBody2.ParentBodyPositionOffset, parentBody.Rotation),
                        componentBody2.Position
                    )
                    > 0.160000011f) {
                    m_outOfMountTime += dt;
                }
                else {
                    m_outOfMountTime = 0f;
                }
                ComponentHealth componentHealth = mount.Entity.FindComponent<ComponentHealth>();
                if (m_outOfMountTime > 0.1f
                    || (componentHealth != null && componentHealth.Health <= 0f)
                    || ComponentCreature.ComponentHealth.Health <= 0f) {
                    // The server owns every rider. A client may only request a
                    // dismount for its local player; observers never mutate it.
                    if (!NetworkManager.IsClientRunning
                        || ComponentCreature.ComponentBody.Player?.PlayerData.IsMainPlayer == true) {
                        StartDismounting();
                    }
                }
                Vector3 positionOffset = mount.MountOffset + m_riderOffset;
                if (ComponentCreature.ComponentBody.IsSneaking) {
                    positionOffset += new Vector3(0f, 0.5f, 0.1f);
                }
                ComponentCreature.ComponentBody.ParentBodyPositionOffset = positionOffset;
                ComponentCreature.ComponentBody.ParentBodyRotationOffset = Quaternion.Identity;
            }
        }

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap) {
            m_subsystemBodies = Project.FindSubsystem<SubsystemBodies>(true);
            m_subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>(true);
            ComponentCreature = Entity.FindComponent<ComponentCreature>(true);
            m_riderOffset = valuesDictionary.GetValue<Vector3>("RiderOffset");
        }

        public virtual float ScoreMount(ComponentMount componentMount, float maxDistance) {
            float? score = null;
            bool modDisallows = false;
            ModsManager.HookAction(
                "ScoreMount",
                modLoader => {
                    modLoader.ScoreMount(this, componentMount, out float? scoreByMod);
                    if (scoreByMod.HasValue
                        && scoreByMod < 0) {
                        modDisallows = true;
                    }
                    if (scoreByMod.HasValue) {
                        score = MathUtils.Max(score ?? float.MinValue, scoreByMod.Value);
                    }
                    return false;
                }
            );
            if (modDisallows) {
                return -1;
            }
            if (score.HasValue) {
                return score.Value;
            }
            if (componentMount.ComponentBody.Velocity.LengthSquared() < 1f) {
                Vector3 v = componentMount.ComponentBody.Position
                    + Vector3.Transform(componentMount.MountOffset, componentMount.ComponentBody.Rotation)
                    - ComponentCreature.ComponentCreatureModel.EyePosition;
                if (v.Length() < maxDistance) {
                    Vector3 forward = Matrix.CreateFromQuaternion(ComponentCreature.ComponentCreatureModel.EyeRotation).Forward;
                    if (Vector3.Dot(Vector3.Normalize(v), forward) > 0.33f) {
                        return maxDistance - v.Length();
                    }
                }
            }
            return 0f;
        }
    }
}




