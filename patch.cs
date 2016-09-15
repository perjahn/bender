using System;
using System.Collections.Generic;
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
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool ExitWindowsEx(int uFlags, int dwReason);


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
                else
                {
                    UpdateDownloader downloader = session.CreateUpdateDownloader();

                    bool downloaded = false;

                    for (int tries = 0; tries < 3 && !downloaded; tries++)
                    {
                        string printtry = tries > 0 ? " (try " + (tries + 1) + ")" : string.Empty;

                        Bender.WriteLine("Downloading" + printtry + ": " + update.Title + ": " + GetPrintSize(update.MaxDownloadSize) + " MB.", output);

                        try
                        {
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
                        catch (COMException ex) when ((uint)ex.HResult == (uint)0x80240004)
                        {
                            Bender.WriteLine("Couldn't download patch: 0x" + ex.HResult.ToString("X"), output);
                        }
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
                    IUpdateInstaller installer = session.CreateUpdateInstaller();
                    Bender.WriteLine("Installing: " + update.Title, output);
                    IInstallationResult installresult = installer.Install();

                    if (installresult.RebootRequired)
                    {
                        reboot = true;
                    }
                }
            }

            if (reboot)
            {
                if (rebootIfNeeded)
                {
                    Bender.WriteLine("Rebooting.", output);
                    ExitWindowsEx(6, 0);
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
    }
}
