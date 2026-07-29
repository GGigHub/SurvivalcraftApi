using Engine;

namespace Game.Network;

public class SignBlockPacket : EditableBlockPacketBase {
    public const int LineCount = 4;
    public const int MaximumLineLength = 256;
    public const int MaximumUrlLength = 2048;

    public override byte ID => (byte)PacketType.SignBlock;
    public override NetworkState MinNeedState => NetworkState.Playing;

    public Point3 m_point;
    public SignData m_signData = new();

    public SignBlockPacket() {
    }

    public SignBlockPacket(Point3 point, string[] lines, Color[] colors, string url) {
        m_point = point;
        m_signData = new SignData {
            Lines = lines?.ToArray() ?? [],
            Colors = colors?.ToArray() ?? [],
            Url = url ?? string.Empty
        };
        InitializeRequest(
            NetworkLogicElementKind.Sign,
            editAsItem: false,
            inventoryId: 0,
            slotIndex: 0,
            new CellFace(point, 0));
    }

    public override void Serialize(PacketSerializer archive) {
        SerializeMutationEnvelope(archive);
        archive.Value(ref m_point);
        m_signData ??= new SignData();
        string url = m_signData.Url ?? string.Empty;
        archive.Value(ref url);
        m_signData.Url = url;
        SerializeColorArray(archive, ref m_signData.Colors);
        SerializeLineArray(archive, ref m_signData.Lines);
    }

    public override void Handle(bool isServer) {
        if (!IsValidData() || GameManager.Project == null) {
            return;
        }
        EditableTarget target = Target(false, 0, 0, new CellFace(m_point, 0));
        SubsystemSignBlockBehavior behavior =
            GameManager.Project.FindSubsystem<SubsystemSignBlockBehavior>();
        if (isServer) {
            EditableLogicResultCode result = ValidateServerRequest(
                NetworkLogicElementKind.Sign,
                target,
                IsSignValue,
                out _,
                out int currentValue);
            if (result == EditableLogicResultCode.Accepted) {
                Apply(behavior);
                PrepareAuthoritativeState(NetworkLogicElementKind.Sign, target);
                NetworkManager.Queue(this);
            }
            else if (From != null) {
                LoadCurrentData(behavior, currentValue);
                PrepareAuthoritativeState(
                    NetworkLogicElementKind.Sign,
                    target,
                    result,
                    advanceRevision: false);
                To = From;
                NetworkManager.Queue(this);
            }
            return;
        }
        if (!TryAcceptClientState(
                NetworkLogicElementKind.Sign,
                target,
                IsSignValue,
                out _,
                out _)) {
            return;
        }
        Apply(behavior);
    }

    private void Apply(SubsystemSignBlockBehavior behavior) =>
        behavior.SetSignData(
            m_point,
            m_signData.Lines,
            m_signData.Colors,
            m_signData.Url);

    private void LoadCurrentData(
        SubsystemSignBlockBehavior behavior,
        int currentValue) {
        SignData current = IsSignValue(currentValue)
            ? behavior.GetSignData(m_point)
            : null;
        m_signData = current ?? new SignData { Url = string.Empty };
        m_signData.Url ??= string.Empty;
    }

    private bool IsValidData() =>
        m_signData?.Lines is { Length: LineCount }
        && m_signData.Colors is { Length: LineCount }
        && (m_signData.Url?.Length ?? 0) <= MaximumUrlLength
        && m_signData.Lines.All(line =>
            line != null && line.Length <= MaximumLineLength);

    private static bool IsSignValue(int value) {
        int contents = Terrain.ExtractContents(value);
        return contents >= 0
            && contents < BlocksManager.Blocks.Length
            && BlocksManager.Blocks[contents] is SignBlock;
    }

    private static void SerializeColorArray(
        PacketSerializer archive,
        ref Color[] colors) {
        int count = archive.IsWriting ? colors?.Length ?? 0 : 0;
        archive.Value(ref count);
        if (count != LineCount) {
            throw new InvalidDataException(
                $"Sign packet requires exactly {LineCount} colors, got {count}.");
        }
        if (archive.IsReading) {
            colors = new Color[count];
        }
        for (int i = 0; i < count; i++) {
            Color color = archive.IsWriting ? colors[i] : default;
            archive.Value(ref color);
            if (archive.IsReading) {
                colors[i] = color;
            }
        }
    }

    private static void SerializeLineArray(
        PacketSerializer archive,
        ref string[] lines) {
        int count = archive.IsWriting ? lines?.Length ?? 0 : 0;
        archive.Value(ref count);
        if (count != LineCount) {
            throw new InvalidDataException(
                $"Sign packet requires exactly {LineCount} lines, got {count}.");
        }
        if (archive.IsReading) {
            lines = new string[count];
        }
        for (int i = 0; i < count; i++) {
            string line = archive.IsWriting ? lines[i] ?? string.Empty : string.Empty;
            archive.Value(ref line);
            if (archive.IsReading) {
                lines[i] = line;
            }
        }
    }
}
