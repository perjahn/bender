using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Bender
{
    public class Tasklist
    {
        private class Cpu
        {
            public TimeSpan TotalProcessorTime;

            public TimeSpan UserProcessorTime;

            public TimeSpan PrivilegedProcessorTime;
        }

        private static Dictionary<int, Cpu> BuildCpuDict(IEnumerable<Process> processes)
        {
            var result = new Dictionary<int, Cpu>();
            foreach (var p in processes)
            {
                try
                {
                    p.Refresh();
                    result.Add(p.Id, new Cpu { PrivilegedProcessorTime = p.PrivilegedProcessorTime, TotalProcessorTime = p.TotalProcessorTime, UserProcessorTime = p.UserProcessorTime });
                }
                catch (Exception)
                {

                }
            }

            return result;
        }

        public static List<string> Age(TimeSpan ts)
        {
            return new List<string>() { ts.TotalSeconds.ToString(CultureInfo.InvariantCulture), (ts - TimeSpan.FromMilliseconds(ts.Milliseconds)).ToString("g") };
        }

        public static void JavaScript(Stream wr)
        {
            var l = new List<Dictionary<string, List<string>>>();

            var dict1 = Process.GetProcesses().ToDictionary(process => process.Id);
            var cpudict1 = BuildCpuDict(dict1.Values);

            Thread.Sleep(TimeSpan.FromMilliseconds(100));

            var cpudict2 = BuildCpuDict(dict1.Values);

            var factor = 1000.0 / Environment.ProcessorCount;

            foreach (var kvp in dict1)
            {
                var process = kvp.Value;
                var id = kvp.Key;
                Cpu cpu1;
                Cpu cpu2;
                if (!cpudict1.TryGetValue(id, out cpu1) || !cpudict2.TryGetValue(id, out cpu2))
                {
                    continue;
                }
                try
                {
                    var dict = new Dictionary<string, List<string>>();

                    dict.Add("id", new List<string> { process.Id.ToString() });

                    dict.Add("name", new List<string> { process.ProcessName });

                    dict.Add("private", new List<string> { process.PrivateMemorySize64.ToString(), Bender.BytesToStr(process.PagedMemorySize64) });

                    // dict.Add("cmdline", new List<string> { process.StartInfo.Arguments });

                    dict.Add("cpu", new List<string> { (factor * (cpu2.TotalProcessorTime - cpu1.TotalProcessorTime).TotalSeconds).ToString(CultureInfo.InvariantCulture) });

                    dict.Add("user", new List<string> { (factor * (cpu2.UserProcessorTime - cpu1.UserProcessorTime).TotalSeconds).ToString(CultureInfo.InvariantCulture) });

                    dict.Add("system", new List<string> { (factor * (cpu2.PrivilegedProcessorTime - cpu1.PrivilegedProcessorTime).TotalSeconds).ToString(CultureInfo.InvariantCulture) });

                    dict.Add("age", Age(DateTime.UtcNow - process.StartTime.ToUniversalTime()));

                    dict.Add("handlecount", new List<string> { process.HandleCount.ToString(CultureInfo.InvariantCulture) });

                    dict.Add("session", new List<string> { process.SessionId.ToString(CultureInfo.InvariantCulture) });

                    dict.Add("elapsedcpu", Age(cpu2.TotalProcessorTime));

                    var ph = IntPtr.Zero;
                    try
                    {
                        OpenProcessToken(process.Handle, TOKEN_QUERY, out ph);
                        WindowsIdentity wi = new WindowsIdentity(ph);
                        dict.Add("username", new List<string> { wi.Name });
                    }
                    finally
                    {
                        if (ph != IntPtr.Zero) { CloseHandle(ph); }
                    }

                    l.Add(dict);
                }
                catch (Exception)
                {

                }
            }

            var ser = new JavaScriptSerializer();

            var text = Encoding.UTF8.GetBytes(ser.Serialize(l));

            wr.Write(text, 0, text.Length);
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr ProcessHandle, UInt32 DesiredAccess, out IntPtr TokenHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);

        static uint TOKEN_QUERY = 0x0008;
    }
}
