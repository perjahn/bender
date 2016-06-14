using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Bender
{
    class Shell
    {
        public static void Do(Socket peer, Stream input, Stream output)
        {
            Do(peer, input, output, "cmd.exe", string.Empty, true);
        }

        public static void Spawn(string arguments)
        {
            var proc = new Process { StartInfo = { FileName = "cmd.exe", Arguments = arguments, UseShellExecute = false } };
            proc.Start();
        }

        delegate void Sender(string s);

        private static void SafeKillProcess(Process proc)
        {
            try
            {
                proc.Kill();
            }
            catch (Win32Exception e)
            {
                if (e.NativeErrorCode != 5)
                {
                    throw;
                }
            }
            catch (InvalidOperationException)
            {

            }
        }

        public static void Do(Socket peer, Stream input, Stream output, string filename, string arguments, bool interactive)
        {
            if (peer != null)
            {
                peer.ReceiveTimeout = 0;
            }

            using (var proc = new Process { StartInfo = { RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true, FileName = filename, Arguments = arguments, UseShellExecute = false } })
            {
                proc.Start();
                var stdIn = proc.StandardInput;
                var open = 2;
                var done = false;
                var doneLock = new object();

                Sender sendOutput = delegate (string s)
                {
                    if (s == null)
                    {
                        if (Interlocked.Decrement(ref open) == 0)
                        {
                            if (peer == null)
                            {
                                output.Close();
                            }
                            else
                            {
                                output.Flush();
                                peer.Shutdown(SocketShutdown.Send);
                            }

                            lock (doneLock)
                            {
                                done = true;
                                Monitor.PulseAll(doneLock);
                            }
                        }
                    }
                    else
                    {
                        var bytes = Encoding.ASCII.GetBytes(s + "\r\n");
                        try
                        {
                            output.Write(bytes, 0, bytes.Length);
                        }
                        catch (IOException)
                        {
                            // ReSharper disable once AccessToDisposedClosure
                            SafeKillProcess(proc);
                        }
                    }
                };

                proc.OutputDataReceived += (sender, args) =>
                {
                    sendOutput(args.Data);
                };

                proc.ErrorDataReceived += (sender, args) =>
                {
                    sendOutput(args.Data);
                };

                proc.BeginErrorReadLine();
                proc.BeginOutputReadLine();

                if (input != null && interactive)
                {
                    var buf = new byte[512];
                    try
                    {
                        try
                        {
                            while (true)
                            {
                                var read = input.Read(buf, 0, buf.Length);
                                if (read == 0)
                                {
                                    break;
                                }
                                stdIn.Write(Encoding.ASCII.GetString(buf, 0, read));
                            }
                        }
                        catch (IOException)
                        {
                        }

                        // Input is lost
                        if (peer == null)
                        {
                            input.Close();
                        }
                        else
                        {
                            peer.Shutdown(SocketShutdown.Receive);
                        }

                        // Wait 1 second for process to shut down orderly, then kill
                        if (!proc.WaitForExit(1000))
                        {
                            SafeKillProcess(proc);
                        }
                    }
                    // ReSharper disable EmptyGeneralCatchClause
                    catch (Exception e)
                    // ReSharper restore EmptyGeneralCatchClause
                    {
                        Bender.LogError(e);
                        proc.Kill();
                    }
                }

                lock (doneLock)
                {
                    while (!done)
                    {
                        Monitor.Wait(doneLock);
                    }
                }

                proc.WaitForExit();

                input?.Close();

                output?.Close();
            }
        }
    }
}
