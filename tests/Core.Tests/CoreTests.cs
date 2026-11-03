using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using MihomoDesk;

class Tests
{
    static int count;
    static void Check(bool ok, string name)
    {
        if (!ok)
            throw new Exception("FAIL " + name);
        Console.WriteLine("PASS " + name);
        count++;
    }

    static void Reject(string text, string name)
    {
        try
        {
            Profiles.Import(text);
        }
        catch
        {
            Check(true, name);
            return;
        }

        throw new Exception("FAIL accepted " + name);
    }

    static void Main()
    {
        Directory.CreateDirectory("artifacts/test-fixtures");
        string uuid = "11111111-1111-4111-8111-111111111111";
        string reality = "vless://" + uuid + "@127.0.0.1:443?security=reality&type=tcp&flow=xtls-rprx-vision&sni=example.com&fp=firefox&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&sid=abcd#Reality";
        var nodes = Profiles.Import(reality);
        var node = nodes[0];
        Check((string)node["type"] == "vless" && (string)node["flow"] == "xtls-rprx-vision" && node.ContainsKey("reality-opts"), "VLESS REALITY Vision import");
        File.WriteAllText("artifacts/test-fixtures/reality.json", Profiles.Json(Profiles.Config(nodes, 0, 17890)));
        string hy = "hysteria2://fake%3Apassword@127.0.0.1:443?sni=example.com&obfs=salamander&obfs-password=fake#HY2";
        var h = Profiles.Import(hy);
        Check((string)h[0]["password"] == "fake:password" && (string)h[0]["obfs"] == "salamander", "Hysteria 2 escaped credentials");
        File.WriteAllText("artifacts/test-fixtures/hysteria2.json", Profiles.Json(Profiles.Config(h, 0, 17890)));
        string ws = "vless://" + uuid + "@[::1]:8443?security=tls&type=ws&path=%2Ftest%3Fed%3D2048&host=example.com#WS";
        var w = Profiles.Import(ws);
        Check((string)w[0]["server"] == "::1" && Convert.ToInt32(w[0]["port"]) == 8443, "IPv6 endpoint and WebSocket");
        File.WriteAllText("artifacts/test-fixtures/websocket.json", Profiles.Json(Profiles.Config(w, 0, 17890)));
        string grpc = "vless://" + uuid + "@127.0.0.1:443?security=tls&type=grpc&serviceName=test#RPC";
        var g = Profiles.Import(grpc);
        File.WriteAllText("artifacts/test-fixtures/grpc.json", Profiles.Json(Profiles.Config(g, 0, 17890)));
        Check(Profiles.Import(Convert.ToBase64String(Encoding.UTF8.GetBytes(reality + "\n" + hy))).Count == 2, "base64 subscription");
        Check((string)Profiles.Import(reality + "\n" + reality)[1]["name"] == "Reality (2)", "duplicate server names");
        string yaml = "proxies:\n  - name: native\n    type: trojan\n    server: 127.0.0.1\n    port: 443\n    password: fake\n    sni: example.com\n";
        var y = Profiles.Import(yaml);
        Check((string)y[0]["type"] == "trojan", "native YAML additional protocol");
        File.WriteAllText("artifacts/test-fixtures/trojan.json", Profiles.Json(Profiles.Config(y, 0, 17890)));
        var roundtrip = Profiles.Import(Profiles.Json(Profiles.Map("proxies", nodes)));
        Check(roundtrip.Count == 1, "JSON persistence roundtrip");
        File.WriteAllText("artifacts/test-fixtures/roundtrip.json", Profiles.Json(Profiles.Config(roundtrip, 0, 17890)));
        Check(roundtrip[0]["tls"] is bool && (bool)roundtrip[0]["tls"], "boolean types survive encrypted-profile persistence");
        Check(Profiles.Parse("{\"value\":null}") is Dictionary<string, object> && ((Dictionary<string, object>)Profiles.Parse("{\"value\":null}"))["value"] == null, "null registry values survive recovery snapshot");
        Reject("{\"outbounds\":[]}", "Xray JSON rejected without silent conversion");
        Reject(reality.Replace("&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", ""), "missing REALITY public key rejected");
        Reject(reality + "\nunknown://key@example.com:443", "unsupported line rejects whole import");
        Reject(reality.Replace("security=reality", "security=reality&insecure=1"), "insecure certificate setting rejected");
        Reject(reality.Replace("type=tcp", "type=unknown"), "unsupported URI transport reported");
        Reject(reality.Replace("type=tcp", "type=tcp&finalmask=abc"), "FinalMask not silently ignored");
        Reject(reality.Replace("type=tcp", "type=tcp&type=ws"), "duplicate query keys rejected");
        Reject(yaml + "proxy-providers: {}\n", "external provider dependency rejected");
        var cfg = Profiles.Config(nodes, 0, 17890);
        Check(!(bool)cfg["allow-lan"] && (string)cfg["bind-address"] == "127.0.0.1" && (string)cfg["external-controller"] == "", "loopback only and controller disabled");
        Check(Profiles.Json("quote\"\n\\") == "\"quote\\\"\\n\\\\\"", "JSON escaping");
        Reject("proxies: &a [*a]", "recursive YAML alias rejected");
        Reject("proxies: !!map {}", "explicit YAML type tag rejected");
        Reject(reality.Replace("type=tcp", "type=tcp&pqv=unknown"), "unknown cryptographic extension rejected");
        var plain = Profiles.Import("vless://" + uuid + "@127.0.0.1:18443?security=none&type=tcp#Local");
        File.WriteAllText("artifacts/test-fixtures/local-vless.json", Profiles.Json(Profiles.Config(plain, 0, 17890)));
        Check(!Profiles.Usable(Profiles.Map("name", "app not supported", "server", "0.0.0.0")), "provider placeholder rejected");
        bool blocked = false;
        try
        {
            Profiles.Config(new List<Dictionary<string, object>> { Profiles.Map("name", "app not supported", "server", "0.0.0.0") }, 0, 17890);
        }
        catch
        {
            blocked = true;
        }

        Check(blocked, "placeholder cannot produce active proxy configuration");
        Check(Profiles.CanXray(nodes[0]), "REALITY profile selects Xray adapter");
        var trojan = Profiles.Import("trojan://fake-password@127.0.0.1:443?sni=example.com#Trojan");
        File.WriteAllText("artifacts/test-fixtures/trojan-link.json", Profiles.Json(Profiles.Config(trojan, 0, 17890)));
        Check((string)trojan[0]["type"] == "trojan", "Trojan link import");
        string auth = Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-128-gcm:fake-password"));
        var shadowsocks = Profiles.Import("ss://" + auth + "@127.0.0.1:8388#SS");
        File.WriteAllText("artifacts/test-fixtures/ss-link.json", Profiles.Json(Profiles.Config(shadowsocks, 0, 17890)));
        Check((string)shadowsocks[0]["cipher"] == "aes-128-gcm", "SIP002 Shadowsocks link import");
        string vmess = Profiles.Json(Profiles.Map("v", "2", "ps", "Test", "add", "127.0.0.1", "port", 443, "id", uuid, "aid", 0, "net", "ws", "path", "/test", "host", "example.com", "tls", "tls"));
        var vm = Profiles.Import("vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(vmess)));
        File.WriteAllText("artifacts/test-fixtures/vmess-link.json", Profiles.Json(Profiles.Config(vm, 0, 17890)));
        Check((string)vm[0]["type"] == "vmess", "VMess WebSocket TLS link import");
        string fm = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"quicParams\":{\"keepAlivePeriod\":10}}"));
        var masked = Profiles.Import(hy.Replace("&obfs=salamander&obfs-password=fake", "&fm=" + Uri.EscapeDataString(fm)));
        Check(Profiles.CanXray(masked[0]), "Hysteria extension selects a capable engine");
        var maskedConfig = Profiles.XrayConfig(masked, 0, 17890);
        var maskedOutbound = (Dictionary<string, object>)((object[])maskedConfig["outbounds"])[0];
        Check(((Dictionary<string, object>)maskedOutbound["streamSettings"]).ContainsKey("finalmask"), "QUIC extension preserved in outbound configuration");
        string pin = new string ('a', 64);
        var extended = Profiles.Import("vless://" + uuid + "@127.0.0.1:443?type=xhttp&security=tls&sni=example.com&path=%2Fx&host=example.com&mode=auto&pcs=" + pin + "#Extended");
        Check(Profiles.CanXray(extended[0]), "XHTTP selects a capable engine");
        var xc = Profiles.XrayConfig(extended, 0, 17890);
        var xo = (Dictionary<string, object>)((object[])xc["outbounds"])[0];
        var xs = (Dictionary<string, object>)xo["streamSettings"];
        Check((string)((Dictionary<string, object>)xs["tlsSettings"])["pinnedPeerCertSha256"] == pin, "certificate pin preserved");
        Check((string)((Dictionary<string, object>)xs["xhttpSettings"])["mode"] == "auto", "XHTTP mode preserved");
        Console.WriteLine("TOTAL " + count);
    }
}
