using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerBodyUpdatePacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerBodyUpdate;
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

    public static readonly Dictionary<int, PlayerBodySyncSnapshot> s_playerBodySyncSnapshots = [];

    public Vector3 Position;
    public Vector3 Velocity;
    public Quaternion Rotation;
    public Vector2 LookAngles;
    public Vector2 ChildLookAngles;
    public ChangeFlag changeFlag;
    public int m_ladderValue;
    public bool m_sneaking;

    public PlayerBodyUpdatePacket() { }

    public PlayerBodyUpdatePacket(ComponentPlayer player) {
        PlayerData playerData = player.PlayerData;
        changeFlag = ChangeFlag.None;
        ComponentBody body = player.ComponentBody.ParentBody;
        s_playerBodySyncSnapshots.TryGetValue(playerData.PlayerIndex, out PlayerBodySyncSnapshot snapshot);
        bool hasParentBody = body != null;
        if (body != null) {
            changeFlag |= ChangeFlag.ParentBodyChange;
            Vector2 lookAngles = body.Locomotion?.LookAngles ?? Vector2.Zero;
            if (!snapshot.Initialized || !snapshot.HasParentBody || BodyUpdatePacket.HasVector2Changed(lookAngles, snapshot.LookAngles, BodyUpdatePacket.LookAnglesEpsilonSquared)) {
                changeFlag |= ChangeFlag.LookAnglesChange;
                LookAngles = lookAngles;
            }
            if (!snapshot.Initialized || !snapshot.HasParentBody || BodyUpdatePacket.HasVector2Changed(player.ComponentLocomotion.LookAngles, snapshot.ChildLookAngles, BodyUpdatePacket.LookAnglesEpsilonSquared)) {
                changeFlag |= ChangeFlag.ChildLookAnglesChange;
                ChildLookAngles = player.ComponentLocomotion.LookAngles;
            }
        }
        else {
            body = player.ComponentBody;
            if (!snapshot.Initialized || snapshot.HasParentBody || BodyUpdatePacket.HasVector2Changed(player.ComponentLocomotion.LookAngles, snapshot.LookAngles, BodyUpdatePacket.LookAnglesEpsilonSquared)) {
                changeFlag |= ChangeFlag.LookAnglesChange;
                LookAngles = player.ComponentLocomotion.LookAngles;
            }
            if (player.ComponentLocomotion.LadderValue.HasValue) {
                changeFlag |= ChangeFlag.LadderChange;
                m_ladderValue = player.ComponentLocomotion.LadderValue.Value;
            }
            if (body.CanCrouch && (!snapshot.Initialized || body.IsCrouching != snapshot.IsCrouching)) {
                changeFlag |= ChangeFlag.SneakChange;
                m_sneaking = body.IsCrouching;
            }
        }
        if (!snapshot.Initialized || BodyUpdatePacket.HasVector3Changed(body.Position, snapshot.Position, BodyUpdatePacket.PositionEpsilonSquared)) {
            Position = body.Position;
            changeFlag |= ChangeFlag.PositionChange;
        }
        if (!snapshot.Initialized || BodyUpdatePacket.HasRotationChanged(body.Rotation, snapshot.Rotation)) {
            Rotation = body.Rotation;
            changeFlag |= ChangeFlag.RotationChange;
        }
        if (!snapshot.Initialized || BodyUpdatePacket.HasVector3Changed(body.Velocity, snapshot.Velocity, BodyUpdatePacket.VelocityEpsilonSquared)) {
            Velocity = body.Velocity;
            changeFlag |= ChangeFlag.VelocityChange;
        }
        s_playerBodySyncSnapshots[playerData.PlayerIndex] = CreateSnapshot(player, body, hasParentBody);
        m_fromPlayerIndex = playerData.PlayerIndex;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        archive.Enum(ref changeFlag);
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
        bool smoothBody = !isServer && !playerData.IsMainPlayer;
        NetworkBodySmoothing.SetTarget(
            body,
            changeFlag.HasFlag(ChangeFlag.PositionChange),
            Position,
            changeFlag.HasFlag(ChangeFlag.RotationChange),
            Rotation,
            changeFlag.HasFlag(ChangeFlag.VelocityChange),
            Velocity,
            smoothBody);
        if (body.Locomotion != null) {
            body.Locomotion.LadderValue = changeFlag.HasFlag(ChangeFlag.LadderChange) ? m_ladderValue : null;
        }
        if (changeFlag.HasFlag(ChangeFlag.SneakChange)) {
            body.IsSneaking = m_sneaking;
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
}


