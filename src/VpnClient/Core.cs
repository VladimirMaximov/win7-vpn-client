using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using YamlDotNet.Serialization;

namespace MihomoDesk
{
    public static class Profiles
    {
        public static Dictionary<string, object> Map(params object[] kv)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            for (int i = 0; i < kv.Length; i += 2)
                d.Add((string)kv[i], kv[i + 1]);
            return d;
        }

        public static object Normalize(object x)
        {
            IDictionary dict = x as IDictionary;
            if (dict != null)
            {
                Dictionary<string, object> d = Map();
                foreach (DictionaryEntry e in dict)
                    d.Add(Convert.ToString(e.Key, CultureInfo.InvariantCulture), Normalize(e.Value));
                return d;
            }

            if (x is IList)
            {
                List<object> a = new List<object>();
                foreach (object v in (IList)x)
                    a.Add(Normalize(v));
                return a;
            }

            return x;
        }

        static object ReadNode(YamlDotNet.RepresentationModel.YamlNode node)
        {
            YamlDotNet.RepresentationModel.YamlMappingNode map = node as YamlDotNet.RepresentationModel.YamlMappingNode;
            if (map != null)
            {
                Dictionary<string, object> d = Map();
                foreach (KeyValuePair<YamlDotNet.RepresentationModel.YamlNode, YamlDotNet.RepresentationModel.YamlNode> pair in map.Children)
                {
                    YamlDotNet.RepresentationModel.YamlScalarNode key = pair.Key as YamlDotNet.RepresentationModel.YamlScalarNode;
                    if (key == null)
                        throw new Exception();
                    d.Add(key.Value, ReadNode(pair.Value));
                }

                return d;
            }

            YamlDotNet.RepresentationModel.YamlSequenceNode list = node as YamlDotNet.RepresentationModel.YamlSequenceNode;
            if (list != null)
            {
                List<object> a = new List<object>();
                foreach (YamlDotNet.RepresentationModel.YamlNode n in list.Children)
                    a.Add(ReadNode(n));
                return a;
            }

