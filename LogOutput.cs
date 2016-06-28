﻿using System;
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

        private readonly bool _nl;

        private bool _first = true;

        private bool _changeCol = false;

        private readonly string _title;

        public LogOutput(Stream net, string title, bool nl, Dictionary<Regex, string> colorMappings)
        {
            _net = net;
            _colorMappings = colorMappings;
            _nl = nl;
            _title = title;

            Write($"HTTP/1.1 200 OK\nAccess-Control-Allow-Origin: *\n{ContentType}\nTransfer-Encoding: Chunked\nX-Accel-Buffering: no\n\n");
        }

        private void WriteChunked(string s)
        {
            var sbuf = Encoding.UTF8.GetBytes(s);
            var lbuf = Encoding.ASCII.GetBytes($"{sbuf.Length:X}\n");
            _net.Write(lbuf, 0, lbuf.Length);
            _net.Write(sbuf, 0, sbuf.Length);
            _net.Write(new byte[] { 10 }, 0, 1);
        }

        private void Write(string s)
        {
            var sbuf = Encoding.UTF8.GetBytes(s);
            _net.Write(sbuf, 0, sbuf.Length);
        }

        public void Add(string s)
        {
            if (_first)
            {
                var clrString = _colorMappings == null ? "style =\"background-color:#FFFFFF;color:#000000\"" : "style =\"background-color:#000000;color:#FFC200\"";

                WriteChunked($"<html><head><title>{HttpUtility.HtmlEncode(_title)}</title></head><body {clrString}><pre>");

                _first = false;
            }

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

                if (_colorMappings != null)
                {
                    foreach (var kvp in _colorMappings)
                    {
                        outs = kvp.Key.Replace(outs, $"<span style=\"color:{kvp.Value}\">$0</span>");
                    }
                }

                if (_changeCol)
                {
                    outs = $"<span style=\"color:{(_colorMappings == null ? "1F1FBF" : "BD8000")}\">{outs}</span>";
                }

                if (_nl)
                {
                    outs = outs.Replace("||", "\r");
                }

                WriteChunked(outs);

                WriteChunked("<script type=\"text/javascript\">window.scrollTo(0,document.body.scrollHeight);</script>\r");

                if (end == -1) break;

                _changeCol = !_changeCol;
                start = end + 1;
            }
        }

        public void Add(byte[] buf, int offset, int count)
        {
            Add(Encoding.UTF8.GetString(buf, offset, count));
        }

        public void End()
        {
            if (!_first)
            {
                WriteChunked("</pre></body><html>");
            }

            WriteChunked(string.Empty);
        }

        public string ContentType => $"Content-Type: text/html; charset=UTF-8";
    }
}
