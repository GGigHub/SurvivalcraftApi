namespace Survivalcraft.Testing.Scenarios;

public sealed class ScenarioRunner {
    private readonly List<ScenarioStepRecord> steps = [];

    public ScenarioRunner(TimeSpan stepTimeout) {
        if (stepTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(stepTimeout));
        }
        StepTimeout = stepTimeout;
    }

    public TimeSpan StepTimeout { get; }
    public IReadOnlyList<ScenarioStepRecord> Steps => steps.ToArray();

    public async Task<T> RunAsync<T>(
        string name,
        string command,
        Func<CancellationToken, Task<T>> action,
        Func<T, string?>? summarize = null,
        CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(action);
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        using CancellationTokenSource stepSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stepSource.CancelAfter(StepTimeout);
        try {
            T result = await action(stepSource.Token)
                .WaitAsync(stepSource.Token)
                .ConfigureAwait(false);
            steps.Add(new ScenarioStepRecord(
                name,
                "passed",
                startedAt,
                DateTimeOffset.UtcNow,
                summarize?.Invoke(result)));
            return result;
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested && stepSource.IsCancellationRequested) {
            TimeoutException timeout = new(
                $"Scenario step '{name}' exceeded its timeout of {StepTimeout}.",
                exception);
            steps.Add(new ScenarioStepRecord(
                name,
                "failed",
                startedAt,
                DateTimeOffset.UtcNow,
                command,
                timeout.ToString()));
            throw timeout;
        }
        catch (Exception exception) {
            steps.Add(new ScenarioStepRecord(
                name,
                "failed",
                startedAt,
                DateTimeOffset.UtcNow,
                command,
                exception.ToString()));
            throw;
        }
    }

    public async Task RunAsync(
        string name,
        string command,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default) {
        await RunAsync(
            name,
            command,
            async token => {
                await action(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
