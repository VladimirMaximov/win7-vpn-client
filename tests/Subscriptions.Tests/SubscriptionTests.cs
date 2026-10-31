using System;
using System.IO;
using System.Collections.Generic;
using MihomoDesk;

class Tests
{
    static int passed;
    static void Check(bool condition, string name)
    {
        if (!condition)
            throw new Exception(name);
        passed++;
        Console.WriteLine("PASS " + name);
    }

    static void Main()
    {
        Diagnostics.Folder = Path.GetFullPath("artifacts/subscription-test-logs");
        Directory.CreateDirectory(Diagnostics.Folder);
        foreach (string p in Directory.GetFiles(Diagnostics.Folder))
            File.Delete(p);
        string url = "https://provider.invalid/private/SUBSCRIPTION_SECRET";
        string good = "vless://11111111-1111-4111-8111-111111111111@127.0.0.1:443?security=reality&type=tcp&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&sni=example.com#Germany";
        string dummy = "vless://11111111-1111-4111-8111-111111111111@0.0.0.0:1?security=none#app%20not%20supported";
        int calls = 0;
        var result = Subscriptions.DownloadWith(url, delegate (Uri uri, string agent)
        {
            calls++;
            return calls == 1 ? dummy : good;
        });
        Check(calls == 2 && result.Nodes.Count == 1, "fallback rejects placeholder and accepts compatible format");
        Check(result.Url == url, "subscription source retained for refresh");
        Check((DateTime.UtcNow - result.UpdatedUtc).TotalSeconds < 10, "successful update timestamp");
        string key = Subscriptions.NodeKey(result.Nodes[0]);
        var other = Profiles.Import(good.Replace("127.0.0.1", "127.0.0.2"))[0];
        var reordered = new List<Dictionary<string, object>>
        {
            other,
            result.Nodes[0]
        };
        Check(Subscriptions.Selection(reordered, key) == 1, "selected server survives reordering");
        Check(Subscriptions.Selection(reordered, "missing") == 0, "removed server falls back to first available");
        var envelope = Subscriptions.Envelope(url, result.Nodes, key, result.UpdatedUtc);
        string serialized = Profiles.Json(envelope);
        var decoded = (Dictionary<string, object>)Profiles.Parse(serialized);
        Check(Profiles.Get(decoded, "url", "") == url && Profiles.Get(decoded, "selected", "") == key && Profiles.Import(serialized).Count == 1, "combined URL/server/selection envelope roundtrip");
        var old = result;
        bool failed = false;
        try
        {
            result = Subscriptions.DownloadWith(url, delegate (Uri u, string agent)
            {
                return dummy;
            });
        }
        catch
        {
            failed = true;
        }

        Check(failed && Object.ReferenceEquals(old, result), "failed replacement leaves working subscription untouched");
        foreach (string bad in new string[]
        {
            "https://user:password@provider.invalid/key",
            "file:///secret"
        }

        )
        {
            bool rejected = false;
            try
            {
                Subscriptions.Validate(bad);
            }
            catch
            {
                rejected = true;
            }

            Check(rejected, "reject unsafe subscription URL shape");
        }

        Check(Subscriptions.Validate(url + "#PaperVPN").AbsoluteUri == url, "fragment label accepted and removed from request");
        Check(Subscriptions.Validate(url + "?token=abc%2Fdef#PaperVPN").Query == "?token=abc%2Fdef", "query token preserved when removing label");
        var labelled = Subscriptions.DownloadWith(url + "#PaperVPN", delegate (Uri u, string agent)
        {
            Check(u.Fragment == "", "fragment never passed to downloader");
            return good;
        });
        Check(labelled.Url == url, "saved refresh URL normalized without label");
        var once = Subscriptions.DownloadWith(url, delegate (Uri u, string agent)
        {
            return good;
        });
        Check(once.Nodes.Count == 1, "one-click URL resolves directly to usable servers");
        string log = "";
        foreach (string p in Directory.GetFiles(Diagnostics.Folder))
            log += File.ReadAllText(p);
        Check(!log.Contains("SUBSCRIPTION_SECRET") && !log.Contains("provider.invalid"), "source URL absent from logs");
        int directFetches = 0;
        Func<Uri, string, string> noNetwork = delegate (Uri u, string agent)
        {
            directFetches++;
            throw new Exception("Unexpected fetch");
        };
        var direct = Subscriptions.ImportInputWith(good, noNetwork);
        Check(direct.Nodes.Count == 1 && direct.Url == "" && directFetches == 0, "direct server link imports without a network request");
        var multiple = Subscriptions.ImportInputWith(good + "\n" + good, noNetwork);
        Check(multiple.Nodes.Count == 2 && directFetches == 0, "multiple server links import locally");
        var encoded = Subscriptions.ImportInputWith(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(good)), noNetwork);
        Check(encoded.Nodes.Count == 1 && encoded.Url == "", "base64 input imports without an update URL");
        Check(direct.UpdatedUtc == DateTime.MinValue, "local input has no automatic refresh source");
        var https = Subscriptions.ImportInputWith(url, delegate (Uri u, string agent)
        {
            return good;
        });
        Check(https.Url == url, "HTTPS input keeps its refresh source");
        var http = Subscriptions.ImportInputWith("http://provider.invalid/sub#label", delegate (Uri u, string agent)
        {
            return good;
        });
        Check(http.Url == "http://provider.invalid/sub", "HTTP subscription accepted with normalized source");
        bool downgradeBlocked = false;
        try
        {
            Subscriptions.ValidateRedirect(new Uri("https://provider.invalid/sub"), new Uri("http://provider.invalid/sub"));
        }
        catch
        {
            downgradeBlocked = true;
        }

        Check(downgradeBlocked, "HTTPS redirect cannot downgrade to HTTP");
        Check(Subscriptions.ValidateRedirect(new Uri("http://provider.invalid/sub"), new Uri("https://provider.invalid/sub")).Scheme == "https", "HTTP redirect may upgrade to HTTPS");
        bool placeholderRejected = false;
        try
        {
            Subscriptions.ImportInputWith(dummy, noNetwork);
        }
        catch
        {
            placeholderRejected = true;
        }

        Check(placeholderRejected, "direct placeholder rejected");
        bool unknownRejected = false;
        try
        {
            Subscriptions.ImportInputWith("unknown://example.invalid/config", noNetwork);
        }
        catch
        {
            unknownRejected = true;
        }

        Check(unknownRejected, "unknown link scheme does not trigger a network request");
        Console.WriteLine("TOTAL " + passed);
    }
}
