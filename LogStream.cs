using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Bender
{
    public class LogStream : Stream
    {
        private readonly string _host;
        private readonly string _pattern;

        private SortedDictionary<long, Fi> _fileInfo;

        private Fi _current;
        private Stm _currentStream;
        private long _currentOffset;
        private long _pos;
        private long _length;
        private FileSystemWatcher _watcher;
        private readonly object _changedLock = new object();
        private bool _changed;

        public LogStream(string pattern) : this(string.Empty, pattern)
        {
        }

        public LogStream(string host, string pattern)
        {
            _host = host;
            _pattern = pattern;

            Rehash();
        }

        public void Rehash()
        {
            var pos = Position;
            List<Fi> fileinfos;
            try
            {
                fileinfos = GetFileInfos();
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Failed to get files in pattern '{_pattern}'", e);
            }

            if (fileinfos.Count == 0)
            {
                throw new IOException($"Files in pattern '{_pattern}' not found.");
            }

            // newest last
            fileinfos.Sort((lhs, rhs) =>
                {
                    if (lhs.Modification >= lhs.Creation || rhs.Modification >= rhs.Creation)
                    {
                        if (lhs.Creation < rhs.Creation) return -1;
                        if (lhs.Creation > rhs.Creation) return 1;
                    }

                    if (lhs.Modification < rhs.Modification) return -1;
                    if (lhs.Modification > rhs.Modification) return 1;

                    return 0;
                });

            var newInfos = new SortedDictionary<long, Fi>();
            long offset = 0;
            foreach (var fi in fileinfos)
            {
                newInfos.Add(offset, fi);
                offset += fi.Size;
            }

            // find _current in newInfos and calculate offset adjustment
            long adjust = -1;

            if (_current != null)
            {
                for (var pass = 1; adjust == -1 && pass <= 3; ++pass)
                {
                    foreach (var kvp in newInfos)
                    {
                        var match = false;

                        switch (pass)
                        {
                            case 1:
                                match = string.Equals(kvp.Value.Name, _current.Name, StringComparison.InvariantCultureIgnoreCase) && kvp.Value.Creation == _current.Creation;
                                break;
                            case 2:
                                match = kvp.Value.Creation == _current.Creation;
                                break;
                            case 3:
                                match = string.Equals(kvp.Value.Name, _current.Name, StringComparison.InvariantCultureIgnoreCase);
                                break;
                        }

                        if (match)
                        {
                            if (adjust == -1)
                            {
                                adjust = kvp.Key - _currentOffset;
                            }
                            else
                            {
                                adjust = -1;
                                break;
                            }
                        }
                    }
                }
            }

            if (adjust == -1)
            {
                adjust = 0;
            }

            _fileInfo = newInfos;

            if (_currentStream != null)
            {
                _currentStream.Close();
                _currentStream = null;
            }

            _current = null;
            _currentOffset = 0;
            _length = offset;
            Position = pos + adjust;

            // Setup filesystemwatcher on last file
            if (_watcher != null)
            {
                _watcher.Dispose();
                _watcher = null;
            }

            if (_fileInfo.Count > 0 && string.IsNullOrEmpty(_host))
            {
                var last = _fileInfo.Values.Last();
                _watcher = new FileSystemWatcher
                {
                    Filter = Path.GetFileName(last.Name),
                    Path = Path.GetDirectoryName(last.Name),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.LastAccess
                };

                _watcher.Changed += (sender, args) =>
                    {
                        lock (_changedLock)
                        {
                            _changed = true;
                            Monitor.PulseAll(_changedLock);
                        }
                    };
                _watcher.EnableRaisingEvents = true;
            }
        }

        public bool WaitForChanges(int maxMs)
        {
            bool result;
            lock (_changedLock)
            {
                if (!_changed)
                {
                    Monitor.Wait(_changedLock, maxMs);
                }
                result = _changed;
                _changed = false;
            }
            return result;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override void Flush()
        {
            throw new InvalidOperationException();
        }

        public override long Length => _length;

        public override long Position
        {
            get
            {
                return _pos;
            }
            set
            {
                if (value > _length)
                {
                    throw new IOException($"Seek out of range {value} > {_length}.");
                }

                if (_current != null && value >= _currentOffset && value < (_currentOffset + _current.Size + (value == _length ? 1 : 0)))
                {
                    if (_currentStream != null)
                    {
                        _currentStream.Position = value - _currentOffset;
                    }

                    _pos = value;
                }
                else
                {
                    var keys = _fileInfo.Keys.ToArray();
                    var val = Array.BinarySearch(keys, value);
                    if (val < 0)
                    {
                        val = ~val;
                        --val;
                    }
                    var pos = keys[val];
                    var fi = _fileInfo[pos];
                    _current = fi;
                    if (_currentStream != null)
                    {
                        _currentStream.Close();
                        _currentStream = null;
                    }
                    _currentOffset = pos;
                    _currentStream = new Stm(_host, fi.Name, value - _currentOffset);
                    _pos = value;
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = 0;
            while (count > 0)
            {
                if (_pos == _length)
                {
                    break;
                }

                var local = (int)Math.Min(count, _current.Size - (_pos - _currentOffset));
                if (local != 0)
                {
                    var local2 = _currentStream.Read(buffer, offset, local);
                    if (local != local2)
                    {
                        throw new IOException("File layout changed.");
                    }
                }

                Position += local;
                count -= local;
                offset += local;
                read += local;
            }
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin)
            {
                Position = offset;
            }
            else if (origin == SeekOrigin.Current)
            {
                Position += offset;
            }
            else if (origin == SeekOrigin.End)
            {
                Position = Length + offset;
            }

            return Position;
        }

        public override void SetLength(long value)
        {
            throw new InvalidOperationException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException();
        }

        public override void Close()
        {
            if (_currentStream != null)
            {
                _currentStream.Close();
                _currentStream = null;
            }

            if (_watcher != null)
            {
                _watcher.Dispose();
                _watcher = null;
            }

            _current = null;
            _pos = 0;
            _length = 0;
            base.Close();
        }

        public class Fi
        {
            public string Name;
            public long Size;
            public DateTime Creation;
            public DateTime Modification;

            public Fi(string name, long size, DateTime date)
            {
                Name = name;
                Size = size;
                Creation = Modification = date;
            }

            public Fi(FileInfo fi)
            {
                Name = fi.FullName;
                Size = fi.Length;
                Creation = fi.CreationTimeUtc;
                Modification = fi.LastWriteTimeUtc;
            }
        }

        public static List<Fi> GetLocalFileInfos(string pattern)
        {
            var fis = new List<Fi>();
            Fi emptyFileInfo = null;

            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(pattern), Path.GetFileName(pattern)))
            {
                var fi = new Fi(new FileInfo(f));
                if (fi.Size > 0)
                {
                    fis.Add(fi);
                }
                else if (emptyFileInfo == null)
                {
                    emptyFileInfo = fi;
                }
            }

            if (fis.Count == 0 && emptyFileInfo != null)
            {
                fis.Add(emptyFileInfo);
            }

            return fis;
        }

        private List<Fi> GetFileInfos()
        {
            var fis = new List<Fi>();

            if (string.IsNullOrEmpty(_host))
            {
                return GetLocalFileInfos(_pattern);
            }
            else
            {
                using (var stm = Remote.ConnectStream(_host))
                {
                    var s = "ls\n" + _pattern + "\n";
                    var b = System.Text.Encoding.ASCII.GetBytes(s);
                    stm.Write(b, 0, b.Length);
                    var lines = new StreamReader(stm).ReadToEnd().Split('\n');

                    // -rw------- 1 root root 169622 2016-04-10 03:20:01.945336135 +0000 /var/log/messages-20160410
                    var re = new Regex(@"^[\-rwx]+\s+[0-9]+\s+[a-z_][a-z0-9_-]*\s+[a-z_][a-z0-9_-]*\s+(?<size>[0-9]+)\s+(?<date>[0-9]{4}-[0-9]{2}-[0-9]{2}(T|\s)[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]+Z?(\s[+\-][0-9]{4})?)\s(?<filename>.*$)", RegexOptions.CultureInvariant | RegexOptions.Compiled);
                    foreach (var line in lines)
                    {
                        var match = re.Match(line);
                        if (match.Success)
                        {
                            var groups = match.Groups;
                            var size = long.Parse(groups["size"].Value);
                            var date = groups["date"].Value;
                            var dt = DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture);
                            var filename = groups["filename"].Value;
                            var fi = new Fi(filename, size, dt);
                            if (fi.Size > 0)
                            {
                                fis.Add(fi);
                            }
                        }
                    }
                }
            }

            return fis;
        }

        private class Stm
        {
            private long _privatePos;
            private FileStream _fs;
            private Stream _ns;
            private readonly string _server;
            private readonly string _path;

            public Stm(string server, string path, long position)
            {
                _server = server;
                _path = path;
                Position = position;

                if (string.IsNullOrEmpty(server))
                {
                    _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete) { Position = position };
                    _privatePos = position;
                }
            }

            public void Close()
            {
                if (_fs != null)
                {
                    _fs.Close();
                    _fs = null;
                }
                if (_ns != null)
                {
                    _ns.Close();
                    _ns = null;
                }
            }

            public int Read(byte[] array, int offset, int count)
            {
                int result = 0;
                if (_fs != null)
                {
                    if (Position != _privatePos)
                    {
                        _fs.Seek(Position, SeekOrigin.Begin);
                        _privatePos = Position;
                    }
                    result = _fs.Read(array, offset, count);
                }
                else
                {
                    // tail -c - length
                    if (Position != _privatePos || _ns == null)
                    {
                        // new command to read.
                        if (_ns != null)
                        {
                            _ns.Close();
                            _ns = null;
                        }

                        _ns = Remote.ConnectStream(_server);
                        var s = "tailc\n" + _path + "\n" + Position.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n";
                        var b = System.Text.Encoding.ASCII.GetBytes(s);
                        _ns.Write(b, 0, b.Length);
                    }

                    while (count != 0)
                    {
                        var read = _ns.Read(array, offset, count);
                        if (read == 0)
                        {
                            break;
                        }
                        result += read;
                        offset += read;
                        count -= read;
                    }
                }

                Position += result;
                _privatePos += result;
                return result;
            }

            public long Position { private get; set; }
        }
    }
}
