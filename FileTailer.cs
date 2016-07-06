using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Bender
{
    public class FileTailer
    {
        public static void Tail(string server, string path, int count, bool tail, Action<byte[], int> output)
        {
            try
            {
                Regex regex = null;

                var index = path.IndexOf('|');

                if (index != -1)
                {
                    regex = new Regex(path.Substring(index + 1), RegexOptions.CultureInvariant | RegexOptions.Compiled);
                    path = path.Substring(0, index);
                }

                using (var logStream = new LogStream(server, path))
                {
                    while (true)
                    {
                        GetStrings(logStream, count, output, regex);

                        output(null, 0);

                        if (tail)
                        {
                            logStream.WaitForChanges(1000);
                            logStream.Rehash();
                            count = 0;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                var buf = Encoding.ASCII.GetBytes(e.ToString());
                output(buf, buf.Length);
            }
        }

        public static void Tail(string server, string path, int count, bool tail, Stream output)
        {
            Tail(server, path, count, tail, (buf, length) =>
            {
                if (length > 0) output.Write(buf, 0, length);
            });
        }

        private static bool CheckRegex(byte[] buf, Regex regex, ref long beg, ref long end)
        {
            if (regex == null) return true;

            var s = Encoding.ASCII.GetString(buf);
            var match = regex.Match(s);
            if (match.Success)
            {
                var groups = match.Groups;
                var output = groups["output"];
                if (output.Success)
                {
                    beg += output.Index;
                    end = beg + output.Length;
                }
                // else match whole line by default

                return true;
            }

            return false;
        }

        private static void CheckRegex(Stream fs, long beg, long end, Regex regex, List<long> lines)
        {
            if (regex == null)
            {
                lines.Add(beg);
                lines.Add(end);
                return;
            }

            var buf = new byte[end - beg];
            var cur = fs.Position;
            try
            {
                fs.Position = beg;
                fs.Read(buf, 0, (int)(end - beg));
                if (CheckRegex(buf, regex, ref beg, ref end))
                {
                    lines.Add(beg);
                    lines.Add(end);
                }
            }
            finally
            {
                fs.Position = cur;
            }
        }

        public static void GetStrings(Stream fs, int lc, Action<byte[], int> output, Regex regex)
        {
            if (lc == 0 && regex == null)
            {
                var tmp = new byte[4096];
                while (true)
                {
                    var read = fs.Read(tmp, 0, tmp.Length);
                    if (read <= 0) break;
                    output(tmp, read);
                }
                return;
            }

            if (lc < 0)
            {
                throw new ArgumentException("Invalid number of lines requested.");
            }

            var lines = new List<long>();

            long offset = (long)lc * 1000;
            long readBytes = 0;
            var buf = new byte[16384];
            while (true)
            {
                var cr = false;
                long ofs = Math.Min(fs.Length, offset);
                if (offset != 0)
                {
                    fs.Seek(-ofs, SeekOrigin.End);
                }
                var absolutePos = fs.Position;
                var readFromStart = absolutePos == 0;

                // start of first line
                var lastLine = absolutePos;
                var previousReadBytes = readBytes;
                readBytes = 0;

                while (true)
                {
                    int local = fs.Read(buf, 0, buf.Length);

                    if (local == 0)
                    {
                        if (cr)
                        {
                            CheckRegex(fs, lastLine, absolutePos, regex, lines);
                            // lastLine = absolutePos;
                        }

                        break;
                    }

                    readBytes += local;

                    for (int i = 0; i < local; ++i)
                    {
                        if (buf[i] == 13)
                        {
                            // EOLed at previous line
                            if (cr)
                            {
                                CheckRegex(fs, lastLine, absolutePos, regex, lines);
                                lastLine = absolutePos;
                            }

                            cr = true;
                        }
                        else if (buf[i] == 10)
                        {
                            CheckRegex(fs, lastLine, absolutePos + 1, regex, lines);
                            lastLine = absolutePos + 1;
                            cr = false;
                        }
                        else if (cr)
                        {
                            // EOLed at previous lines
                            CheckRegex(fs, lastLine, absolutePos, regex, lines);
                            lastLine = absolutePos;
                            cr = false;
                        }

                        ++absolutePos;
                    }
                }

                if (readBytes == previousReadBytes)
                {
                    readFromStart = true;
                }

                if (readFromStart || lc == 0 || lines.Count > lc * 2)
                {
                    // Read whole file or found all lines. For the third exit condition, note that partial lines are considered, so more lines than expected need to be read.
                    break;
                }

                // Restart by reading twice as much data as before
                offset = 2 * offset;

                lines.Clear();
            }

            if (lc == 0)
            {
                lc = lines.Count;
            }

            for (int i = Math.Max(0, lines.Count - 2 * lc); i < lines.Count; i += 2)
            {
                long start = lines[i];
                long end = lines[i + 1];
                fs.Position = start;
                var buf2 = new byte[end - start];
                fs.Read(buf2, 0, buf2.Length);

                output(buf2, buf2.Length);
            }
        }
    }
}
