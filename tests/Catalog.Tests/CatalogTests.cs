using System;
using System.IO;
using System.Collections.Generic;
using MihomoDesk;

class Tests
{
    static int passed;
    static void Check(bool condition, string text)
    {
        if (!condition)
            throw new Exception(text);
        Console.WriteLine("PASS " + text);
        passed++;
    }

    static SubscriptionResult Result(string url, string server)
    {
        return new SubscriptionResult
        {
            Url = url,
            UpdatedUtc = DateTime.UtcNow,
            Nodes = Profiles.Import("vless://11111111-1111-4111-8111-111111111111@" + server + ":443?security=reality&type=tcp&flow=xtls-rprx-vision&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&sni=example.com#%F0%9F%87%A9%F0%9F%87%AA%20Frankfurt")
        };
    }

    static void Main()
    {
        var first = Result("https://provider.invalid/a", "127.0.0.1");
        var second = Result("https://provider.invalid/b", "127.0.0.2");
        SubscriptionCatalog catalog = new SubscriptionCatalog();
        var a = catalog.Add(first, "Дом");
        var b = catalog.Add(second, "Работа");
        Check(catalog.Entries.Count == 2 && a.Id != b.Id, "independent subscriptions retain separate identities");
        catalog.Select(a.Id, 0);
        string original = catalog.Serialize();
        var copy = catalog.Copy();
        copy.Remove(b.Id);
        Check(catalog.Serialize() == original && copy.Entries.Count == 1 && copy.ActiveId == a.Id, "removing a non-active subscription leaves the active subscription intact");
        copy = catalog.Copy();
        copy.Update(b.Id, Result(second.Url, "127.0.0.3"));
        Check(copy.Find(a.Id).Url == first.Url && Profiles.Get(copy.Find(a.Id).Nodes[0], "server", "") == "127.0.0.1", "updating one subscription does not replace another");
        Check(catalog.Serialize() == original, "editing a candidate catalog does not mutate saved state");
        bool rejected = false;
        try
        {
            copy.Update(a.Id, new SubscriptionResult { Url = first.Url, Nodes = new List<Dictionary<string, object>>() });
        }
        catch
        {
            rejected = true;
        }

        Check(rejected && catalog.Serialize() == original, "failed update preserves existing subscriptions");
        var restored = SubscriptionCatalog.Parse(original);
        Check(restored.Entries.Count == 2 && restored.ActiveId == a.Id && restored.Find(b.Id).Name == "Работа", "catalog roundtrip preserves names, active subscription and servers");
        restored.Remove(a.Id);
        Check(restored.Entries.Count == 1 && restored.ActiveId == b.Id, "deleting active subscription selects a remaining subscription");
        restored.Remove(b.Id);
        Check(SubscriptionCatalog.Parse(restored.Serialize()).Entries.Count == 0, "empty catalog roundtrip after deleting last subscription");
        string legacy = Profiles.Json(Subscriptions.Envelope(first.Url, first.Nodes, Subscriptions.NodeKey(first.Nodes[0]), first.UpdatedUtc));
        var migrated = SubscriptionCatalog.FromLegacy(legacy);
        Check(migrated.Entries.Count == 1 && migrated.Active.Url == first.Url && migrated.Active.Selected == Subscriptions.NodeKey(first.Nodes[0]), "legacy migration preserves source, server choice and credentials");
        var offline = new SubscriptionCatalog();
        offline.Add(new SubscriptionResult { Url = "", UpdatedUtc = DateTime.MinValue, Nodes = first.Nodes }, "Из файла");
        Check(SubscriptionCatalog.Parse(offline.Serialize()).Active.Url == "", "manual profiles remain local without refresh URL");
        bool duplicate = false;
        try
        {
            catalog.Add(first, "Повтор");
        }
        catch
        {
            duplicate = true;
        }

        Check(duplicate && catalog.Entries.Count == 2, "duplicate subscription URL rejected without data loss");
        string geo = ServerPresentation.Location(first.Nodes[0]);
        Check(geo.Contains("Германия") && geo.Contains("Frankfurt"), "country flag becomes readable location on Windows 7");
        Check(ServerPresentation.Protocol(first.Nodes[0]).Contains("REALITY") && ServerPresentation.Protocol(first.Nodes[0]).Contains("Vision"), "server protocol details preserved for subtitle");
        Check(ServerPresentation.Location(Profiles.Map("name", "Server 1")) == "Server 1", "unknown geography is not invented");
        Check(ServerPresentation.Location(Profiles.Map("name", "Broken\ud800Label")).Contains("Label"), "malformed Unicode label cannot crash presentation");
        var json = Profiles.Json(Profiles.XrayConfig(first.Nodes, 0, 19000));
        var imported = Profiles.Import(json);
        Check(imported.Count == 1 && Profiles.CanXray(imported[0]), "Xray JSON accepted without losing outbound settings");
        var list = Profiles.Import("[" + json + "," + json + "]");
        Check(list.Count == 2, "array of exported JSON profiles imports all servers");
        var config = Profiles.XrayConfig(imported, 0, 19000);
        var outbound = (Dictionary<string, object>)((object[])config["outbounds"])[0];
        Check(((Dictionary<string, object>)outbound["streamSettings"]).ContainsKey("realitySettings"), "REALITY settings preserved across JSON import and runtime generation");
        Diagnostics.Folder = Path.GetFullPath("artifacts/catalog-test-logs");
        Directory.CreateDirectory(Diagnostics.Folder);
        File.WriteAllText(Diagnostics.FilePath, "2026-11-01T10:00:00Z event1\n2026-11-02T11:00:00Z event2\n");
        string export = Path.Combine(Diagnostics.Folder, "saved.log");
        Diagnostics.ExportLog(export, "test");
        string body = File.ReadAllText(export);
        Check(body.Contains("2026-11-01 10:00:00") && body.Contains("2026-11-02 11:00:00") && body.Contains("event2"), "exported diagnostic log includes date range and events");
        bool ownLog = false;
        try
        {
            Diagnostics.ExportLog(Diagnostics.FilePath, "test");
        }
        catch
        {
            ownLog = true;
        }

        Check(ownLog, "export cannot overwrite active diagnostic log");
        var alternative = new Dictionary<string, object>(first.Nodes[0]);
        alternative["network"] = "grpc";
        Check(Subscriptions.NodeKey(first.Nodes[0]) != Subscriptions.NodeKey(alternative), "same endpoint with different transports remains independently selectable");
        var legacyNodes = new List<Dictionary<string, object>>
        {
            first.Nodes[0],
            second.Nodes[0]
        };
        var old = SubscriptionCatalog.FromLegacy(Profiles.Json(Subscriptions.Envelope(first.Url, legacyNodes, Subscriptions.LegacyNodeKey(second.Nodes[0]), first.UpdatedUtc)));
        Check(Subscriptions.Selection(old.Active.Nodes, old.Active.Selected) == 1, "old selected server survives upgraded identity rules");
        Console.WriteLine("TOTAL " + passed);
    }
}
