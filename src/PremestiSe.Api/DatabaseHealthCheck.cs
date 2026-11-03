using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PremestiSe.Db.Contracts;
using VeloxDB.Client;

namespace PremestiSe.Api;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly Func<Task> execute;
    private readonly TimeSpan timeout;

    public DatabaseHealthCheck(IOptions<DatabaseOptions> options)
    {
        timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
        var connection = new ConnectionStringParams
        {
            OpenTimeout = (int)timeout.TotalMilliseconds,
            RetryTimeout = (int)timeout.TotalMilliseconds,
            PoolSize = 1
        };
        connection.AddAddress(options.Value.Address);
        var database = ConnectionFactory.Get<IConnectivityApi>(connection.GenerateConnectionString());
        execute = async () => _ = await database.Check();
    }

    internal DatabaseHealthCheck(Func<Task> execute, TimeSpan timeout)
    {
        this.execute = execute;
        this.timeout = timeout;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // VeloxDB's awaitable has no cancellation API. Bound the HTTP wait as well
            // as its connection/retry budgets; observe any later protocol failure.
            var operation = execute();
            await operation.WaitAsync(timeout, cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            // Do not expose protocol exceptions or connection configuration in health output.
            return HealthCheckResult.Unhealthy("Database unavailable.");
        }
    }
}
