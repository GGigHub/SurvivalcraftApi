using Engine;
using Game;
using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class LogicPacketCodecTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void EditableMutationEnvelopeAndDispenserPayloadRoundTrip() {
        DispenserPacket source = new() {
            m_packetKind = EditableLogicPacketKind.Request,
            m_result = EditableLogicResultCode.None,
            m_requestId = 4_294_967_411L,
            m_expectedRevision = 37,
            m_revision = -1,
            m_terrainRevision = 91,
            m_point = new Point3(4, 70, -8),
            m_flag = 3
        };

        DispenserPacket decoded = RoundTrip(source, static () => new DispenserPacket());

        Assert.Equal(source.m_packetKind, decoded.m_packetKind);
        Assert.Equal(source.m_result, decoded.m_result);
        Assert.Equal(source.m_requestId, decoded.m_requestId);
        Assert.Equal(source.m_expectedRevision, decoded.m_expectedRevision);
        Assert.Equal(source.m_revision, decoded.m_revision);
        Assert.Equal(source.m_terrainRevision, decoded.m_terrainRevision);
        Assert.Equal(source.m_point, decoded.m_point);
        Assert.Equal(source.m_flag, decoded.m_flag);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void SignAuthoritativeStateRequiresFixedShapeAndRoundTrips() {
        SignBlockPacket source = new() {
            m_packetKind = EditableLogicPacketKind.State,
            m_result = EditableLogicResultCode.Accepted,
            m_requestId = 123,
            m_expectedRevision = -1,
            m_revision = 12,
            m_terrainRevision = 33,
            m_point = new Point3(7, 71, 9),
            m_signData = new SignData {
                Lines = ["one", "two", "three", "four"],
                Colors = [Color.Red, Color.Green, Color.Blue, Color.White],
                Url = "https://example.invalid/sign"
            }
        };

        SignBlockPacket decoded = RoundTrip(source, static () => new SignBlockPacket());

        Assert.Equal(source.m_revision, decoded.m_revision);
        Assert.Equal(source.m_point, decoded.m_point);
        Assert.Equal(source.m_signData.Lines, decoded.m_signData.Lines);
        Assert.Equal(source.m_signData.Colors, decoded.m_signData.Colors);
        Assert.Equal(source.m_signData.Url, decoded.m_signData.Url);

        source.m_signData.Lines = ["too", "short", "shape"];
        Assert.Throws<InvalidDataException>(() => Encode(source));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void StructuredLogicDataPreservesMemoryOutputAndNibbles() {
        MemoryBankDataPacket memory = new() {
            m_packetKind = EditableLogicPacketKind.State,
            m_result = EditableLogicResultCode.Accepted,
            m_revision = 51,
            m_expectedRevision = -1,
            m_terrainRevision = 8,
            m_data = [0, 3, 9, 15],
            m_lastOutput = 14
        };
        TruthTableDataPacket truth = new() {
            m_packetKind = EditableLogicPacketKind.State,
            m_result = EditableLogicResultCode.Accepted,
            m_revision = 52,
            m_expectedRevision = -1,
            m_terrainRevision = 8,
            m_data = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray()
        };

        MemoryBankDataPacket decodedMemory = RoundTrip(
            memory,
            static () => new MemoryBankDataPacket());
        TruthTableDataPacket decodedTruth = RoundTrip(
            truth,
            static () => new TruthTableDataPacket());

        Assert.Equal(memory.m_data, decodedMemory.m_data);
        Assert.Equal(14, decodedMemory.m_lastOutput);
        Assert.Equal(truth.m_data, decodedTruth.m_data);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ElectricitySnapshotPreservesRevisionStepAndVoltages() {
        SubsystemElectricity.NetSimulate simulate = new() { StartStep = 812 };
        simulate.SaveData[new Point3(1, 70, 2)] = 0.25f;
        simulate.SaveData[new Point3(-3, 68, 4)] = 0.9375f;
        SubsystemElectricityPacket source = new() {
            m_revision = 4_294_967_422L,
            NetSimulates = { simulate }
        };

        SubsystemElectricityPacket decoded = RoundTrip(
            source,
            static () => new SubsystemElectricityPacket());
        SubsystemElectricity.NetSimulate state = Assert.Single(decoded.NetSimulates);

        Assert.Equal(source.m_revision, decoded.m_revision);
        Assert.Equal(812, state.StartStep);
        Assert.Equal(simulate.SaveData, state.SaveData);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void MovingBlockPacketsPreserveGenerationRevisionAndTombstoneIdentity() {
        MovingBlockAddPacket add = new() {
            m_addPosition = new Vector3(1.5f, 70f, 2.5f),
            m_addTargetPosition = new Vector3(2.5f, 70f, 2.5f),
            m_addSpeed = 2f,
            m_addAcceleration = 0.5f,
            m_addDrag = 0.1f,
            m_addSmoothness = new Vector2(0.2f, 0.3f),
            m_addId = "Piston",
            m_hasTag = true,
            m_tagPosition = new Point3(1, 70, 2),
            m_instanceId = 101,
            m_generation = 7,
            m_revision = 11
        };
        add.m_addBlocks.Add(new MovingBlockAddPacket.MovingBlockData {
            Offset = new Point3(0, 0, 0),
            Value = 15,
            HasMemoryBankData = true,
            MemoryBankData = "0;1;2;3"
        });
        MovingBlockRemovePacket remove = new() {
            movingBlockId = "Piston",
            m_hasTag = true,
            m_position = new Point3(1, 70, 2),
            m_stop = true,
            m_instanceId = 101,
            m_generation = 7,
            m_revision = 12
        };

        MovingBlockAddPacket decodedAdd = RoundTrip(
            add,
            static () => new MovingBlockAddPacket());
        MovingBlockRemovePacket decodedRemove = RoundTrip(
            remove,
            static () => new MovingBlockRemovePacket());

        Assert.Equal(("Piston", true, 101L, 7L, 11L),
            (decodedAdd.m_addId, decodedAdd.m_hasTag,
                decodedAdd.m_instanceId, decodedAdd.m_generation, decodedAdd.m_revision));
        MovingBlockAddPacket.MovingBlockData block = Assert.Single(decodedAdd.m_addBlocks);
        Assert.Equal((new Point3(0, 0, 0), 15, true, "0;1;2;3"),
            (block.Offset, block.Value, block.HasMemoryBankData, block.MemoryBankData));
        Assert.Equal(("Piston", true, new Point3(1, 70, 2), true, 101L, 7L, 12L),
            (decodedRemove.movingBlockId, decodedRemove.m_hasTag,
                decodedRemove.m_position, decodedRemove.m_stop,
                decodedRemove.m_instanceId, decodedRemove.m_generation,
                decodedRemove.m_revision));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void InteractionRequestIdentityRoundTrips() {
        PlayerInteractPacket source = new() {
            m_fromPlayerIndex = 5,
            m_interactEvent = ComponentPlayer.InteractEvent.Start,
            m_netInteractRay = new Ray3(
                new Vector3(1f, 2f, 3f),
                Vector3.UnitZ),
            m_netInteractRaycast = null,
            m_requestId = 9_223_372_036_800L
        };

        PlayerInteractPacket decoded = RoundTrip(
            source,
            static () => new PlayerInteractPacket());

        Assert.Equal(source.m_fromPlayerIndex, decoded.m_fromPlayerIndex);
        Assert.Equal(source.m_interactEvent, decoded.m_interactEvent);
        Assert.Equal(source.m_netInteractRay, decoded.m_netInteractRay);
        Assert.Null(decoded.m_netInteractRaycast);
        Assert.Equal(source.m_requestId, decoded.m_requestId);
    }

    private static TPacket RoundTrip<TPacket>(
        TPacket source,
        Func<TPacket> create)
        where TPacket : Packet {
        byte[] payload = Encode(source);
        TPacket decoded = create();
        using PacketStreamReader reader = new(payload);
        decoded.Serialize(new PacketSerializer(reader));
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        return decoded;
    }

    private static byte[] Encode(Packet packet) {
        using PacketStreamWriter writer = new();
        packet.Serialize(new PacketSerializer(writer));
        return writer.Data();
    }
}
