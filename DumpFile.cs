using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Bender
{
    public class DumpFile
    {
        public static void Create(string path, string processname)
        {
            bool fulldump = true;
            Process dump = null;
            var processes = Process.GetProcessesByName(processname);
            if (processes.Length == 1)
            {
                dump = processes[0];
            }
            else
            {
                int pid;
                if (int.TryParse(processname, out pid))
                {
                    dump = Process.GetProcessById(pid);
                }
            }

            if (dump == null)
            {
                return;
            }

            string fname = $"{dump.ProcessName}({dump.Id})_{DateTime.Now}.{(fulldump ? "dmp" : "mdmp")}".Replace(':', '_').Replace('/', '_');

            fname = Path.Combine(path, fname);

            Bender.LogInfo($"Dumping {dump.ProcessName}({dump.Id}) to {fname}.");

            using (var f = File.Open(fname, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                // Set NTFS compression first
                int ignored = 0;
                int result = NativeMethods.DeviceIoControl(f.SafeFileHandle.DangerousGetHandle(), NativeMethods.FSCTL_SET_COMPRESSION, ref NativeMethods.COMPRESSION_FORMAT_DEFAULT, 2 /*sizeof(short)*/, IntPtr.Zero, 0, ref ignored, IntPtr.Zero);
                if (result == 0)
                {
                    var error = Marshal.GetLastWin32Error();
                    Bender.LogWarning(string.Format("Failed to set NTFS compression for {0}.. Error {1}.", fname, error));
                }

                bool bRet = NativeMethods.MiniDumpWriteDump(
                  dump.Handle,
                  (uint)dump.Id,
                  f.SafeFileHandle.DangerousGetHandle(),
                  fulldump ? (uint)NativeMethods.Typ.MiniDumpWithFullMemory : (uint)NativeMethods.Typ.MiniDumpNormal,
                  IntPtr.Zero,
                  IntPtr.Zero,
                  IntPtr.Zero);

                if (!bRet)
                {
                    var error = Marshal.GetLastWin32Error();
                    Bender.LogError(new InvalidOperationException(string.Format("Failed to create minidump for {0}. Error {1:X}.", fname, error)));
                    f.Close();
                    try
                    {
                        File.Delete(fname);
                    }
                    catch
                    {
                        // empty
                    }
                }
            }
        }

        static class NativeMethods
        {
            // http://social.msdn.microsoft.com/Forums/vstudio/en-US/1b63b4a4-b197-4286-8f3f-af2498e3afe5/ntfs-compression-in-c
            [DllImport("kernel32.dll")]
            public static extern int DeviceIoControl(IntPtr hDevice, int dwIoControlCode, ref short lpInBuffer, int nInBufferSize, IntPtr lpOutBuffer, int nOutBufferSize, ref int lpBytesReturned, IntPtr lpOverlapped);

            public const int FSCTL_SET_COMPRESSION = 0x9C040;
            public static short COMPRESSION_FORMAT_DEFAULT = 1;

            // http://blog.kalmbach-software.de/2008/12/13/writing-minidumps-in-c/
            [Flags]
            public enum Typ : uint
            {
                // From dbghelp.h:
                MiniDumpNormal = 0x00000000,
                MiniDumpWithDataSegs = 0x00000001,
                MiniDumpWithFullMemory = 0x00000002,
                MiniDumpWithHandleData = 0x00000004,
                MiniDumpFilterMemory = 0x00000008,
                MiniDumpScanMemory = 0x00000010,
                MiniDumpWithUnloadedModules = 0x00000020,
                MiniDumpWithIndirectlyReferencedMemory = 0x00000040,
                MiniDumpFilterModulePaths = 0x00000080,
                MiniDumpWithProcessThreadData = 0x00000100,
                MiniDumpWithPrivateReadWriteMemory = 0x00000200,
                MiniDumpWithoutOptionalData = 0x00000400,
                MiniDumpWithFullMemoryInfo = 0x00000800,
                MiniDumpWithThreadInfo = 0x00001000,
                MiniDumpWithCodeSegs = 0x00002000,
                MiniDumpWithoutAuxiliaryState = 0x00004000,
                MiniDumpWithFullAuxiliaryState = 0x00008000,
                MiniDumpWithPrivateWriteCopyMemory = 0x00010000,
                MiniDumpIgnoreInaccessibleMemory = 0x00020000,
                MiniDumpValidTypeFlags = 0x0003ffff,
            };

            [StructLayout(LayoutKind.Sequential, Pack = 4)]  // Pack=4 is important! So it works also for x64!
            struct MiniDumpExceptionInformation
            {
                public uint ThreadId;
                public IntPtr ExceptioonPointers;
                [MarshalAs(UnmanagedType.Bool)]
                public bool ClientPointers;
            }

            [DllImport("dbghelp.dll",
              EntryPoint = "MiniDumpWriteDump",
              CallingConvention = CallingConvention.StdCall,
              CharSet = CharSet.Unicode,
              ExactSpelling = true, SetLastError = true)]
            public static extern bool MiniDumpWriteDump(
              IntPtr hProcess,
              uint processId,
              IntPtr hFile,
              uint dumpType,
              IntPtr expParam,
              IntPtr userStreamParam,
              IntPtr callbackParam);
        }
    }
}

