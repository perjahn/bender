﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Bender
{
    class EventLogStream : Stream
    {
        private readonly string _source;

        private MemoryStream _ms = new MemoryStream();

        public EventLogStream(string source)
        {
            _source = source;
            CanRead = true;
            CanSeek = false;
            CanWrite = false;
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        private static string EscapeNl(string input)
        {
            return input.TrimEnd('\r', '\n').Replace("\r\n", "||").Replace("\n", "||").Replace("\r", "||");
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin != SeekOrigin.End)
            {
                throw new InvalidOperationException();
            }

            offset = -offset;

            var strings = new List<string>();
            var written = 0;

            foreach (var log in EventLog.GetEventLogs())
            {
                if (!log.Log.Equals(_source, StringComparison.OrdinalIgnoreCase)) continue;

                var index = log.Entries.Count - 1;

                while (written < offset && index >= 0)
                {
                    var e = log.Entries[index--];
                    var s = $"{Bender.Date(e.TimeWritten)} {e.EntryType} {e.Source} {EscapeNl(e.Message)} {EscapeNl(Encoding.ASCII.GetString(e.Data))}\r\n";
                    written += s.Length;
                    strings.Add(s);
                }

                var ms = new MemoryStream();
                var w = new StreamWriter(ms);
                for (var i = strings.Count - 1; i >= 0; --i)
                {
                    w.Write(strings[i]);
                }
                w.Flush();
                ms.Position = Math.Max(0, written - offset);
                _ms = ms;
            }

            return Position;
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _ms.Read(buffer, offset, count);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override bool CanRead { get; }
        public override bool CanSeek { get; }
        public override bool CanWrite { get; }
        public override long Length => long.MaxValue;

        public override long Position
        {
            get { return _ms.Position; }
            set { _ms.Position = value; }
        }
    }
}
