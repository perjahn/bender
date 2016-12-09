using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Bender
{
    public class LogOutput
    {
        private readonly Stream _net;

        private readonly Dictionary<Regex, string> _colorMappings;

        private readonly bool _newline;

        private readonly bool _scroll;

        private bool _first = true;

        private bool _changeCol;

        private readonly string _title;

        private readonly bool _plainText;

        private readonly string _appendLocation;

        private readonly StringBuilder _body = new StringBuilder();

        public LogOutput(Stream net, Format fmt)
        {
            _net = net;
            _colorMappings = fmt.ColorMappings;
            _newline = fmt.NewLine;
            _scroll = fmt.Scroll;
            _title = fmt.Title;
            _appendLocation = fmt.AppendLocation;
            _plainText = fmt.PlainText;
            if (_plainText)
            {
                _scroll = false;
                _colorMappings = null;
            }

            WriteNet($"HTTP/1.1 200 OK\nAccess-Control-Allow-Origin: *\n{ContentType}\nTransfer-Encoding: Chunked\nX-Accel-Buffering: no\nCache-Control: no-cache, no-store, must-revalidate\nPragma: no-cache\nExpires: 0\n\n");
        }
        
        public class Format
        {
            public bool NewLine = true;

            public bool Scroll = true;

            public string Title;

            public string AppendLocation;

            public Dictionary<Regex, string> ColorMappings;

            public bool PlainText;
        }

        private void WriteBody(string s)
        {
            _body.Append(s);
        }

        private void WriteNet(string s)
        {
            var sbuf = Encoding.UTF8.GetBytes(s);
            _net.Write(sbuf, 0, sbuf.Length);
        }

        public void Add(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                if (_body.Length > 0)
                {
                    if (_scroll && !_first)
                    {
                        WriteBody("<script type=\"text/javascript\">window.scrollTo(0,document.body.scrollHeight);</script>");
                    }
                }

                Flush();

                return;
            }

            if (_first)
            {
                if (!_plainText)
                {
                    var colorString = _colorMappings == null ? "style =\"background-color:#FFFFFF;color:#000000\"" : "style =\"background-color:#000000;color:#FFC200\"";

                    WriteBody($"<html><head><title>{HttpUtility.HtmlEncode(_title)}</title></head><body {colorString}><pre>");

                    if (!string.IsNullOrEmpty(_appendLocation))
                    {
                        WriteBody($"<script type=\"text/javascript\">if (window.history.replaceState) window.history.replaceState({{}}, '{HttpUtility.JavaScriptStringEncode(_title)}', window.location.href + '{HttpUtility.JavaScriptStringEncode(_appendLocation)}');</script>");
                    }

                }
                _first = false;
            }

            s = s.Replace("\r", string.Empty);

            if (!_plainText)
            {
                s = HttpUtility.HtmlEncode(s);
            }

            var start = 0;

            while (true)
            {
                if (start == s.Length)
                {
                    break;
                }

                var end = s.IndexOf('\n', start);
                var outs = s.Substring(start, (end == -1 ? s.Length : end + 1) - start);

                if (_colorMappings != null)
                {
                    foreach (var kvp in _colorMappings)
                    {
                        outs = kvp.Key.Replace(outs, $"<span style=\"color:{kvp.Value}\">$0</span>");
                    }
                }

                if (_changeCol && !_plainText)
                {
                    outs = $"<span style=\"color:{(_colorMappings == null ? "1F1FBF" : "BD8000")}\">{outs}</span>";
                }

                if (_newline)
                {
                    outs = outs.Replace("||", "\n");
                }

                WriteBody(outs);

                if (end == -1) break;

                _changeCol = !_changeCol;
                start = end + 1;
            }
        }

        public void Add(byte[] buf, int offset, int count)
        {
            Add(buf == null ? string.Empty : Encoding.UTF8.GetString(buf, offset, count));
        }

        public void End()
        {
            if (!_first && !_plainText)
            {
                WriteBody("</pre></body></html>");
            }

            Flush();

            WriteNet("0\n\n");
        }

        public void Flush()
        {
            if (_body.Length > 0)
            {
                var body = Encoding.UTF8.GetBytes(_body.ToString());
                _body.Clear();
                var lbuf = Encoding.ASCII.GetBytes($"{body.Length:X}\n");
                _net.Write(lbuf, 0, lbuf.Length);
                _net.Write(body, 0, body.Length);
                _net.Write(new byte[] { 10 }, 0, 1);
            }
        }

        public string ContentType => $"Content-Type: text/{(_plainText ? "plain" : "html")}; charset=UTF-8";
    }
}
