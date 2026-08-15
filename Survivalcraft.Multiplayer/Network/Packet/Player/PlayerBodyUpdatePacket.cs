using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerBodyUpdatePacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerBodyUpdate;
    public override NetworkState MinNeedState => NetworkState.Playing;
    public struct PlayerBodySyncSnapshot {
        public bool Initialized;
        public bool HasParentBody;
        public Vector3 Position;
        public Vector3 Velocity;
        public Quaternion Rotation;
        public Vector2 LookAngles;
        public Vector2 ChildLookAngles;
        public int? LadderValue;
        public bool IsCrouching;
    }

    public static readonly Dictionary<SnapshotKey, PlayerBodySyncSnapshot> s_playerBodySyncSnapshots = [];

    public Vector3 Position;
    public Vector3 Velocity;
    public Quaternion Rotation;
    public Vector2 LookAngles;
    public Vector2 ChildLookAngles;
    public ChangeFlag changeFlag;
    public int m_ladderValue;
    public bool m_sneaking;
    public double SendTime;

    public PlayerBodyUpdatePacket() { }

    public readonly record struct SnapshotKey(ClientSession Session, int PlayerIndex);

    public PlayerBodyUpdatePacket(ComponentPlayer player, ClientSession targetSession = null, bool forceFull = false) {
        SendTime = Engine.Time.RealTime;
        PlayerData playerData = player.PlayerData;
        changeFlag = ChangeFlag.None;
        ComponentBody body = player.ComponentBody.ParentBody;
        SnapshotKey snapshotKey = new(targetSession, playerData.PlayerIndex);
        s_playerBodySyncSnapshots.TryGetValue(snapshotKey, out PlayerBodySyncSnapshot snapshot);
        bool hasParentBody = body != null;
        if (body != null) {
            changeFlag |= ChangeFlag.ParentBodyChange;
            Vector2 lookAngles = body.Locomotion?.LookAngles ?? Vector2.Zero;
            if (forceFull || !snapshot.Initialized || !snapshot.HasParentBody || NetworkBodySyncHelpers.HasVector2Changed(lookAngles, snapshot.LookAngles, NetworkBodySyncHelpers.LookAnglesEpsilonSquared)) {
                changeFlag |= ChangeFlag.LookAnglesChange;
                LookAngles = lookAngles;
            }
            if (forceFull || !snapshot.Initialized || !snapshot.HasParentBody || NetworkBodySyncHelpers.HasVector2Changed(player.ComponentLocomotion.LookAngles, snapshot.ChildLookAngles, NetworkBodySyncHelpers.LookAnglesEpsilonSquared)) {
                changeFlag |= ChangeFlag.ChildLookAnglesChange;
                ChildLookAngles = player.ComponentLocomotion.LookAngles;
            }
        }
        else {
            body = player.ComponentBody;
            if (forceFull || !snapshot.Initialized || snapshot.HasParentBody || NetworkBodySyncHelpers.HasVector2Changed(player.ComponentLocomotion.LookAngles, snapshot.LookAngles, NetworkBodySyncHelpers.LookAnglesEpsilonSquared)) {
                changeFlag |= ChangeFlag.LookAnglesChange;
                LookAngles = player.ComponentLocomotion.LookAngles;
            }
            if (player.ComponentLocomotion.LadderValue.HasValue) {
                changeFlag |= ChangeFlag.LadderChange;
                m_ladderValue = player.ComponentLocomotion.LadderValue.Value;
            }
            if (body.CanCrouch && (forceFull || !snapshot.Initialized || body.IsCrouching != snapshot.IsCrouching)) {
                changeFlag |= ChangeFlag.SneakChange;
                m_sneaking = body.IsCrouching;
            }
        }
        if (forceFull || !snapshot.Initialized || NetworkBodySyncHelpers.HasVector3Changed(body.Position, snapshot.Position, NetworkBodySyncHelpers.PositionEpsilonSquared)) {
            Position = body.Position;
            changeFlag |= ChangeFlag.PositionChange;
        }
        if (forceFull || !snapshot.Initialized || NetworkBodySyncHelpers.HasRotationChanged(body.Rotation, snapshot.Rotation)) {
            Rotation = body.Rotation;
            changeFlag |= ChangeFlag.RotationChange;
        }
        if (forceFull || !snapshot.Initialized || NetworkBodySyncHelpers.HasVector3Changed(body.Velocity, snapshot.Velocity, NetworkBodySyncHelpers.VelocityEpsilonSquared)) {
            Velocity = body.Velocity;
            changeFlag |= ChangeFlag.VelocityChange;
        }
        s_playerBodySyncSnapshots[snapshotKey] = CreateSnapshot(player, body, hasParentBody);
        m_fromPlayerIndex = playerData.PlayerIndex;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        archive.Enum(ref changeFlag);
        archive.Value(ref SendTime);
        if (changeFlag.HasFlag(ChangeFlag.LookAnglesChange)) archive.Value(ref LookAngles);
        if (changeFlag.HasFlag(ChangeFlag.ChildLookAnglesChange)) archive.Value(ref ChildLookAngles);
        if (changeFlag.HasFlag(ChangeFlag.PositionChange)) archive.Value(ref Position);
        if (changeFlag.HasFlag(ChangeFlag.RotationChange)) archive.Value(ref Rotation);
        if (changeFlag.HasFlag(ChangeFlag.VelocityChange)) archive.Value(ref Velocity);
        if (changeFlag.HasFlag(ChangeFlag.LadderChange)) archive.Value(ref m_ladderValue);
        if (changeFlag.HasFlag(ChangeFlag.SneakChange)) archive.Value(ref m_sneaking);
    }

    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData) || playerData.ComponentPlayer == null) {
            return;
        }
        ComponentPlayer player = playerData.ComponentPlayer;
        ComponentBody body;
        if (changeFlag.HasFlag(ChangeFlag.ParentBodyChange) && player.ComponentBody.ParentBody != null) {
            body = player.ComponentBody.ParentBody;
            if (changeFlag.HasFlag(ChangeFlag.LookAnglesChange)) {
                if (body.Locomotion != null) {
                    body.Locomotion.LookAngles = LookAngles;
                }
            }
            if (changeFlag.HasFlag(ChangeFlag.ChildLookAnglesChange)) {
                if (player.ComponentBody.Locomotion != null) {
                    player.ComponentBody.Locomotion.LookAngles = ChildLookAngles;
                }
            }
        }
        else {
            body = player.ComponentBody;
            if (changeFlag.HasFlag(ChangeFlag.LookAnglesChange)) {
                player.ComponentLocomotion.LookAngles = LookAngles;
            }
        }
        bool snapStoppedRemoteBody = !isServer
            && !playerData.IsMainPlayer
            && changeFlag.HasFlag(ChangeFlag.PositionChange)
            && changeFlag.HasFlag(ChangeFlag.VelocityChange)
            && Velocity.LengthSquared() <= NetworkBodySyncHelpers.VelocityEpsilonSquared;
        bool interpolateBody = !playerData.IsMainPlayer && !snapStoppedRemoteBody;
        if (snapStoppedRemoteBody) {
            NetworkBodyInterpolation.RemoveTarget(body);
        }
        if (interpolateBody) {
            bool positionChanged = changeFlag.HasFlag(ChangeFlag.PositionChange);
            bool rotationChanged = changeFlag.HasFlag(ChangeFlag.RotationChange);
            bool velocityChanged = changeFlag.HasFlag(ChangeFlag.VelocityChange);
            if (isServer) {
                NetworkBodyInterpolation.PushServerSnapshot(
                    body,
                    positionChanged,
                    Position,
                    rotationChanged,
                    Rotation,
                    velocityChanged,
                    Velocity);
            }
            else {
                NetworkBodyInterpolation.PushSnapshot(
                    body,
                    positionChanged,
                    Position,
                    rotationChanged,
                    Rotation,
                    velocityChanged,
                    Velocity,
                    SendTime,
                    NetworkBodyInterpolation.RemoteBodyDelay);
            }
        }
        else if (changeFlag.HasFlag(ChangeFlag.VelocityChange)) {
            body.Velocity = Velocity;
        }
        if (body.Locomotion != null) {
            body.Locomotion.LadderValue = changeFlag.HasFlag(ChangeFlag.LadderChange) ? m_ladderValue : null;
        }
        if (!interpolateBody && changeFlag.HasFlag(ChangeFlag.PositionChange)) {
            body.Position = Position;
        }
        if (!interpolateBody && changeFlag.HasFlag(ChangeFlag.RotationChange)) {
            body.Rotation = Rotation;
        }
        if (changeFlag.HasFlag(ChangeFlag.SneakChange)) {
            float crouchFactor = m_sneaking ? 1f : 0f;
            body.TargetCrouchFactor = crouchFactor;
        }
        if (isServer && HasRelayableChange()) {
            QueueBodyUpdateToObservers(player);
        }
    }

    private bool HasRelayableChange() {
        const ChangeFlag relayableChanges =
            ChangeFlag.LookAnglesChange
            | ChangeFlag.ChildLookAnglesChange
            | ChangeFlag.PositionChange
            | ChangeFlag.RotationChange
            | ChangeFlag.VelocityChange
            | ChangeFlag.LadderChange
            | ChangeFlag.SneakChange;
        return (changeFlag & relayableChanges) != ChangeFlag.None;
    }

    private static void QueueBodyUpdateToObservers(ComponentPlayer player) {
        foreach (ClientSession session in NetworkManager.ServerSessions) {
            if (session == null
                || session.NetworkState < NetworkState.Playing
                || session.PlayerIndex == player.PlayerData.PlayerIndex) {
                continue;
            }
            NetworkManager.Queue(new PlayerBodyUpdatePacket(player, session, forceFull: true) { To = session });
        }
    }

    public static PlayerBodySyncSnapshot CreateSnapshot(ComponentPlayer player, ComponentBody body, bool hasParentBody) {
        return new PlayerBodySyncSnapshot {
            Initialized = true,
            HasParentBody = hasParentBody,
            Position = body.Position,
            Rotation = body.Rotation,
            Velocity = body.Velocity,
            LookAngles = hasParentBody ? body.Locomotion?.LookAngles ?? Vector2.Zero : player.ComponentLocomotion.LookAngles,
            ChildLookAngles = player.ComponentLocomotion.LookAngles,
            LadderValue = hasParentBody ? null : player.ComponentLocomotion.LadderValue,
            IsCrouching = body.IsCrouching
        };
    }

    public static void RemoveSnapshotsForSession(ClientSession session) {
        if (session == null) {
            return;
        }

        foreach (SnapshotKey key in s_playerBodySyncSnapshots.Keys.ToArray()) {
            if (key.Session == session) {
                s_playerBodySyncSnapshots.Remove(key);
            }
        }
    }

    public static void ClearSnapshots() {
        s_playerBodySyncSnapshots.Clear();
    }
}


