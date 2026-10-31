using System;
using System.IO;
using System.Net;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;

namespace MihomoDesk
{
    public static class Diagnostics
    {
        public static string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MihomoDesk");
        public static string FilePath
        {
            get
            {
                return Path.Combine(Folder, "diagnostics.log");
            }
        }

        static readonly object Gate = new object ();
        // Only fixed event names and allowlisted structural values enter the persistent log.
        public static void Write(string eventName, string safeFields)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Folder);
                    if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1048576)
                    {
                        string old = FilePath + ".1", older = FilePath + ".2";
                        if (File.Exists(older))
                            File.Delete(older);
                        if (File.Exists(old))
                            File.Move(old, older);
                        File.Move(FilePath, old);
                    }

                    File.AppendAllText(FilePath, DateTime.UtcNow.ToString("o") + " pid=" + Process.GetCurrentProcess().Id + " " + eventName + " " + safeFields + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
            } // A logging failure must never prevent proxy recovery.
        }

        public static void Failure(string stage, Exception e)
        {
            int depth = 0;
            while (e != null && depth++ < 6)
            {
                string fields = "stage=" + stage + " exception=" + e.GetType().Name;
                WebException w = e as WebException;
                if (w != null)
                {
                    fields += " webStatus=" + w.Status;
                    HttpWebResponse r = w.Response as HttpWebResponse;
                    if (r != null)
                        fields += " http=" + (int)r.StatusCode;
                }

                System.Net.Sockets.SocketException socket = e as System.Net.Sockets.SocketException;
                if (socket != null)
                    fields += " socket=" + socket.SocketErrorCode;
                Write("error", fields);
                e = e.InnerException;
            }
        }

        public static string Protocol(string s)
        {
            switch (s)
            {
                case "vless":
                case "vmess":
                case "hysteria2":
                case "hysteria":
                case "trojan":
                case "ss":
                case "tuic":
                case "socks5":
                case "http":
                    return s;
                default:
                    return "other";
            }
        }

        public static void Nodes(List<Dictionary<string, object>> nodes, string stage)
        {
            Write("profiles.summary", "stage=" + stage + " count=" + nodes.Count);
            int i = 0;
            foreach (Dictionary<string, object> n in nodes)
            {
                string name = Profiles.Get(n, "name", "").ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
                string server = Profiles.Get(n, "server", "");
                Write("profiles.node", "index=" + (i++) + " type=" + Protocol(Profiles.Get(n, "type", "")) + " tls=" + (Profiles.Get(n, "tls", "false").ToLowerInvariant() == "true") + " reality=" + n.ContainsKey("reality-opts") + " placeholder=" + (server == "0.0.0.0" || server == "::") + " appUnsupported=" + (name.Contains("notsupported") || name.Contains("unsupported")));
            }
        }

        public static void Input(string text)
        {
            string t = text.Trim();
            Write("import.begin", "characters=" + t.Length + " format=" + (t.StartsWith("{") ? "json" : t.Contains("proxies:") ? "yaml" : t.Contains("://") ? "links" : "base64-or-text"));
        }

        public static void Core(string line, bool test)
        {
            if (String.IsNullOrEmpty(line))
                return;
            string s = line.ToLowerInvariant();
            string level = s.Contains("level=error") || s.Contains("level=fatal") ? "error" : s.Contains("level=warning") ? "warning" : "info";
            string category = s.Contains("certificate") || s.Contains("x509") ? "certificate" : s.Contains("timeout") || s.Contains("timed out") ? "timeout" : s.Contains("no such host") || s.Contains("dns") ? "dns" : s.Contains("refused") ? "connection-refused" : s.Contains("unreachable") ? "unreachable" : s.Contains("handshake") || s.Contains("tls") ? "tls-handshake" : s.Contains("eof") ? "unexpected-eof" : s.Contains("authentication") || s.Contains("invalid user") ? "authentication" : s.Contains("listen") || s.Contains("address already in use") ? "listener" : s.Contains("test is successful") ? "config-valid" : s.Contains("test failed") || s.Contains("parse") || s.Contains("expected type") ? "configuration" : s.Contains("dial") ? "dial" : "other";
            Write("core.event", "test=" + test + " level=" + level + " category=" + category);
        }
    }
}
