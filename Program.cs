using System;
using System.Collections;
using System.ComponentModel;
using System.Configuration;
using System.Configuration.Install;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace Bender
{
    public class ServiceShell : ServiceBase
    {
        public ServiceShell()
        {
            this.ServiceName = Program.ServiceName;
        }

        public static void Start()
        {
            new Thread(Bender.Start) { IsBackground = true }.Start();
        }

        protected override void OnStart(string[] args)
        {
            base.OnStart(args);

            Start();
        }

        protected override void OnStop()
        {
            Bender.Stop();

            base.OnStop();
        }
    }

    [RunInstaller(true)]
    public class Installer : System.Configuration.Install.Installer
    {
        public Installer()
        {
            var process = new ServiceProcessInstaller { Account = ServiceAccount.LocalSystem };

            var serviceAdmin = new ServiceInstaller
            {
                StartType = ServiceStartMode.Automatic,
                ServiceName = Program.ServiceName,
                DisplayName = Program.DisplayName,
                Description = Program.Description
            };

            Installers.Add(process);
            Installers.Add(serviceAdmin);
        }
    }

    internal class Program
    {
        public static string ServiceName => ConfigurationManager.AppSettings["ServiceName"];

        public static string DisplayName => ConfigurationManager.AppSettings["DisplayName"];

        public static string Description => ConfigurationManager.AppSettings["Description"];

        static void Main(string[] args)
        {
            bool debug = false;
            StringBuilder batchSb = null;
            if (Environment.UserInteractive || GetConsoleWindow() != IntPtr.Zero)
            {
                try
                {

                    foreach (var arg in args)
                    {
                        if (batchSb != null)
                        {
                            if (batchSb.Length > 0)
                            {
                                batchSb.AppendLine();
                            }
                            batchSb.Append(arg);
                            continue;
                        }
                        var a = arg.ToLowerInvariant();
                        switch (a)
                        {
                            case "-install":
                            case "/install":
                                CheckAdmin();
                                InstallService();
                                Console.WriteLine("Service '{0}' installed.", ServiceName);
                                break;
                            case "-uninstall":
                            case "/uninstall":
                            case "-remove":
                            case "/remove":
                                CheckAdmin();
                                UninstallService();
                                Console.WriteLine("Service '{0}' uninstalled.", ServiceName);
                                break;
                            case "-start":
                            case "/start":
                            case "-startservice":
                            case "/startservice":
                                CheckAdmin();
                                StartService();
                                Console.WriteLine("Service '{0}' started.", ServiceName);
                                break;
                            case "-stop":
                            case "/stop":
                            case "-stopservice":
                            case "/stopservice":
                                CheckAdmin();
                                StopService();
                                Console.WriteLine("Service '{0}' stopped.", ServiceName);
                                break;
                            case "-status":
                            case "/status":
                                bool installed = IsInstalled();
                                bool running = installed && IsRunning();
                                if (installed)
                                {
                                    if (running)
                                    {
                                        Console.WriteLine("Service '{0}' installed and running.", ServiceName);
                                    }
                                    else
                                    {
                                        Console.WriteLine("Service '{0}' installed but not running.", ServiceName);
                                    }
                                }
                                else
                                {
                                    Console.WriteLine("Service '{0}' not installed.", ServiceName);
                                }
                                break;
                            case "-debug":
                            case "/debug":
                                debug = true;
                                break;
                            case "-batch":
                            case "/batch":
                                batchSb = new StringBuilder();
                                break;
                            default:
                                Console.WriteLine("Unknown option {0}.", a);
                                return;
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e.ToString());
                    return;
                }

                if (debug)
                {
                    Console.WriteLine("Starting service '{0}'", ServiceName);
                    ServiceShell.Start();

                    Thread.CurrentThread.Join();
                }
                else
                {
                    ServiceShell.Start();
                    Bender.DoCommand(null, new ConsoleStream(batchSb == null ? Console.In : new StringReader(batchSb.ToString()), null), new ConsoleStream(null, Console.Out), Bender.ReadFileMappings());
                }
            }
            else
            {
                ServiceBase.Run(new ServiceShell());
            }
        }

        private static void CheckAdmin()
        {
            if (!IsUserAnAdmin())
            {
                throw new InvalidOperationException("The user needs to be an administrator.");
            }
        }

        [DllImport("shell32.dll")]
        public static extern bool IsUserAnAdmin();

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetConsoleWindow();

        private static bool IsInstalled()
        {
            using (var controller = new ServiceController(ServiceName))
            {
                try
                {
                    ServiceControllerStatus status = controller.Status;
                }
                catch
                {
                    return false;
                }
                return true;
            }
        }

        public static ServiceStartMode GetServiceStartMode()
        {
            using (var controller = new ServiceController(ServiceName))
            {
                try
                {
                    return (ServiceStartMode)controller.GetType().GetProperty("StartType").GetValue(controller, null);
                }
                catch
                {
                    return (ServiceStartMode)~0;
                }
            }
        }

        private static bool IsRunning()
        {
            using (var controller = new ServiceController(ServiceName))
            {
                if (!IsInstalled()) return false;
                return (controller.Status == ServiceControllerStatus.Running);
            }
        }

        private static AssemblyInstaller GetInstaller()
        {
            var installer = new AssemblyInstaller(typeof(ServiceShell).Assembly, null);
            installer.UseNewContext = true;
            return installer;
        }

        private static void InstallService()
        {
            if (IsInstalled()) return;

            using (var installer = GetInstaller())
            {
                IDictionary state = new Hashtable();
                try
                {
                    installer.Install(state);
                    installer.Commit(state);
                }
                catch
                {
                    try
                    {
                        installer.Rollback(state);
                    }
                    catch { }
                    throw;
                }
            }
        }

        private static void UninstallService()
        {
            if (!IsInstalled()) return;

            using (var installer = GetInstaller())
            {
                IDictionary state = new Hashtable();
                installer.Uninstall(state);
            }
        }

        private static void StartService()
        {
            if (!IsInstalled()) return;

            using (var controller = new ServiceController(ServiceName))
            {
                if (controller.Status != ServiceControllerStatus.Running)
                {
                    controller.Start();
                    controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                }
            }
        }

        private static void StopService()
        {
            if (!IsInstalled()) return;

            using (var controller = new ServiceController(ServiceName))
            {
                if (controller.Status != ServiceControllerStatus.Stopped)
                {
                    controller.Stop();
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                }
            }
        }
    }
}