            YamlDotNet.RepresentationModel.YamlScalarNode scalar = node as YamlDotNet.RepresentationModel.YamlScalarNode;
            if (scalar == null)
                throw new Exception();
            string v = scalar.Value ?? "";
            if (scalar.Style == YamlDotNet.Core.ScalarStyle.Plain || scalar.Style == YamlDotNet.Core.ScalarStyle.Any)
            {
                if (v == "" || v == "~" || v.Equals("null", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (v.Equals("true", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (v.Equals("false", StringComparison.OrdinalIgnoreCase))
                    return false;
                long integer;
                if (Int64.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                    return integer;
                double number;
                if (Double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !Double.IsInfinity(number) && !Double.IsNaN(number))
                    return number;
            }

            return v;
        }

        public static object Parse(string text)
        {
            if (text.Length > 4 * 1024 * 1024)
                throw new Exception("Профиль больше 4 МБ.");
            try
            {
                YamlDotNet.Core.Parser parser = new YamlDotNet.Core.Parser(new StringReader(text));
                int depth = 0, events = 0, documents = 0;
                while (parser.MoveNext())
                {
                    if (++events > 100000)
                        throw new Exception();
                    YamlDotNet.Core.Events.ParsingEvent ev = parser.Current;
                    if (ev is YamlDotNet.Core.Events.AnchorAlias)
                        throw new Exception();
                    YamlDotNet.Core.Events.NodeEvent n = ev as YamlDotNet.Core.Events.NodeEvent;
                    if (n != null && (!String.IsNullOrEmpty(n.Anchor) || !String.IsNullOrEmpty(n.Tag)))
                        throw new Exception();
                    if (ev is YamlDotNet.Core.Events.MappingStart || ev is YamlDotNet.Core.Events.SequenceStart)
                    {
                        if (++depth > 48)
                            throw new Exception();
                    }

                    if (ev is YamlDotNet.Core.Events.MappingEnd || ev is YamlDotNet.Core.Events.SequenceEnd)
                        depth--;
                    if (ev is YamlDotNet.Core.Events.DocumentStart && ++documents > 1)
                        throw new Exception();
                }

                YamlDotNet.RepresentationModel.YamlStream yaml = new YamlDotNet.RepresentationModel.YamlStream();
                yaml.Load(new StringReader(text));
                if (yaml.Documents.Count != 1)
                    throw new Exception();
                return ReadNode(yaml.Documents[0].RootNode);
            }
            catch
            {
                throw new Exception("Не удалось прочитать YAML/JSON. Нужен профиль Mihomo без YAML-якорей/тегов или список ссылок серверов.");
            }
        }

        public static string Get(Dictionary<string, object> d, string k, string def)
        {
            object x;
            return d.TryGetValue(k, out x) && x != null ? Convert.ToString(x, CultureInfo.InvariantCulture) : def;
        }

        public static string Decode64(string s)
        {
            s = s.Trim().Replace('-', '+').Replace('_', '/');
            return Encoding.UTF8.GetString(Convert.FromBase64String(s.PadRight((s.Length + 3) / 4 * 4, '=')));
        }

        static string Dec(string s)
        {
            return Uri.UnescapeDataString(s);
        }

        static Dictionary<string, string> Query(string q)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in q.Split('&'))
            {
                if (pair.Length == 0)
                    continue;
                int p = pair.IndexOf('=');
                string k = Dec(p < 0 ? pair : pair.Substring(0, p));
                if (d.ContainsKey(k))
                    throw new Exception("Повторяющийся параметр ссылки: " + k);
                d[k] = Dec(p < 0 ? "" : pair.Substring(p + 1));
            }

            return d;
        }

        static string Q(Dictionary<string, string> d, string k, string def)
        {
            string v;
            return d.TryGetValue(k, out v) ? v : def;
        }

        static int Port(string s)
        {
            int n;
            if (!Int32.TryParse(s, out n) || n < 1 || n > 65535)
                throw new Exception("Неверный порт сервера.");
            return n;
        }

        public static Dictionary<string, object> Link(string line, int index)
        {
            int sep = line.IndexOf("://");
            if (sep < 0)
                throw new Exception("Неверная ссылка сервера.");
            string scheme = line.Substring(0, sep).ToLowerInvariant();
            string tail = line.Substring(sep + 3);
            if (scheme == "vmess")
                return Vmess(tail, index);
            if (scheme == "ss" && tail.IndexOf('@') < 0)
            {
                int fragment = tail.IndexOf('#');
                string name = fragment < 0 ? "" : tail.Substring(fragment);
                tail = Decode64(fragment < 0 ? tail : tail.Substring(0, fragment)) + name;
            }

            string label = "Сервер " + index;
            int hash = tail.IndexOf('#');
            if (hash >= 0)
            {
                label = Dec(tail.Substring(hash + 1));
                tail = tail.Substring(0, hash);
            }

            int question = tail.IndexOf('?');
            Dictionary<string, string> q = Query(question < 0 ? "" : tail.Substring(question + 1));
            if (question >= 0)
                tail = tail.Substring(0, question);
            if (q.ContainsKey("finalmask") || q.ContainsKey("finalMask") || q.ContainsKey("encryption") && Q(q, "encryption", "") != "none" && Q(q, "encryption", "") != "")
                throw new Exception("Эта ссылка содержит расширение FinalMask/encryption. Импортируйте совместимый профиль Mihomo от провайдера.");
            if (Q(q, "allowInsecure", "0") == "1" || Q(q, "insecure", "0") == "1" || Q(q, "insecure", "") == "true")
                throw new Exception("Ссылка отключает проверку TLS. Нужен профиль с корректным сертификатом.");
            string allowed = scheme == "trojan" ? "|sni|peer|security|type|fp|alpn|allowinsecure|insecure|" : scheme == "ss" ? "|" : scheme == "vless" ? "|security|type|flow|sni|servername|fp|alpn|pbk|sid|path|host|servicename|packetencoding|encryption|allowinsecure|insecure|headertype|mode|pcs|" : "|sni|obfs|obfs-password|pinsha256|insecure|allowinsecure|fm|";
            foreach (string key in q.Keys)
                if (allowed.IndexOf("|" + key.ToLowerInvariant() + "|", StringComparison.Ordinal) < 0)
                    throw new Exception("Параметр ссылки «" + key + "» пока не поддерживается. Нужен профиль Mihomo с этим параметром.");
            if (Q(q, "headerType", "none") != "none")
                throw new Exception("Маскировка headerType не поддерживается импортом ссылок.");
            int at = tail.LastIndexOf('@');
            if (at <= 0)
                throw new Exception("В ссылке нет данных авторизации.");
            string auth = Dec(tail.Substring(0, at));
            string endpoint = tail.Substring(at + 1).TrimEnd('/');
            Uri host;
            try
            {
                host = new Uri("https://" + endpoint);
            }
            catch
            {
                throw new Exception("Неверный адрес сервера.");
            }

            if (host.AbsolutePath != "/" || host.UserInfo.Length != 0 || host.Host.Length == 0)
                throw new Exception("Неверный адрес сервера.");
            Dictionary<string, object> d = Map("name", label, "server", host.Host.Trim('[', ']'), "port", Port(host.Port.ToString(CultureInfo.InvariantCulture)), "udp", true);
            if (scheme == "vless")
            {
                Guid uuid;
                if (!TryGuid(auth, out uuid))
                    throw new Exception("В ссылке VLESS неверный UUID.");
                string security = Q(q, "security", "none"), net = Q(q, "type", "tcp");
                if (security != "none" && security != "tls" && security != "reality")
                    throw new Exception("Неизвестная защита VLESS: " + security);
                if (net != "tcp" && net != "ws" && net != "grpc" && net != "xhttp")
                    throw new Exception("Этот транспорт пока не поддерживается импортом ссылок.");
                d["type"] = "vless";
                d["uuid"] = auth;
                d["tls"] = security != "none";
                d["network"] = net;
                string flow = Q(q, "flow", "");
                if (flow != "" && flow != "xtls-rprx-vision")
                    throw new Exception("Неподдерживаемый flow VLESS.");
                if (flow != "")
                    d["flow"] = flow;
                string sni = Q(q, "sni", Q(q, "serverName", ""));
                if (sni != "")
                    d["servername"] = sni;
                string fp = Q(q, "fp", "");
                if (fp != "")
                    d["client-fingerprint"] = fp;
                string alpn = Q(q, "alpn", "");
                if (alpn != "")
                    d["alpn"] = alpn.Split(',');
                if (security == "reality")
                {
                    string pk = Q(q, "pbk", "");
                    if (pk == "")
                        throw new Exception("В REALITY отсутствует публичный ключ pbk.");
                    d["reality-opts"] = Map("public-key", pk, "short-id", Q(q, "sid", ""));
                    if (fp == "")
                        d["client-fingerprint"] = "chrome";
                }

                if (net == "ws")
                {
                    Dictionary<string, object> w = Map("path", Q(q, "path", "/"));
                    string h = Q(q, "host", "");
                    if (h != "")
                        w["headers"] = Map("Host", h);
                    d["ws-opts"] = w;
                }

                if (net == "grpc")
                    d["grpc-opts"] = Map("grpc-service-name", Q(q, "serviceName", ""));
                string packet = Q(q, "packetEncoding", "");
                if (packet != "")
                    d["packet-encoding"] = packet;
                if (net == "xhttp" || q.ContainsKey("pcs") || q.ContainsKey("mode"))
                {
                    Dictionary<string, object> stream = Map("network", net, "security", security);
                    if (security == "tls")
                    {
                        Dictionary<string, object> tls = Map("serverName", sni, "fingerprint", fp == "" ? "chrome" : fp);
                        if (alpn != "")
                            tls["alpn"] = alpn.Split(',');
                        if (Q(q, "pcs", "") != "")
                            tls["pinnedPeerCertSha256"] = Q(q, "pcs", "");
                        stream["tlsSettings"] = tls;
                    }

                    if (security == "reality")
                        stream["realitySettings"] = Map("serverName", sni, "fingerprint", fp == "" ? "chrome" : fp, "publicKey", Q(q, "pbk", ""), "shortId", Q(q, "sid", ""));
                    if (net == "xhttp")
                        stream["xhttpSettings"] = Map("path", Q(q, "path", "/"), "host", Q(q, "host", ""), "mode", Q(q, "mode", "auto"));
                    if (net == "grpc")
                        stream["grpcSettings"] = Map("serviceName", Q(q, "serviceName", ""), "multiMode", Q(q, "mode", "gun") == "multi");
                    if (net == "ws")
                        stream["wsSettings"] = Map("path", Q(q, "path", "/"), "headers", Map("Host", Q(q, "host", "")));
                    d["xray-outbound"] = Map("protocol", "vless", "settings", Map("vnext", new object[] { Map("address", d["server"], "port", d["port"], "users", new object[] { Map("id", auth, "encryption", "none", "flow", flow) }) }), "streamSettings", stream);
                }
            }
            else if (scheme == "trojan")
            {
                if (Q(q, "type", "tcp") != "tcp" || Q(q, "security", "tls") != "tls")
                    throw new Exception("Для этого варианта подключения нужен файл настроек.");
                d["type"] = "trojan";
                d["password"] = auth;
                d["sni"] = Q(q, "sni", Q(q, "peer", host.Host));
                string alpn = Q(q, "alpn", "");
                if (alpn != "")
                    d["alpn"] = alpn.Split(',');
                string fp = Q(q, "fp", "");
                if (fp != "")
                    d["client-fingerprint"] = fp;
            }
            else if (scheme == "ss")
            {
                if (auth.IndexOf(':') < 0)
                    auth = Decode64(auth);
                int colon = auth.IndexOf(':');
                if (colon < 1)
                    throw new Exception("Неверные реквизиты подключения.");
                d["type"] = "ss";
                d["cipher"] = auth.Substring(0, colon);
                d["password"] = auth.Substring(colon + 1);
            }
            else if (scheme == "hysteria2" || scheme == "hy2")
            {
                d["type"] = "hysteria2";
                d["password"] = auth;
                string sni = Q(q, "sni", "");
                if (sni != "")
                    d["sni"] = sni;
                string obfs = Q(q, "obfs", "");
                if (obfs != "")
                {
                    if (obfs != "salamander")
                        throw new Exception("Неизвестная обфускация Hysteria 2.");
                    d["obfs"] = obfs;
                    d["obfs-password"] = Q(q, "obfs-password", "");
                }

                string pin = Q(q, "pinSHA256", "");
                if (pin != "")
                    d["fingerprint"] = pin;
                if (q.ContainsKey("fm"))
                {
                    string encoded = Q(q, "fm", "");
                    if (encoded.Length > 16384)
                        throw new Exception("Слишком большой параметр расширения.");
                    Dictionary<string, object> mask = Parse(encoded.TrimStart().StartsWith("{") ? encoded : Decode64(encoded)) as Dictionary<string, object>;
                    if (mask == null)
                        throw new Exception("Неверный формат расширения.");
                    Dictionary<string, object> tls = Map("serverName", sni == "" ? host.Host : sni, "alpn", new string[] { "h3" });
                    if (pin != "")
                        tls["pinnedPeerCertSha256"] = pin;
                    Dictionary<string, object> stream = Map("network", "hysteria", "security", "tls", "tlsSettings", tls, "hysteriaSettings", Map("auth", auth, "version", 2), "finalmask", mask);
                    if (obfs != "")
                        throw new Exception("Совместное использование этих расширений пока не поддерживается.");
                    d["xray-outbound"] = Map("protocol", "hysteria", "settings", Map("address", d["server"], "port", d["port"], "version", 2), "streamSettings", stream);
                }
            }
            else
                throw new Exception("Прямой импорт ссылки: VLESS и Hysteria 2. Для остальных протоколов используйте YAML/JSON Mihomo.");
            return d;
        }

        static Dictionary<string, object> Vmess(string tail, int index)
        {
            Dictionary<string, object> v = Parse(Decode64(tail)) as Dictionary<string, object>;
            if (v == null)
                throw new Exception("Неверный формат подключения.");
            string net = Get(v, "net", "tcp");
            if (net != "tcp" && net != "ws" && net != "grpc")
                throw new Exception("Для этого транспорта нужен файл настроек.");
            Dictionary<string, object> d = Map("name", Get(v, "ps", "Сервер " + index), "type", "vmess", "server", Get(v, "add", ""), "port", Port(Get(v, "port", "")), "uuid", Get(v, "id", ""), "alterId", Int32.Parse(Get(v, "aid", "0")), "cipher", Get(v, "scy", "auto"), "network", net, "udp", true, "tls", Get(v, "tls", "") == "tls");
            string sni = Get(v, "sni", Get(v, "host", ""));
            if (sni != "")
                d["servername"] = sni;
            if (net == "ws")
            {
                Dictionary<string, object> w = Map("path", Get(v, "path", "/"));
                if (Get(v, "host", "") != "")
                    w["headers"] = Map("Host", Get(v, "host", ""));
                d["ws-opts"] = w;
            }

            if (net == "grpc")
                d["grpc-opts"] = Map("grpc-service-name", Get(v, "path", ""));
            return d;
        }

        static bool TryGuid(string s, out Guid g)
        {
            try
            {
                g = new Guid(s);
                return true;
            }
            catch
            {
                g = Guid.Empty;
                return false;
            }
        }

        public static List<Dictionary<string, object>> Import(string text)
        {
            text = text.Trim().TrimStart('\uFEFF');
            if (text.Length == 0)
                throw new Exception("Вставьте ссылку или профиль.");
            if (text.Length > 4 * 1024 * 1024)
                throw new Exception("Профиль больше 4 МБ.");
            if (text.IndexOf("://") < 0 && !text.StartsWith("{") && !text.StartsWith("[") && !text.Contains("proxies:"))
            {
                try
                {
                    text = Decode64(text).Trim();
                }
                catch
                {
                }
            }

            List<Dictionary<string, object>> nodes = new List<Dictionary<string, object>>();
            if (text.StartsWith("{") || text.StartsWith("[") || text.StartsWith("proxies:") || text.Contains("\nproxies:"))
            {
                object parsed = Parse(text);
                IList array = parsed as IList;
                if (array != null)
                {
                    foreach (object item in array)
                        ImportObject(item as Dictionary<string, object>, nodes);
                }
                else
                    ImportObject(parsed as Dictionary<string, object>, nodes);
            }
            else
            {
                string[] lines = text.Replace("\r", "").Split('\n');
                int n = 0;
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;
                    n++;
                    try
                    {
                        nodes.Add(Link(line, n));
                    }
                    catch (Exception e)
                    {
                        throw new Exception("Строка " + n + ": " + e.Message);
                    }
                }
            }

            if (nodes.Count == 0 || nodes.Count > 2000)
                throw new Exception("Нужен список от 1 до 2000 серверов.");
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            int num = 0;
            foreach (Dictionary<string, object> d in nodes)
            {
                num++;
                string name = Get(d, "name", "Сервер " + num);
                if (name.Length > 180)
                    name = name.Substring(0, 180);
                string unique = name;
                int suffix = 2;
                while (!names.Add(unique))
                    unique = name + " (" + (suffix++) + ")";
                d["name"] = unique;
                if (Get(d, "type", "") == "")
                    throw new Exception("Не указан тип сервера.");
                if (Get(d, "server", "") == "")
                    throw new Exception("Не указан адрес сервера.");
                Port(Get(d, "port", ""));
                if (d.ContainsKey("dialer-proxy"))
                    throw new Exception("Цепочки dialer-proxy требуют полного профиля и пока не поддерживаются.");
            }

            return nodes;
        }

        static Dictionary<string, object> ObjectMap(object value)
        {
            return value as Dictionary<string, object>;
        }

        static Dictionary<string, object> FirstObject(object value)
        {
            IList list = value as IList;
            if (list == null || list.Count != 1)
                throw new InvalidDataException("Этот JSON содержит сложную цепочку серверов. Нужен отдельный профиль сервера.");
            Dictionary<string, object> d = ObjectMap(list[0]);
            if (d == null)
                throw new InvalidDataException("Неверное описание сервера.");
            return d;
        }

        static void ImportObject(Dictionary<string, object> root, List<Dictionary<string, object>> nodes)
        {
            if (root == null)
                throw new InvalidDataException("Неверный формат настроек.");
            if (root.ContainsKey("proxies"))
            {
                if (root.ContainsKey("proxy-providers"))
                    throw new InvalidDataException("Внешние списки серверов в этом формате пока не поддерживаются.");
                IList list = root["proxies"] as IList;
                if (list == null)
                    throw new InvalidDataException("Неверное описание серверов.");
                foreach (object item in list)
                {
                    Dictionary<string, object> d = ObjectMap(item);
                    if (d == null)
                        throw new InvalidDataException("Неверное описание сервера.");
                    nodes.Add(d);
                }

                return;
            }

            IList outbounds = root.ContainsKey("outbounds") ? root["outbounds"] as IList : null;
            if (outbounds == null)
                throw new InvalidDataException("JSON не содержит поддерживаемых настроек серверов.");
            foreach (object item in outbounds)
            {
                Dictionary<string, object> outbound = ObjectMap(item);
                if (outbound == null)
                    continue;
                string protocol = Get(outbound, "protocol", "");
                if (protocol != "vless" && protocol != "vmess" && protocol != "trojan" && protocol != "shadowsocks" && protocol != "hysteria")
                    continue;
                if (outbound.ContainsKey("proxySettings"))
                    throw new InvalidDataException("Цепочки подключений из JSON пока не поддерживаются.");
                Dictionary<string, object> settings = outbound.ContainsKey("settings") ? ObjectMap(outbound["settings"]) : null;
                if (settings == null)
                    throw new InvalidDataException("В JSON отсутствуют реквизиты сервера.");
                Dictionary<string, object> stream = outbound.ContainsKey("streamSettings") ? ObjectMap(outbound["streamSettings"]) : Map();
                if (stream == null)
                    stream = Map();
                if (stream.ContainsKey("sockopt"))
                {
                    Dictionary<string, object> sock = ObjectMap(stream["sockopt"]);
                    if (sock != null && sock.ContainsKey("dialerProxy"))
                        throw new InvalidDataException("Цепочки подключений из JSON пока не поддерживаются.");
                }

                Dictionary<string, object> endpoint, user = null;
                string type = protocol == "shadowsocks" ? "ss" : protocol == "hysteria" ? "hysteria2" : protocol;
                if (protocol == "vless" || protocol == "vmess")
                {
                    endpoint = FirstObject(settings["vnext"]);
                    user = FirstObject(endpoint["users"]);
                }
                else if (protocol == "hysteria")
                {
                    if (Get(settings, "version", "2") != "2")
                        throw new InvalidDataException("Версия этого протокола пока не поддерживается.");
                    endpoint = settings;
                }
                else
                    endpoint = FirstObject(settings["servers"]);
                Dictionary<string, object> node = Map("name", Get(root, "remarks", Get(outbound, "tag", "Сервер")), "type", type, "server", Get(endpoint, "address", ""), "port", endpoint["port"], "network", Get(stream, "network", "tcp"), "tls", Get(stream, "security", "none") != "none", "xray-outbound", outbound);
                if (user != null)
                {
                    node["uuid"] = Get(user, "id", "");
                    node["flow"] = Get(user, "flow", "");
                }
                else if (endpoint.ContainsKey("password"))
                    node["password"] = endpoint["password"];
                if (stream.ContainsKey("realitySettings"))
                {
                    Dictionary<string, object> reality = ObjectMap(stream["realitySettings"]);
                    if (reality != null)
                        node["reality-opts"] = Map("public-key", Get(reality, "publicKey", ""), "short-id", Get(reality, "shortId", ""));
                }

                nodes.Add(node);
            }

            if (nodes.Count == 0)
                throw new InvalidDataException("JSON прочитан, но поддерживаемые серверы не найдены.");
        }

        public static bool Usable(Dictionary<string, object> n)
        {
            string server = Get(n, "server", "").Trim();
            string name = Get(n, "name", "").ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
            return server != "" && server != "0.0.0.0" && server != "::" && !name.Contains("appnotsupported");
        }

        public static Dictionary<string, object> Config(List<Dictionary<string, object>> nodes, int selected, int port)
        {
            if (selected < 0 || selected >= nodes.Count)
                throw new Exception("Выберите сервер.");
            if (!Usable(nodes[selected]))
                throw new Exception("Выбран служебный профиль-заглушка, а не VPN-сервер.");
            // One selected node avoids name collisions and makes browser traffic use that exact server.
            Dictionary<string, object> node = new Dictionary<string, object>(nodes[selected]);
            node["name"] = "VPN";
            return Map("mixed-port", port, "allow-lan", false, "bind-address", "127.0.0.1", "ipv6", false, "mode", "rule", "log-level", "warning", "external-controller", "", "profile", Map("store-selected", false, "store-fake-ip", false), "proxies", new object[] { node }, "rules", new string[] { "MATCH,VPN" });
        }

        public static bool CanXray(Dictionary<string, object> n)
        {
            return n.ContainsKey("xray-outbound") || (Get(n, "type", "") == "vless" && Get(n, "network", "tcp") == "tcp" && n.ContainsKey("reality-opts"));
        }

        public static Dictionary<string, object> XrayConfig(List<Dictionary<string, object>> nodes, int selected, int port)
        {
            Dictionary<string, object> n = nodes[selected];
            if (!Usable(n) || !CanXray(n))
                throw new Exception("Профиль не поддерживается адаптером Xray.");
            if (n.ContainsKey("xray-outbound"))
            {
                Dictionary<string, object> raw = new Dictionary<string, object>((Dictionary<string, object>)n["xray-outbound"]);
                raw["tag"] = "proxy";
                return Map("log", Map("loglevel", "warning"), "inbounds", new object[] { Map("listen", "127.0.0.1", "port", port, "protocol", "http", "settings", Map()) }, "outbounds", new object[] { raw });
            }

            Dictionary<string, object> reality = (Dictionary<string, object>)n["reality-opts"];
            Dictionary<string, object> user = Map("id", Get(n, "uuid", ""), "encryption", "none", "flow", Get(n, "flow", ""));
            Dictionary<string, object> outbound = Map("tag", "proxy", "protocol", "vless", "settings", Map("vnext", new object[] { Map("address", Get(n, "server", ""), "port", n["port"], "users", new object[] { user }) }), "streamSettings", Map("network", "tcp", "security", "reality", "tcpSettings", Map(), "realitySettings", Map("serverName", Get(n, "servername", ""), "fingerprint", Get(n, "client-fingerprint", "chrome"), "publicKey", Get(reality, "public-key", ""), "shortId", Get(reality, "short-id", ""))));
            return Map("log", Map("loglevel", "warning"), "inbounds", new object[] { Map("listen", "127.0.0.1", "port", port, "protocol", "http", "settings", Map()) }, "outbounds", new object[] { outbound });
        }

        public static string Json(object value)
        {
            if (value == null)
                return "null";
            if (value is bool)
                return (bool)value ? "true" : "false";
            if (value is string)
            {
                StringBuilder b = new StringBuilder("\"");
                foreach (char c in (string)value)
                {
                    switch (c)
                    {
                        case '"':
                            b.Append("\\\"");
                            break;
                        case '\\':
                            b.Append("\\\\");
                            break;
                        case '\n':
                            b.Append("\\n");
                            break;
                        case '\r':
                            b.Append("\\r");
                            break;
                        case '\t':
                            b.Append("\\t");
                            break;
                        default:
                            if (c < 32)
                                b.Append("\\u" + ((int)c).ToString("x4"));
                            else
                                b.Append(c);
                            break;
                    }
                }

                return b.Append('"').ToString();
            }

            IDictionary dict = value as IDictionary;
            if (dict != null)
            {
                List<string> entries = new List<string>();
                foreach (DictionaryEntry e in dict)
                    entries.Add(Json(Convert.ToString(e.Key, CultureInfo.InvariantCulture)) + ":" + Json(e.Value));
                return "{" + String.Join(",", entries.ToArray()) + "}";
            }

            IEnumerable list = value as IEnumerable;
            if (list != null)
            {
                List<string> a = new List<string>();
                foreach (object v in list)
                    a.Add(Json(v));
                return "[" + String.Join(",", a.ToArray()) + "]";
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
