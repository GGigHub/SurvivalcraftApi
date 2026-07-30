namespace Survivalcraft.Testing.Networking;

public sealed record NetworkFaultProfile(
    int Seed = 1,
    int BaseLatencyMilliseconds = 0,
    int JitterMilliseconds = 0,
    double LossRate = 0,
    double DuplicationRate = 0,
    double ReorderRate = 0,
    int ReorderDelayMilliseconds = 0,
    int BandwidthBytesPerSecond = 0,
    string[]? TargetPacketTypes = null) {

    public NetworkFaultProfile Validate() {
        if (BaseLatencyMilliseconds < 0) {
            throw new ArgumentOutOfRangeException(nameof(BaseLatencyMilliseconds));
        }
        if (JitterMilliseconds < 0) {
            throw new ArgumentOutOfRangeException(nameof(JitterMilliseconds));
        }
        if (ReorderDelayMilliseconds < 0) {
            throw new ArgumentOutOfRangeException(nameof(ReorderDelayMilliseconds));
        }
        if (BandwidthBytesPerSecond < 0) {
            throw new ArgumentOutOfRangeException(nameof(BandwidthBytesPerSecond));
        }
        ValidateProbability(LossRate, nameof(LossRate));
        ValidateProbability(DuplicationRate, nameof(DuplicationRate));
        ValidateProbability(ReorderRate, nameof(ReorderRate));
        if (TargetPacketTypes is { Length: > 64 }) {
            throw new ArgumentOutOfRangeException(nameof(TargetPacketTypes));
        }
        if (TargetPacketTypes != null) {
            HashSet<string> packetTypes = new(StringComparer.Ordinal);
            foreach (string packetType in TargetPacketTypes) {
                if (string.IsNullOrWhiteSpace(packetType) || packetType.Length > 256) {
                    throw new ArgumentException(
                        "Target packet types must contain 1 to 256 non-whitespace characters.",
                        nameof(TargetPacketTypes));
                }
                if (!packetTypes.Add(packetType)) {
                    throw new ArgumentException(
                        $"Target packet type '{packetType}' is duplicated.",
                        nameof(TargetPacketTypes));
                }
            }
        }
        return this;
    }

    private static void ValidateProbability(double value, string parameterName) {
        if (double.IsNaN(value) || value < 0 || value > 1) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Probability must be between 0 and 1.");
        }
    }
}
