using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace Bender
{
    public class LogOutput
    {
        private readonly Action<string> _flusher;

        private readonly Dictionary<Regex, string> _colorMappings;

        private readonly bool _nl;

        private bool _first = true;

        private bool _changeCol = false;

        public LogOutput(Action<string> flusher, bool nl, Dictionary<Regex, string> colorMappings)
        {
            _flusher = flusher;
            _colorMappings = colorMappings;
            _nl = nl;
        }

        public void Add(byte[] buf, int offset, int count)
        {
            if (_first)
            {
                if (_colorMappings != null)
                {

                    _flusher("<html><body style=\"background-color:#000000;color:#FFC200\"><pre>");
                }

                _first = false;
            }

            var s = Encoding.UTF8.GetString(buf, offset, count);

            if (_colorMappings == null)
            {
                if (_nl)
                {
                    s = s.Replace("||", "\r");
                }

                _flusher(s);
            }
            else
            {
                s = s.Replace("\n", string.Empty);

                s = HttpUtility.HtmlEncode(s);

                var start = 0;

                while (true)
                {
                    if (start == s.Length)
                    {
                        break;
                    }

                    var end = s.IndexOf('\r', start);
                    var outs = s.Substring(start, (end == -1 ? s.Length : end + 1) - start);

                    foreach (var kvp in _colorMappings)
                    {
                        outs = kvp.Key.Replace(outs, $"<span style=\"color:{kvp.Value}\">$0</span>");
                    }

                    if (_changeCol)
                    {
                        outs = $"<span style=\"color:BD8000\">{outs}</span>";
                    }

                    if (_nl)
                    {
                        outs = outs.Replace("||", "\r");
                    }

                    _flusher(outs);

                    if (end == -1) break;

                    _changeCol = !_changeCol;
                    start = end + 1;
                }
            }
        }

        public void End()
        {
            if (!_first)
            {
                if (_colorMappings != null)
                {
                    _flusher("</pre></body><html>");
                }
            }
        }

        public string ContentType => $"Content-Type: text/{(_colorMappings == null ? "plain" : "html")}; charset=UTF-8";
    }
}
