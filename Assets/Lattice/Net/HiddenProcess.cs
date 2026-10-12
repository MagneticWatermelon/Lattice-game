using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Lattice.Net
{
    /// <summary>
    /// Runs a console program without a window and returns what it printed:
    /// kernel32's CreateProcess with a pipe, because IL2CPP's Process.Start
    /// can't redirect output (it fails with "Native error= Success"). For the
    /// dev setup's one question to WSL, once at start.
    /// </summary>
    static class HiddenProcess
    {
        /// <summary>The program's standard output, or null if it couldn't run or
        /// took longer than <paramref name="timeoutMs"/>.</summary>
        public static unsafe string Run(string commandLine, int timeoutMs)
        {
            var sa = new SecurityAttributes { length = sizeof(SecurityAttributes), inheritHandle = 1 };
            if (!CreatePipe(out var read, out var write, ref sa, 0))
                return null;
            var process = new ProcessInformation();
            try
            {
                // Only the write end goes to the child.
                SetHandleInformation(read, HandleFlagInherit, 0);
                var si = new StartupInfo
                {
                    cb = sizeof(StartupInfo),
                    flags = StartfUseStdHandles,
                    stdOutput = write,
                    stdError = write,
                };
                var line = new StringBuilder(commandLine);
                if (!CreateProcessW(null, line, IntPtr.Zero, IntPtr.Zero, true, CreateNoWindow, IntPtr.Zero, null, ref si, out process))
                    return null;
                // Ours closed, the pipe ends when the child exits.
                CloseHandle(write);
                write = IntPtr.Zero;
                var output = new StringBuilder();
                var buffer = new byte[512];
                var started = Environment.TickCount;
                while (Environment.TickCount - started < timeoutMs)
                {
                    if (!PeekNamedPipe(read, IntPtr.Zero, 0, IntPtr.Zero, out uint available, IntPtr.Zero))
                        break; // the child is gone and the pipe drained
                    if (available == 0)
                    {
                        if (WaitForSingleObject(process.process, 10) == WaitObject0 && PeekNamedPipe(read, IntPtr.Zero, 0, IntPtr.Zero, out available, IntPtr.Zero) && available == 0)
                            break;
                        continue;
                    }
                    fixed (byte* b = buffer)
                    {
                        if (!ReadFile(read, b, (uint)Math.Min(available, (uint)buffer.Length), out uint got, IntPtr.Zero) || got == 0)
                            break;
                        output.Append(Encoding.UTF8.GetString(buffer, 0, (int)got));
                    }
                }
                return output.ToString();
            }
            finally
            {
                if (write != IntPtr.Zero)
                    CloseHandle(write);
                CloseHandle(read);
                if (process.process != IntPtr.Zero)
                    CloseHandle(process.process);
                if (process.thread != IntPtr.Zero)
                    CloseHandle(process.thread);
            }
        }

        const uint HandleFlagInherit = 1, StartfUseStdHandles = 0x100, CreateNoWindow = 0x08000000, WaitObject0 = 0;

        [StructLayout(LayoutKind.Sequential)]
        struct SecurityAttributes
        {
            public int length;
            public IntPtr securityDescriptor;
            public int inheritHandle;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct StartupInfo
        {
            public int cb;
            public IntPtr reserved, desktop, title;
            public int x, y, xSize, ySize, xCountChars, yCountChars, fillAttribute;
            public uint flags;
            public short showWindow, reserved2;
            public IntPtr reserved3, stdInput, stdOutput, stdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessInformation
        {
            public IntPtr process, thread;
            public int processId, threadId;
        }

        [DllImport("kernel32", SetLastError = true)]
        static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref SecurityAttributes attributes, uint size);

        [DllImport("kernel32", SetLastError = true)]
        static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CreateProcessW(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes,
            bool inheritHandles, uint creationFlags, IntPtr environment, string currentDirectory, ref StartupInfo startup, out ProcessInformation info);

        [DllImport("kernel32", SetLastError = true)]
        static extern bool PeekNamedPipe(IntPtr pipe, IntPtr buffer, uint size, IntPtr read, out uint available, IntPtr left);

        [DllImport("kernel32", SetLastError = true)]
        static extern unsafe bool ReadFile(IntPtr file, byte* buffer, uint toRead, out uint read, IntPtr overlapped);

        [DllImport("kernel32")]
        static extern uint WaitForSingleObject(IntPtr handle, uint ms);

        [DllImport("kernel32")]
        static extern bool CloseHandle(IntPtr handle);
    }
}
