using Engine;
using GameEntitySystem;
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
        public ComponentCreature ComponentCreature { get; set; }

        public ComponentMount Mount {
            get {
                if (ComponentCreature.ComponentBody.ParentBody != null) {
                    return ComponentCreature.ComponentBody.ParentBody.Entity.FindComponent<ComponentMount>();
                }
                return null;
            }
        }

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

        public virtual void StartMounting(ComponentMount componentMount) {
            if (!m_isAnimating
                && Mount == null) {
                if (NetWork.NetworkManager.WorkType != NetWork.WorkType.Client) {
                    StartNetMounting(componentMount);
                    NetWork.NetworkManager.QueuePackage(new NetWork.ComponentMountPackage(this, componentMount));
                }
                else {
                    NetWork.NetworkManager.QueuePackage(new NetWork.ComponentMountPackage(this, componentMount, true));
                }
            }
        }

        public virtual void StartDismounting() {
            if (!m_isAnimating
                && Mount != null) {
                if (NetWork.NetworkManager.WorkType != NetWork.WorkType.Client) {
                    StartNetDismounting();
                    NetWork.NetworkManager.QueuePackage(new NetWork.ComponentMountPackage(this));
                }
                else {
                    NetWork.NetworkManager.QueuePackage(new NetWork.ComponentMountPackage(this, true));
                }
            }
        }

        public void StartNetMounting(ComponentMount componentMount) {
            StartMounting(componentMount);
        }

        public void StartNetDismounting() {
            StartDismounting();
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
                    //服务器和客户端都由自己控制下马
                    if (NetWork.NetworkManager.WorkType != NetWork.WorkType.Local && ComponentCreature.ComponentBody.Player != null && !ComponentCreature.ComponentBody.Player.PlayerData.IsMainPlayer) {

                    }
                    else {
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



