using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PremestiSe.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection("Database"))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Address), "Database address is required.")
    .Validate(o => o.TimeoutSeconds is >= 1 and <= 30, "Database timeout must be 1–30 seconds.")
    .ValidateOnStart();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();
if (args.Contains("--check-database"))
{
    var report = await app.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync();
    Environment.ExitCode = report.Status == HealthStatus.Healthy ? 0 : 1;
    return;
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapHealthChecks("/api/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(entry => entry.Key,
                entry => new { status = entry.Value.Status.ToString() })
        });
    }
});
// More specific than SPA fallback, including extensionless unknown API paths.
app.Map("/api/{**path}", () => Results.Problem(statusCode: 404, title: "API route not found."));
app.MapFallbackToFile("index.html");
app.Run();
