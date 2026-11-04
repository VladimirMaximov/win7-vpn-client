using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;

namespace MihomoDesk
{
    static class ProbeRunner
    {
        static readonly object Gate = new object ();
        static readonly List<Process> Active = new List<Process>();
        static string Quote(string s)
        {
            return "\"" + s + "\"";
        }

        public static long MeasureProxy(int port)
        {
            // Separate endpoints help distinguish a blocked check site from a broken tunnel.
            string[] endpoints = { "https://www.gstatic.com/generate_204", "https://example.com/" };
            Exception failure = null;
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            for (int i = 0; i < endpoints.Length; i++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(endpoints[i]);
                request.Method = "GET";
                request.AllowAutoRedirect = false;
                request.KeepAlive = false;
                request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                request.Proxy = new WebProxy("http://127.0.0.1:" + port);
                request.Timeout = 4000;
                request.ReadWriteTimeout = 4000;
                try
                {
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
                            throw new IOException("Probe response");
                    }
                    Diagnostics.Write("probe.endpoint", "index=" + i + " success=True elapsedMs=" + watch.ElapsedMilliseconds);
                    return Math.Max(1, watch.ElapsedMilliseconds);
                }
                catch (Exception e)
                {
                    failure = e;
                    Diagnostics.Write("probe.endpoint", "index=" + i + " success=False elapsedMs=" + watch.ElapsedMilliseconds);
                    Diagnostics.Failure("probe.endpoint-" + i, e);
                }
                finally { request.Abort(); }
            }
            throw new IOException("HTTPS connectivity checks failed", failure);
        }

        public static long Measure(Dictionary<string, object> node)
        {
            string root = Path.Combine(Program.Home, "probes");
            Directory.CreateDirectory(root);
            string folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "probe.json");
            Process child = null;
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                bool xray = Profiles.CanXray(node);
                List<Dictionary<string, object>> nodes = new List<Dictionary<string, object>>();
                nodes.Add(node);
                File.WriteAllText(file, Profiles.Json(xray ? Profiles.XrayConfig(nodes, 0, port) : Profiles.Config(nodes, 0, port)), new System.Text.UTF8Encoding(false));
                string binary = Assets.Get(xray ? "xray.exe" : "mihomo.exe");
                string args = xray ? "run -c " + Quote(file) : "-d " + Quote(folder) + " -f " + Quote(file);
                ProcessStartInfo start = new ProcessStartInfo(binary, args);
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                child = new Process();
                child.StartInfo = start;
                child.OutputDataReceived += delegate (object sender, DataReceivedEventArgs e)
                {
                    Diagnostics.Core(e.Data, true);
                };
                child.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs e)
                {
                    Diagnostics.Core(e.Data, true);
                };
                child.Start();
                child.BeginOutputReadLine();
                child.BeginErrorReadLine();
                lock (Gate)
                    Active.Add(child);
                Process parent = Process.GetCurrentProcess();
                ProcessStartInfo guard = new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath, "--watch-probe " + parent.Id + " " + parent.StartTime.ToUniversalTime().Ticks + " " + child.Id + " " + child.StartTime.ToUniversalTime().Ticks + " " + Quote(folder));
                guard.UseShellExecute = false;
                guard.CreateNoWindow = true;
                Process.Start(guard).Dispose();
                bool ready = false;
                for (int i = 0; i < 40; i++)
                {
                    if (child.HasExited)
                        throw new IOException("Probe engine stopped");
                    using (TcpClient c = new TcpClient())
                    {
                        try
                        {
                            IAsyncResult result = c.BeginConnect(IPAddress.Loopback, port, null, null);
                            if (result.AsyncWaitHandle.WaitOne(100, false))
                            {
                                c.EndConnect(result);
                                ready = true;
                            }

                            result.AsyncWaitHandle.Close();
                        }
                        catch
                        {
                        }
                    }

                    if (ready)
                        break;
                    Thread.Sleep(100);
                }

                if (!ready)
                    throw new IOException("Probe listener timeout");
                return MeasureProxy(port);
            }
            finally
            {
                if (child != null)
                {
                    lock (Gate)
                        Active.Remove(child);
                    try
                    {
                        if (!child.HasExited)
                        {
                            child.Kill();
                            child.WaitForExit(2000);
                        }
                    }
                    catch
                    {
                    }

                    child.Dispose();
                }

                try
                {
                    Directory.Delete(folder, true);
                }
                catch
                {
                }
            }
        }

        public static void CancelAll()
        {
            lock (Gate)
                foreach (Process p in Active)
                    try
                    {
                        if (!p.HasExited)
                            p.Kill();
                    }
                    catch
                    {
                    }
        }

        public static void Supervise(string[] args)
        {
            int parentId = Int32.Parse(args[1]), childId = Int32.Parse(args[3]);
            long parentTicks = Int64.Parse(args[2]), childTicks = Int64.Parse(args[4]);
            string folder = args[5];
            try
            {
                using (Process p = Process.GetProcessById(parentId))
                using (Process child = Process.GetProcessById(childId))
                {
                    if (child.StartTime.ToUniversalTime().Ticks != childTicks)
                        return;
                    if (p.StartTime.ToUniversalTime().Ticks == parentTicks)
                    {
                        for (int i = 0; i < 35; i++)
                        {
                            if (child.WaitForExit(1000))
                                return;
                            if (p.HasExited)
                                break;
                        }
                    }

                    if (!child.HasExited)
                        child.Kill();
                }
            }
            catch
            {
                try
                {
                    using (Process child = Process.GetProcessById(childId))
                    {
                        if (child.StartTime.ToUniversalTime().Ticks == childTicks && !child.HasExited)
                            child.Kill();
                    }
                }
                catch
                {
                }
            }

            try
            {
                string allowed = Path.GetFullPath(Path.Combine(Program.Home, "probes")) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(folder);
                if (full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(full, true);
            }
            catch
            {
            }
        }
    }
}
