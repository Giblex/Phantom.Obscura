namespace PhantomVault.Core.Services.Security
{

    public interface IDefenceEngine
    {

        void RaiseThreat(ThreatEvent threat);
    }
}

