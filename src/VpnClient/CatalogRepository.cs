using System;
using System.IO;

namespace MihomoDesk
{
    static class CatalogRepository
    {
        public static string PathName
        {
            get
            {
                return Path.Combine(Program.Home, "subscriptions.dat");
            }
        }

        public static SubscriptionCatalog Load()
        {
            if (File.Exists(PathName))
                return SubscriptionCatalog.Parse(Program.ReadSecret(PathName));
            string envelope = Path.Combine(Program.Home, "subscription.dat"), profiles = Path.Combine(Program.Home, "profiles.dat");
            string source = File.Exists(envelope) ? envelope : File.Exists(profiles) ? profiles : null;
            if (source == null)
                return new SubscriptionCatalog();
            SubscriptionCatalog migrated = SubscriptionCatalog.FromLegacy(Program.ReadSecret(source));
            Save(migrated);
            Diagnostics.Write("subscriptions.migrated", "entries=" + migrated.Entries.Count);
            return migrated;
        }

        public static void Save(SubscriptionCatalog catalog)
        {
            Program.SaveSecret(PathName, catalog.Serialize());
        }
    }
}
