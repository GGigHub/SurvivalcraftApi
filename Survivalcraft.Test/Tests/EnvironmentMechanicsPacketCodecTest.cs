using Engine;
using Game.Network;
using Xunit;

namespace Survivalcraft.Test;

public sealed class EnvironmentMechanicsPacketCodecTest {
    [Fact]
    [Trait("Layer", "L0")]
    public void EnvironmentPacketsPreserveAuthorityKindRevisionAndRequestIdentity() {
        SubsystemTimePacket timeState = RoundTrip(
            new SubsystemTimePacket {
                m_kind = SubsystemTimePacket.PacketKind.State,
                m_time = 123.456,
                m_timeOfDayOffset = 0.375,
                m_timeOfYear = 0.625f,
                m_revision = 4_294_967_501L,
                m_requestId = 0
            },
            static () => new SubsystemTimePacket());
        SubsystemTimePacket timeRequest = RoundTrip(
            new SubsystemTimePacket {
                m_kind = SubsystemTimePacket.PacketKind.Request,
                m_timeOfDayOffset = 0.875,
                m_requestId = 4_294_967_502L
            },
            static () => new SubsystemTimePacket());
        SubsystemWeatherPacket weather = RoundTrip(
            new SubsystemWeatherPacket {
                m_kind = SubsystemWeatherPacket.PacketKind.State,
                m_hasPrecipitationSchedule = true,
                m_hasFogSchedule = true,
                m_precipitationStartTime = 10,
                m_precipitationEndTime = double.PositiveInfinity,
                m_precipitationRampTime = 1.5f,
                m_lightningIntensity = 0.75f,
                m_fogStartTime = 20,
                m_fogEndTime = 80,
                m_fogRampTime = 3f,
                FogProgress = 0.25f,
                m_revision = 4_294_967_503L
            },
            static () => new SubsystemWeatherPacket());
        SubsystemSkyPacket lightningRequest = RoundTrip(
            new SubsystemSkyPacket {
                m_kind = SubsystemSkyPacket.PacketKind.Request,
                LightningStrikePosition = new Vector3(1.25f, 64.5f, -2.75f),
                m_direction = Vector3.Normalize(new Vector3(1f, -0.25f, 2f)),
                m_requestId = 4_294_967_504L
            },
            static () => new SubsystemSkyPacket());
        SubsystemSkyPacket lightningState = RoundTrip(
            new SubsystemSkyPacket {
                m_kind = SubsystemSkyPacket.PacketKind.State,
                LightningStrikePosition = new Vector3(-4.5f, 70.25f, 8.75f),
                m_revision = 4_294_967_505L
            },
            static () => new SubsystemSkyPacket());

        Assert.Equal(SubsystemTimePacket.PacketKind.State, timeState.m_kind);
        Assert.Equal(123.456, timeState.m_time);
        Assert.Equal(0.375, timeState.m_timeOfDayOffset);
        Assert.Equal(0.625f, timeState.m_timeOfYear);
        Assert.Equal(4_294_967_501L, timeState.m_revision);
        Assert.Equal(SubsystemTimePacket.PacketKind.Request, timeRequest.m_kind);
        Assert.Equal(0.875, timeRequest.m_timeOfDayOffset);
        Assert.Equal(4_294_967_502L, timeRequest.m_requestId);
        Assert.Equal(SubsystemWeatherPacket.PacketKind.State, weather.m_kind);
        Assert.True(weather.m_hasPrecipitationSchedule);
        Assert.True(weather.m_hasFogSchedule);
        Assert.True(double.IsPositiveInfinity(weather.m_precipitationEndTime));
        Assert.Equal((1.5f, 0.75f, 3f, 0.25f, 4_294_967_503L),
            (weather.m_precipitationRampTime, weather.m_lightningIntensity,
                weather.m_fogRampTime, weather.FogProgress, weather.m_revision));
        Assert.Equal(SubsystemSkyPacket.PacketKind.Request, lightningRequest.m_kind);
        Assert.Equal(new Vector3(1.25f, 64.5f, -2.75f),
            lightningRequest.LightningStrikePosition);
        Assert.Equal(4_294_967_504L, lightningRequest.m_requestId);
        Assert.Equal(SubsystemSkyPacket.PacketKind.State, lightningState.m_kind);
        Assert.Equal(new Vector3(-4.5f, 70.25f, 8.75f),
            lightningState.LightningStrikePosition);
        Assert.Equal(4_294_967_505L, lightningState.m_revision);
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void SleepWakeAndFirePacketsPreserveLifecycleStateAndRequestIdentity() {
        SleepRequestPacket sleepRequest = RoundTrip(
            new SleepRequestPacket {
                m_entityId = 41,
                allowManualWakeup = true,
                m_requestId = 4_294_967_601L
            },
            static () => new SleepRequestPacket());
        SleepStatusPacket sleepStatus = RoundTrip(
            new SleepStatusPacket {
                m_entityId = 41,
                allowManualWakeup = false,
                m_result = true,
                m_isSleeping = true,
                reason = "accepted",
                m_lifecycleRevision = 7,
                m_revision = 11,
                m_requestId = 4_294_967_601L
            },
            static () => new SleepStatusPacket());
        WakeupRequestPacket wakeRequest = RoundTrip(
            new WakeupRequestPacket {
                m_entityId = 41,
                m_requestId = 4_294_967_602L
            },
            static () => new WakeupRequestPacket());
        WakeupStatusPacket wakeStatus = RoundTrip(
            new WakeupStatusPacket {
                m_entityId = 41,
                m_lifecycleRevision = 7,
                m_revision = 12,
                m_requestId = 4_294_967_602L
            },
            static () => new WakeupStatusPacket());
        EntityOnFirePacket fire = RoundTrip(
            new EntityOnFirePacket {
                m_entityId = 41,
                m_attackerEntityId = 52,
                m_isOnFire = true,
                m_duration = 120f,
                m_lifecycleRevision = 7,
                m_revision = 13
            },
            static () => new EntityOnFirePacket());
        EntityOnFirePacket extinguished = RoundTrip(
            new EntityOnFirePacket {
                m_entityId = 41,
                m_isOnFire = false,
                m_duration = 0f,
                m_lifecycleRevision = 7,
                m_revision = 14
            },
            static () => new EntityOnFirePacket());

        Assert.Equal((41, true, 4_294_967_601L),
            (sleepRequest.m_entityId, sleepRequest.allowManualWakeup,
                sleepRequest.m_requestId));
        Assert.Equal((41, false, true, true, "accepted", 7L, 11L, 4_294_967_601L),
            (sleepStatus.m_entityId, sleepStatus.allowManualWakeup,
                sleepStatus.m_result, sleepStatus.m_isSleeping, sleepStatus.reason,
                sleepStatus.m_lifecycleRevision, sleepStatus.m_revision,
                sleepStatus.m_requestId));
        Assert.Equal((41, 4_294_967_602L),
            (wakeRequest.m_entityId, wakeRequest.m_requestId));
        Assert.Equal((41, 7L, 12L, 4_294_967_602L),
            (wakeStatus.m_entityId, wakeStatus.m_lifecycleRevision,
                wakeStatus.m_revision, wakeStatus.m_requestId));
        Assert.Equal((41, 52, true, 120f, 7L, 13L),
            (fire.m_entityId, fire.m_attackerEntityId, fire.m_isOnFire,
                fire.m_duration, fire.m_lifecycleRevision, fire.m_revision));
        Assert.Equal((41, false, 0f, 7L, 14L),
            (extinguished.m_entityId, extinguished.m_isOnFire,
                extinguished.m_duration, extinguished.m_lifecycleRevision,
                extinguished.m_revision));
    }

    [Fact]
    [Trait("Layer", "L0")]
    public void MountPacketsPreserveOwnershipLifecycleRevisionAndRequestIdentity() {
        ComponentMountPacket request = RoundTrip(
            new ComponentMountPacket {
                m_type = ComponentMountPacket.EventType.MountRequest,
                m_result = ComponentMountPacket.ResultCode.None,
                m_riderEntityId = 41,
                m_mountEntityId = 52,
                m_riderLifecycleRevision = 7,
                m_mountLifecycleRevision = 9,
                m_revision = 11,
                m_requestId = 4_294_967_701L
            },
            static () => new ComponentMountPacket());
        ComponentMountPacket state = RoundTrip(
            new ComponentMountPacket {
                m_type = ComponentMountPacket.EventType.State,
                m_result = ComponentMountPacket.ResultCode.OwnershipMismatch,
                m_riderEntityId = 41,
                m_mountEntityId = 0,
                m_riderLifecycleRevision = 7,
                m_mountLifecycleRevision = -1,
                m_revision = 12,
                m_requestId = 4_294_967_701L
            },
            static () => new ComponentMountPacket());

        Assert.Equal((
                ComponentMountPacket.EventType.MountRequest,
                ComponentMountPacket.ResultCode.None,
                41,
                52,
                7L,
                9L,
                11L,
                4_294_967_701L),
            (request.m_type, request.m_result, request.m_riderEntityId,
                request.m_mountEntityId, request.m_riderLifecycleRevision,
                request.m_mountLifecycleRevision, request.m_revision,
                request.m_requestId));
        Assert.Equal((
                ComponentMountPacket.EventType.State,
                ComponentMountPacket.ResultCode.OwnershipMismatch,
                41,
                0,
                7L,
                -1L,
                12L,
                4_294_967_701L),
            (state.m_type, state.m_result, state.m_riderEntityId,
                state.m_mountEntityId, state.m_riderLifecycleRevision,
                state.m_mountLifecycleRevision, state.m_revision,
                state.m_requestId));
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
