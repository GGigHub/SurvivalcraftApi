using Engine;

namespace Game.NetWork.Packages;

/// <summary>
/// 鍙戝皠鍣?
/// </summary>
public class DispenserPackage : IPackage {
    public byte ID => (byte)PackageType.Dispenser;

    public Client To { get; set; }
    public Client Except { get; set; }
    public Client From { get; set; }

    public ClientStage MinNeedState => ClientStage.ProjectLoaded;

    private Point3 m_point;
    private byte m_flag;
    public DispenserPackage() { }
    public DispenserPackage(Point3 point, byte flag) {
        m_point = point;
        m_flag = flag;
    }

    public void Handle(bool isServer) {
        var sut = GameManager.Project.FindSubsystem<SubsystemTerrain>();
        var value = sut.Terrain.GetCellValue(m_point.X, m_point.Y, m_point.Z);
        if (Terrain.ExtractContents(value) == DispenserBlock.Index) {
            var data = Terrain.ExtractData(value);
            data = DispenserBlock.SetMode(data, (m_flag & 1) != 0 ? DispenserBlock.Mode.Shoot : DispenserBlock.Mode.Dispense);
            data = DispenserBlock.SetAcceptsDrops(data, (m_flag & (1 << 1)) != 0);
            sut.ChangeCell(m_point.X, m_point.Y, m_point.Z, Terrain.ReplaceData(value, data));
        }
        if (isServer) NetworkManager.QueuePackage(this);
    }

    public void ReadData(PackageStreamReader reader) {
        m_point = reader.ReadPoint3();
        m_flag = reader.ReadByte();
    }

    public void WriteData(PackageStreamWriter writer) {
        writer.Write(m_point);
        writer.Write(m_flag);
    }

}
