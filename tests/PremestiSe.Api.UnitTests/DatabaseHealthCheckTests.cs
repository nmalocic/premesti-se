using Microsoft.Extensions.Diagnostics.HealthChecks;
using PremestiSe.Api;
using Xunit;

namespace PremestiSe.Api.UnitTests;

public sealed class DatabaseHealthCheckTests
{
    [Fact]
    public async Task Successful_operation_reports_healthy()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = new DatabaseHealthCheck(() => completion.Task, TimeSpan.FromSeconds(5));

        var resultTask = check.CheckHealthAsync(new HealthCheckContext());
        Assert.False(resultTask.IsCompleted);
        completion.SetResult();

        var result = await resultTask;
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Null(result.Exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_operation_reports_sanitized_failure(bool synchronous)
    {
        var exception = new InvalidOperationException("Secret connection details");
        var check = new DatabaseHealthCheck(
            () => synchronous ? throw exception : Task.FromException(exception),
            TimeSpan.FromSeconds(5));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        AssertUnavailable(result);
    }

    [Fact]
    public async Task Pending_operation_is_bounded_by_timeout()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = new DatabaseHealthCheck(() => completion.Task, TimeSpan.FromMilliseconds(20));

        var result = await check.CheckHealthAsync(new HealthCheckContext()).WaitAsync(TimeSpan.FromSeconds(5));

        AssertUnavailable(result);
        Assert.False(completion.Task.IsCompleted);
        // A late protocol failure must remain separate from the sanitized health result.
        completion.SetException(new InvalidOperationException("Late protocol failure"));
    }

    [Fact]
    public async Task Cancellation_stops_waiting_for_pending_operation()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var check = new DatabaseHealthCheck(() => completion.Task, TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();

        var resultTask = check.CheckHealthAsync(new HealthCheckContext(), cancellation.Token);
        cancellation.Cancel();
        var result = await resultTask.WaitAsync(TimeSpan.FromSeconds(5));

        AssertUnavailable(result);
        Assert.False(completion.Task.IsCompleted);
        completion.SetResult();
    }

    private static void AssertUnavailable(HealthCheckResult result)
    {
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Database unavailable.", result.Description);
        Assert.Null(result.Exception);
        Assert.Empty(result.Data);
    }
}
