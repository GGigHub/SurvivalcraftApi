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
    public ChangeFlag changeFlag;
    public int m_ladderValue;
    public bool m_sneaking;

    public PlayerBodyUpdatePacket() { }

    public PlayerBodyUpdatePacket(ComponentPlayer player) {
        PlayerData playerData = player.PlayerData;
        changeFlag = ChangeFlag.None;
        ComponentBody body = player.ComponentBody.ParentBody;
        if (body != null) {
            changeFlag |= ChangeFlag.ParentBodyChange;
            if (body.Locomotion != null && body.Locomotion.SendLookAngles.HasValue) {
                changeFlag |= ChangeFlag.LookAnglesChange;
                LookAngles = body.Locomotion.SendLookAngles.Value;
                body.Locomotion.SendLookAngles = null;
            }
            if (player.ComponentLocomotion.SendLookAngles.HasValue) {
                changeFlag |= ChangeFlag.ChildLookAnglesChange;
                ChildLookAngles = player.ComponentLocomotion.SendLookAngles.Value;
                player.ComponentLocomotion.SendLookAngles = null;
            }
        }
        else {
            body = player.ComponentBody;
            if (player.ComponentLocomotion.SendLookAngles.HasValue) {
                changeFlag |= ChangeFlag.LookAnglesChange;
                LookAngles = player.ComponentLocomotion.SendLookAngles.Value;
                player.ComponentLocomotion.SendLookAngles = null;
            }
            if (player.ComponentLocomotion.LadderValue.HasValue) {
                changeFlag |= ChangeFlag.LadderChange;
                m_ladderValue = player.ComponentLocomotion.LadderValue.Value;
            }
            if (body.CanCrouch) {
                changeFlag |= ChangeFlag.SneakChange;
                m_sneaking = body.IsCrouching;
            }
        }
        if (body.SendPosition.HasValue) {
            Position = body.SendPosition.Value;
            changeFlag |= ChangeFlag.PositionChange;
            body.SendPosition = null;
        }
        if (body.SendRotation.HasValue) {
            Rotation = body.SendRotation.Value;
            changeFlag |= ChangeFlag.RotationChange;
            body.SendRotation = null;
        }
        if (body.SendVelocity.HasValue) {
            Velocity = body.SendVelocity.Value;
            changeFlag |= ChangeFlag.VelocityChange;
            body.SendVelocity = null;
        }
        m_fromPlayerId = playerData?.ClientId ?? 0;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerId) : null);
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
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) {
            return;
        }
        ComponentPlayer player = playerData.ComponentPlayer;
        ComponentBody body;
        if (changeFlag.HasFlag(ChangeFlag.ParentBodyChange) && player.ComponentBody.ParentBody != null) {
            body = player.ComponentBody.ParentBody;
            if (changeFlag.HasFlag(ChangeFlag.LookAnglesChange)) {
                body.Locomotion?.NetLookAngles.SetNext(LookAngles);
            }
            if (changeFlag.HasFlag(ChangeFlag.ChildLookAnglesChange)) {
                player.ComponentBody.Locomotion?.NetLookAngles.SetNext(ChildLookAngles);
            }
        }
        else {
            body = player.ComponentBody;
            if (changeFlag.HasFlag(ChangeFlag.LookAnglesChange)) {
                player.ComponentLocomotion.NetLookAngles.SetNext(LookAngles);
            }
        }
        if (changeFlag.HasFlag(ChangeFlag.VelocityChange)) {
            body.netVelocity.SetNext(Velocity);
        }
        if (body.Locomotion != null) {
            body.Locomotion.LadderValue = changeFlag.HasFlag(ChangeFlag.LadderChange) ? m_ladderValue : null;
        }
        if (changeFlag.HasFlag(ChangeFlag.PositionChange)) {
            body.netPosition.SetNext(Position);
        }
        if (changeFlag.HasFlag(ChangeFlag.RotationChange)) {
            body.netRotation.SetNext(Rotation);
        }
        if (changeFlag.HasFlag(ChangeFlag.SneakChange)) {
            body.IsSneaking = m_sneaking;
        }
    }
}

