using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace PhantomVault.Core.Services
{

    public sealed class SpawnedProcessTracker : IDisposable
    {
        private static readonly Lazy<SpawnedProcessTracker> _instance =
            new(() => new SpawnedProcessTracker(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static SpawnedProcessTracker Instance => _instance.Value;

        private readonly ConcurrentDictionary<int, TrackedProcess> _trackedProcesses = new();
        private bool _disposed;

        private SpawnedProcessTracker() { }

        public void RegisterProcess(int processId, string processName, string? description = null)
        {
            if (_disposed) return;

            _trackedProcesses[processId] = new TrackedProcess
            {
                ProcessId = processId,
                ProcessName = processName,
                Description = description ?? string.Empty,
                SpawnedAt = DateTimeOffset.UtcNow
            };

            Debug.WriteLine($"[ProcessTracker] Registered process: {processName} (PID: {processId}) - {description}");
        }

        public void UnregisterProcess(int processId)
        {
            if (_disposed) return;

            if (_trackedProcesses.TryRemove(processId, out var removed))
            {
                Debug.WriteLine($"[ProcessTracker] Unregistered process: {removed.ProcessName} (PID: {processId})");
            }
        }

        public bool IsTracked(int processId)
        {
            return _trackedProcesses.ContainsKey(processId);
        }

        public void TerminateAllTrackedProcesses()
        {
            if (_disposed) return;

            var processIds = _trackedProcesses.Keys;

            foreach (var pid in processIds)
            {
                if (_trackedProcesses.TryRemove(pid, out var tracked))
                {
                    try
                    {
                        using var process = Process.GetProcessById(pid);
                        if (!process.HasExited)
                        {
                            Debug.WriteLine($"[ProcessTracker] Terminating tracked process: {tracked.ProcessName} (PID: {pid})");
                            process.Kill(entireProcessTree: true);
                        }
                    }
                    catch (ArgumentException)
                    {

                        Debug.WriteLine($"[ProcessTracker] Process already exited: {tracked.ProcessName} (PID: {pid})");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ProcessTracker] Failed to terminate {tracked.ProcessName} (PID: {pid}): {ex.Message}");
                    }
                }
            }
        }

        public int TrackedCount => _trackedProcesses.Count;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            TerminateAllTrackedProcesses();
            _trackedProcesses.Clear();
        }

        private struct TrackedProcess
        {
            public int ProcessId { get; set; }
            public string ProcessName { get; set; }
            public string Description { get; set; }
            public DateTimeOffset SpawnedAt { get; set; }
        }
    }
}

