namespace PremestiSe.Api;

public sealed class DatabaseOptions
{
    public string Address { get; set; } = "localhost:7568";
    public int TimeoutSeconds { get; set; } = 3;
}
