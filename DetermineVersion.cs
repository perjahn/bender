using System.Configuration;
using System.Diagnostics;
using System.IO;

namespace Bender
{
    class DetermineVersion
    {
        public static void Do(int pid, Stream output, string delimiter)
        {
            string version = string.Empty;
            var p = Process.GetProcessById(pid);
            var path = Path.Combine(Path.GetDirectoryName(p.MainModule.FileName), "Services");
            if (Directory.Exists(path))
            {
                var dirs = Directory.GetDirectories(path);
                if (dirs.Length == 1)
                {
                    string serviceBinName = ConfigurationManager.AppSettings["ServiceBinName"];

                    var assembly = Path.Combine(dirs[0], serviceBinName);
                    version = GetAssemblyVersion(assembly, true) ?? Path.GetFileName(dirs[0]);
                    if (delimiter != null)
                    {
                        var dt = BuildDate.RetrieveLinkerTimestamp(assembly);
                        version += delimiter + Bender.Date(dt);
                    }
                }
            }
            else if (p.ProcessName == "w3wp")
            {
                string webBinName = ConfigurationManager.AppSettings["WebBinName"];
                var l = DetermineWebSiteFolder();

                if (Directory.Exists(l))
                {
                    var assembly = Path.Combine(l, "bin", webBinName);
                    version = GetAssemblyVersion(assembly, true) ?? Path.GetFileName(Path.GetDirectoryName(l));
                    if (delimiter != null)
                    {
                        var dt = BuildDate.RetrieveLinkerTimestamp(assembly);
                        version += delimiter + Bender.Date(dt);
                    }
                }
            }
            else
            {
                version = GetAssemblyVersion(p.MainModule.FileName, false);

                if (delimiter != null)
                {
                    var dt = BuildDate.RetrieveLinkerTimestamp(p.MainModule.FileName);

                    version += delimiter + Bender.Date(dt);
                }
            }

            Bender.WriteLine(version, output);
        }

        private static string GetAssemblyVersion(string path, bool validate)
        {
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(path).FileVersion;

                if (!validate || vi.Split('.').Length == 4)
                {
                    return vi;
                }
            }
            catch (IOException)
            {
                if (!validate)
                {
                    throw;
                }
            }

            return null;
        }

        public static string DetermineWebSiteFolder()
        {
            var appPool = ConfigurationManager.AppSettings["AppPool"];
            var appPoolLine = $"<application path=\"/\" applicationPool=\"{appPool}\">";
            var cfgPath = @"C:\Windows\System32\inetsrv\config\applicationHost.config";
            if (File.Exists(cfgPath))
            {
                using (var fs = File.OpenRead(cfgPath))
                {
                    using (var rdr = new StreamReader(fs))
                    {
                        while (true)
                        {
                            var l = rdr.ReadLine();
                            if (l == null)
                            {
                                break;
                            }
                            if (l.Contains(appPoolLine))
                            {
                                l = rdr.ReadLine().Trim().Replace("<virtualDirectory path=\"/\" physicalPath=\"", string.Empty).Replace("\" />", string.Empty);

                                if (Directory.Exists(l))
                                {
                                    return l;
                                }

                                break;
                            }
                        }
                    }
                }
            }

            return string.Empty;
        }
    }
}
