using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AgentQ.Desktop.Services;

/// <summary>Best-effort cleanup for a process that this service started.</summary>
internal static class OwnedProcessCleanup
{
    public static void TryKillTree(Process? process)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // Cleanup is best effort; callers preserve the original cancellation/failure.
        }
    }
}

/// <summary>
/// Owns a process tree started by Agent Q. On Windows it places the root process in a
/// Job Object with KILL_ON_JOB_CLOSE so descendants such as cmd.exe -> npm.cmd -> node
/// cannot escape merely because an intermediate process exits first.
/// </summary>
internal sealed class OwnedProcessLifetime : IDisposable
{
    private readonly Process _process;
    private readonly SafeJobHandle? _job;
    private bool _disposed;

    private OwnedProcessLifetime(Process process, SafeJobHandle? job)
    {
        _process = process;
        _job = job;
    }

    public int ProcessId => _process.Id;

    public static OwnedProcessLifetime Attach(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new OwnedProcessLifetime(process, null);
        }

        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            job.Dispose();
            return new OwnedProcessLifetime(process, null);
        }

        try
        {
            var information = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                }
            };
            if (!SetInformationJobObject(
                    job,
                    JobObjectExtendedLimitInformationClass,
                    ref information,
                    (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (!AssignProcessToJobObject(job, process.Handle))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return new OwnedProcessLifetime(process, job);
        }
        catch (Win32Exception)
        {
            job.Dispose();
            return new OwnedProcessLifetime(process, null);
        }
        catch (InvalidOperationException)
        {
            job.Dispose();
            return new OwnedProcessLifetime(process, null);
        }
    }

    public void KillAndWait(TimeSpan timeout)
    {
        if (_disposed)
        {
            return;
        }

        // Closing the job terminates every non-breakaway descendant, including children
        // that were re-parented after cmd.exe/npm.cmd exited.
        _job?.Dispose();
        OwnedProcessCleanup.TryKillTree(_process);
        try
        {
            _process.WaitForExit((int)Math.Clamp(timeout.TotalMilliseconds, 0, int.MaxValue));
        }
        catch (InvalidOperationException)
        {
            // The process already exited or was disposed by a concurrent shutdown path.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _job?.Dispose();
        _process.Dispose();
    }

    private const uint JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeJobHandle CreateJobObject(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeJobHandle job,
        uint jobObjectInformationClass,
        ref JobObjectExtendedLimitInformation jobObjectInformation,
        uint jobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeJobHandle job, IntPtr process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public IntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    private sealed class SafeJobHandle : SafeHandle
    {
        public SafeJobHandle() : base(IntPtr.Zero, ownsHandle: true) { }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
