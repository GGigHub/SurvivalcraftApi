using Engine;
using TemplatesDatabase;

namespace Game.NetWork;

public class PlayerBodyUpdatePackage : PlayerPackageBase {
    public override byte ID => (byte)PackageType.PlayerBodyUpdate;
    public Vector3 Position;
    public Vector3 Velocity;
    public Quaternion Rotation;
    public Vector2 LookAngles;
    public Vector2 ChildLookAngles;
    public PlayerPackageHelpers.ChangeFlag changeFlag;
    public int m_ladderValue;
    public bool m_sneaking;

    public PlayerBodyUpdatePackage() { }

    public PlayerBodyUpdatePackage(ComponentPlayer player) {
        PlayerData playerData = player.PlayerData;
        changeFlag = PlayerPackageHelpers.ChangeFlag.None;
        ComponentBody body = player.ComponentBody.ParentBody;
        if (body != null) {
            changeFlag |= PlayerPackageHelpers.ChangeFlag.ParentBodyChange;
            if (body.Locomotion != null && body.Locomotion.SendLookAngles.HasValue) {
                changeFlag |= PlayerPackageHelpers.ChangeFlag.LookAnglesChange;
                LookAngles = body.Locomotion.SendLookAngles.Value;
                body.Locomotion.SendLookAngles = null;
            }
            if (player.ComponentLocomotion.SendLookAngles.HasValue) {
                changeFlag |= PlayerPackageHelpers.ChangeFlag.ChildLookAnglesChange;
                ChildLookAngles = player.ComponentLocomotion.SendLookAngles.Value;
                player.ComponentLocomotion.SendLookAngles = null;
            }
        }
        else {
            body = player.ComponentBody;
            if (player.ComponentLocomotion.SendLookAngles.HasValue) {
                changeFlag |= PlayerPackageHelpers.ChangeFlag.LookAnglesChange;
                LookAngles = player.ComponentLocomotion.SendLookAngles.Value;
                player.ComponentLocomotion.SendLookAngles = null;
            }
            if (player.ComponentLocomotion.LadderValue.HasValue) {
                changeFlag |= PlayerPackageHelpers.ChangeFlag.LadderChange;
                m_ladderValue = player.ComponentLocomotion.LadderValue.Value;
            }
            if (body.CanCrouch) {
                changeFlag |= PlayerPackageHelpers.ChangeFlag.SneakChange;
                m_sneaking = body.IsCrouching;
            }
        }
        if (body.SendPosition.HasValue) {
            Position = body.SendPosition.Value;
            changeFlag |= PlayerPackageHelpers.ChangeFlag.PositionChange;
            body.SendPosition = null;
        }
        if (body.SendRotation.HasValue) {
            Rotation = body.SendRotation.Value;
            changeFlag |= PlayerPackageHelpers.ChangeFlag.RotationChange;
            body.SendRotation = null;
        }
        if (body.SendVelocity.HasValue) {
            Velocity = body.SendVelocity.Value;
            changeFlag |= PlayerPackageHelpers.ChangeFlag.VelocityChange;
            body.SendVelocity = null;
        }
        m_fromPlayerId = playerData?.ClientId ?? 0;
    }

    public override void Serialize(PackageSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerPackageHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Enum(ref changeFlag);
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.LookAnglesChange)) archive.Value(ref LookAngles);
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.ChildLookAnglesChange)) archive.Value(ref ChildLookAngles);
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.PositionChange)) archive.Value(ref Position);
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.RotationChange)) archive.Value(ref Rotation);
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.VelocityChange)) archive.Value(ref Velocity);
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.LadderChange)) archive.Value(ref m_ladderValue);
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.SneakChange)) archive.Value(ref m_sneaking);
    }

    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) {
            return;
        }
        ComponentPlayer player = playerData.ComponentPlayer;
        ComponentBody body;
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.ParentBodyChange) && player.ComponentBody.ParentBody != null) {
            body = player.ComponentBody.ParentBody;
            if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.LookAnglesChange)) {
                body.Locomotion?.NetLookAngles.SetNext(LookAngles);
            }
            if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.ChildLookAnglesChange)) {
                player.ComponentBody.Locomotion?.NetLookAngles.SetNext(ChildLookAngles);
            }
        }
        else {
            body = player.ComponentBody;
            if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.LookAnglesChange)) {
                player.ComponentLocomotion.NetLookAngles.SetNext(LookAngles);
            }
        }
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.VelocityChange)) {
            body.netVelocity.SetNext(Velocity);
        }
        if (body.Locomotion != null) {
            body.Locomotion.LadderValue = changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.LadderChange) ? m_ladderValue : null;
        }
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.PositionChange)) {
            body.netPosition.SetNext(Position);
        }
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.RotationChange)) {
            body.netRotation.SetNext(Rotation);
        }
        if (changeFlag.HasFlag(PlayerPackageHelpers.ChangeFlag.SneakChange)) {
            body.IsSneaking = m_sneaking;
        }
    }
}