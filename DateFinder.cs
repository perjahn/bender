using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Bender
{
    public class DateFinder
    {
        public static void Find(Stream input, Stream output, string pattern)
        {
            long minline = 0;
            long maxline = input.Length;

            while (minline != maxline)
            {
                long mid = (minline + maxline) / 2;
                long start;

                while (true)
                {
                    start = mid;

                    if (start == minline)
                    {
                        break;
                    }

                    // attempt to read one line at start.
                    start = FindEol(input, start, maxline, null);

                    if (start == maxline)
                    {
                        // need to look before mid
                        mid = Math.Max(minline, mid - 10000);
                    }
                    else
                    {
                        break;
                    }
                }

                // found start of line at start, read bytes into line
                var bytes = new List<byte>();
                var end = FindEol(input, start, maxline, bytes);

                // line from [start..end)
                var s = Encoding.ASCII.GetString(bytes.ToArray());

                var compResult = Compare(pattern, s);

                if (compResult > 0)
                {
                    // compStr is before pattern, search after compStr
                    minline = end;
                }
                else if (compResult <= 0)
                {
                    // compStr is after or equal to pattern, search before compStr
                    maxline = start;
                }
            }

            // go to minline and output from there
            input.Position = minline;
            bool matched = false;
            maxline = input.Length;
            while (minline != maxline)
            {
                var bytes = new List<byte>();
                minline = FindEol(input, minline, maxline, bytes);
                var byteArray = bytes.ToArray();
                if (!matched)
                {
                    matched = Compare(pattern, Encoding.ASCII.GetString(byteArray)) <= 0;
                }
                if (matched)
                {
                    output.Write(byteArray, 0, byteArray.Length);
                }
            }
        }

        private static int Compare(string pattern, string line)
        {
            var compStr = line.Substring(0, Math.Min(line.Length, pattern.Length));
            return string.Compare(pattern, compStr, StringComparison.OrdinalIgnoreCase);
        }

        private static long FindEol(Stream input, long start, long end, List<byte> bytes)
        {
            var buf = new byte[4096];
            int bufoffset = 0;
            int bufleft = 0;

            input.Position = start;
            // cr lf or lf
            bool cr = false;
            bool lf = false;
            while (start != end)
            {
                if (bufleft == 0)
                {
                    bufleft = input.Read(buf, 0, buf.Length);
                    bufoffset = 0;

                    if (bufleft <= 0)
                    {
                        throw new IOException();
                    }
                }

                var b = buf[bufoffset++];
                --bufleft;

                if (b == 0x0d)
                {
                    if (cr || lf)
                    {
                        break; // empty line
                    }
                    cr = true;
                }
                else if (b == 0xa)
                {
                    if (lf)
                    {
                        break; // empty line
                    }
                    lf = true;
                }
                else if (b != '#')
                {
                    if (cr || lf)
                    {
                        // start of line
                        break;
                    }
                }
                else
                {
                    // Found comment string
                    cr = lf = false;
                }

                bytes?.Add(b);

                ++start;
            }

            return start;
        }
    }
}
