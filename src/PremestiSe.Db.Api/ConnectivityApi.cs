using PremestiSe.Db.Contracts;
using PremestiSe.Db.Model;
using VeloxDB.ObjectInterface;
using VeloxDB.Protocol;

namespace PremestiSe.Db.Api;

[DbAPI(Name = "PremestiSe.Connectivity")]
public sealed class ConnectivityApi
{
    [DbAPIOperation(OperationType = DbAPIOperationType.Read)]
    public ConnectivityResult Check(ObjectModel model) => new()
    {
        ProbeCount = model.GetAllObjects<ConnectivityProbe>().LongCount()
    };
}
