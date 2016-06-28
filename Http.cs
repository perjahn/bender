using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;

namespace Bender
{
    class Http
    {
        public static void Do(Stream net, Dictionary<string, string> fileMappings, Dictionary<Regex, string> colorMappings)
        {
            Do(null, net, fileMappings, colorMappings);
        }

        public static void Do(string getLine, Stream net, Dictionary<string, string> fileMappings, Dictionary<Regex, string> colorMappings)
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
                            if (headers == null && methodKnown && !string.IsNullOrEmpty(getLine))
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
                                    Bender.DoCommand(null, ms, rs, fileMappings, colorMappings);
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
                            body = null;
                            contentType = null;

                            var index = commandString.IndexOf('?');
                            var paramString = string.Empty;
                            if (index != -1)
                            {
                                paramString = commandString.Substring(index + 1);
                                commandString = commandString.Substring(0, index);
                            }

                            if (commandString.Equals("/log", StringComparison.OrdinalIgnoreCase))
                            {
                                const string defaults = "lines=80&tail=0&scroll=1&bw=1&newlines=1";
                                var appendLocation = string.Empty;
                                var param = ParseParams(paramString, string.Empty);
                                if (param.Count == 1 && param.ContainsKey("file"))
                                {
                                    appendLocation = "&" + defaults;
                                }
                                param = ParseParams(paramString, defaults);
                                var bw = param["bw"] != "0";
                                var scroll = param["scroll"] != "0";
                                var file = param.ContainsKey("file") ? param["file"] : null;
                                var lines = param.ContainsKey("val") ? param["val"] : param["lines"];
                                var tail = param["tail"];

                                var newLines = param["newlines"] != "0";

                                if (lines == "-f")
                                {
                                    lines = "40";
                                    tail = "1";
                                }

                                var serverPath = Bender.ReadServerPath(file, fileMappings);
                                var server = serverPath.Item1;
                                var path = serverPath.Item2;
                                var logOut = new LogOutput(net, path, appendLocation, newLines, scroll, bw ? null : colorMappings);

                                FileTailer.Tail(server, path, int.Parse(lines), int.Parse(tail) > 0, (bytes, i) =>
                                {
                                    logOut.Add(bytes, 0, i);
                                });

                                logOut.End();
                            }
                            else
                            {
                                var output = new MemoryStream();
                                var title = string.Empty;

                                var param = ParseParams(paramString, string.Empty);

                                if (commandString.Equals("/stack", StringComparison.OrdinalIgnoreCase))
                                {
                                    StackTrace.DoManaged(null, output, Bender.GetPid2(param["exe"]));
                                    title = $"Stacktrace for {param["exe"]}";
                                }
                                else if (commandString.Equals("/wer", StringComparison.OrdinalIgnoreCase))
                                {
                                    StackTrace.OpenWerDump(null, output, param["exe"]);
                                    title = $"Werstack for {param["exe"]}";
                                }
                                else if (commandString.Equals("/get", StringComparison.OrdinalIgnoreCase))
                                {
                                    var serverPath = Bender.ReadServerPath(param["uri"], fileMappings);
                                    var server = serverPath.Item1;
                                    var path = serverPath.Item2;
                                    FetchUri.Fetch(path, output);
                                    title = $"GET {path}";
                                }

                                if (output.Length > 0)
                                {
                                    var str = Encoding.UTF8.GetString(output.ToArray());
                                    var logOut = new LogOutput(net, title, string.Empty, false, false, null);
                                    logOut.Add(str);
                                    logOut.End();
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
                    }
                    catch (Exception)
                    {
                        Write(net, "HTTP/1.1 500 Internal server error\nConnection: Close\n\n", null);
                        throw;
                    }

                    if (contentType != null && body != null)
                    {
                        var header = $"HTTP/1.1 200 OK\nAccess-Control-Allow-Origin: *\n{contentType}\nContent-Length: {body.Length}\n\n";
                        Write(net, header, body);
                    }
                    methodKnown = false;
                }
            }
        }

        private static Dictionary<string, string> ParseParams(string paramString, string defaultString)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in (defaultString + "&" + paramString).Split('&'))
            {
                var line = entry.Split('=');
                if (line.Length == 2)
                {
                    result[line[0]] = line[1];
                }
            }

            return result;
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
