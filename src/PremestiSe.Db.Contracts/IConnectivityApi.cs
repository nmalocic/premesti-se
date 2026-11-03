using VeloxDB.Client;
using VeloxDB.Protocol;

namespace PremestiSe.Db.Contracts;

[DbAPI(Name = "PremestiSe.Connectivity")]
public interface IConnectivityApi
{
    [DbAPIOperation(OperationType = DbAPIOperationType.Read)]
    DatabaseTask<ConnectivityResult> Check();
}

public sealed class ConnectivityResult
{
    public long ProbeCount { get; set; }
}
