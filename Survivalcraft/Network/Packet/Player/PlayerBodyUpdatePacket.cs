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
    public PlayerHelpers.ChangeFlag changeFlag;
    public int m_ladderValue;
    public bool m_sneaking;

    public PlayerBodyUpdatePacket() { }

    public PlayerBodyUpdatePacket(ComponentPlayer player) {
        PlayerData playerData = player.PlayerData;
        changeFlag = PlayerHelpers.ChangeFlag.None;
        ComponentBody body = player.ComponentBody.ParentBody;
        if (body != null) {
            changeFlag |= PlayerHelpers.ChangeFlag.ParentBodyChange;
            if (body.Locomotion != null && body.Locomotion.SendLookAngles.HasValue) {
                changeFlag |= PlayerHelpers.ChangeFlag.LookAnglesChange;
                LookAngles = body.Locomotion.SendLookAngles.Value;
                body.Locomotion.SendLookAngles = null;
            }
            if (player.ComponentLocomotion.SendLookAngles.HasValue) {
                changeFlag |= PlayerHelpers.ChangeFlag.ChildLookAnglesChange;
                ChildLookAngles = player.ComponentLocomotion.SendLookAngles.Value;
                player.ComponentLocomotion.SendLookAngles = null;
            }
        }
        else {
            body = player.ComponentBody;
            if (player.ComponentLocomotion.SendLookAngles.HasValue) {
                changeFlag |= PlayerHelpers.ChangeFlag.LookAnglesChange;
                LookAngles = player.ComponentLocomotion.SendLookAngles.Value;
                player.ComponentLocomotion.SendLookAngles = null;
            }
            if (player.ComponentLocomotion.LadderValue.HasValue) {
                changeFlag |= PlayerHelpers.ChangeFlag.LadderChange;
                m_ladderValue = player.ComponentLocomotion.LadderValue.Value;
            }
            if (body.CanCrouch) {
                changeFlag |= PlayerHelpers.ChangeFlag.SneakChange;
                m_sneaking = body.IsCrouching;
            }
        }
        if (body.SendPosition.HasValue) {
            Position = body.SendPosition.Value;
            changeFlag |= PlayerHelpers.ChangeFlag.PositionChange;
            body.SendPosition = null;
        }
        if (body.SendRotation.HasValue) {
            Rotation = body.SendRotation.Value;
            changeFlag |= PlayerHelpers.ChangeFlag.RotationChange;
            body.SendRotation = null;
        }
        if (body.SendVelocity.HasValue) {
            Velocity = body.SendVelocity.Value;
            changeFlag |= PlayerHelpers.ChangeFlag.VelocityChange;
            body.SendVelocity = null;
        }
        m_fromPlayerId = playerData?.ClientId ?? 0;
    }

    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Enum(ref changeFlag);
        if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.LookAnglesChange)) archive.Value(ref LookAngles);
        if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.ChildLookAnglesChange)) archive.Value(ref ChildLookAngles);
        if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.PositionChange)) archive.Value(ref Position);
        if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.RotationChange)) archive.Value(ref Rotation);
        if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.VelocityChange)) archive.Value(ref Velocity);
        if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.LadderChange)) archive.Value(ref m_ladderValue);
        if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.SneakChange)) archive.Value(ref m_sneaking);
    }

    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) {
            return;
        }
        ComponentPlayer player = playerData.ComponentPlayer;
        ComponentBody body;
        if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.ParentBodyChange) && player.ComponentBody.ParentBody != null) {
            body = player.ComponentBody.ParentBody;
            if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.LookAnglesChange)) {
                body.Locomotion?.NetLookAngles.SetNext(LookAngles);
            }
            if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.ChildLookAnglesChange)) {
                player.ComponentBody.Locomotion?.NetLookAngles.SetNext(ChildLookAngles);
            }
        }
        else {
            body = player.ComponentBody;
            if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.LookAnglesChange)) {
                player.ComponentLocomotion.NetLookAngles.SetNext(LookAngles);
            }
        }
        if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.VelocityChange)) {
            body.netVelocity.SetNext(Velocity);
        }
        if (body.Locomotion != null) {
            body.Locomotion.LadderValue = changeFlag.HasFlag(PlayerHelpers.ChangeFlag.LadderChange) ? m_ladderValue : null;
        }
        if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.PositionChange)) {
            body.netPosition.SetNext(Position);
        }
        if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.RotationChange)) {
            body.netRotation.SetNext(Rotation);
        }
        if (changeFlag.HasFlag(PlayerHelpers.ChangeFlag.SneakChange)) {
            body.IsSneaking = m_sneaking;
        }
    }
}

