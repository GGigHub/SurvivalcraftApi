using Engine;

namespace Game.Network;

public class MovingBlockAddPacket : Packet {
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

    public MovingBlockAddPacket(SubsystemMovingBlocks.MovingBlockSet movingSet) {
        if (GameManager.Project == null) return;
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
        SerializeBlockList(archive, m_addBlocks);
    }

    public override void Handle(bool isServer) {
        if (isServer) {
            return;
        }
        SubsystemMovingBlocks subsystemMovingBlocks = GameManager.Project.FindSubsystem<SubsystemMovingBlocks>();

        // The client simulates collapsing columns locally (dig prediction and
        // block behaviors for network terrain changes), so the server's
        // replicated "CollapsingBlock" set would otherwise be added on top of
        // the local set. Two overlapping sets stop each other mid-air in
        // SubsystemMovingBlocks.Update and get written back to the client's
        // terrain as blocks the server does not have ("ghost sand"). Drop the
        // local set and keep the server's authoritative set.
        if (m_addId == SubsystemCollapsingBlockBehavior.IdString) {
            RemoveMatchingLocalCollapseSet(subsystemMovingBlocks);
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
            ApplyMovingBlockData(movingSet);
        }
        if (movingSet != null && movingSet.Id == SubsystemPistonBlockBehavior.IdString) {
            GameManager.Project.FindSubsystem<SubsystemAudio>().PlaySound("Audio/Piston", 1f, 0f, movingSet.Position, 2f, autoDelay: true);
        }
    }

    private void RemoveMatchingLocalCollapseSet(SubsystemMovingBlocks subsystemMovingBlocks) {
        Point3 addOrigin = Terrain.ToCell(m_addPosition);
        foreach (SubsystemMovingBlocks.MovingBlockSet existing in subsystemMovingBlocks.MovingBlockSetEnumerable.ToArray()) {
            if (existing.Id != SubsystemCollapsingBlockBehavior.IdString
                || existing.Blocks.Count != m_addBlocks.Count
                || Terrain.ToCell(existing.StartPosition) != addOrigin) {
                continue;
            }
            bool matches = true;
            for (int i = 0; i < m_addBlocks.Count; i++) {
                if (existing.Blocks[i].Offset != m_addBlocks[i].Offset
                    || existing.Blocks[i].Value != m_addBlocks[i].Value) {
                    matches = false;
                    break;
                }
            }
            if (!matches) {
                continue;
            }
            // fromNetwork: true so the client removes the set locally without
            // echoing a removal request back to the server.
            subsystemMovingBlocks.RemoveMovingBlockSet(existing, true);
            return;
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
            if (count < 0) {
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

