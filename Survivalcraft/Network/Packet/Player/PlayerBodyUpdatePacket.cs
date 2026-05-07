using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerBodyUpdatePacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerBodyUpdate;
    public Vector3 Position;
    public Vector3 Velocity;
    public Quaternion Rotation;
    public Vector2 LookAngles;
    public Vector2 ChildLookAngles;
    public PlayerPacketHelpers.ChangeFlag changeFlag;
    public int m_ladderValue;
    public bool m_sneaking;

    public PlayerBodyUpdatePacket() { }

    public PlayerBodyUpdatePacket(ComponentPlayer player) {
        PlayerData playerData = player.PlayerData;
        changeFlag = PlayerPacketHelpers.ChangeFlag.None;
        ComponentBody body = player.ComponentBody.ParentBody;
        if (body != null) {
            changeFlag |= PlayerPacketHelpers.ChangeFlag.ParentBodyChange;
            if (body.Locomotion != null && body.Locomotion.SendLookAngles.HasValue) {
                changeFlag |= PlayerPacketHelpers.ChangeFlag.LookAnglesChange;
                LookAngles = body.Locomotion.SendLookAngles.Value;
                body.Locomotion.SendLookAngles = null;
            }
            if (player.ComponentLocomotion.SendLookAngles.HasValue) {
                changeFlag |= PlayerPacketHelpers.ChangeFlag.ChildLookAnglesChange;
                ChildLookAngles = player.ComponentLocomotion.SendLookAngles.Value;
                player.ComponentLocomotion.SendLookAngles = null;
            }
        }
        else {
            body = player.ComponentBody;
            if (player.ComponentLocomotion.SendLookAngles.HasValue) {
                changeFlag |= PlayerPacketHelpers.ChangeFlag.LookAnglesChange;
                LookAngles = player.ComponentLocomotion.SendLookAngles.Value;
                player.ComponentLocomotion.SendLookAngles = null;
            }
            if (player.ComponentLocomotion.LadderValue.HasValue) {
                changeFlag |= PlayerPacketHelpers.ChangeFlag.LadderChange;
                m_ladderValue = player.ComponentLocomotion.LadderValue.Value;
            }
            if (body.CanCrouch) {
                changeFlag |= PlayerPacketHelpers.ChangeFlag.SneakChange;
                m_sneaking = body.IsCrouching;
            }
        }
        if (body.SendPosition.HasValue) {
            Position = body.SendPosition.Value;
            changeFlag |= PlayerPacketHelpers.ChangeFlag.PositionChange;
            body.SendPosition = null;
        }
        if (body.SendRotation.HasValue) {
            Rotation = body.SendRotation.Value;
            changeFlag |= PlayerPacketHelpers.ChangeFlag.RotationChange;
            body.SendRotation = null;
        }
        if (body.SendVelocity.HasValue) {
            Velocity = body.SendVelocity.Value;
            changeFlag |= PlayerPacketHelpers.ChangeFlag.VelocityChange;
            body.SendVelocity = null;
        }
        m_fromPlayerId = playerData?.ClientId ?? 0;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPacketHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Enum(ref changeFlag);
        if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.LookAnglesChange)) archive.Value(ref LookAngles);
        if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.ChildLookAnglesChange)) archive.Value(ref ChildLookAngles);
        if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.PositionChange)) archive.Value(ref Position);
        if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.RotationChange)) archive.Value(ref Rotation);
        if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.VelocityChange)) archive.Value(ref Velocity);
        if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.LadderChange)) archive.Value(ref m_ladderValue);
        if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.SneakChange)) archive.Value(ref m_sneaking);
    }

    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) {
            return;
        }
        ComponentPlayer player = playerData.ComponentPlayer;
        ComponentBody body;
        if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.ParentBodyChange) && player.ComponentBody.ParentBody != null) {
            body = player.ComponentBody.ParentBody;
            if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.LookAnglesChange)) {
                body.Locomotion?.NetLookAngles.SetNext(LookAngles);
            }
            if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.ChildLookAnglesChange)) {
                player.ComponentBody.Locomotion?.NetLookAngles.SetNext(ChildLookAngles);
            }
        }
        else {
            body = player.ComponentBody;
            if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.LookAnglesChange)) {
                player.ComponentLocomotion.NetLookAngles.SetNext(LookAngles);
            }
        }
        if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.VelocityChange)) {
            body.netVelocity.SetNext(Velocity);
        }
        if (body.Locomotion != null) {
            body.Locomotion.LadderValue = changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.LadderChange) ? m_ladderValue : null;
        }
        if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.PositionChange)) {
            body.netPosition.SetNext(Position);
        }
        if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.RotationChange)) {
            body.netRotation.SetNext(Rotation);
        }
        if (changeFlag.HasFlag(PlayerPacketHelpers.ChangeFlag.SneakChange)) {
            body.IsSneaking = m_sneaking;
        }
    }
}

