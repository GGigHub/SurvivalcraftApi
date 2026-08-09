using Engine;
using Game.Network;
using TemplatesDatabase;
using Xunit;

namespace Survivalcraft.Test;

public sealed class SurvivalStatePacketCodecTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void CreatureBehaviorPacketsPreserveLifecycleAndStateRevision() {
        ChaseBehaviorPacket chase = RoundTrip(
            new ChaseBehaviorPacket {
                m_entityId = 41,
                m_isAttack = true,
                m_lifecycleRevision = 7,
                m_revision = 19
            },
            static () => new ChaseBehaviorPacket());
        StubbornSteedBehaviorPacket stubborn = RoundTrip(
            new StubbornSteedBehaviorPacket {
                m_entityId = 42,
                m_headShakeOrder = 0.75f,
                m_lifecycleRevision = 8,
                m_revision = 20
            },
            static () => new StubbornSteedBehaviorPacket());
        HumanRowBehaviorPacket row = RoundTrip(
            new HumanRowBehaviorPacket {
                m_entityId = 43,
                m_rowLeft = true,
                m_rowRight = false,
                m_lifecycleRevision = 9,
                m_revision = 21
            },
            static () => new HumanRowBehaviorPacket());

        Assert.Equal((41, true, 7L, 19L),
            (chase.m_entityId, chase.m_isAttack,
                chase.m_lifecycleRevision, chase.m_revision));
        Assert.Equal((42, 0.75f, 8L, 20L),
            (stubborn.m_entityId, stubborn.m_headShakeOrder,
                stubborn.m_lifecycleRevision, stubborn.m_revision));
        Assert.Equal((43, true, false, 9L, 21L),
            (row.m_entityId, row.m_rowLeft, row.m_rowRight,
                row.m_lifecycleRevision, row.m_revision));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ClothingPacketPreservesBoundedSlotsLifecycleAndRevision() {
        ClothingSyncPacket decoded = RoundTrip(
            new ClothingSyncPacket {
                m_inventoryId = 31,
                m_entityId = 17,
                m_componentTypeName = "Game.ComponentClothing",
                m_lifecycleRevision = 12,
                m_revision = 55,
                m_slots = [
                    new ClothingSlotSync { slotId = 0, clothes = [101, 102] },
                    new ClothingSlotSync { slotId = 1, clothes = [201] }
                ]
            },
            static () => new ClothingSyncPacket());

        Assert.Equal((31, 17, "Game.ComponentClothing", 12L, 55L),
            (decoded.m_inventoryId, decoded.m_entityId,
                decoded.m_componentTypeName, decoded.m_lifecycleRevision,
                decoded.m_revision));
        Assert.Equal(
            new[] { (0, "101,102"), (1, "201") },
            decoded.m_slots.Select(slot =>
                (slot.slotId, string.Join(',', slot.clothes))));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ClothingPacketRejectsOversizedSlotAndLayerCounts() {
        ClothingSyncPacket tooManySlots = new() {
            m_slots = Enumerable.Range(0, ClothingSyncPacket.MaximumSlotCount + 1)
                .Select(index => new ClothingSlotSync { slotId = index })
                .ToList()
        };
        ClothingSyncPacket tooManyLayers = new() {
            m_slots = [
                new ClothingSlotSync {
                    slotId = 0,
                    clothes = Enumerable.Repeat(
                        1,
                        ClothingSyncPacket.MaximumClothesPerSlot + 1).ToList()
                }
            ]
        };

        Assert.Throws<InvalidDataException>(() => Serialize(tooManySlots));
        Assert.Throws<InvalidDataException>(() => Serialize(tooManyLayers));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void StatusEffectPacketsPreserveEventLifecycleAndRevision() {
        ComponentFluPacket flu = RoundTrip(
            new ComponentFluPacket {
                entityId = 81,
                m_eventType = ComponentFluPacket.EventType.Impulse,
                m_fluOnset = 12f,
                m_fluDuration = 800f,
                m_coughDuration = 3f,
                m_sneezeDuration = 0.5f,
                m_Impulse = new Vector3(1f, 2f, 3f),
                m_lifecycleRevision = 15,
                m_revision = 99
            },
            static () => new ComponentFluPacket());
        ComponentSicknessPacket sickness = RoundTrip(
            new ComponentSicknessPacket {
                entityId = 82,
                m_eventType = ComponentSicknessPacket.EventType.NauseaEffect,
                m_sicknessDuration = 700f,
                m_lifecycleRevision = 16,
                m_revision = 100
            },
            static () => new ComponentSicknessPacket());

        Assert.Equal((81, ComponentFluPacket.EventType.Impulse, 12f, 800f,
                3f, 0.5f, new Vector3(1f, 2f, 3f), 15L, 99L),
            (flu.entityId, flu.m_eventType, flu.m_fluOnset,
                flu.m_fluDuration, flu.m_coughDuration, flu.m_sneezeDuration,
                flu.m_Impulse, flu.m_lifecycleRevision, flu.m_revision));
        Assert.Equal((82, ComponentSicknessPacket.EventType.NauseaEffect,
                700f, 16L, 100L),
            (sickness.entityId, sickness.m_eventType,
                sickness.m_sicknessDuration, sickness.m_lifecycleRevision,
                sickness.m_revision));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void ProgressionPacketsPreserveIdempotencyIdentityAndBoundStats() {
        PlayerAddExperiencePacket experience = RoundTrip(
            new PlayerAddExperiencePacket {
                m_fromPlayerIndex = 2,
                m_count = 4,
                m_playSound = true,
                m_level = 3.25f,
                m_entityId = 91,
                m_lifecycleRevision = 17,
                m_revision = 101
            },
            static () => new PlayerAddExperiencePacket());
        ValuesDictionary values = new();
        values.SetValue("TimesHadFlu", 3L);
        PlayerStatSyncPacket stats = RoundTrip(
            new PlayerStatSyncPacket {
                m_fromPlayerIndex = 2,
                m_stat = values,
                m_entityId = 91,
                m_lifecycleRevision = 17,
                m_revision = 102
            },
            static () => new PlayerStatSyncPacket());

        Assert.Equal((2, 4, true, 3.25f, 91, 17L, 101L),
            (experience.m_fromPlayerIndex, experience.m_count,
                experience.m_playSound, experience.m_level,
                experience.m_entityId, experience.m_lifecycleRevision,
                experience.m_revision));
        Assert.Equal((2, 91, 17L, 102L),
            (stats.m_fromPlayerIndex, stats.m_entityId,
                stats.m_lifecycleRevision, stats.m_revision));
        Assert.Equal(3L, stats.m_stat.GetValue<long>("TimesHadFlu"));
    }

    [Theory]
    [Trait("Layer", "L0")]
    [InlineData("../escape.scskin")]
    [InlineData("folder/skin.scskin")]
    [InlineData("folder\\skin.scskin")]
    [InlineData("$Male1")]
    [InlineData("skin.png")]
    [InlineData(" skin.scskin")]
    public void SkinTransferRejectsUnsafeNames(string name) {
        Assert.False(SkinPacketBase.TryValidateSkinName(name, out _));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void SkinTransferAcceptsSafeNameAndRejectsOversizedPayload() {
        Assert.True(SkinPacketBase.TryValidateSkinName(
            "learner.scskin",
            out string normalized));
        Assert.Equal("learner.scskin", normalized);
        ReplySkinPacket packet = new() {
            SkinName = normalized,
            m_skinData = new byte[SkinPacketBase.MaximumSkinDataBytes + 1]
        };

        Assert.Throws<InvalidOperationException>(() => Serialize(packet));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void SkinTransferRejectsOversizedWireNameBeforeReadingItsBytes() {
        using PacketStreamWriter writer = new();
        writer.Write(new string('a', SkinPacketBase.MaximumSkinNameUtf8Bytes + 1));
        byte[] payload = writer.Data();
        using PacketStreamReader reader = new(payload);
        RequestSkinPacket packet = new();

        Assert.Throws<InvalidOperationException>(() =>
            packet.Serialize(new PacketSerializer(reader)));
        Assert.True(reader.BaseStream.Position < reader.BaseStream.Length);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void EntityResyncRequestRejectsOversizedEntityList() {
        EntitySyncRequestPacket packet = new(
            Enumerable.Range(1, EntitySyncRequestPacket.MaximumEntityCount + 1).ToList());

        Assert.Throws<InvalidDataException>(() => Serialize(packet));
    }

    private static byte[] Serialize(Packet packet) {
        using PacketStreamWriter writer = new();
        packet.Serialize(new PacketSerializer(writer));
        return writer.Data();
    }

    private static TPacket RoundTrip<TPacket>(
        TPacket source,
        Func<TPacket> create)
        where TPacket : Packet {
        byte[] payload = Serialize(source);
        TPacket decoded = create();
        using PacketStreamReader reader = new(payload);
        decoded.Serialize(new PacketSerializer(reader));
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        return decoded;
    }
}
