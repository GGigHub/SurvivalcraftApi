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
        m_fromPlayerId = player.PlayerData?.ClientId ?? 0;
        bodyId = body.Entity.Id;
        m_hitPosition = hitPosition;
        m_hitDirection = hitDirection;
    }
    public override void Serialize(PacketSerializer archive) {
        SerializePlayerId(archive, archive.IsWriting ? PlayerHelpers.ResolvePlayer(m_fromPlayerId) : null);
        archive.Value(ref bodyId);
        archive.Value(ref m_hitPosition);
        archive.Value(ref m_hitDirection);
    }
    public override void Handle(bool isServer) {
        if (!TryResolvePlayer(isServer, out PlayerData playerData)) return;
        if (isServer && PlayerHelpers.ThrottleHit(playerData)) {
            return;
        }
        ComponentPlayer player = playerData.ComponentPlayer;
        Vector3 eye = player.ComponentMiner.ComponentCreature.ComponentCreatureModel.EyePosition;
        Vector3 dir = m_hitDirection;
        if (dir.LengthSquared() < 1E-06f) {
            dir = player.ComponentMiner.ComponentCreature.ComponentBody.Matrix.Forward;
        }
        dir = Vector3.Normalize(dir);
        Ray3 serverRay = new(eye, dir);
        BodyRaycastResult? bodyHit = player.ComponentMiner.Raycast<BodyRaycastResult>(serverRay, RaycastMode.Interaction);
        if (bodyHit.HasValue) {
            BodyRaycastResult hit = bodyHit.Value;
            if (Vector3.Distance(hit.HitPoint(), eye) <= 6f && hit.ComponentBody?.Entity?.Id == bodyId) {
                GameManager.Project.FindEntity(bodyId, entity => {
                    ComponentBody body = entity.FindComponent<ComponentBody>();
                    player.ComponentMiner.Hit(body, hit.HitPoint(), dir);
                    PlayerHelpers.QueuePlayerPacket(this, isServer);
                });
            }
        }
    }
}

