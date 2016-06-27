using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Bender
{
    internal class Bender
    {
        public static Dictionary<string, string> ReadMappings(string key)
        {
            var fileMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var assemblyPath = Path.GetDirectoryName(Uri.UnescapeDataString(new UriBuilder(Assembly.GetExecutingAssembly().CodeBase).Path));
            using (var fs = File.OpenRead(Path.Combine(assemblyPath, ConfigurationManager.AppSettings[key])))
            {
                using (var r = new StreamReader(fs))
                {
                    while (true)
                    {
                        var l = r.ReadLine();
                        if (l == null) break;
                        try
                        {
                            var i = l.IndexOf(' ');
                            fileMappings.Add(l.Substring(0, i), l.Substring(i + 1));
                        }
                        catch (Exception e)
                        {
                            throw new InvalidOperationException($"Read invalid mapping line '{l}'.", e);
                        }
                    }
                }
            }

            return fileMappings;
        }

        public static Dictionary<string, string> ReadFileMappings()
        {
            return ReadMappings("FileMappings");
        }

        public static Dictionary<string, string> ReadServers()
        {
            return ReadMappings("Servers");
        }

        public static Dictionary<Regex, string> ReadColorization()
        {
            return ReadMappings("ColorMappings").ToDictionary(kvp => new Regex(kvp.Key, RegexOptions.Compiled | RegexOptions.CultureInvariant), kvp => kvp.Value);
        }

        public static string ListenPort => ConfigurationManager.AppSettings["ListenPort"];

        private static Socket _socket;

        public static void Start()
        {
            try
            {
                var port = int.Parse(ListenPort);
                var fileMappings = ReadFileMappings();
                var colorMappings = ReadColorization();

                LogInfo("Starting service at port " + port + " with " + fileMappings.Count + " mappings. ServiceStartMode: " + Program.GetServiceStartMode() + ". Process ID " + Process.GetCurrentProcess().Id + ".");
                Socket sock;

                for (int i = 0; ; ++i)
                {
                    try
                    {
                        sock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        sock.Bind(new IPEndPoint(IPAddress.Any, port));
                        sock.Listen(5);
                        _socket = sock;
                        break;
                    }
                    catch (Exception)
                    {
                        if (i == 10)
                        {
                            throw;
                        }

                        Thread.Sleep(1000);
                    }
                }
                while (true)
                {
                    try
                    {
                        var peer = sock.Accept();
                        peer.ReceiveTimeout = 10000; // milliseconds
                        new Thread(() =>
                        {
                            using (var netStream = new NetworkStream(peer, true))
                            {
                                DoCommand(peer, netStream, netStream, fileMappings, colorMappings);
                            }
                        })
                        { IsBackground = true }.Start();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (SocketException e)
                    {
                        if (e.ErrorCode != (int)SocketError.Interrupted)
                        {
                            throw;
                        }
                    }
                    catch (Exception e)
                    {
                        LogError(e);
                    }
                }
            }
            catch (Exception e)
            {
                LogError(e);

                throw;
            }
        }

        public static void Stop()
        {
            LogInfo("Shutting down service.");
            _socket?.Close();
        }

        public static Tuple<string, string> ReadServerPath(string serverPath, Dictionary<string, string> fileMappings)
        {
            var s = serverPath.Split(':');
            var server = s.Length == 2 ? s[0] : string.Empty;
            var path = s.Length == 2 ? s[1] : s[0];
            if (server.Length < 2)
            {
                server = string.Empty;
                path = serverPath;
            }
            string result;

            if (fileMappings != null && fileMappings.TryGetValue(path, out result))
            {
                path = result;
            }
            else if (fileMappings != null)
            {
                if (!fileMappings.Values.Contains(path))
                {
                    // throw new InvalidOperationException("Illegal path");
                }
            }

            if (path.Contains("$front"))
            {
                path = path.Replace("$front", DetermineVersion.DetermineWebSiteFolder());
            }

            return new Tuple<string, string>(server, path);
        }

        public static void DoCommand(Socket peer, Stream input, Stream output, Dictionary<string, string> fileMappings, Dictionary<Regex, string> colorMappings)
        {
            using (input)
            {
                var crash = false;
                try
                {
                    var line = ReadLine(input);
                    var i3 = line.LastIndexOf((char)3);
                    if (i3 != -1)
                    {
                        line = line.Substring(i3 + 1);
                    }
                    var lineOrig = line;
                    line = line.ToLowerInvariant();
                    switch (line)
                    {
                        case "crash":
                            {
                                crash = true;
                                throw new InvalidOperationException();
                            }
                        case "fulldump":
                            {
                                var enable = int.Parse(ReadLine(input));
                                DumpConfig.Enable(enable != 0);
                                break;
                            }
                        case "mem":
                            {
                                MEMORYSTATUSEX memStatus = new MEMORYSTATUSEX();
                                if (GlobalMemoryStatusEx(memStatus))
                                {
                                    Write($"{Math.Round((double)memStatus.ullTotalPhys / (1024 * 1024 * 1024))} GB", output);
                                }
                                break;
                            }
                        case "pc":
                            {
                                var counter = ReadLine(input);
                                Write(PerformanceCounter.GetValue(counter).ToString(CultureInfo.InvariantCulture), output);
                                break;
                            }
                        case "rpc":
                            {
                                var counter = ReadLine(input);
                                var specs = (ReadLine(input) ?? "0").Split('|');

                                var index = int.Parse(specs[0]);
                                if (index == 4)
                                {
                                    Write(Date(PerformanceCounterClient.GetDate(counter)), output);
                                }
                                else
                                {
                                    var value = PerformanceCounterClient.GetValue(counter, index);
                                    Write(value.ToString(CultureInfo.InvariantCulture), output);
                                    if (specs.Length == 2)
                                    {
                                        Write("\n", output);
                                        switch (specs[1].ToLowerInvariant())
                                        {
                                            case "timespan":
                                                Write(TimeSpan.FromSeconds(value) + "\n", output);
                                                break;
                                        }
                                    }
                                }
                                break;
                            }
                        case "time":
                            {
                                Write(Environment.MachineName + " " + Date() + "\r\n", output);
                                break;
                            }
                        case "time2":
                            {
                                Write(Date(), output);
                                break;
                            }
                        case "tail":
                            {
                                var serverPath = ReadServerPath(ReadLine(input), fileMappings);
                                var server = serverPath.Item1;
                                var path = serverPath.Item2;
                                var countLine = ReadLine(input);
                                int count = 0;
                                if (!string.IsNullOrEmpty(countLine))
                                {
                                    count = int.Parse(countLine);
                                }
                                line = ReadLine(input);
                                bool tail = false;
                                if (!string.IsNullOrEmpty(line))
                                {
                                    tail = int.Parse(line) != 0;
                                }
                                FileTailer.Tail(server, path, count, tail, output);
                                break;
                            }
                        case "age":
                            {
                                var serverPath = ReadServerPath(ReadLine(input), fileMappings);
                                var server = serverPath.Item1;
                                var path = serverPath.Item2;
                                var ms = new MemoryStream();
                                FileTailer.Tail(server, path, 1, false, ms);
                                var str = Encoding.ASCII.GetString(ms.ToArray());
                                str = str.Substring(0, Math.Min(str.Length, 100)).Replace(",", ".");
                                string os = null;
                                var endsWithU = false;
                                while (str.Length > 0)
                                {
                                    DateTime dt;
                                    if (DateTime.TryParse(str, CultureInfo.InvariantCulture, endsWithU ? DateTimeStyles.AssumeUniversal : DateTimeStyles.AssumeLocal, out dt))
                                    {
                                        os = ((long)(DateTime.Now - dt).TotalSeconds).ToString();
                                        break;
                                    }
                                    endsWithU = str.EndsWith("U") && !str.EndsWith(" U");
                                    str = str.Substring(0, str.Length - 1);
                                }
                                Write(os ?? "null", output);
                                break;
                            }
                        case "uri":
                            {
                                var path = ReadLine(input);
                                FetchUri.Fetch(path, output);
                                break;
                            }
                        case "date":
                            {
                                var serverPath = ReadServerPath(ReadLine(input), fileMappings);
                                var server = serverPath.Item1;
                                var path = serverPath.Item2;
                                var pattern = ReadLine(input);
                                DateFinder.Find(new LogStream(server, path), output, pattern);
                                break;
                            }
                        case "upload":
                            {
                                var path = ReadLine(input);
                                Zip.Upload(path, input);
                            }
                            break;
                        case "download":
                            {
                                var path = ReadLine(input);
                                Zip.Download(path, output);
                            }
                            break;
                        case "uploadzip":
                            {
                                var path = ReadLine(input);
                                Zip.Unzipit(path, input);
                            }
                            break;
                        case "downloadzip":
                            {
                                var path = ReadLine(input);
                                Zip.Zipit(path, output);
                            }
                            break;
                        case "shell":
                            {
                                Shell.Do(peer, input, output);
                            }
                            break;
                        case "apppools":
                            {
                                Shell.Do(peer, input, output, @"c:\windows\system32\inetsrv\appcmd", "list apppool /config /xml", false);
                            }
                            break;
                        case "sites":
                            {
                                Shell.Do(peer, input, output, @"c:\windows\system32\inetsrv\appcmd", "list site /config /xml", false);
                            }
                            break;
                        case "buildtime":
                            {
                                Write(Environment.MachineName + " " + Date(BuildDate.RetrieveLinkerTimestamp()) + "\r\n", output);
                                break;
                            }
                        case "online":
                            Online.Do(true);
                            break;
                        case "offline":
                            Online.Do(false);
                            break;
                        case "isonline":
                            {
                                Write(Environment.MachineName + " ", output);
                                FetchUri.FetchHeaders("http://localhost/IsOnline.aspx", output);
                                Write($" {Path.GetFileName(Path.GetDirectoryName(DetermineVersion.DetermineWebSiteFolder()))} \r\n", output);
                                break;
                            }
                        case "isonline2":
                            {
                                FetchUri.FetchHeaders("http://localhost/IsOnline.aspx", output);
                                break;
                            }
                        case "tickcount":
                            {
                                var tc = Environment.TickCount;
                                var now = DateTime.Now;
                                var prev = -(long)int.MinValue + tc;
                                var next = (long)int.MaxValue - tc;
                                var bytes = Encoding.ASCII.GetBytes(Environment.MachineName + " Environment.TickCount is " + Environment.TickCount + " previous " + Date(now - TimeSpan.FromMilliseconds(prev)) + " next " + Date(now + TimeSpan.FromMilliseconds(next)) + " uptime " + GetUpTime() + "\r\n");
                                output.Write(bytes, 0, bytes.Length);
                                break;
                            }
                        case "tickcount2":
                            {
                                Write(Environment.TickCount.ToString(CultureInfo.InvariantCulture), output);
                                break;
                            }
                        case "tickcounts":
                            {
                                var tc = Environment.TickCount;
                                var secs = ((double)int.MaxValue - tc) / 1000.0;
                                Write(secs.ToString(CultureInfo.InvariantCulture) + "\n", output);
                                Write(Date(DateTime.UtcNow.AddSeconds(secs)) + "\n", output);
                                break;
                            }
                        case "uptime2":
                            {
                                var tc = GetTickCount64();
                                Write((tc / 1000.0).ToString(CultureInfo.InvariantCulture) + "\n", output);
                                Write(TimeSpan.FromMilliseconds(tc) + "\n", output);
                                break;
                            }
                        case "uptime":
                            {
                                Write(GetUpTime().ToString(), output);
                                break;
                            }
                        case "update":
                            {
                                var sourcePath = ReadLine(input);
                                var destPath = Path.GetDirectoryName(Uri.UnescapeDataString(new UriBuilder(Assembly.GetExecutingAssembly().CodeBase).Path));
                                var arguments = string.Format("/C net stop \"{0}\" && timeout /t 5 /nobreak && move /y \"{1}\\*\" \"{2}\" && net start \"{0}\"", Program.ServiceName, sourcePath, destPath);
                                Shell.Spawn(arguments);
                                break;
                            }
                        case "dump":
                            {
                                var path = ReadLine(input);
                                var process = ReadLine(input);
                                DumpFile.Create(path, process);
                                break;
                            }
                        case "ntp":
                            {
                                Shell.Do(peer, input, output, "ntpq.exe", "-np", false);
                                break;
                            }
                        case "tasklist":
                            {
                                Shell.Do(peer, input, output, "tasklist.exe", "", false);
                                break;
                            }
                        case "tasklistl":
                            {
                                Shell.Do(peer, input, output, "tasklist.exe", "/FO LIST", false);
                                break;
                            }
                        case "tasklistj":
                            {
                                Tasklist.JavaScript(output);
                                break;
                            }
                        case "systeminfo":
                            {
                                Shell.Do(peer, input, output, "systeminfo.exe", "", false);
                                break;
                            }
                        case "post / http/1.0":
                        case "post / http/1.1":
                            Http.Do(input, fileMappings, colorMappings);
                            break;
                        case "users":
                            {
                                Shell.Do(peer, input, output, "query.exe", "user", false);
                                break;
                            }
                        case "stacktrace":
                        case "stacktracenative":
                        case "verifyheap":
                            {
                                var pidOrSpec = GetPid2(ReadLine(input));

                                switch (line)
                                {
                                    case "stacktrace":
                                        StackTrace.DoManaged(peer, output, pidOrSpec);
                                        break;
                                    case "stacktracenative":
                                        StackTrace.DoNative(peer, output, pidOrSpec);
                                        break;
                                    case "verifyheap":
                                        StackTrace.VerifyHeap(peer, output, pidOrSpec);
                                        break;
                                }
                                break;
                            }
                        case "wertrace":
                            {
                                var process = ReadLine(input);
                                StackTrace.OpenWerDump(peer, output, process);
                                break;
                            }
                        case "version":
                        case "version2":
                        case "version3":
                            {
                                var pid = GetPid(ReadLine(input));

                                if (pid != -1)
                                {
                                    DetermineVersion.Do(pid, output, line == "version" ? " " : (line == "version2" ? null : "\n"));
                                }
                            }
                            break;
                        case "bend":
                            {
                                Remote.Do(peer, input, output);
                            }
                            break;
                        default:
                            if (line.StartsWith("get /"))
                            {
                                Http.Do(lineOrig, input, fileMappings, colorMappings);
                            }
                            else
                            {
                                throw new InvalidOperationException($"Unknown command {line}.");
                            }
                            break;
                    }
                }
                catch (Exception e)
                {
                    if (crash)
                    {
                        throw;
                    }

                    if (!(e is IOException))
                    {
                        LogError(e);
                    }
                }
            }
        }

        public static TimeSpan GetUpTime()
        {
            return TimeSpan.FromMilliseconds(GetTickCount64());
        }

        [DllImport("kernel32")]
        static extern UInt64 GetTickCount64();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
            public MEMORYSTATUSEX()
            {
                this.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public static string BytesToStr(long value)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = value;
            int order = 0;
            while (len >= 1024 && order + 1 < sizes.Length)
            {
                order++;
                len = len / 1024;
            }

            // Adjust the format string to your preferences. For example "{0:0.#}{1}" would
            // show a single decimal place, and no space.
            return $"{len:0.##} {sizes[order]}";
        }

        public static void LogError(Exception e)
        {
            Log($"{Date()} ERROR {e}\r\n");
        }

        public static void LogInfo(string info)
        {
            Log($"{Date()} INFO {info}\r\n");
        }

        public static void LogWarning(string warning)
        {
            Log($"{Date()} WARN {warning}\r\n");
        }

        private static string Date()
        {
            return Date(DateTime.UtcNow);
        }

        public static string Date(DateTime inp)
        {
            return inp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss,fffU");
        }

        private static FileStream _log;

        private static void Log(string entry)
        {
            if (Environment.UserInteractive)
            {
                Console.Error.WriteLine(entry);
            }

            FileStream log;
            while (true)
            {
                log = _log;
                if (log == null)
                {
                    log = new FileStream(ConfigurationManager.AppSettings["LogFile"], FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                    log.Position = log.Length;
                    if (Interlocked.CompareExchange(ref _log, log, null) != null)
                    {
                        log.Close();
                    }
                }
                else
                {
                    break;
                }
            }

            if (entry.EndsWith("\r\n"))
            {
                entry = entry.Remove(entry.Length - 2);
            }

            byte[] buf = Encoding.ASCII.GetBytes(entry.Replace("\r\n", " || ").Replace("\n", " || ") + "\r\n");

            lock (log)
            {
                log.Write(buf, 0, buf.Length);
                log.Flush();
            }
        }

        public static string ReadLine(Stream s)
        {
            var buf = new List<byte>();
            bool lf = false;
            while (true)
            {
                int b = s.ReadByte();
                if (b == -1) break;
                if (b == 10) lf = true;
                else if (b != 13) buf.Add((byte)b);
                if (lf) break;
            }
            return Encoding.ASCII.GetString(buf.ToArray());
        }

        public static void Write(string str, Stream output)
        {
            var bytes = Encoding.ASCII.GetBytes(str);
            output.Write(bytes, 0, bytes.Length);
        }

        public static void WriteLine(string str, Stream output)
        {
            var bytes = Encoding.ASCII.GetBytes(str + "\r\n");
            output.Write(bytes, 0, bytes.Length);
        }


        public static string GetPid2(string processId)
        {
            var pid = GetPid(processId);

            if (pid == -1)
            {
                return processId;
            }
            else
            {
                return pid.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static int GetPid(string processId)
        {
            int pid;
            if (!int.TryParse(processId, out pid))
            {
                pid = -1;
                long maxPrivate = 0;
                foreach (var process in Process.GetProcessesByName(processId))
                {
                    if (process.PrivateMemorySize64 > maxPrivate)
                    {
                        pid = process.Id;
                        maxPrivate = process.PrivateMemorySize64;
                    }
                }
            }

            return pid;
        }
    }
}
