using Engine;

namespace Game.Network;

public class DispenserPacket : Packet {
    public override byte ID => (byte)PacketType.Dispenser;
    public override ClientStage MinNeedState => ClientStage.ProjectLoaded;

    private Point3 m_point;
    private byte m_flag;

    public DispenserPacket() {
    }

    public DispenserPacket(Point3 point, byte flag) {
        m_point = point;
        m_flag = flag;
    }

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_point);
        archive.Value(ref m_flag);
    }

    public override void Handle(bool isServer) {
        var sut = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        var value = sut.Terrain.GetCellValue(m_point.X, m_point.Y, m_point.Z);
        if (Terrain.ExtractContents(value) == DispenserBlock.Index) {
            var data = Terrain.ExtractData(value);
            data = DispenserBlock.SetMode(data, (m_flag & 1) != 0 ? DispenserBlock.Mode.Shoot : DispenserBlock.Mode.Dispense);
            data = DispenserBlock.SetAcceptsDrops(data, (m_flag & (1 << 1)) != 0);
            sut.ChangeCell(m_point.X, m_point.Y, m_point.Z, Terrain.ReplaceData(value, data));
        }
        if (isServer) {
            NetworkManager.QueuePacket(this);
        }
    }
}

