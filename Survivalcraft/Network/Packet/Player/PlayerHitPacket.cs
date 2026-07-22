using Engine;
using TemplatesDatabase;

namespace Game.Network;

public class PlayerHitPacket : PlayerPacketBase {
    public override byte ID => (byte)PacketType.PlayerHit;
    public int bodyId;
    public Vector3 m_hitPosition;
    public Vector3 m_hitDirection;
    public PlayerHitPacket() { }
    public PlayerHitPacket(ComponentPlayer player, ComponentBody body, Vector3 hitPosition, Vector3 hitDirection) {
        m_fromPlayerIndex = player.PlayerData.PlayerIndex;
        bodyId = body.Entity.Id;
        m_hitPosition = hitPosition;
        m_hitDirection = hitDirection;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? ResolvePlayer(m_fromPlayerIndex) : null);
        archive.Value(ref bodyId);
        archive.Value(ref m_hitPosition);
        archive.Value(ref m_hitDirection);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        if (isServer && ThrottleHit(playerData)) {
            return;
        }
        ComponentPlayer player = playerData.ComponentPlayer;
        player.ComponentCreatureModel.Update(0f);
        Vector3 eye = player.ComponentMiner.ComponentCreature.ComponentCreatureModel.EyePosition;
        Vector3 dir = m_hitDirection;
        if (dir.LengthSquared() < 1E-06f) {
            dir = player.ComponentMiner.ComponentCreature.ComponentBody.Matrix.Forward;
        }
        dir = Vector3.Normalize(dir);
        GameManager.Project.FindEntity(bodyId, entity => {
            ComponentBody body = entity.FindComponent<ComponentBody>();
            if (body == null) {
                return;
            }
            if (isServer) {
                if (Vector3.Distance(m_hitPosition, eye) > 6f) {
                    return;
                }
                BoundingBox box = body.BoundingBox;
                box.Min -= new Vector3(0.5f);
                box.Max += new Vector3(0.5f);
                if (m_hitPosition.X < box.Min.X || m_hitPosition.X > box.Max.X
                    || m_hitPosition.Y < box.Min.Y || m_hitPosition.Y > box.Max.Y
                    || m_hitPosition.Z < box.Min.Z || m_hitPosition.Z > box.Max.Z) {
                    return;
                }
            }
            player.ComponentMiner.Hit(body, m_hitPosition, dir, !isServer, isServer);
            QueuePlayerPacket(this, isServer);
        });
    }
}


