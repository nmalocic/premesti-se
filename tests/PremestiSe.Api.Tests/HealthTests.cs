using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using PremestiSe.Db.Contracts;
using VeloxDB.Client;
using Xunit;

namespace PremestiSe.Api.Tests;

// These tests require the actual Compose stack; no database is mocked.
public sealed class HealthTests
{
    private static bool ExpectHealthy => Environment.GetEnvironmentVariable("EXPECT_DATABASE_HEALTHY") != "false";
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri(Environment.GetEnvironmentVariable("API_URL") ?? "http://localhost:8080"),
        Timeout = TimeSpan.FromSeconds(10)
    };

    [Fact]
    public async Task Health_reports_real_database_state_within_timeout()
    {
        var watch = Stopwatch.StartNew();
        using var response = await Http.GetAsync("/api/health");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "Health exceeded the default 3s budget plus HTTP overhead.");
        Assert.Equal(ExpectHealthy ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var status = ExpectHealthy ? "Healthy" : "Unhealthy";
        Assert.Equal(status, json.RootElement.GetProperty("status").GetString());
        Assert.Equal(status, json.RootElement.GetProperty("checks").GetProperty("database").GetProperty("status").GetString());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Deployed_operation_queries_the_model_over_the_client_protocol()
    {
        var parameters = new ConnectionStringParams { OpenTimeout = 1000, RetryTimeout = 1000 };
        parameters.AddAddress(Environment.GetEnvironmentVariable("DATABASE_ADDRESS") ?? "localhost:7568");
        var database = ConnectionFactory.Get<IConnectivityApi>(parameters.GenerateConnectionString());
        async Task<ConnectivityResult> Check() => await database.Check();
        if (ExpectHealthy)
            Assert.True((await Check()).ProbeCount >= 0);
        else
            await Assert.ThrowsAnyAsync<Exception>(() => Check().WaitAsync(TimeSpan.FromSeconds(4)));
    }

    [Theory]
    [InlineData("/api/unknown")]
    [InlineData("/api/unknown.json")]
    [InlineData("/api")]
    public async Task Unknown_api_routes_do_not_return_the_spa(string path)
    {
        using var response = await Http.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Spa_is_served_for_browser_navigation()
    {
        using var response = await Http.GetAsync("/starter");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Development_smtp_and_mailpit_ui_are_available()
    {
        var host = Environment.GetEnvironmentVariable("SMTP_HOST") ?? "localhost";
        using var smtp = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await smtp.ConnectAsync(host, 1025, timeout.Token);
        using var reader = new StreamReader(smtp.GetStream());
        Assert.StartsWith("220 ", await reader.ReadLineAsync(timeout.Token));
        using var response = await Http.GetAsync($"http://{host}:8025/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
