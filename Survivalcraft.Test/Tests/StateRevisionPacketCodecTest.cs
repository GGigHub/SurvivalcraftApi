using Game;
using Game.Network;
using GameEntitySystem;
using TemplatesDatabase;
using Xunit;

namespace Survivalcraft.Test;

public sealed class StateRevisionPacketCodecTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void InventorySnapshotPreservesLifecycleStateRevisionAndActiveSlot() {
        InventorySyncPacket source = new() {
            m_inventoryId = 19,
            m_entityId = 42,
            m_inventoryComponentTypeName = "Game.ComponentInventory",
            m_activeSlotIndex = 3,
            m_lifecycleRevision = 7,
            m_revision = 4_294_967_311L,
            m_slots = [
                new InventorySlot { slotIndex = 0, value = 201, count = 2 },
                new InventorySlot { slotIndex = 1, value = 22, count = 8 }
            ]
        };

        InventorySyncPacket decoded = RoundTrip(
            source,
            static () => new InventorySyncPacket());

        Assert.Equal(source.m_inventoryId, decoded.m_inventoryId);
        Assert.Equal(source.m_entityId, decoded.m_entityId);
        Assert.Equal(source.m_inventoryComponentTypeName, decoded.m_inventoryComponentTypeName);
        Assert.Equal(source.m_activeSlotIndex, decoded.m_activeSlotIndex);
        Assert.Equal(source.m_lifecycleRevision, decoded.m_lifecycleRevision);
        Assert.Equal(source.m_revision, decoded.m_revision);
        Assert.Equal(
            source.m_slots.Select(slot => (slot.slotIndex, slot.value, slot.count)),
            decoded.m_slots.Select(slot => (slot.slotIndex, slot.value, slot.count)));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ActiveSlotPacketPreservesAuthoritativeRevision() {
        ActiveSlotChangePacket decoded = RoundTrip(
            new ActiveSlotChangePacket {
                m_inventoryId = 21,
                m_activeSlotIndex = 4,
                m_revision = 9_223_372_036_854L,
                m_requestId = 7_777_777_777L
            },
            static () => new ActiveSlotChangePacket());

        Assert.Equal((21, 4, 9_223_372_036_854L, 7_777_777_777L),
            (decoded.m_inventoryId, decoded.m_activeSlotIndex,
                decoded.m_revision, decoded.m_requestId));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void InventoryAndInjuryMutationsPreserveRequestIdentity() {
        MoveItemPacket move = RoundTrip(
            new MoveItemPacket {
                fromInventoryId = 10,
                fromSlotIndex = 1,
                toInventoryId = 20,
                toSlotIndex = 2,
                count = 3,
                requestId = 4_294_967_399L
            },
            static () => new MoveItemPacket());
        DragDropItemPacket dragDrop = RoundTrip(
            new DragDropItemPacket {
                fromInventoryId = 11,
                fromSlotIndex = 3,
                toInventoryId = 21,
                toSlotIndex = 4,
                dragMode = DragMode.AllItems,
                processingOnly = false,
                requestedDragCount = 5,
                requestId = 4_294_967_400L
            },
            static () => new DragDropItemPacket());
        InjureRequestPacket injury = RoundTrip(
            new InjureRequestPacket {
                m_targetId = 31,
                m_attackerId = 0,
                m_health = 0.9f,
                m_ignoreInvulnerability = true,
                m_amount = 0.1f,
                m_cause = "ignored-client-cause",
                m_requestInjureType = InjureRequestPacket.RequestInjureType.Choke,
                m_requestId = 4_294_967_401L
            },
            static () => new InjureRequestPacket());

        Assert.Equal((10, 1, 20, 2, 3, 4_294_967_399L),
            (move.fromInventoryId, move.fromSlotIndex, move.toInventoryId,
                move.toSlotIndex, move.count, move.requestId));
        Assert.Equal((11, 3, 21, 4, DragMode.AllItems, false, 5, 4_294_967_400L),
            (dragDrop.fromInventoryId, dragDrop.fromSlotIndex,
                dragDrop.toInventoryId, dragDrop.toSlotIndex, dragDrop.dragMode,
                dragDrop.processingOnly, dragDrop.requestedDragCount, dragDrop.requestId));
        Assert.Equal((31, 0, 0.9f, true, 0.1f,
                InjureRequestPacket.RequestInjureType.Choke, 4_294_967_401L),
            (injury.m_targetId, injury.m_attackerId, injury.m_health,
                injury.m_ignoreInvulnerability, injury.m_amount,
                injury.m_requestInjureType, injury.m_requestId));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void HealthPacketsPreserveLifecycleAndStateRevisions() {
        HealthSyncPacket health = RoundTrip(
            new HealthSyncPacket {
                m_targetId = 31,
                m_health = 0.25f,
                m_cause = "test",
                m_hasDeathTime = true,
                m_deathTime = 123.5,
                m_lifecycleRevision = 8,
                m_revision = 101
            },
            static () => new HealthSyncPacket());
        InjureEventPacket injury = RoundTrip(
            new InjureEventPacket {
                m_targetId = 31,
                m_attackerId = 44,
                m_amount = 0.5f,
                m_health = 0.25f,
                m_cause = "melee",
                m_ignoreInvulnerability = false,
                m_lifecycleRevision = 8,
                m_revision = 100
            },
            static () => new InjureEventPacket());

        Assert.Equal((31, 0.25f, "test", true, 123.5, 8L, 101L),
            (health.m_targetId, health.m_health, health.m_cause,
                health.m_hasDeathTime, health.m_deathTime,
                health.m_lifecycleRevision, health.m_revision));
        Assert.Equal((31, 44, 0.5f, 0.25f, "melee", false, 8L, 100L),
            (injury.m_targetId, injury.m_attackerId, injury.m_amount,
                injury.m_health, injury.m_cause, injury.m_ignoreInvulnerability,
                injury.m_lifecycleRevision, injury.m_revision));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void DamagePacketPreservesLifecycleAndStateRevisions() {
        DamageSyncPacket decoded = RoundTrip(
            new DamageSyncPacket {
                m_targetId = 71,
                m_health = 0.75f,
                m_lifecycleRevision = 12,
                m_revision = 500
            },
            static () => new DamageSyncPacket());

        Assert.Equal((71, 0.75f, 12L, 500L),
            (decoded.m_targetId, decoded.m_health,
                decoded.m_lifecycleRevision, decoded.m_revision));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void VitalStatsPacketPreservesLifecycleAndStateRevisions() {
        VitalStatSyncPacket decoded = RoundTrip(
            new VitalStatSyncPacket {
                m_entityId = 19,
                Food = 0.25f,
                Stamina = 0.75f,
                Sleep = 0.5f,
                Wetness = 0.125f,
                Temperature = 11.5f,
                m_lifecycleRevision = 28,
                m_revision = 34
            },
            static () => new VitalStatSyncPacket());

        Assert.Equal((19, 0.25f, 0.75f, 0.5f, 0.125f, 11.5f, 28L, 34L),
            (decoded.m_entityId, decoded.Food, decoded.Stamina, decoded.Sleep,
                decoded.Wetness, decoded.Temperature,
                decoded.m_lifecycleRevision, decoded.m_revision));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void FurnacePacketPreservesInventoryLifecycleAndIndependentRevision() {
        ComponentFurnacePacket source = new();
        source.furnaceDataList.Add(new ComponentFurnacePacket.FurnacePacketData {
            inventoryID = 91,
            entityID = 73,
            fireTimeRemaining = 12.5f,
            smeltingProgress = 0.625f,
            heatLevel = 1f,
            lifecycleRevision = 17,
            revision = 4_294_967_721L
        });

        ComponentFurnacePacket decoded = RoundTrip(
            source,
            static () => new ComponentFurnacePacket());
        ComponentFurnacePacket.FurnacePacketData state = Assert.Single(decoded.furnaceDataList);

        Assert.Equal(
            (91, 73, 12.5f, 0.625f, 1f, 17L, 4_294_967_721L),
            (state.inventoryID, state.entityID, state.fireTimeRemaining,
                state.smeltingProgress, state.heatLevel,
                state.lifecycleRevision, state.revision));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void FurnacePacketRejectsOversizedStateCount() {
        byte[] payload;
        using (PacketStreamWriter writer = new()) {
            writer.Write(ComponentFurnacePacket.MaximumFurnaceCount + 1);
            payload = writer.Data();
        }
        using PacketStreamReader reader = new(payload);
        ComponentFurnacePacket packet = new();

        Assert.Throws<InvalidDataException>(() =>
            packet.Serialize(new PacketSerializer(reader)));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void FurnaceSavePersistsInProgressSmeltingState() {
        ComponentFurnace furnace = new() {
            m_fireTimeRemaining = 18.5f,
            m_heatLevel = 1f,
            m_smeltingProgress = 0.375f
        };
        ValuesDictionary values = new();

        furnace.Save(values, new EntityToIdMap([]));

        Assert.Equal(18.5f, values.GetValue<float>("FireTimeRemaining"));
        Assert.Equal(1f, values.GetValue<float>("HeatLevel"));
        Assert.Equal(0.375f, values.GetValue<float>("SmeltingProgress"));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ContainerOpenPacketsPreserveRequestIdentityAndTypedResult() {
        OpenInventoryByIDPacket byId = RoundTrip(
            new OpenInventoryByIDPacket {
                m_inventoryId = 51,
                m_requestId = 4_294_967_801L
            },
            static () => new OpenInventoryByIDPacket());
        OpenInventoryByPointPacket byPoint = RoundTrip(
            new OpenInventoryByPointPacket {
                m_point3 = new Engine.Point3(4, 70, -8),
                m_requestId = 4_294_967_802L
            },
            static () => new OpenInventoryByPointPacket());
        OpenInventoryResultPacket result = RoundTrip(
            new OpenInventoryResultPacket {
                m_requestId = 4_294_967_802L,
                m_requestKind = NetworkContainerOpenRequestKind.ByPoint,
                m_result = NetworkContainerOpenResultCode.PermissionDenied,
                m_inventoryId = 51
            },
            static () => new OpenInventoryResultPacket());

        Assert.Equal((51, 4_294_967_801L), (byId.m_inventoryId, byId.m_requestId));
        Assert.Equal((new Engine.Point3(4, 70, -8), 4_294_967_802L),
            (byPoint.m_point3, byPoint.m_requestId));
        Assert.Equal(
            (4_294_967_802L, NetworkContainerOpenRequestKind.ByPoint,
                NetworkContainerOpenResultCode.PermissionDenied, 51),
            (result.m_requestId, result.m_requestKind, result.m_result, result.m_inventoryId));
    }

    private static TPacket RoundTrip<TPacket>(
        TPacket source,
        Func<TPacket> create)
        where TPacket : Packet {
        byte[] payload;
        using (PacketStreamWriter writer = new()) {
            source.Serialize(new PacketSerializer(writer));
            payload = writer.Data();
        }

        TPacket decoded = create();
        using PacketStreamReader reader = new(payload);
        decoded.Serialize(new PacketSerializer(reader));
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        return decoded;
    }
}
