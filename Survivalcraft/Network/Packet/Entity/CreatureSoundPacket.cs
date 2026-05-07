using Engine;
using GameEntitySystem;

namespace Game.Network;

public class CreatureSoundPacket : Packet {
    public override byte ID => (byte)PacketType.BehaviorCreatureSound;
    public override ClientStage MinNeedState => ClientStage.Playing;
    public int m_entityId;
    public byte m_type;
    public bool m_skipIfRecentlyPlayed;
    public CreatureSoundPacket() { }
    public CreatureSoundPacket(ComponentCreatureSounds creatureSound, byte type, bool skip = false) {
        m_entityId = creatureSound.Entity.Id;
        m_type = type;
        m_skipIfRecentlyPlayed = skip;
    }
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_entityId);
        archive.Value(ref m_type);
        archive.Value(ref m_skipIfRecentlyPlayed);
    }
    public override void Handle(bool isServer) {
        GameManager.Project.FindEntity(m_entityId, entity => {
            ComponentCreatureSounds creatureSound = entity.FindComponent<ComponentCreatureSounds>();
            if (creatureSound == null) {
                return;
            }
            if (m_type == 0) creatureSound.PlayIdleSoundLogic(m_skipIfRecentlyPlayed);
            else if (m_type == 1) creatureSound.PlayPainSoundLogic();
            else if (m_type == 2) creatureSound.PlayMoanSoundLogic();
            else if (m_type == 3) creatureSound.PlaySneezeSoundLogic();
            else if (m_type == 4) creatureSound.PlayCoughSoundLogic();
            else if (m_type == 5) creatureSound.PlayPukeSoundLogic();
            else if (m_type == 6) creatureSound.PlayAttackSoundLogic();
        });
    }
}

