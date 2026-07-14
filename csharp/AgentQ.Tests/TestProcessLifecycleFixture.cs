using System.Diagnostics;
using System.Runtime.InteropServices;
using Xunit;

namespace AgentQ.Tests;

[CollectionDefinition("Environment variable tests", DisableParallelization = true)]
public sealed class EnvironmentVariableTestCollection : ICollectionFixture<TestProcessLifecycleFixture>
{
}

/// <summary>
/// Last-resort guard for processes started by this testhost. Ownership is proved from the
/// live Windows parent-PID chain; no process is selected by executable name.
/// </summary>
public sealed class TestProcessLifecycleFixture : IDisposable
{
    private readonly int _testHostProcessId = Environment.ProcessId;
    private readonly HashSet<int> _initialProcessIds = EnumerateProcesses()
        .Select(process => process.ProcessId)
        .ToHashSet();

    public void Dispose()
    {
        // testhost itself may own its console host before the collection starts. It is
        // infrastructure, not a process created by an Agent Q test.
        var remaining = FindDescendants(_testHostProcessId)
            .Where(process => !_initialProcessIds.Contains(process.ProcessId))
            .ToList();
        if (remaining.Count == 0)
        {
            return;
        }

        foreach (var processInfo in remaining.OrderByDescending(process => process.Depth))
        {
            try
            {
                using var process = Process.GetProcessById(processInfo.ProcessId);
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch (ArgumentException)
            {
                // It exited between the snapshot and cleanup.
            }
            catch (InvalidOperationException)
            {
                // It exited between the snapshot and cleanup.
            }
        }

        var stillRemaining = FindDescendants(_testHostProcessId)
            .Where(process => !_initialProcessIds.Contains(process.ProcessId))
            .ToList();
        var evidence = string.Join(
            Environment.NewLine,
            remaining.Select(process => $"PID={process.ProcessId}; PPID={process.ParentProcessId}; depth={process.Depth}; name={process.ExecutableName}"));
        if (stillRemaining.Count > 0)
        {
            evidence += Environment.NewLine + "Still alive:" + Environment.NewLine + string.Join(
                Environment.NewLine,
                stillRemaining.Select(process => $"PID={process.ProcessId}; PPID={process.ParentProcessId}; depth={process.Depth}; name={process.ExecutableName}"));
        }

        throw new InvalidOperationException(
            "Testhost-owned child processes remained at collection teardown. Only the proven descendant tree was terminated." +
            Environment.NewLine + evidence);
    }

    private static List<ProcessSnapshot> FindDescendants(int rootProcessId)
    {
        var processes = EnumerateProcesses();
        var byParent = processes
            .GroupBy(process => process.ParentProcessId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var result = new List<ProcessSnapshot>();
        var pending = new Queue<(int ProcessId, int Depth)>();
        pending.Enqueue((rootProcessId, 0));

        while (pending.Count > 0)
        {
            var (parentProcessId, depth) = pending.Dequeue();
            if (!byParent.TryGetValue(parentProcessId, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                var descendant = child with { Depth = depth + 1 };
                result.Add(descendant);
                pending.Enqueue((descendant.ProcessId, descendant.Depth));
            }
        }

        return result;
    }

    private static List<ProcessSnapshot> EnumerateProcesses()
    {
        const uint Th32csSnapProcess = 0x00000002;
        using var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot.IsInvalid)
        {
            throw new InvalidOperationException("Could not take a process ownership snapshot for test cleanup.");
        }

        var entry = new ProcessEntry32 { DwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
        var result = new List<ProcessSnapshot>();
        if (!Process32First(snapshot, ref entry))
        {
            return result;
        }

        do
        {
            result.Add(new ProcessSnapshot((int)entry.Th32ProcessId, (int)entry.Th32ParentProcessId, entry.ExeFile, 0));
            entry.DwSize = (uint)Marshal.SizeOf<ProcessEntry32>();
        }
        while (Process32Next(snapshot, ref entry));

        return result;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeSnapshotHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(SafeSnapshotHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(SafeSnapshotHandle snapshot, ref ProcessEntry32 entry);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint DwSize;
        public uint CntUsage;
        public uint Th32ProcessId;
        public nuint Th32DefaultHeapId;
        public uint Th32ModuleId;
        public uint CntThreads;
        public uint Th32ParentProcessId;
        public int PcPriClassBase;
        public uint DwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    private sealed class SafeSnapshotHandle : SafeHandle
    {
        public SafeSnapshotHandle() : base(IntPtr.Zero, ownsHandle: true)
        {
        }

        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    private sealed record ProcessSnapshot(int ProcessId, int ParentProcessId, string ExecutableName, int Depth);
}
