namespace Game.Network;

public enum ApplicationReliableFrameKind : byte {
    Data = 1,
    Acknowledgement = 2
}

public sealed class ApplicationReliablePacket : Packet {
    private ApplicationReliableFrameKind kind;
    private long deliveryId;
    private byte originalChannel;
    private NetworkState originalMinNeedState;
    private bool originalRequiresProject;
    private byte[] payload = [];

    public override byte ID => (byte)PacketType.ApplicationReliable;
    public override byte Channel => originalChannel;
    public override NetworkState MinNeedState =>
        kind == ApplicationReliableFrameKind.Data
            ? originalMinNeedState
            : NetworkState.NotConnected;
    public override bool RequiresProject =>
        kind == ApplicationReliableFrameKind.Data && originalRequiresProject;

    public ApplicationReliableFrameKind Kind => kind;
    public long DeliveryId => deliveryId;
    public byte OriginalChannel => originalChannel;
    public NetworkState OriginalMinNeedState => originalMinNeedState;
    public bool OriginalRequiresProject => originalRequiresProject;
    public byte[] Payload => payload;
    public int PayloadLength => payload.Length;

    public static ApplicationReliablePacket CreateData(
        long deliveryId,
        byte originalChannel,
        NetworkState originalMinNeedState,
        bool originalRequiresProject,
        byte[] payload) =>
        new ApplicationReliablePacket() {
            kind = ApplicationReliableFrameKind.Data,
            deliveryId = deliveryId,
            originalChannel = originalChannel,
            originalMinNeedState = originalMinNeedState,
            originalRequiresProject = originalRequiresProject,
            payload = payload?.ToArray() ?? []
        }.Validate();

    public static ApplicationReliablePacket CreateAcknowledgement(
        long deliveryId,
        byte originalChannel) =>
        new ApplicationReliablePacket() {
            kind = ApplicationReliableFrameKind.Acknowledgement,
            deliveryId = deliveryId,
            originalChannel = originalChannel,
            originalMinNeedState = NetworkState.NotConnected,
            originalRequiresProject = false,
            payload = []
        }.Validate();

    public override void Serialize(PacketSerializer archive) {
        if (archive.IsWriting) {
            Validate();
        }
        archive.Enum(ref kind);
        archive.Value(ref deliveryId);
        archive.Value(ref originalChannel);
        archive.Enum(ref originalMinNeedState);
        archive.Value(ref originalRequiresProject);
        archive.Buffer(ref payload, PacketStreamHelper.MaximumDecompressedLength);
        if (archive.IsReading) {
            Validate();
        }
    }

    public override void Handle(bool isServer) =>
        ApplicationReliabilityRuntime.Handle(this, isServer);

    private ApplicationReliablePacket Validate() {
        if (!Enum.IsDefined(kind)) {
            throw new InvalidDataException(
                $"Unknown application-reliable frame kind '{kind}'.");
        }
        if (deliveryId <= 0) {
            throw new InvalidDataException(
                "Application-reliable delivery ID must be positive.");
        }
        if (originalChannel >= NetworkChannels.Count) {
            throw new InvalidDataException(
                $"Application-reliable channel {originalChannel} is invalid.");
        }
        if (!Enum.IsDefined(originalMinNeedState)) {
            throw new InvalidDataException(
                $"Application-reliable state '{originalMinNeedState}' is invalid.");
        }
        if (kind == ApplicationReliableFrameKind.Data && payload.Length == 0) {
            throw new InvalidDataException(
                "Application-reliable data frame must contain one packet.");
        }
        if (kind == ApplicationReliableFrameKind.Acknowledgement
            && (payload.Length != 0
                || originalMinNeedState != NetworkState.NotConnected
                || originalRequiresProject)) {
            throw new InvalidDataException(
                "Application-reliable acknowledgement has invalid payload metadata.");
        }
        return this;
    }
}
