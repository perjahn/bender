using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Bender
{
    public class PerformanceCounterClient
    {
        public class Value
        {
            public DateTime When;
            public double Cnt, Avg, Min, Max;

            public Value()
            {
                When = DateTime.MinValue;
                Cnt = Avg = Min = Max = double.NaN;
            }

            public Value(BinaryReader reader)
            {
                When = DateTime.FromBinary(reader.ReadInt64());
                Cnt = reader.ReadDouble();
                Avg = reader.ReadDouble();
                Min = reader.ReadDouble();
                Max = reader.ReadDouble();
            }

            public void WriteTo(BinaryWriter writer)
            {
                writer.Write(When.ToBinary());
                writer.Write(Cnt);
                writer.Write(Avg);
                writer.Write(Min);
                writer.Write(Max);
            }
        };

        private static Value Get(string key)
        {
            var client = new UdpClient();
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            bw.Write(key);
            bw.Flush();
            var request = ms.ToArray();
            client.Send(request, request.Length, new IPEndPoint(IPAddress.Loopback, int.Parse(ConfigurationManager.AppSettings["CPCServerPort"])));
            client.Client.ReceiveTimeout = 1000;
            IPEndPoint remote = default(IPEndPoint);
            try
            {
                var response = client.Receive(ref remote);
                return new Value(new BinaryReader(new MemoryStream(response)));
            }
            catch (SocketException)
            {
                return new Value();
            }
        }

        public static DateTime GetDate(string key)
        {
            return Get(key).When;
        }

        public static double GetValue(string key, int index)
        {
            var value = Get(key);

            if (index == 0)
            {
                return value.Cnt;
            }
            else if (index == 1)
            {
                return value.Avg;
            }
            else if (index == 2)
            {
                return value.Min;
            }
            else if (index == 3)
            {
                return value.Max;
            }

            return double.NaN;
        }
    }
}
