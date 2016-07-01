using System;
using System.IO;
using System.Net;
using System.Text;

namespace Bender
{
    internal class FetchUri
    {
        public static void Fetch(string path, Stream output)
        {
            Fetch(path, output, true);
        }

        public static void FetchHeaders(string path, Stream output)
        {
            Fetch(path, output, false);
        }

        public static void Fetch(string path, Stream output, bool body)
        {
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                HttpWebResponse response;
                try
                {
                    var req = WebRequest.Create(path) as HttpWebRequest;
                    // ReSharper disable once PossibleNullReferenceException
                    req.UserAgent = "Mozilla/Bender";
                    response = req.GetResponse() as HttpWebResponse;
                }
                catch (WebException we)
                {
                    response = we.Response as HttpWebResponse;
                }
                if (response != null)
                {
                    using (response)
                    {
                        using (var stm = response.GetResponseStream())
                        {
                            var bytes = Encoding.ASCII.GetBytes($"HTTP/1.1 {(int) response.StatusCode} {response.StatusDescription}");
                            output.Write(bytes, 0, bytes.Length);
                            if (body)
                            {
                                bytes = Encoding.ASCII.GetBytes("\r\n");
                                output.Write(bytes, 0, bytes.Length);
                                // ReSharper disable once PossibleNullReferenceException
                                stm.CopyTo(output);
                            }
                        }
                    }
                }
            }
        }
    }
}
