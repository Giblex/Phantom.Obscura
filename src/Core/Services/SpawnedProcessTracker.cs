using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;

namespace PhantomVault.Core.Services
{
    /// <summary>
    /// Tracks processes spawned by this application for proper cleanup on exit.
    /// Only processes registered with this tracker will be terminated on app exit,
    /// preventing interference with unrelated processes (e.g., VeraCrypt volumes
    /// mounted by other applications).
    /// </summary>
    public sealed class SpawnedProcessTracker : IDisposable
    {
        private static readonly Lazy<SpawnedProcessTracker> _instance = 
            new(() => new SpawnedProcessTracker(), LazyThreadSafetyMode.ExecutionAndPublication);
        
        /// <summary>
        /// Gets the singleton instance of the process tracker.
        /// </summary>
        public static SpawnedProcessTracker Instance => _instance.Value;
        
        private readonly ConcurrentDictionary<int, TrackedProcess> _trackedProcesses = new();
        private bool _disposed;

        private SpawnedProcessTracker() { }

        /// <summary>
        /// Registers a process spawned by this application for tracking.
        /// The process will be terminated on application exit.
        /// </summary>
        /// <param name="processId">The ID of the spawned process.</param>
        /// <param name="processName">The name of the process (for logging).</param>
        /// <param name="description">Optional description of why the process was spawned.</param>
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

        /// <summary>
        /// Unregisters a process (e.g., when it has exited normally).
        /// </summary>
        /// <param name="processId">The ID of the process to unregister.</param>
        public void UnregisterProcess(int processId)
        {
            if (_disposed) return;
            
            if (_trackedProcesses.TryRemove(processId, out var removed))
            {
                Debug.WriteLine($"[ProcessTracker] Unregistered process: {removed.ProcessName} (PID: {processId})");
            }
        }

        /// <summary>
        /// Checks if a process ID is tracked by this application.
        /// </summary>
        public bool IsTracked(int processId)
        {
            return _trackedProcesses.ContainsKey(processId);
        }

        /// <summary>
        /// Terminates all tracked processes that haven't exited yet.
        /// Called during application shutdown.
        /// </summary>
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
                        // Process has already exited - this is expected
                        Debug.WriteLine($"[ProcessTracker] Process already exited: {tracked.ProcessName} (PID: {pid})");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ProcessTracker] Failed to terminate {tracked.ProcessName} (PID: {pid}): {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Gets the count of currently tracked processes.
        /// </summary>
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
