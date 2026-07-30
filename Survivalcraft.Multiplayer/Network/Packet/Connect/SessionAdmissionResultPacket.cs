using System;
using Engine;

namespace Game.Network;

public sealed class SessionAdmissionResultPacket : Packet {
    public override byte ID => (byte)PacketType.SessionAdmissionResult;

    public NetworkSessionAdmissionResult Result = new();

    public SessionAdmissionResultPacket() {
    }

    public SessionAdmissionResultPacket(NetworkSessionAdmissionResult result) {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public override void Serialize(PacketSerializer serializer) {
        byte[] payload = serializer.IsWriting
            ? NetworkSessionAdmissionWireCodec.Encode(Result)
            : null;
        serializer.Buffer(ref payload, NetworkSessionAdmissionWireCodec.MaximumEncodedBytes);
        if (serializer.IsReading) {
            Result = NetworkSessionAdmissionWireCodec.Decode(payload);
        }
    }

    public override void Handle(bool isServer) {
        ClientSession session = NetworkManager.MySession;
        if (isServer
            || session == null
            || session.CompatibilityState != NetworkCompatibilityState.Accepted
            || session.AdmissionState != NetworkSessionAdmissionState.AwaitingRequest) {
            return;
        }
        NetworkSessionAdmissionRuntime.RecordClientResult(session, Result);
        if (Result.Accepted) {
            Log.Information("Server accepted multiplayer session identity.");
        }
        else {
            Log.Warning($"Server rejected multiplayer session identity: {Result.Summary}");
        }
    }
}
