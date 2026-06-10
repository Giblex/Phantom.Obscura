namespace PhantomVault.Core.Services.Security
{

    public enum DefenceActionType
    {

        AddDelay,

        TempLockout,

        RequirePhantomKey,

        SwitchToDecoyVault,

        EnterReadOnlyMode,

        ScrubShortLivedData
    }
}

