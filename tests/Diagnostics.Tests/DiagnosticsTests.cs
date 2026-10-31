using System;
using System.IO;
using System.Collections.Generic;
using MihomoDesk;

class Test
{
    static void Main()
    {
        Diagnostics.Folder = Path.GetFullPath("artifacts/diagnostics-test-logs");
        Directory.CreateDirectory(Diagnostics.Folder);
        foreach (string p in Directory.GetFiles(Diagnostics.Folder))
            File.Delete(p);
        string secret = "SECRET_TEST_TOKEN_1234";
        string url = "https://private.example/sub/" + secret;
        Diagnostics.Input(url);
        Diagnostics.Failure("subscription", new Exception(url, new Exception("password=" + secret)));
        Diagnostics.Core("level=warning msg=\"dial timeout " + url + " uuid=" + secret + " password=" + secret + "\"", false);
        var nodes = new List<Dictionary<string, object>>();
        nodes.Add(Profiles.Map("name", "app not supported " + secret, "type", "vless", "server", "0.0.0.0", "password", secret));
        Diagnostics.Nodes(nodes, "import");
        string log = File.ReadAllText(Diagnostics.FilePath);
        if (log.Contains(secret) || log.Contains("private.example"))
            throw new Exception("Secret leaked");
        if (!log.Contains("appUnsupported=True") || !log.Contains("placeholder=True") || !log.Contains("category=timeout"))
            throw new Exception("Missing diagnostics");
        Console.WriteLine("PASS privacy and unsupported-app/placeholder diagnostics");
        for (int i = 0; i < 2300; i++)
            Diagnostics.Write("rotation.test", new string ('x', 1000));
        if (!File.Exists(Diagnostics.FilePath + ".1") || !File.Exists(Diagnostics.FilePath + ".2"))
            throw new Exception("Rotation failed");
        Console.WriteLine("PASS bounded log rotation");
        string old = Diagnostics.Folder;
        Diagnostics.Folder = Path.Combine(old, "not-a-directory");
        File.WriteAllText(Diagnostics.Folder, "x");
        Diagnostics.Write("failure.test", "");
        Console.WriteLine("PASS logging IO failure does not crash app");
    }
}
