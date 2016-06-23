using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;

namespace Bender
{
    class Ajax
    {
        public static void Do(Stream net, Dictionary<string, string> fileMappings)
        {
            Do(null, net, fileMappings);
        }

        public static void Do(string getLine, Stream net, Dictionary<string, string> fileMappings)
        {
            var ns = net as NetworkStream;
            if (ns != null)
            {
                ns.ReadTimeout = 30 * 1000;
            }

            bool methodKnown = true;

            using (net)
            {
                while (true)
                {
                    List<string> headers = null;
                    byte[] body;
                    int cl = -1;
                    bool readAnything = methodKnown;
                    try
                    {
                        string type = string.Empty;
                        while (true)
                        {
                            var line = Bender.ReadLine(net);
                            if (string.IsNullOrEmpty(line)) break;
                            readAnything = true;
                            if (line.StartsWith("Get ", StringComparison.OrdinalIgnoreCase))
                            {
                                getLine = line;
                                methodKnown = true;
                            }
                            else if (line.StartsWith("Post ", StringComparison.OrdinalIgnoreCase))
                            {
                                getLine = null;
                                methodKnown = true;
                            }
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                cl = int.Parse(line.Substring(15));
                            }
                            if (line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
                            {
                                type = line.Substring(14);
                            }
                            if (headers == null && methodKnown && getLine != null && (getLine.StartsWith("get /ping", StringComparison.OrdinalIgnoreCase) || getLine.StartsWith("get /log", StringComparison.OrdinalIgnoreCase)))
                            {
                                headers = new List<string> { getLine };
                            }
                            else if (headers != null)
                            {
                                headers.Add(line);
                            }
                        }

                        if (!readAnything)
                        {
                            return;
                        }

                    }
                    catch (IOException)
                    {
                        return;
                    }

                    string contentType;

                    try
                    {
                        string commandString = string.Empty;
                        if (!string.IsNullOrEmpty(getLine))
                        {
                            commandString = HttpUtility.UrlDecode(getLine.Substring(4, getLine.Length - 13));
                        }
                        if (cl != -1)
                        {
                            var contents = new byte[cl];
                            var offset = 0;
                            while (cl > 0)
                            {
                                var cl2 = net.Read(contents, offset, cl);
                                if (cl2 <= 0)
                                {
                                    throw new InvalidOperationException($"Unable to read {cl} bytes of input data. Read {cl2}.");
                                }
                                cl -= cl2;
                                offset += cl2;
                            }
                            commandString = Encoding.UTF8.GetString(contents);
                        }

                        if (!methodKnown)
                        {
                            throw new InvalidOperationException("Illegal method");
                        }

                        if (headers == null)
                        {
                            JavaScriptSerializer ser = new JavaScriptSerializer();
                            var commands = ser.DeserializeObject(commandString) as Dictionary<string, object>;
                            var tasks = new List<Task<Tuple<string, MemoryStream>>>();
                            foreach (var command in commands)
                            {
                                var cmds = command.Value as object[];
                                var ms = new MemoryStream();
                                var serializer = new StreamWriter(ms);
                                foreach (var cmd in cmds)
                                {
                                    serializer.WriteLine(cmd as string);
                                }
                                serializer.Flush();
                                ms.Position = 0;
                                var key = command.Key;
                                var task = Task.Factory.StartNew(() =>
                                {
                                    var rs = new MemoryStream();
                                    Bender.DoCommand(null, ms, rs, fileMappings);
                                    return Tuple.Create(key, rs);
                                });
                                tasks.Add(task);
                            }

                            var js = string.Empty;
                            var result = new Dictionary<string, List<string>>();
                            foreach (var task in tasks)
                            {
                                var key = task.Result.Item1;
                                var stm = task.Result.Item2;
                                stm = new MemoryStream(stm.ToArray());
                                var lines = new List<string>();
                                using (var rdr = new StreamReader(stm))
                                {
                                    while (true)
                                    {
                                        var l = rdr.ReadLine();
                                        if (l == null)
                                        {
                                            break;
                                        }
                                        lines.Add(l);
                                    }
                                }
                                if (key.Equals("tasklistj", StringComparison.InvariantCultureIgnoreCase) && lines.Count == 1)
                                {
                                    js = lines[0];
                                }

                                result.Add(key, lines);

                                if (result.Count > 1)
                                {
                                    js = string.Empty;
                                }
                            }

                            if (string.IsNullOrEmpty(js))
                            {
                                js = ser.Serialize(result);
                            }

                            contentType = "Content-Type: application/json; charset=UTF-8";
                            body = Encoding.UTF8.GetBytes(js);
                        }
                        else
                        {
                            if (commandString != null && commandString.StartsWith("/log?", StringComparison.OrdinalIgnoreCase))
                            {
                                string file = null;
                                string lines = "0";
                                string tail = "0";
                                var newLines = false;
                                foreach (var entry in commandString.Substring(5).Split('&'))
                                {
                                    var line = entry.Split('=');
                                    switch (line[0].ToLowerInvariant())
                                    {
                                        case "file":
                                            file = line[1];
                                            break;
                                        case "lines":
                                        case "var":
                                            lines = line[1];
                                            break;
                                        case "tail":
                                            tail = line[1];
                                            break;
                                        case "newlines":
                                            newLines = line[1] != "0";
                                            break;

                                    }
                                }

                                if (lines == "-f")
                                {
                                    lines = "40";
                                    tail = "1";
                                }

                                contentType = "Content-Type: text/plain; charset=UTF-8";
                                Write(net, $"HTTP/1.1 200 OK\nAccess-Control-Allow-Origin: *\n{contentType}\nTransfer-Encoding: Chunked\n\n", null);

                                var serverPath = Bender.ReadServerPath(file, fileMappings);
                                var server = serverPath.Item1;
                                var path = serverPath.Item2;

                                Action<byte[], int> writeChunk = (bytes, i) =>
                                {
                                    var b = Encoding.ASCII.GetBytes($"{i:X}\n");
                                    net.Write(b, 0, b.Length);
                                    net.Write(bytes, 0, i);
                                    net.Write(new byte[] { 10 }, 0, 1);
                                };

                                FileTailer.Tail(server, path, int.Parse(lines), int.Parse(tail) > 0, (bytes, i) =>
                                {
                                    if (newLines)
                                    {
                                        var output = new List<byte>(i);
                                        for (var t = 0; t < i; ++t)
                                        {
                                            if (t < i - 1 && bytes[t] == '|' && bytes[t + 1] == '|')
                                            {
                                                output.Add(10);
                                                ++t;
                                            }
                                            else
                                            {
                                                output.Add(bytes[t]);
                                            }
                                        }

                                        bytes = output.ToArray();
                                        i = bytes.Length;
                                    }

                                    writeChunk(bytes, i);
                                });

                                net.Write(new byte[] { (byte)'0', 10, 10 }, 0, 3);
                                methodKnown = false;
                                continue;
                            }
                            else
                            {
                                contentType = "Content-Type: text/plain; charset=UTF-8";
                                var sb = new StringBuilder();
                                foreach (var h in headers)
                                {
                                    sb.AppendLine(h);
                                }
                                body = Encoding.UTF8.GetBytes(sb.ToString());
                            }
                        }
                    }
                    catch (Exception)
                    {
                        Write(net, "HTTP/1.1 500 Internal server error\nConnection: Close\n\n", null);
                        throw;
                    }

                    var header = $"HTTP/1.1 200 OK\nAccess-Control-Allow-Origin: *\n{contentType}\nContent-Length: {body.Length}\n\n";
                    Write(net, header, body);
                    methodKnown = false;
                }
            }
        }

        private static void Write(Stream net, string header, byte[] body)
        {
            var headerbytes = Encoding.ASCII.GetBytes(header);
            net.Write(headerbytes, 0, headerbytes.Length);
            if (body != null)
            {
                net.Write(body, 0, body.Length);
            }
        }
    }
}
