using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Bender
{
    public class Remote
    {
        private static Socket Connect(string remote)
        {
            var remoteServers = Bender.ReadServers();
            string remoteServer;
            if (remoteServers.TryGetValue(remote, out remoteServer))
            {
                var s = remoteServer.Split(':');
                var ip = IPAddress.Parse(s[0]);
                var port = int.Parse(s.Length == 2 ? s[1] : Bender.ListenPort);
                var sock = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                sock.Connect(ip, port);
                sock.ReceiveTimeout = 10000;
                return sock;
            }

            return null;
        }

        public static Stream ConnectStream(string remote)
        {
            return new NetworkStream(Connect(remote));
        }

        public static void Do(Socket peer, Stream input, Stream output)
        {
            var host = Bender.ReadLine(input);
            using (var sock = Connect(host))
            {
                if (peer != null)
                {
                    sock.ReceiveTimeout = peer.ReceiveTimeout;
                }
                var buf = new byte[512];

                Action startRead = null;

                var locko = new object();
                var reading = true;

                startRead = () =>
                {
                    // ReSharper disable once AccessToDisposedClosure
                    sock.BeginReceive(buf, 0, buf.Length, SocketFlags.None, ar =>
                    {
                        // ReSharper disable once AccessToDisposedClosure
                        var read = sock.EndReceive(ar);
                        if (read == 0)
                        {
                            output.Close();
                            peer?.Shutdown(SocketShutdown.Send);

                            lock (locko)
                            {
                                reading = false;
                                Monitor.PulseAll(locko);
                            }
                        }
                        else
                        {
                            output.Write(buf, 0, read);
                            // ReSharper disable once PossibleNullReferenceException
                            // ReSharper disable once AccessToModifiedClosure
                            startRead();
                        }
                    }, null);
                };

                startRead();

                var buf2 = new byte[512];
                while (true)
                {
                    var read = input.Read(buf2, 0, buf2.Length);
                    if (read == 0)
                    {
                        sock.Shutdown(SocketShutdown.Send);
                        break;
                    }
                    else
                    {
                        var data = buf2.Take(read).Where(b => b != 0xd).ToArray();
                        sock.Send(data, 0, data.Length, SocketFlags.None);
                    }
                }

                lock (locko)
                {
                    while (reading)
                    {
                        Monitor.Wait(locko);
                    }
                }
            }
        }
    }
}
