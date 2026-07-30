using Engine;

namespace Game.Network;

public class DispenserPacket : EditableBlockPacketBase {
    public const byte MaximumFlag = 3;

    public override byte ID => (byte)PacketType.Dispenser;
    public override NetworkState MinNeedState => NetworkState.Playing;

    public Point3 m_point;
    public byte m_flag;

    public DispenserPacket() {
    }

    public DispenserPacket(Point3 point, byte flag) {
        m_point = point;
        m_flag = flag;
        InitializeRequest(
            NetworkLogicElementKind.Dispenser,
            editAsItem: false,
            inventoryId: 0,
            slotIndex: 0,
            new CellFace(point, 0));
    }

    public override void Serialize(PacketSerializer archive) {
        SerializeMutationEnvelope(archive);
        archive.Value(ref m_point);
        archive.Value(ref m_flag);
    }

    public override void Handle(bool isServer) {
        if (GameManager.Project == null) {
            return;
        }
        EditableTarget target = Target(false, 0, 0, new CellFace(m_point, 0));
        if (isServer) {
            EditableLogicResultCode result = ValidateServerRequest(
                NetworkLogicElementKind.Dispenser,
                target,
                DispenserBlock.Index,
                out _,
                out int currentValue);
            if (result == EditableLogicResultCode.Accepted
                && m_flag > MaximumFlag) {
                result = EditableLogicResultCode.InvalidValue;
            }
            if (result == EditableLogicResultCode.Accepted) {
                Apply(currentValue);
                PrepareAuthoritativeState(NetworkLogicElementKind.Dispenser, target);
                NetworkManager.Queue(this);
            }
            else if (From != null) {
                if (Terrain.ExtractContents(currentValue) == DispenserBlock.Index) {
                    m_flag = GetFlag(Terrain.ExtractData(currentValue));
                }
                PrepareAuthoritativeState(
                    NetworkLogicElementKind.Dispenser,
                    target,
                    result,
                    advanceRevision: false);
                To = From;
                NetworkManager.Queue(this);
            }
            return;
        }
        if (m_flag > MaximumFlag
            || !TryAcceptClientState(
                NetworkLogicElementKind.Dispenser,
                target,
                DispenserBlock.Index,
                out _,
                out int clientValue)) {
            return;
        }
        Apply(clientValue);
    }

    private void Apply(int value) {
        int data = Terrain.ExtractData(value);
        data = DispenserBlock.SetMode(
            data,
            (m_flag & 1) != 0
                ? DispenserBlock.Mode.Shoot
                : DispenserBlock.Mode.Dispense);
        data = DispenserBlock.SetAcceptsDrops(data, (m_flag & 2) != 0);
        GameManager.Project.FindSubsystem<SubsystemTerrain>().ChangeCell(
            m_point.X,
            m_point.Y,
            m_point.Z,
            Terrain.ReplaceData(value, data));
    }

    private static byte GetFlag(int data) =>
        (byte)((DispenserBlock.GetMode(data) == DispenserBlock.Mode.Shoot ? 1 : 0)
            | (DispenserBlock.GetAcceptsDrops(data) ? 2 : 0));
}
