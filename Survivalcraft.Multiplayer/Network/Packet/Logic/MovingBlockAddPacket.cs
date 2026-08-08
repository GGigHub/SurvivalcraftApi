using Engine;

namespace Game.Network;

public class MovingBlockAddPacket : Packet {
    public const int MaximumBlocks = 256;

    public override byte ID => (byte)PacketType.MovingBlockAdd;
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;

    public Vector3 m_addPosition;
    public Vector3 m_addTargetPosition;
    public float m_addSpeed;
    public float m_addAcceleration;
    public float m_addDrag;
    public Vector2 m_addSmoothness;
    public string m_addId;
    public bool m_hasTag;
    public Point3 m_tagPosition;
    public long m_instanceId;
    public long m_generation;
    public long m_revision;
    public readonly List<MovingBlockData> m_addBlocks = [];

    public struct MovingBlockData {
        public Point3 Offset;
        public int Value;
        public bool HasSignData;
        public Point3 SignPoint;
        public string[] SignLines;
        public Color[] SignColors;
        public string SignUrl;
        public bool HasMemoryBankData;
        public string MemoryBankData;
        public bool HasTruthTableData;
        public string TruthTableData;
    }

    public MovingBlockAddPacket() { }

    public MovingBlockAddPacket(SubsystemMovingBlocks.MovingBlockSet movingSet)
        : this(movingSet, baseline: false) {
    }

    private MovingBlockAddPacket(
        SubsystemMovingBlocks.MovingBlockSet movingSet,
        bool baseline) {
        ArgumentNullException.ThrowIfNull(movingSet);
        if (GameManager.Project == null) {
            return;
        }
        m_addPosition = movingSet.Position;
        m_addTargetPosition = movingSet.TargetPosition;
        m_addSpeed = movingSet.Speed;
        m_addAcceleration = movingSet.Acceleration;
        m_addDrag = movingSet.Drag;
        m_addSmoothness = movingSet.Smoothness;
        m_addId = movingSet.Id;
        if (movingSet.Tag is Point3 tagPosition) {
            m_hasTag = true;
            m_tagPosition = tagPosition;
        }
        m_instanceId = NetworkMovingBlockIdentityTracker.GetOrCreate(movingSet);
        if (GameManager.Project != null) {
            NetworkLogicStateKey key = StateKey();
            m_generation = baseline
                ? NetworkLogicStateTracker.EnsureAuthoritativeMovingGeneration(
                    GameManager.Project,
                    key)
                : NetworkLogicStateTracker.AdvanceAuthoritativeMovingGeneration(
                    GameManager.Project,
                    key);
            m_revision = NetworkLogicStateTracker.GetAuthoritativeRevision(
                GameManager.Project,
                key);
            if (m_revision <= 0) {
                m_revision = NetworkLogicStateTracker.AdvanceAuthoritativeRevision(
                    GameManager.Project,
                    key);
            }
        }
        SubsystemSignBlockBehavior subsystemSigns = GameManager.Project.FindSubsystem<SubsystemSignBlockBehavior>();
        SubsystemMemoryBankBlockBehavior subsystemMemoryBanks = GameManager.Project.FindSubsystem<SubsystemMemoryBankBlockBehavior>();
        SubsystemTruthTableCircuitBlockBehavior subsystemTruthTables = GameManager.Project.FindSubsystem<SubsystemTruthTableCircuitBlockBehavior>();
        foreach (MovingBlock block in movingSet.Blocks) {
            MovingBlockData data = new() {
                Offset = block.Offset,
                Value = block.Value
            };
            if (subsystemSigns.m_textsByMovingBlock.TryGetValue(block, out SubsystemSignBlockBehavior.TextData signData)) {
                data.HasSignData = true;
                data.SignPoint = signData.Point;
                data.SignLines = signData.Lines.ToArray();
                data.SignColors = signData.Colors.ToArray();
                data.SignUrl = signData.Url;
            }
            MemoryBankData memoryBankData = subsystemMemoryBanks.GetBlockData(block);
            if (memoryBankData != null) {
                data.HasMemoryBankData = true;
                data.MemoryBankData = memoryBankData.SaveString();
            }
            TruthTableData truthTableData = subsystemTruthTables.GetBlockData(block);
            if (truthTableData != null) {
                data.HasTruthTableData = true;
                data.TruthTableData = truthTableData.SaveString();
            }
            m_addBlocks.Add(data);
        }
    }

