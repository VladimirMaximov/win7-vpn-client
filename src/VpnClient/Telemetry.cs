using System;
using System.IO;
using System.Net;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace MihomoDesk
{
    public sealed class TrafficSample
    {
        public long Up, Down;
        public bool Reachable, Probed, MetricsAvailable;
    }

    public static class Telemetry
    {
        public static void Configure(Dictionary<string, object> config, bool xray, int apiPort, string secret)
        {
            if (!xray)
            {
                config["external-controller"] = "127.0.0.1:" + apiPort;
                config["secret"] = secret;
                return;
            }

            config["api"] = Profiles.Map("tag", "stats-api", "services", new string[] { "StatsService" });
            config["stats"] = Profiles.Map();
            config["policy"] = Profiles.Map("system", Profiles.Map("statsOutboundUplink", true, "statsOutboundDownlink", true));
            List<object> inbounds = new List<object>();
            foreach (object inbound in (IEnumerable)config["inbounds"])
                inbounds.Add(inbound);
            inbounds.Add(Profiles.Map("tag", "stats-api", "listen", "127.0.0.1", "port", apiPort, "protocol", "dokodemo-door", "settings", Profiles.Map("address", "127.0.0.1")));
            config["inbounds"] = inbounds;
            config["routing"] = Profiles.Map("rules", new object[] { Profiles.Map("type", "field", "inboundTag", new string[] { "stats-api" }, "outboundTag", "stats-api") });
        }

        static long Number(object value)
        {
            long n;
            return Int64.TryParse(Convert.ToString(value), out n) ? n : 0;
        }

        public static TrafficSample Read(bool xray, string binary, int apiPort, string secret)
        {
            string json;
            if (xray)
            {
                ProcessStartInfo s = new ProcessStartInfo(binary, "api statsquery --server=127.0.0.1:" + apiPort + " --pattern=outbound>>>proxy>>>traffic>>>");
                s.UseShellExecute = false;
                s.CreateNoWindow = true;
                s.RedirectStandardOutput = true;
                s.RedirectStandardError = true;
                using (Process p = Process.Start(s))
                {
                    if (!p.WaitForExit(1800))
                    {
                        p.Kill();
                        throw new IOException("Statistics timeout");
                    }

                    json = p.StandardOutput.ReadToEnd();
                    if (p.ExitCode != 0)
                        throw new IOException("Statistics unavailable");
                }
            }
            else
            {
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + apiPort + "/connections");
                r.Proxy = null;
                r.Timeout = 1500;
                r.ReadWriteTimeout = 1500;
                r.Headers["Authorization"] = "Bearer " + secret;
                using (WebResponse response = r.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    json = reader.ReadToEnd();
                }
            }

            Dictionary<string, object> d = Profiles.Parse(json) as Dictionary<string, object>;
            TrafficSample sample = new TrafficSample();
            if (d == null)
                throw new IOException("Statistics format");
            if (xray)
            {
                object stats;
                if (d.TryGetValue("stat", out stats) && stats is IList)
                    foreach (object raw in (IList)stats)
                    {
                        Dictionary<string, object> item = raw as Dictionary<string, object>;
                        if (item == null)
                            continue;
                        string name = Profiles.Get(item, "name", "");
                        object value;
                        item.TryGetValue("value", out value);
                        if (name.EndsWith(">>>uplink"))
                            sample.Up += Number(value);
                        if (name.EndsWith(">>>downlink"))
                            sample.Down += Number(value);
                    }
            }
            else
            {
                sample.Up = Number(d["uploadTotal"]);
                sample.Down = Number(d["downloadTotal"]);
            }

            sample.MetricsAvailable = true;
            return sample;
        }

        public static bool Probe(int port)
        {
            try
            {
                return ProbeRunner.MeasureProxy(port) > 0;
            }
            catch
            {
                return false;
            }
        }

        public static string Bytes(double b)
        {
            string[] units =
            {
                "Б",
                "КБ",
                "МБ",
                "ГБ"
            };
            int i = 0;
            while (b >= 1024 && i < units.Length - 1)
            {
                b /= 1024;
                i++;
            }

            return b.ToString(i == 0 ? "0" : "0.0") + " " + units[i];
        }
    }
}
