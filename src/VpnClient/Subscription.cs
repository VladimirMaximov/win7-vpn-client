using System;
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace MihomoDesk
{
    public sealed class SubscriptionResult
    {
        public string Url;
        public List<Dictionary<string, object>> Nodes;
        public DateTime UpdatedUtc;
    }

    public static class Subscriptions
    {
        public static Uri Validate(string text)
        {
            Uri uri;
            if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http") || uri.UserInfo != "")
                throw new InvalidDataException("Нужна HTTP- или HTTPS-ссылка подписки без логина и пароля в адресе.");
            UriBuilder clean = new UriBuilder(uri);
            clean.Fragment = "";
            return clean.Uri;
        }

        public static bool IsSubscriptionLink(string text)
        {
            string input = text.Trim();
            return input.IndexOf('\n') < 0 && input.IndexOf('\r') < 0 && (input.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || input.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        }

        public static SubscriptionResult ImportInput(string text)
        {
            return ImportInputWith(text, Fetch);
        }

        public static SubscriptionResult ImportInputWith(string text, Func<Uri, string, string> fetch)
        {
            string input = text.Trim();
            Diagnostics.Input(input);
            if (IsSubscriptionLink(input))
                return DownloadWith(input, fetch);
            List<Dictionary<string, object>> nodes = Profiles.Import(input);
            nodes.RemoveAll(delegate (Dictionary<string, object> node)
            {
                return !Profiles.Usable(node);
            });
            if (nodes.Count == 0)
                throw new InvalidDataException("В настройках нет рабочих серверов.");
            Diagnostics.Nodes(nodes, "direct-import");
            return new SubscriptionResult
            {
                Url = "",
                Nodes = nodes,
                UpdatedUtc = DateTime.MinValue
            };
        }

        public static Uri ValidateRedirect(Uri source, Uri target)
        {
            Uri next = Validate(target.AbsoluteUri);
            if (source.Scheme == "https" && next.Scheme != "https")
                throw new InvalidDataException("Сервер перенаправил защищённую ссылку на HTTP. Такое перенаправление не выполняется.");
            return next;
        }

        public static SubscriptionResult Download(string url)
        {
            return DownloadWith(url, Fetch);
        }

        public static SubscriptionResult DownloadWith(string url, Func<Uri, string, string> fetch)
        {
            Uri uri = Validate(url);
            string[] agents =
            {
                "clash.meta/1.19.33",
                "v2rayN/7.15.7",
                "Hiddify/2.5.7",
                "Happ/3.0.0"
            };
            Exception last = null;
            foreach (string agent in agents)
                try
                {
                    string body = fetch(uri, agent);
                    List<Dictionary<string, object>> nodes = Profiles.Import(body);
                    Diagnostics.Nodes(nodes, "subscription-candidate");
                    nodes.RemoveAll(delegate (Dictionary<string, object> n)
                    {
                        return !Profiles.Usable(n);
                    });
                    if (nodes.Count == 0)
                    {
                        Diagnostics.Write("subscription.rejected", "reason=placeholder-only userAgent=" + agent);
                        continue;
                    }

                    Diagnostics.Write("subscription.accepted", "userAgent=" + agent + " nodes=" + nodes.Count);
                    return new SubscriptionResult
                    {
                        Url = uri.AbsoluteUri,
                        Nodes = nodes,
                        UpdatedUtc = DateTime.UtcNow
                    };
                }
                catch (Exception e)
                {
                    last = e;
                    Diagnostics.Failure("subscription.candidate", e);
                }

            throw new InvalidDataException("Не удалось получить рабочие серверы. Прежняя подписка и серверы сохранены. Провайдер может ограничивать сторонние приложения; подробности — в журнале.", last);
        }

        public static string Fetch(Uri uri, string agent)
        {
            for (int redirects = 0; redirects <= 3; redirects++)
            {
                if (uri.Scheme == "https")
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                HttpWebRequest r = (HttpWebRequest)WebRequest.Create(uri);
                r.AllowAutoRedirect = false;
                r.Timeout = 15000;
                r.ReadWriteTimeout = 15000;
                r.UserAgent = agent;
                r.Proxy = null;
                r.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                Diagnostics.Write("subscription.request", "scheme=" + uri.Scheme + " userAgent=" + agent + " redirectStep=" + redirects);
                using (HttpWebResponse response = (HttpWebResponse)r.GetResponse())
                {
                    int code = (int)response.StatusCode;
                    Diagnostics.Write("subscription.response", "http=" + code + " contentLength=" + response.ContentLength);
                    if (code >= 300 && code <= 399)
                    {
                        Uri target;
                        string location = response.Headers["Location"];
                        if (redirects == 3 || String.IsNullOrEmpty(location) || !Uri.TryCreate(uri, location, out target))
                            throw new InvalidDataException("Слишком много или неверное перенаправление подписки.");
                        uri = ValidateRedirect(uri, target);
                        continue;
                    }

                    if (code != 200)
                        throw new InvalidDataException("Сервер подписки не вернул конфигурацию.");
                    using (Stream stream = response.GetResponseStream())
                    using (MemoryStream bytes = new MemoryStream())
                    {
                        byte[] buffer = new byte[8192];
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (bytes.Length + read > 4 * 1024 * 1024)
                                throw new InvalidDataException("Подписка больше 4 МБ.");
                            bytes.Write(buffer, 0, read);
                        }

                        Diagnostics.Write("subscription.downloaded", "bytes=" + bytes.Length);
                        return Encoding.UTF8.GetString(bytes.ToArray());
                    }
                }
            }

            throw new InvalidDataException("Не удалось загрузить подписку.");
        }

        public static string NodeKey(Dictionary<string, object> n)
        {
            string s = Profiles.Get(n, "type", "") + "|" + Profiles.Get(n, "server", "") + "|" + Profiles.Get(n, "port", "") + "|" + Profiles.Get(n, "uuid", "");
            using (SHA256 hash = SHA256.Create())
                return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(s)));
        }

        public static int Selection(List<Dictionary<string, object>> nodes, string key)
        {
            for (int i = 0; i < nodes.Count; i++)
                if (NodeKey(nodes[i]) == key)
                    return i;
            return nodes.Count > 0 ? 0 : -1;
        }

        public static Dictionary<string, object> Envelope(string url, List<Dictionary<string, object>> nodes, string selection, DateTime updated)
        {
            return Profiles.Map("url", url, "proxies", nodes, "selected", selection, "updatedUtc", updated.ToUniversalTime().ToString("o"));
        }
    }
}
