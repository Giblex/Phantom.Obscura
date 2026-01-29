using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace PhantomVault.Core.Services.Security;

/// <summary>
/// Verifies build integrity and detects DEBUG builds at runtime.
/// SECURITY: Prevents distribution of development builds with reduced security.
/// </summary>
public static class BuildIntegrityVerifier
{
    /// <summary>
    /// Gets the build configuration (Debug or Release) determined at compile time.
    /// </summary>
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

    /// <summary>
    /// Checks if the current build is a DEBUG build.
    /// </summary>
    public static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    /// <summary>
    /// Detects if DEBUG symbols are present in the executing assembly.
    /// SECURITY: DEBUG symbols indicate a development build and should not be distributed.
    /// </summary>
    /// <returns>True if DEBUG symbols detected</returns>
    public static bool HasDebugSymbols()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var assemblyPath = assembly.Location;

            if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
                return false;

            // Check for .pdb file (Program Database - debug symbols)
            var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
            if (File.Exists(pdbPath))
                return true;

            // Check DebuggableAttribute which is present in DEBUG builds
            var debuggableAttributes = assembly.GetCustomAttributes<DebuggableAttribute>();
            foreach (var attr in debuggableAttributes)
            {
                // JIT tracking and disable optimizations indicate DEBUG build
                if (attr.IsJITTrackingEnabled)
                    return true;
            }

            return false;
        }
        catch
        {
            // If we can't determine, assume no debug symbols for safety
            return false;
        }
    }

    /// <summary>
    /// Gets build metadata embedded in the assembly.
    /// </summary>
    /// <returns>Build metadata string</returns>
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

        // Try to extract build date from assembly attributes
        var buildDateAttr = assembly.GetCustomAttribute<AssemblyMetadataAttribute>();
        if (buildDateAttr != null && buildDateAttr.Key == "BuildDate")
        {
            metadata.BuildDate = buildDateAttr.Value;
        }

        // Try to extract commit hash from assembly attributes
        var commitHashAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (commitHashAttr != null)
        {
            metadata.CommitHash = commitHashAttr.InformationalVersion;
        }

        return metadata;
    }

    /// <summary>
    /// Computes SHA256 hash of the executing assembly for integrity verification.
    /// </summary>
    /// <returns>Hex-encoded SHA256 hash</returns>
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

    /// <summary>
    /// Enforces production build requirements.
    /// SECURITY: Throws exception if running a DEBUG build in production.
    /// </summary>
    /// <param name="allowDebugBuilds">If false, throws on DEBUG build detection</param>
    /// <exception cref="InvalidOperationException">If DEBUG build detected and not allowed</exception>
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

/// <summary>
/// Contains build metadata for integrity verification.
/// </summary>
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
