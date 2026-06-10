using System;
using System.Collections.Generic;

namespace PhantomVault.Core.Services
{

    public interface IUsbDetector : IDisposable
    {

        event Action<string>? RemovableDriveInserted;

        event Action<string>? RemovableDriveRemoved;

        IEnumerable<string> GetRemovableDrives();

        bool IsValidRemovableDrive(string path);

        RemovableDriveInfo? GetDriveInfo(string path);
    }
}

