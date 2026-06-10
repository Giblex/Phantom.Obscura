using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PhantomVault.Core.Services.Security;

public static class BuildIntegrityVerifier
{

    public static string BuildConfiguration
    {
        get
        {
#if DEBUG
            return "Debug";
#else
            return "Release";
#endif
        }
    }

    public static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    public static bool HasDebugSymbols()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var assemblyPath = assembly.Location;

            if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
                return false;

            var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
            if (File.Exists(pdbPath))
                return true;

            var debuggableAttributes = assembly.GetCustomAttributes<DebuggableAttribute>();
            foreach (var attr in debuggableAttributes)
            {

                if (attr.IsJITTrackingEnabled)
                    return true;
            }

            return false;
        }
        catch
        {

            return false;
        }
    }

    public static BuildMetadata GetBuildMetadata()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyName = assembly.GetName();

        var metadata = new BuildMetadata
        {
            Version = assemblyName.Version?.ToString() ?? "Unknown",
            BuildConfiguration = BuildConfiguration,
            HasDebugSymbols = HasDebugSymbols(),
            AssemblyPath = assembly.Location
        };

        var buildDateAttr = assembly.GetCustomAttribute<AssemblyMetadataAttribute>();
        if (buildDateAttr != null && buildDateAttr.Key == "BuildDate")
        {
            metadata.BuildDate = buildDateAttr.Value;
        }

        var commitHashAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (commitHashAttr != null)
        {
            metadata.CommitHash = commitHashAttr.InformationalVersion;
        }

        return metadata;
    }

    public static string ComputeAssemblyHash()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var assemblyPath = assembly.Location;

            if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
                return string.Empty;

            using var fileStream = File.OpenRead(assemblyPath);
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(fileStream);

            var sb = new StringBuilder();
            foreach (byte b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    public static void EnforceProductionBuild(bool allowDebugBuilds = false)
    {
        if (!allowDebugBuilds && IsDebugBuild())
        {
            throw new InvalidOperationException(
                "SECURITY VIOLATION: DEBUG build detected. This application cannot run in production mode with DEBUG symbols. " +
                "Please use a RELEASE build with code signing for production deployment.");
        }

        if (!allowDebugBuilds && HasDebugSymbols())
        {
            throw new InvalidOperationException(
                "SECURITY VIOLATION: DEBUG symbols (.pdb) detected. This application cannot run in production mode with debug symbols. " +
                "Please strip symbols from RELEASE builds before distribution.");
        }
    }
}

public class BuildMetadata
{
    public string Version { get; set; } = string.Empty;
    public string BuildConfiguration { get; set; } = string.Empty;
    public bool HasDebugSymbols { get; set; }
    public string AssemblyPath { get; set; } = string.Empty;
    public string? BuildDate { get; set; }
    public string? CommitHash { get; set; }

    public override string ToString()
    {
        return $"Version={Version}, Config={BuildConfiguration}, DebugSymbols={HasDebugSymbols}, " +
               $"BuildDate={BuildDate ?? "N/A"}, Commit={CommitHash ?? "N/A"}";
    }
}

