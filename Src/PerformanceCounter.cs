using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Bender
{
    class PerformanceCounter
    {
        class Holder
        {
            public float Value;

            public System.Diagnostics.PerformanceCounter Counter;
        };

        private static readonly Dictionary<string, Holder> Counters = new Dictionary<string, Holder>(StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static Thread _worker;

        private static readonly Regex InstanceRegex = new Regex("^(?<instance>.+)_p(?<pid>[0-9]+)_r(?<ref>[0-9]+)$");

        private const int MaxInstanceLength = 64;

        public static float GetValue(string counter)
        {
            // \Processor Information(_Total)\% Processor Time
            lock (Counters)
            {
                Holder pc;
                if (Counters.TryGetValue(counter, out pc))
                {
                    return pc.Value;
                }

                Pending.Add(counter);
            }

            if (_worker == null)
            {
                _worker = new Thread(o =>
                {
                    while (true)
                    {
                        List<string> pending;
                        List<string> dead = new List<string>();
                        lock (Counters)
                        {
                            pending = Pending.ToList();
                            Pending.Clear();
                        }

                        foreach (var p in pending)
                        {
                            try
                            {
                                var split = p.Split('\\');
                                var ctr = split[2];
                                split = split[1].Split('(');
                                var cat = split[0];
                                string inst = string.Empty;

                                if (split.Length > 1)
                                {
                                    inst = split[1].Remove(split[1].Length - 1);
                                }

                                foreach (var instance in new PerformanceCounterCategory(cat).GetInstanceNames())
                                {
                                    var truncated = instance.Length == MaxInstanceLength;
                                    var usedInstance = instance;
                                    var m = InstanceRegex.Match(usedInstance);
                                    if (m.Success)
                                    {
                                        usedInstance = m.Groups["instance"].Value;
                                    }

                                    if (inst.Equals(usedInstance, StringComparison.OrdinalIgnoreCase) || truncated && inst.Length > usedInstance.Length && inst.StartsWith(usedInstance))
                                    {
                                        inst = instance;
                                        break;
                                    }
                                }

                                var c = new System.Diagnostics.PerformanceCounter(cat, ctr, inst);

                                lock (Counters)
                                {
                                    Counters.Add(p, new Holder { Counter = c });
                                }
                            }
                            catch (Exception e)
                            {
                                Bender.LogError(e);
                            }
                        }

                        List<KeyValuePair<string, Holder>> pcs;
                        lock (Counters)
                        {
                            pcs = Counters.ToList();
                        }

                        foreach (var pcc in pcs)
                        {
                            try
                            {
                                pcc.Value.Value = pcc.Value.Counter.NextValue();
                            }
                            catch (Exception e)
                            {
                                Bender.LogError(e);
                                dead.Add(pcc.Key);
                            }
                        }

                        lock (Counters)
                        {
                            foreach (var remove in dead)
                            {
                                Counters.Remove(remove);
                            }
                        }

                        Thread.Sleep(TimeSpan.FromSeconds(10));
                    }
                    // ReSharper disable once FunctionNeverReturns
                })
                { IsBackground = true };
                _worker.Start();
            }

            return float.NaN;
        }
    }
}
