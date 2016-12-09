using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

namespace Bender
{
    class StackTrace
    {
        public static void DoManaged(Socket peer, Stream output, string pidOrSpec)
        {
            Do(peer, output, pidOrSpec, new[] { ".ecxr", "!dumpdomain", "!EEStack -ee" });
        }

        public static void DoNative(Socket peer, Stream output, string pidOrSpec)
        {
            Do(peer, output, pidOrSpec, new[] { ".ecxr", "!EEStack" });
        }

        public static void VerifyHeap(Socket peer, Stream output, string pidOrSpec)
        {
            Do(peer, output, pidOrSpec, new[] { "!verifyheap" });
        }

        public static void OpenWerDump(Socket peer, Stream output, string processName)
        {
            var dirs = Directory.GetDirectories(@"C:\ProgramData\Microsoft\Windows\WER\ReportQueue");
            var newestModificationDate = DateTime.MinValue;
            var dumpfile = string.Empty;
            foreach (var dir in dirs)
            {
                if (dir.Contains(processName))
                {
                    var di = new DirectoryInfo(dir);
                    if (di.LastWriteTimeUtc > newestModificationDate)
                    {
                        newestModificationDate = di.LastWriteTimeUtc;
                        dumpfile = Path.Combine(dir, "triagedump.dmp");
                    }
                }
            }

            Do(peer, output, dumpfile, new[] { ".ecxr", "!dumpdomain", "!EEStack" });
        }

        private static void Do(Socket peer, Stream output, string pidOrSpec, IEnumerable<string> commands)
        {
            const string key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Internet Settings\Connections";
            const string value = @"WinHttpSettings";
            // netsh.exe winhttp set proxy proxy-server="a" bypass-list="*"
            var fakeProxy = new byte[] { 0x28, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x61, 0x01, 0x00, 0x00, 0x00, 0x2a };
            var old = Registry.GetValue(key, value, null);
            try
            {
                Registry.SetValue(key, value, fakeProxy);
            }
            catch (UnauthorizedAccessException)
            { 
            }
            var tf = Path.GetTempFileName();
            Directory.CreateDirectory(@"c:\symbols");
            string sosPath = string.Empty;

            int pid = -1;
            if (!int.TryParse(pidOrSpec, out pid))
            {
                pid = -1;
            }

            if (pid != -1)
            {
                var p = Process.GetProcessById(pid);
                foreach (ProcessModule module in p.Modules)
                {
                    var path = Path.GetDirectoryName(module.FileName);
                    if (!string.IsNullOrEmpty(path) && path.StartsWith(@"C:\Windows\Microsoft.NET\Framework64\") && Path.GetFileName(module.FileName) == "clr.dll")
                    {
                        sosPath = path;
                        break;
                    }
                }
            }
            else
            {
                sosPath = @"C:\Windows\Microsoft.NET\Framework64\v4.0.30319";
            }

            using (var fs = File.OpenWrite(tf))
            {
                using (var wr = new StreamWriter(fs))
                {
                    // wr.WriteLine(@"!sym noisy");
                    var path = DetermineVersion.DetermineWebSiteFolder();
                    if (!string.IsNullOrEmpty(path))
                    {
                        path += "\\bin;";
                    }
                    wr.WriteLine(@".sympath {0}cache*c:\symbols;srv*http://msdl.microsoft.com/download/symbols", path);
                    wr.WriteLine(@".load {0}\sos.dll", sosPath); // needs to be explicitly specified - $PATH may not be set correctly?
                    wr.WriteLine(".reload");
                    foreach (var command in commands)
                    {
                        wr.WriteLine(command);
                    }
                    wr.WriteLine("qd");
                }
            }

            var attachArguments = pid != -1 ? $"-p {pid}" : $"-z \"{pidOrSpec}\"";

            Shell.Do(peer, null, output, @"C:\Program Files\Windows Kits\10\Debuggers\x64\cdb.exe", $"{attachArguments} -cfr \"{tf}\"", false);

            try
            {
                Registry.SetValue(key, value, old);
            }
            catch (UnauthorizedAccessException)
            {
            }

            File.Delete(tf);
        }
    }
}
