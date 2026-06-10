namespace PhantomVault.Core.Services
{

    public sealed class RemovableDriveInfo
    {

        public string Label { get; set; } = string.Empty;

        public long TotalSizeBytes { get; set; }

        public long AvailableFreeSpaceBytes { get; set; }

        public string FileSystem { get; set; } = string.Empty;

        public string DeviceIdentifier { get; set; } = string.Empty;

        public string RootPath { get; set; } = string.Empty;
    }
}

