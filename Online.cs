using System.IO;

namespace Bender
{
    public class Online
    {
        public static void Do(bool online)
        {
            var frontDir = DetermineVersion.DetermineWebSiteFolder();

            var onlineFile = Path.Combine(frontDir, "IsOnline.aspx");

            var isonline = File.Exists(onlineFile);

            if (isonline != online)
            {
                var offlineFile = Path.Combine(frontDir, "IsOnline.offline.aspx");

                if (online)
                {
                    var offlineFile2 = Path.Combine(frontDir, "IsOnline.off.aspx");

                    if (File.Exists(offlineFile))
                    {
                        File.Move(offlineFile, onlineFile);
                    }
                    else if (File.Exists(offlineFile2))
                    {
                        File.Move(offlineFile2, onlineFile);
                    }
                }
                else
                {
                    File.Move(onlineFile, offlineFile);
                }
            }
        }
    }
}
