using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using WUApiLib;

namespace Bender
{
    class Patch
    {
        private struct LUID
        {
            public int LowPart;
            public int HighPart;
        }

        private struct LUID_AND_ATTRIBUTES
        {
            public LUID pLuid;
            public int Attributes;
        }

        private struct TOKEN_PRIVILEGES
        {
            public int PrivilegeCount;
            public LUID_AND_ATTRIBUTES Privileges;
        }

        [DllImport("advapi32.dll")]
        static extern int OpenProcessToken(IntPtr ProcessHandle, int DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AdjustTokenPrivileges(IntPtr TokenHandle,
            [MarshalAs(UnmanagedType.Bool)]bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState,
            UInt32 BufferLength,
            IntPtr PreviousState,
            IntPtr ReturnLength);

        [DllImport("advapi32.dll")]
        static extern int LookupPrivilegeValue(string lpSystemName, string lpName, out LUID lpLuid);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool ExitWindowsEx(int uFlags, int dwReason);

        const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";
        const short SE_PRIVILEGE_ENABLED = 2;
        const short TOKEN_ADJUST_PRIVILEGES = 32;
        const short TOKEN_QUERY = 8;

        public void InstallPatches(bool rebootIfNeeded, bool onlyList, Stream output)
        {
            Bender.WriteLine("Searching for updates...", output);

            UpdateSession session = new UpdateSession();

            List<IUpdate5> updates = GetPatches(session, output);

            PrintStats(updates, output);

            if (onlyList)
            {
                ListPatches(updates, output);
            }
            else
            {
                DownloadPatches(session, updates, output);

                updates = GetPatches(session, output);

                InstallPatches(session, updates, rebootIfNeeded, output);
            }

            return;
        }

        private List<IUpdate5> GetPatches(UpdateSession session, Stream output)
        {
            UpdateServiceManager manager = new UpdateServiceManager();

            Bender.WriteLine("Found " + manager.Services.Count + " update services.", output);

            List<IUpdate5> updates = new List<IUpdate5>();
            foreach (IUpdateService2 service in manager.Services)
            {
                Bender.WriteLine("Retrieving patches from: " + service.Name, output);

                try
                {
                    var searcher = session.CreateUpdateSearcher();
                    searcher.ServerSelection = ServerSelection.ssWindowsUpdate;
                    searcher.ServiceID = service.ServiceID;

                    ISearchResult searchresult = searcher.Search("");

                    UpdateCollection updatecollection = searchresult.Updates;

                    Bender.WriteLine("Found " + updatecollection.Count + " updates.", output);

                    foreach (IUpdate5 update in updatecollection)
                    {
                        if (!updates.Any(u => u.Title == update.Title))
                        {
                            updates.Add(update);
                        }
                    }
                }
                catch (COMException ex)
                {
                    Bender.WriteLine("Couldn't retrive patches: 0x" + ex.HResult.ToString("X"), output);
                    Bender.WriteLine(ex.ToString(), output);
                }
            }

            return updates;
        }

        private void PrintStats(List<IUpdate5> updates, Stream output)
        {
            string printsize = GetPrintSize(updates.Sum(u => u.MaxDownloadSize));

            Bender.WriteLine("Total unique updates: " + updates.Count + ": " + printsize + " MB.", output);
        }

        private void ListPatches(List<IUpdate5> updates, Stream output)
        {
            foreach (IUpdate5 update in updates.OrderBy(u => u.Title))
            {
                Bender.WriteLine(update.Title + ": " + GetPrintSize(update.MaxDownloadSize) + " MB.", output);
            }
        }

        private void DownloadPatches(UpdateSession session, List<IUpdate5> updates, Stream output)
        {
            Bender.WriteLine("Downloading " + updates.Count + " patches...", output);

            foreach (IUpdate5 update in updates.OrderBy(u => u.Title))
            {
                if (update.IsDownloaded)
                {
                    Bender.WriteLine("Patch is already downloaded: " + update.Title, output);
                    continue;
                }


                UpdateCollection updateCollection = new UpdateCollection();
                updateCollection.Add(update);

                UpdateDownloader downloader = session.CreateUpdateDownloader();
                downloader.Updates = updateCollection;

                bool downloaded = false;

                for (int tries = 0; tries < 3 && !downloaded; tries++)
                {
                    try
                    {
                        string printtry = tries > 0 ? " (try " + (tries + 1) + ")" : string.Empty;

                        Bender.WriteLine("Downloading" + printtry + ": " + update.Title + ": " + GetPrintSize(update.MaxDownloadSize) + " MB.", output);

                        IDownloadResult downloadresult = downloader.Download();
                        if (downloadresult.ResultCode == OperationResultCode.orcSucceeded)
                        {
                            downloaded = true;
                        }
                        else
                        {
                            Bender.WriteLine("Couldn't download patch: " + downloadresult.ResultCode + ": 0x" + downloadresult.HResult.ToString("X"), output);
                        }
                    }
                    catch (COMException ex)
                    {
                        Bender.WriteLine("Couldn't download patch: 0x" + ex.HResult.ToString("X"), output);
                    }
                }
            }
        }

        private void InstallPatches(UpdateSession session, List<IUpdate5> updates, bool rebootIfNeeded, Stream output)
        {
            Bender.WriteLine("Installing " + updates.Count + " patches...", output);

            bool reboot = false;

            foreach (IUpdate5 update in updates.OrderBy(u => u.Title))
            {
                if (update.IsInstalled)
                {
                    Bender.WriteLine("Patch is already installed: " + update.Title, output);
                    continue;
                }
                else if (!update.IsDownloaded)
                {
                    Bender.WriteLine("Patch isn't downloaded yet: " + update.Title, output);
                }
                else
                {
                    try
                    {
                        Bender.WriteLine("Installing: " + update.Title, output);

                        UpdateCollection updateCollection = new UpdateCollection();
                        updateCollection.Add(update);

                        IUpdateInstaller installer = session.CreateUpdateInstaller();
                        installer.Updates = updateCollection;

                        IInstallationResult installresult = installer.Install();
                        if (installresult.ResultCode == OperationResultCode.orcSucceeded)
                        {
                            if (installresult.RebootRequired)
                            {
                                reboot = true;
                            }
                        }
                        else
                        {
                            Bender.WriteLine("Couldn't install patch: " + installresult.ResultCode + ": 0x" + installresult.HResult.ToString("X"), output);
                        }
                    }
                    catch (COMException ex)
                    {
                        Bender.WriteLine("Couldn't download patch: 0x" + ex.HResult.ToString("X"), output);
                    }
                }
            }

            string regpath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired";
            if (reboot || CheckIfLocalMachineKeyExists(regpath))
            {
                if (rebootIfNeeded)
                {
                    Bender.WriteLine("Rebooting.", output);

                    IntPtr hToken;
                    TOKEN_PRIVILEGES tkp;

                    OpenProcessToken(Process.GetCurrentProcess().Handle, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out hToken);
                    tkp.PrivilegeCount = 1;
                    tkp.Privileges.Attributes = SE_PRIVILEGE_ENABLED;
                    LookupPrivilegeValue("", SE_SHUTDOWN_NAME, out tkp.Privileges.pLuid);
                    AdjustTokenPrivileges(hToken, false, ref tkp, 0U, IntPtr.Zero, IntPtr.Zero);

                    if (!ExitWindowsEx(6, 0))
                    {
                        Bender.WriteLine("Couldn't reboot.", output);
                    }
                }
                else
                {
                    Bender.WriteLine("Reboot required.", output);
                }
            }
        }

        string GetPrintSize(decimal size)
        {
            return size > 0 && (int)(size / 1024 / 1024) == 0 ? "<1" : ((int)(size / 1024 / 1024)).ToString();
        }

        private bool CheckIfLocalMachineKeyExists(string regpath)
        {
            RegistryKey key = Registry.LocalMachine.OpenSubKey(regpath);
            if (key != null)
            {
                key.Close();
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
