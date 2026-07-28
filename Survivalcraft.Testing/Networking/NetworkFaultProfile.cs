namespace Survivalcraft.Testing.Networking;

public sealed record NetworkFaultProfile(
    int Seed = 1,
    int BaseLatencyMilliseconds = 0,
    int JitterMilliseconds = 0,
    double LossRate = 0,
    double DuplicationRate = 0,
    double ReorderRate = 0,
    int ReorderDelayMilliseconds = 0,
    int BandwidthBytesPerSecond = 0) {

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
