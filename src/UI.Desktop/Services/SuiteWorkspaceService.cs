using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PhantomVault.UI.Services
{
    /// <summary>
    /// Resolves sibling Phantom suite applications from the current workspace/build output.
    /// </summary>
    public sealed class SuiteWorkspaceService
    {
        public string SuiteRoot => ResolveSuiteRoot();

        public string? ResolveAttestorExecutablePath()
            => ResolveExecutablePath(
                Path.Combine("Phantom.Attestor", "src", "UI.Desktop", "bin", "Debug", "net9.0", "PhantomAttestor.App.exe"),
                Path.Combine("Phantom.Attestor", "src", "UI.Desktop", "bin", "Release", "net9.0", "PhantomAttestor.App.exe"),
                Path.Combine("Phantom.Attestor", "src", "UI.Desktop", "bin", "Debug", "net8.0", "PhantomAttestor.App.exe"),
                Path.Combine("Phantom.Attestor", "src", "UI.Desktop", "bin", "Release", "net8.0", "PhantomAttestor.App.exe"),
                "PhantomAttestor.App.exe");

        public string? ResolveRecoveryExecutablePath()
            => ResolveExecutablePath(
                Path.Combine("Phantom.Recovery", "PhantomRecovery.App", "bin", "Debug", "net9.0", "PhantomRecovery.App.exe"),
                Path.Combine("Phantom.Recovery", "PhantomRecovery.App", "bin", "Release", "net9.0", "PhantomRecovery.App.exe"),
                Path.Combine("Phantom.Recovery", "PhantomRecovery.App", "bin", "Debug", "net8.0", "PhantomRecovery.App.exe"),
                Path.Combine("Phantom.Recovery", "PhantomRecovery.App", "bin", "Release", "net8.0", "PhantomRecovery.App.exe"),
                "PhantomRecovery.App.exe");

        private string? ResolveExecutablePath(params string[] candidates)
        {
            foreach (var candidate in ExpandCandidates(candidates))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private IEnumerable<string> ExpandCandidates(IEnumerable<string> candidates)
        {
            foreach (var candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                if (Path.IsPathRooted(candidate))
                {
                    yield return candidate;
                    continue;
                }

                yield return Path.GetFullPath(Path.Combine(SuiteRoot, candidate));
                yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, candidate));
            }
        }

        private static string ResolveSuiteRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "Phantom.Attestor")) ||
                    Directory.Exists(Path.Combine(current.FullName, "Phantom.Recovery")) ||
                    Directory.Exists(Path.Combine(current.FullName, "Phantom.Obscura")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        }
    }
}
