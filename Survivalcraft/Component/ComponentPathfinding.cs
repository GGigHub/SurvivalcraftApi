using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    public class ComponentPathfinding : Component, IUpdateable {
        public SubsystemTime m_subsystemTime;

        public SubsystemPathfinding m_subsystemPathfinding;

        public ComponentCreature m_componentCreature;

        public ComponentPilot m_componentPilot;

        public StateMachine m_stateMachine = new();

        public Random m_random = new();

        public Vector3? m_lastPathfindingDestination;

        public double? m_lastPathfindingTime;

        public float m_pathfindingCongestion;

        public PathfindingResult m_pathfindingResult = new();

        public double m_nextUpdateTime;

        public int m_randomMoveCount;

        public bool m_destinationChanged;

        // This is incremented whenever the server-side path snapshot changes.
        // Clients use it to identify a new authoritative path, while the
        // actual remaining path is kept in m_pathfindingResult.Path.
        public int NetworkPathVersion { get; private set; }

        // The cursor advances as the server consumes path points.  The path
        // array itself is kept in the game's native reverse order, so the
        // cursor is metadata used to detect progress rather than an index
        // into the array.
        public int NetworkPathCursor { get; private set; }

        private bool m_networkPathAuthoritative;
        private int m_appliedNetworkPathVersion;
        private int m_appliedNetworkPathCursor;

        public const float m_minPathfindingPeriod = 6f;

        public const float m_pathfindingCongestionCapacity = 500f;

        public const float m_pathfindingCongestionCapacityLimit = 1000f;

        public const float m_pathfindingCongestionDecayRate = 20f;

        public static bool DrawPathfinding;

        public Vector3? Destination { get; set; }

        public float Range { get; set; }

        public float Speed { get; set; }

        public int MaxPathfindingPositions { get; set; }

        public bool UseRandomMovements { get; set; }

        public bool IgnoreHeightDifference { get; set; }

        public bool RaycastDestination { get; set; }

        public ComponentBody DoNotAvoidBody { get; set; }

        public bool IsStuck { get; set; }

        public UpdateOrder UpdateOrder => UpdateOrder.Default;

        public virtual void SetDestination(Vector3? destination,
            float speed,
            float range,
            int maxPathfindingPositions,
            bool useRandomMovements,
            bool ignoreHeightDifference,
            bool raycastDestination,
            ComponentBody doNotAvoidBody) {
            bool changed = HasNetworkDestinationChanged(destination)
                || Speed != speed
                || Range != range
                || MaxPathfindingPositions != maxPathfindingPositions
                || UseRandomMovements != useRandomMovements
                || IgnoreHeightDifference != ignoreHeightDifference
                || RaycastDestination != raycastDestination
                || DoNotAvoidBody != doNotAvoidBody;
            Destination = destination;
            Speed = speed;
            Range = range;
            MaxPathfindingPositions = maxPathfindingPositions;
            UseRandomMovements = useRandomMovements;
            IgnoreHeightDifference = ignoreHeightDifference;
            RaycastDestination = raycastDestination;
            DoNotAvoidBody = doNotAvoidBody;
            m_destinationChanged = true;
            m_nextUpdateTime = 0.0;
            if (changed) {
                NetworkPathVersion++;
                NetworkPathCursor = 0;
            }
        }

        public virtual void Stop() {
            SetDestination(
                null,
                0f,
                0f,
                0,
                false,
                false,
                false,
                null
            );
            m_componentPilot.Stop();
            IsStuck = false;
        }

        public virtual void Update(float dt) {
            if (m_subsystemTime.GameTime >= m_nextUpdateTime) {
                float num = m_random.Float(0.08f, 0.12f);
                m_nextUpdateTime = m_subsystemTime.GameTime + num;
                m_pathfindingCongestion = MathUtils.Max(m_pathfindingCongestion - 20f * num, 0f);
                if (m_networkPathAuthoritative) {
                    UpdateNetworkPath();
                }
                else {
                    m_stateMachine.Update();
                }
            }
        }

        /// <summary>
        /// Applies the path calculated by the server.  Once this is called,
        /// this component no longer queues local A* searches; it only follows
        /// the server's path and pilot destination snapshots.
        /// </summary>
        public virtual void ApplyNetworkPath(int pathVersion, int pathCursor, List<Vector3> path) {
            if (m_networkPathAuthoritative
                && m_appliedNetworkPathVersion == pathVersion
                && m_appliedNetworkPathCursor == pathCursor
                && IsNetworkPathEqual(path)) {
                return;
            }

            m_networkPathAuthoritative = true;
            m_appliedNetworkPathVersion = pathVersion;
            m_appliedNetworkPathCursor = pathCursor;
            m_pathfindingResult.Path.Clear();
            for (int i = 0; i < path.Count; i++) {
                m_pathfindingResult.Path.Add(path[i]);
            }
            m_pathfindingResult.IsInProgress = false;
            m_pathfindingResult.IsCompleted = true;
            m_destinationChanged = false;
            IsStuck = false;
        }

        private void UpdateNetworkPath() {
            if (m_componentPilot == null || !Destination.HasValue) {
                m_componentPilot?.Stop();
                return;
            }

            // The pilot destination is also authoritative.  It represents
            // either the currently followed waypoint or the server's direct
            // movement fallback.
            if (m_componentPilot.Destination.HasValue) {
                if (m_componentPilot.IsStuck) {
                    m_componentPilot.Stop();
                }
                return;
            }

            if (m_pathfindingResult.Path.Count > 0) {
                Vector3 value = m_pathfindingResult.Path.Array[m_pathfindingResult.Path.Count - 1];
                m_pathfindingResult.Path.RemoveAt(m_pathfindingResult.Path.Count - 1);
                m_componentPilot.SetDestination(
                    value,
                    MathUtils.Min(Speed, 0.75f),
                    0.75f,
                    false,
                    false,
                    Speed >= 1f,
                    DoNotAvoidBody
                );
            }
            else {
                // Do not locally invent a direct destination while the
                // server is searching or has decided that the creature is
                // stuck.  The next server snapshot will provide the next
                // waypoint or direct pilot target.
                m_componentPilot.Stop();
            }
        }

        private bool HasNetworkDestinationChanged(Vector3? destination) {
            if (Destination.HasValue != destination.HasValue) {
                return true;
            }
            return Destination.HasValue
                && destination.HasValue
                && (Destination.Value - destination.Value).LengthSquared() > 0.0001f;
        }

        private bool IsNetworkPathEqual(List<Vector3> path) {
            if (m_pathfindingResult.Path.Count != path.Count) {
                return false;
            }
            for (int i = 0; i < path.Count; i++) {
                if ((m_pathfindingResult.Path.Array[i] - path[i]).LengthSquared() > 0.0001f) {
                    return false;
                }
            }
            return true;
        }

        public override void Load(ValuesDictionary valuesDictionary, IdToEntityMap idToEntityMap) {
            m_subsystemTime = Project.FindSubsystem<SubsystemTime>(true);
            m_subsystemPathfinding = Project.FindSubsystem<SubsystemPathfinding>(true);
            m_componentCreature = Entity.FindComponent<ComponentCreature>(true);
            m_componentPilot = Entity.FindComponent<ComponentPilot>(true);
            m_stateMachine.AddState(
                "Stopped",
                delegate {
                    Stop();
                    m_randomMoveCount = 0;
                },
                delegate {
                    if (Destination.HasValue) {
                        m_stateMachine.TransitionTo("MovingDirect");
                    }
                },
                null
            );
            m_stateMachine.AddState(
                "MovingDirect",
                delegate {
                    IsStuck = false;
                    m_destinationChanged = true;
                },
                delegate {
                    if (!Destination.HasValue) {
                        m_stateMachine.TransitionTo("Stopped");
                    }
                    else if (m_destinationChanged) {
                        m_componentPilot.SetDestination(
                            Destination,
                            Speed,
                            Range,
                            IgnoreHeightDifference,
                            RaycastDestination,
                            Speed >= 1f,
                            DoNotAvoidBody
                        );
                        m_destinationChanged = false;
                    }
                    else if (!m_componentPilot.Destination.HasValue) {
                        m_stateMachine.TransitionTo("Stopped");
                    }
                    else if (m_componentPilot.IsStuck) {
                        if (MaxPathfindingPositions > 0
                            && m_componentCreature.ComponentLocomotion.WalkSpeed > 0f) {
                            m_stateMachine.TransitionTo("SearchingForPath");
                        }
                        else if (UseRandomMovements) {
                            m_stateMachine.TransitionTo("MovingRandomly");
                        }
                        else {
                            m_stateMachine.TransitionTo("Stuck");
                        }
                    }
                },
                null
            );
            m_stateMachine.AddState(
                "SearchingForPath",
                delegate {
                    m_pathfindingResult.IsCompleted = false;
                    m_pathfindingResult.IsInProgress = false;
                },
                delegate {
                    if (!Destination.HasValue) {
                        m_stateMachine.TransitionTo("Stopped");
                    }
                    else if (!m_pathfindingResult.IsInProgress
                        && (!m_lastPathfindingTime.HasValue || m_subsystemTime.GameTime - m_lastPathfindingTime > 8.0)
                        && m_pathfindingCongestion < 500f) {
                        m_lastPathfindingDestination = Destination.Value;
                        m_lastPathfindingTime = m_subsystemTime.GameTime;
                        Vector3 start = m_componentCreature.ComponentBody.Position + new Vector3(0f, 0.01f, 0f);
                        Vector3 end = Destination.Value + new Vector3(0f, 0.01f, 0f);
                        ComponentMiner componentMiner = Entity.FindComponent<ComponentMiner>();
                        bool ignoreDoors = componentMiner != null && componentMiner.AutoInteractRate > 0f && m_random.Bool(0.5f);
                        m_subsystemPathfinding.QueuePathSearch(
                            start,
                            end,
                            1f,
                            m_componentCreature.ComponentBody.BoxSize,
                            ignoreDoors,
                            MaxPathfindingPositions,
                            m_pathfindingResult
                        );
                    }
                    else if (UseRandomMovements) {
                        m_stateMachine.TransitionTo("MovingRandomly");
                    }
                    if (m_pathfindingResult.IsCompleted) {
                        m_pathfindingCongestion = MathUtils.Min(m_pathfindingCongestion + m_pathfindingResult.PositionsChecked, 1000f);
                        NetworkPathVersion++;
                        NetworkPathCursor = 0;
                        if (m_pathfindingResult.Path.Count > 0) {
                            m_stateMachine.TransitionTo("MovingWithPath");
                        }
                        else if (UseRandomMovements) {
                            m_stateMachine.TransitionTo("MovingRandomly");
                        }
                        else {
                            m_stateMachine.TransitionTo("Stuck");
                        }
                    }
                },
                null
            );
            m_stateMachine.AddState(
                "MovingWithPath",
                delegate {
                    m_componentPilot.Stop();
                    m_randomMoveCount = 0;
                },
                delegate {
                    if (!Destination.HasValue) {
                        m_stateMachine.TransitionTo("Stopped");
                    }
                    else if (!m_componentPilot.Destination.HasValue) {
                        if (m_pathfindingResult.Path.Count > 0) {
                            Vector3 value = m_pathfindingResult.Path.Array[m_pathfindingResult.Path.Count - 1];
                            m_componentPilot.SetDestination(
                                value,
                                MathUtils.Min(Speed, 0.75f),
                                0.75f,
                                false,
                                false,
                                Speed >= 1f,
                                DoNotAvoidBody
                            );
                            m_pathfindingResult.Path.RemoveAt(m_pathfindingResult.Path.Count - 1);
                            NetworkPathVersion++;
                            NetworkPathCursor++;
                        }
                        else {
                            m_stateMachine.TransitionTo("MovingDirect");
                        }
                    }
                    else if (m_componentPilot.IsStuck) {
                        if (UseRandomMovements) {
                            m_stateMachine.TransitionTo("MovingRandomly");
                        }
                        else {
                            m_stateMachine.TransitionTo("Stuck");
                        }
                    }
                    else {
                        float num = Vector3.DistanceSquared(m_componentCreature.ComponentBody.Position, Destination.Value);
                        if (Vector3.DistanceSquared(m_lastPathfindingDestination.Value, Destination.Value) > num) {
                            m_stateMachine.TransitionTo("MovingDirect");
                        }
                    }
                },
                null
            );
            m_stateMachine.AddState(
                "MovingRandomly",
                delegate {
                    m_componentPilot.SetDestination(
                        m_componentCreature.ComponentBody.Position + new Vector3(5f * m_random.Float(-1f, 1f), 0f, 5f * m_random.Float(-1f, 1f)),
                        1f,
                        1f,
                        true,
                        false,
                        false,
                        DoNotAvoidBody
                    );
                    m_randomMoveCount++;
                },
                delegate {
                    if (!Destination.HasValue) {
                        m_stateMachine.TransitionTo("Stopped");
                    }
                    else if (m_randomMoveCount > 3) {
                        m_stateMachine.TransitionTo("Stuck");
                    }
                    else if (m_componentPilot.IsStuck
                        || !m_componentPilot.Destination.HasValue) {
                        m_stateMachine.TransitionTo("MovingDirect");
                    }
                },
                null
            );
            m_stateMachine.AddState(
                "Stuck",
                delegate { IsStuck = true; },
                delegate {
                    if (!Destination.HasValue) {
                        m_stateMachine.TransitionTo("Stopped");
                    }
                    else if (m_destinationChanged) {
                        m_destinationChanged = false;
                        m_stateMachine.TransitionTo("MovingDirect");
                    }
                },
                null
            );
            m_stateMachine.TransitionTo("Stopped");
        }
    }
}