    public static MovingBlockAddPacket CreateBaseline(
        SubsystemMovingBlocks.MovingBlockSet movingSet) =>
        new(movingSet, baseline: true);

    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref m_addPosition);
        archive.Value(ref m_addTargetPosition);
        archive.Value(ref m_addSpeed);
        archive.Value(ref m_addAcceleration);
        archive.Value(ref m_addDrag);
        archive.Value(ref m_addSmoothness);
        archive.Value(ref m_addId);
        archive.Value(ref m_hasTag);
        if (m_hasTag) {
            archive.Value(ref m_tagPosition);
        }
        archive.Value(ref m_instanceId);
        archive.Value(ref m_generation);
        archive.Value(ref m_revision);
        SerializeBlockList(archive, m_addBlocks);
    }

    public override void Handle(bool isServer) {
        if (isServer
            || !IsValidState()
            || GameManager.Project == null) {
            return;
        }
        SubsystemMovingBlocks subsystemMovingBlocks = GameManager.Project.FindSubsystem<SubsystemMovingBlocks>();
        IMovingBlockSet existing = subsystemMovingBlocks.FindMovingBlocks(
            m_addId,
            m_hasTag ? m_tagPosition : null);
        bool hasRegisteredInstance = NetworkMovingBlockIdentityTracker.TryGet(
                GameManager.Project,
                m_instanceId,
                out SubsystemMovingBlocks.MovingBlockSet existingInstance);
        if (hasRegisteredInstance
            && subsystemMovingBlocks.m_movingBlockSets.Contains(existingInstance)) {
            existing = existingInstance;
        }
        else {
            if (hasRegisteredInstance) {
                NetworkMovingBlockIdentityTracker.Unregister(
                    GameManager.Project,
                    existingInstance);
            }
            if (!m_hasTag) {
                existing = FindPreloadedUntaggedSet(subsystemMovingBlocks);
            }
        }
        NetworkLogicStateKey key = StateKey();
        if (!NetworkLogicStateTracker.ReceiveMoving(
                GameManager.Project,
                key,
                m_generation,
                m_revision,
                present: true,
                forceRefresh: existing == null).Apply) {
            return;
        }
        if (existing != null) {
            NetworkMovingBlockIdentityTracker.Unregister(
                GameManager.Project,
                (SubsystemMovingBlocks.MovingBlockSet)existing);
            subsystemMovingBlocks.RemoveMovingBlockSet(existing, true);
        }
        List<MovingBlock> blocks = new(m_addBlocks.Count);
        foreach (MovingBlockData block in m_addBlocks) {
            blocks.Add(new MovingBlock {
                Offset = block.Offset,
                Value = block.Value
            });
        }
        object tag = m_hasTag ? m_tagPosition : null;
        var movingSet = subsystemMovingBlocks.AddMovingBlockSet(m_addPosition, m_addTargetPosition, m_addSpeed, m_addAcceleration, m_addDrag, m_addSmoothness, blocks, m_addId, tag, false) as SubsystemMovingBlocks.MovingBlockSet;
        if (movingSet != null) {
            NetworkMovingBlockIdentityTracker.Register(
                GameManager.Project,
                movingSet,
                m_instanceId);
            ApplyMovingBlockData(movingSet);
        }
        if (movingSet != null && movingSet.Id == SubsystemPistonBlockBehavior.IdString) {
            GameManager.Project.FindSubsystem<SubsystemAudio>().PlaySound("Audio/Piston", 1f, 0f, movingSet.Position, 2f, autoDelay: true);
        }
    }

    private void ApplyMovingBlockData(SubsystemMovingBlocks.MovingBlockSet movingSet) {
        SubsystemSignBlockBehavior subsystemSigns = GameManager.Project.FindSubsystem<SubsystemSignBlockBehavior>();
        SubsystemMemoryBankBlockBehavior subsystemMemoryBanks = GameManager.Project.FindSubsystem<SubsystemMemoryBankBlockBehavior>();
        SubsystemTruthTableCircuitBlockBehavior subsystemTruthTables = GameManager.Project.FindSubsystem<SubsystemTruthTableCircuitBlockBehavior>();
        for (int i = 0; i < m_addBlocks.Count && i < movingSet.Blocks.Count; i++) {
            MovingBlockData data = m_addBlocks[i];
            MovingBlock movingBlock = movingSet.Blocks[i];
            if (data.HasSignData) {
                subsystemSigns.SetSignData(data.SignPoint, data.SignLines, data.SignColors, data.SignUrl, movingBlock);
            }
            if (data.HasMemoryBankData) {
                MemoryBankData memoryBankData = new();
                memoryBankData.LoadString(data.MemoryBankData);
                subsystemMemoryBanks.SetBlockData(movingBlock, memoryBankData);
            }
            if (data.HasTruthTableData) {
                TruthTableData truthTableData = new();
                truthTableData.LoadString(data.TruthTableData);
                subsystemTruthTables.SetBlockData(movingBlock, truthTableData);
            }
        }
    }

    public static void SerializeBlockList(PacketSerializer archive, List<MovingBlockAddPacket.MovingBlockData> blocks) {
        int count = archive.IsWriting ? blocks.Count : 0;
        archive.Value(ref count);
        if (archive.IsReading) {
            blocks.Clear();
            if (count is < 0 or > MaximumBlocks) {
                throw new InvalidDataException($"MovingBlockPacket contains invalid block count: {count}");
            }
            for (int i = 0; i < count; i++) {
                MovingBlockAddPacket.MovingBlockData data = new();
                archive.Value(ref data.Offset);
                archive.Value(ref data.Value);
                SerializeMovingBlockData(archive, ref data);
                blocks.Add(data);
            }
        }
        else {
            for (int i = 0; i < count; i++) {
                MovingBlockAddPacket.MovingBlockData data = blocks[i];
                archive.Value(ref data.Offset);
                archive.Value(ref data.Value);
                SerializeMovingBlockData(archive, ref data);
            }
        }
    }

    private NetworkLogicStateKey StateKey() =>
        NetworkMovingBlockIdentityTracker.CreateStateKey(
            m_addId,
            m_hasTag,
            m_tagPosition,
            m_instanceId);

    private SubsystemMovingBlocks.MovingBlockSet FindPreloadedUntaggedSet(
        SubsystemMovingBlocks subsystemMovingBlocks) =>
        subsystemMovingBlocks.m_movingBlockSets
            .Where(set =>
                set.Id == m_addId
                && set.Tag == null
                && !NetworkMovingBlockIdentityTracker.IsRegistered(
                    GameManager.Project,
                    set)
                && BlocksMatch(set.Blocks))
            .OrderBy(set =>
                Vector3.DistanceSquared(set.Position, m_addPosition)
                + Vector3.DistanceSquared(set.TargetPosition, m_addTargetPosition))
            .FirstOrDefault();

    private bool BlocksMatch(IReadOnlyList<MovingBlock> blocks) {
        if (blocks.Count != m_addBlocks.Count) {
            return false;
        }
        for (int index = 0; index < blocks.Count; index++) {
            if (blocks[index].Offset != m_addBlocks[index].Offset
                || Terrain.ReplaceLight(blocks[index].Value, 0)
                    != Terrain.ReplaceLight(m_addBlocks[index].Value, 0)) {
                return false;
            }
        }
        return true;
    }

    private bool IsValidState() =>
        m_instanceId > 0
        && m_generation > 0
        && m_revision > 0
        && !string.IsNullOrWhiteSpace(m_addId)
        && m_addId.Length <= 256
        && IsFinite(m_addPosition)
        && IsFinite(m_addTargetPosition)
        && float.IsFinite(m_addSpeed)
        && float.IsFinite(m_addAcceleration)
        && float.IsFinite(m_addDrag)
        && float.IsFinite(m_addSmoothness.X)
        && float.IsFinite(m_addSmoothness.Y)
        && m_addBlocks.Count is > 0 and <= MaximumBlocks
        && m_addBlocks.All(block =>
            block.HasSignData
                ? block.SignLines is { Length: <= 16 }
                    && block.SignColors is { Length: <= 16 }
                    && block.SignUrl?.Length <= 2048
                : true)
        && m_addBlocks.All(block =>
            !block.HasMemoryBankData || block.MemoryBankData?.Length <= 512)
        && m_addBlocks.All(block =>
            !block.HasTruthTableData || block.TruthTableData?.Length <= 64);

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X)
        && float.IsFinite(value.Y)
        && float.IsFinite(value.Z);

    private static void SerializeMovingBlockData(PacketSerializer archive, ref MovingBlockAddPacket.MovingBlockData data) {
        archive.Value(ref data.HasSignData);
        if (data.HasSignData) {
            archive.Value(ref data.SignPoint);
            SerializeStringArray(archive, ref data.SignLines);
            SerializeColorArray(archive, ref data.SignColors);
            archive.Value(ref data.SignUrl);
        }
        archive.Value(ref data.HasMemoryBankData);
        if (data.HasMemoryBankData) {
            archive.Value(ref data.MemoryBankData);
        }
        archive.Value(ref data.HasTruthTableData);
        if (data.HasTruthTableData) {
            archive.Value(ref data.TruthTableData);
        }
    }

    private static void SerializeStringArray(PacketSerializer archive, ref string[] values) {
        byte count = archive.IsWriting ? (byte)values.Length : (byte)0;
        archive.Value(ref count);
        if (archive.IsReading) {
            values = new string[count];
        }
        for (int i = 0; i < count; i++) {
            string value = archive.IsWriting ? values[i] : string.Empty;
            archive.Value(ref value);
            if (archive.IsReading) {
                values[i] = value;
            }
        }
    }

    private static void SerializeColorArray(PacketSerializer archive, ref Color[] values) {
        byte count = archive.IsWriting ? (byte)values.Length : (byte)0;
        archive.Value(ref count);
        if (archive.IsReading) {
            values = new Color[count];
        }
        for (int i = 0; i < count; i++) {
            Color value = archive.IsWriting ? values[i] : default;
            archive.Value(ref value);
            if (archive.IsReading) {
                values[i] = value;
            }
        }
    }
}

