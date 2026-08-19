using System.Text.Json.Serialization;

namespace PhantomVault.Core.Services.Privileged
{
    /// <summary>
    /// Named-pipe wire contract between the desktop UI (client) and the elevated
    /// <c>PhantomVault.PrivilegedBroker</c> service. Messages are newline-delimited,
    /// compact (non-indented) UTF-8 JSON: the client sends exactly one
    /// <see cref="BrokerRequest"/> line; the server replies with zero or more
    /// <see cref="BrokerMessageType.Progress"/> lines followed by a single
    /// <see cref="BrokerMessageType.Result"/> or <see cref="BrokerMessageType.Error"/> line.
    /// </summary>
    public static class BrokerProtocol
    {
        /// <summary>Fixed pipe name. The server ACL — not the name — is the security boundary.</summary>
        public const string PipeName = "PhantomObscura.PrivilegedBroker.v1";

        /// <summary>Windows service name used for install/uninstall/start.</summary>
        public const string ServiceName = "PhantomObscuraPrivilegedBroker";

        /// <summary>Human-friendly display name shown in services.msc.</summary>
        public const string ServiceDisplayName = "Phantom Obscura Privileged Helper";

        /// <summary>Protocol revision; bumped on breaking changes to fail closed.</summary>
        public const int ProtocolVersion = 2;
    }

    public enum BrokerOperation
    {
        Ping = 0,
        ApplyProtection = 1,
        EnableWriteAccess = 2,
        DisableWriteAccess = 3,
        CreateVolumeFromDirectory = 4,
        InvalidateVolumeHeader = 5,
        ExtractVolume = 6,
        IsBlackSecureVolume = 7,

        // Suite-shared VHDX operations (PhantomKey / Obscura / Attestor).
        // The broker is the single elevated authority for VHDX attach/detach so
        // the user sees zero per-launch UAC prompts across the whole suite.
        ProvisionPhantomVolume = 8,
        MountPhantomVolume = 9,
        UnmountPhantomVolume = 10,
        GetIntegrityVerdict = 11,
        AuthorizeIntegrityWrite = 12
    }

    public enum BrokerMessageType
    {
        Progress = 0,
        Result = 1,
        Error = 2
    }

    /// <summary>Single request envelope (one JSON line).</summary>
    public sealed class BrokerRequest
    {
        [JsonPropertyName("v")]
        public int ProtocolVersion { get; set; } = BrokerProtocol.ProtocolVersion;

        [JsonPropertyName("op")]
        public BrokerOperation Operation { get; set; }

        [JsonPropertyName("driveRoot")]
        public string? DriveRoot { get; set; }

        [JsonPropertyName("devicePath")]
        public string? PhysicalDevicePath { get; set; }

        [JsonPropertyName("sourceRoot")]
        public string? SourceRoot { get; set; }

        [JsonPropertyName("destRoot")]
        public string? DestinationRoot { get; set; }

        [JsonPropertyName("verify")]
        public bool Verify { get; set; }

        /// <summary>Serialized <c>UsbWriteProtectionState</c> for ApplyProtection.</summary>
        [JsonPropertyName("stateJson")]
        public string? StateJson { get; set; }

        /// <summary>Absolute path to the .phk VHDX for Phantom volume operations.</summary>
        [JsonPropertyName("containerPath")]
        public string? ContainerPath { get; set; }

        /// <summary>Size in bytes for ProvisionPhantomVolume (ignored otherwise).</summary>
        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("challenge")]
        public string? Challenge { get; set; }

        [JsonPropertyName("relativePath")]
        public string? RelativePath { get; set; }

        [JsonPropertyName("expectedOldHash")]
        public string? ExpectedOldHash { get; set; }

        [JsonPropertyName("expectedNewHash")]
        public string? ExpectedNewHash { get; set; }

        [JsonPropertyName("maxLength")]
        public long MaximumLength { get; set; }

        [JsonPropertyName("changeKind")]
        public int ChangeKind { get; set; }
    }

    /// <summary>Response envelope (one JSON line per message).</summary>
    public sealed class BrokerMessage
    {
        [JsonPropertyName("type")]
        public BrokerMessageType Type { get; set; }

        /// <summary>Progress fraction 0..1 (Progress messages only).</summary>
        [JsonPropertyName("progress")]
        public double Progress { get; set; }

        [JsonPropertyName("boolResult")]
        public bool BoolResult { get; set; }

        [JsonPropertyName("stringResult")]
        public string? StringResult { get; set; }

        /// <summary>Updated <c>UsbWriteProtectionState</c> echoed back after ApplyProtection.</summary>
        [JsonPropertyName("stateJson")]
        public string? StateJson { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
