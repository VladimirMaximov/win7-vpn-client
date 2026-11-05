using System;
using System.Collections.Generic;
using MihomoDesk;
class ProxyTests
{
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static void Main(string[] args)
    {
        Check(WindowsProxy.UsesPort("127.0.0.1:12345", "12345"), "plain app proxy recognized");
        Check(WindowsProxy.UsesPort("http=127.0.0.1:12345;https=127.0.0.1:12345", "12345"), "per-protocol Windows representation recognized");
        Check(!WindowsProxy.UsesPort("http=127.0.0.1:12345;https=127.0.0.1:22222", "12345"), "mixed settings from another application preserved");
        Check(!WindowsProxy.UsesPort("127.0.0.1:123456", "12345"), "other loopback ports preserved");
        Check(!WindowsProxy.Equivalent(Profiles.Map("flags", 1), Profiles.Map("flags", 3)), "enabled proxy cannot pass restoration verification for direct connection");
        Check(!WindowsProxy.Equivalent(Profiles.Map("flags", 9), Profiles.Map("flags", 1)), "automatic discovery must be restored");
        Check(!WindowsProxy.Equivalent(Profiles.Map("flags", 5, "pac", "http://example.invalid/a"), Profiles.Map("flags", 5, "pac", "http://example.invalid/b")), "PAC address must be restored");
        if (args.Length == 1 && args[0] == "--windows")
        {
            Dictionary<string, object> original = WindowsProxy.Capture();
            try
            {
                var enabled = Profiles.Map("flags", 3, "server", "127.0.0.1:17999", "bypass", "<local>", "pac", "");
                WindowsProxy.Apply(enabled);
                Check(WindowsProxy.Equivalent(enabled, WindowsProxy.Capture()), "native proxy activation verified");
                Check(!WindowsProxy.DisableOwned(18000), "emergency repair preserves a different proxy port");
                Check(WindowsProxy.Equivalent(enabled, WindowsProxy.Capture()), "unrelated proxy settings unchanged");
                Check(WindowsProxy.DisableOwned(17999), "owned proxy disabled even without a recovery file");
                Check((Int32.Parse(Profiles.Get(WindowsProxy.Capture(), "flags", "1")) & 2) == 0, "emergency repair actually disables manual proxy");
                var direct = Profiles.Map("flags", 1, "server", "", "bypass", "", "pac", "");
                WindowsProxy.Apply(direct);
                Check(WindowsProxy.Equivalent(direct, WindowsProxy.Capture()), "native proxy disable verified");
                var automatic = Profiles.Map("flags", 9, "server", "", "bypass", "", "pac", "");
                WindowsProxy.Apply(automatic);
                Check(WindowsProxy.Equivalent(automatic, WindowsProxy.Capture()), "native automatic discovery restored");
            }
            finally { WindowsProxy.Apply(original); }
            Check(WindowsProxy.Equivalent(original, WindowsProxy.Capture()), "original Windows settings restored after test");
        }
    }
}
