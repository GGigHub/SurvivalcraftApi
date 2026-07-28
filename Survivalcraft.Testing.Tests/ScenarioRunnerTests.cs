using Survivalcraft.Testing.Scenarios;
using Xunit;

namespace Survivalcraft.Testing.Tests;

public sealed class ScenarioRunnerTests {
    [Fact]
    [Trait("Layer", "L0")]
    public async Task RunnerRecordsTypedStepSuccessAndFailure() {
        ScenarioRunner runner = new(TimeSpan.FromSeconds(2));
        int value = await runner.RunAsync(
            "success",
            "test.success",
            _ => Task.FromResult(42),
            result => result.ToString());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(
                "failure",
                "test.failure",
                _ => Task.FromException(new InvalidOperationException("expected"))));

        Assert.Equal(42, value);
        Assert.Collection(
            runner.Steps,
            step => {
                Assert.Equal("passed", step.Status);
                Assert.Equal("42", step.Summary);
            },
            step => {
                Assert.Equal("failed", step.Status);
                Assert.Contains("expected", step.Error, StringComparison.Ordinal);
            });
    }

    [Fact]
    [Trait("Layer", "L0")]
    public async Task RunnerCancelsWorkWhenStepDeadlineExpires() {
        ScenarioRunner runner = new(TimeSpan.FromMilliseconds(20));

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            runner.RunAsync(
                "timeout",
                "test.timeout",
                token => Task.Delay(TimeSpan.FromMinutes(1), token)));

        Assert.Contains("timeout", exception.Message, StringComparison.OrdinalIgnoreCase);
        ScenarioStepRecord step = Assert.Single(runner.Steps);
        Assert.Equal("failed", step.Status);
    }
}
