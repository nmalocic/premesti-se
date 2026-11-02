using VeloxDB.ObjectInterface;

namespace PremestiSe.Db.Model;

// Infrastructure-only model: no business data is introduced by the skeleton.
[DatabaseClass]
public abstract class ConnectivityProbe : DatabaseObject
{
    [DatabaseProperty]
    public abstract string Label { get; set; }
}
