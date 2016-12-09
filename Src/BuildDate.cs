using System;

namespace Bender
{
    public class BuildDate
    {
        // http://stackoverflow.com/questions/1600962/displaying-the-build-date
        public static DateTime RetrieveLinkerTimestamp()
        {
            return RetrieveLinkerTimestamp(System.Reflection.Assembly.GetCallingAssembly().Location);
        }

        public static DateTime RetrieveLinkerTimestamp(string filePath)
        {
            const int cPeHeaderOffset = 60;
            const int cLinkerTimestampOffset = 8;
            var b = new byte[2048];

            using (var s = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read))
            {
                s.Read(b, 0, 2048);
            }

            int i = BitConverter.ToInt32(b, cPeHeaderOffset);
            int secondsSince1970 = BitConverter.ToInt32(b, i + cLinkerTimestampOffset);
            var dt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            dt = dt.AddSeconds(secondsSince1970);
            return dt;
        }
    }
}
