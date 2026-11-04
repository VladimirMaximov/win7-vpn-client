using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace MihomoDesk
{
    public sealed class SubscriptionEntry
    {
        public string Id, Name, Url, Selected;
        public DateTime UpdatedUtc;
        public List<Dictionary<string, object>> Nodes;
        public SubscriptionEntry Copy()
        {
            return new SubscriptionEntry
            {
                Id = Id,
                Name = Name,
                Url = Url,
                Selected = Selected,
                UpdatedUtc = UpdatedUtc,
                Nodes = new List<Dictionary<string, object>>(Nodes)
            };
        }
    }

    public sealed class SubscriptionCatalog
    {
        public List<SubscriptionEntry> Entries = new List<SubscriptionEntry>();
        public string ActiveId = "";
        public SubscriptionEntry Find(string id)
        {
            return Entries.Find(delegate (SubscriptionEntry e)
            {
                return e.Id == id;
            });
        }

        public SubscriptionEntry Active
        {
            get
            {
                return Find(ActiveId);
            }
        }

        public SubscriptionCatalog Copy()
        {
            SubscriptionCatalog c = new SubscriptionCatalog
            {
                ActiveId = ActiveId
            };
            foreach (SubscriptionEntry e in Entries)
                c.Entries.Add(e.Copy());
            return c;
        }

        static List<Dictionary<string, object>> UsableNodes(List<Dictionary<string, object>> nodes)
        {
            List<Dictionary<string, object>> result = new List<Dictionary<string, object>>(nodes);
            result.RemoveAll(delegate (Dictionary<string, object> n)
            {
                return !Profiles.Usable(n);
            });
            if (result.Count == 0)
                throw new InvalidDataException("В настройках нет рабочих серверов.");
            return result;
        }

        public static string CleanName(string value, string fallback)
        {
            string name = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (name.Length == 0)
                name = fallback;
            return name.Length > 60 ? name.Substring(0, 60) : name;
        }

        public SubscriptionEntry Add(SubscriptionResult result, string name)
        {
            if (!String.IsNullOrEmpty(result.Url) && Entries.Exists(delegate (SubscriptionEntry e)
            {
                return e.Url == result.Url;
            }))
                throw new InvalidDataException("Эта подписка уже добавлена. Нажмите «Обновить» в её карточке.");
            if (Entries.Count >= 30)
                throw new InvalidDataException("Можно сохранить до 30 подписок.");
            SubscriptionEntry entry = new SubscriptionEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = CleanName(name, "Подписка " + (Entries.Count + 1)),
                Url = result.Url ?? "",
                Nodes = UsableNodes(result.Nodes),
                UpdatedUtc = result.UpdatedUtc,
                Selected = ""
            };
            entry.Selected = Subscriptions.NodeKey(entry.Nodes[0]);
            Entries.Add(entry);
            ActiveId = entry.Id;
            return entry;
        }

        public void Update(string id, SubscriptionResult result)
        {
            SubscriptionEntry entry = Find(id);
            if (entry == null)
                throw new InvalidDataException("Подписка не найдена.");
            List<Dictionary<string, object>> updated = UsableNodes(result.Nodes);
            int selected = Subscriptions.Selection(updated, entry.Selected);
            entry.Nodes = updated;
            entry.Url = result.Url ?? "";
            entry.UpdatedUtc = result.UpdatedUtc;
            entry.Selected = Subscriptions.NodeKey(updated[selected]);
        }

        public void Select(string id, int index)
        {
            SubscriptionEntry entry = Find(id);
            if (entry == null || index < 0 || index >= entry.Nodes.Count)
                throw new InvalidDataException("Выберите сервер.");
            ActiveId = id;
            entry.Selected = Subscriptions.NodeKey(entry.Nodes[index]);
        }

        public void Remove(string id)
        {
            int index = Entries.FindIndex(delegate (SubscriptionEntry e)
            {
                return e.Id == id;
            });
            if (index < 0)
                return;
            Entries.RemoveAt(index);
            if (ActiveId == id)
                ActiveId = Entries.Count == 0 ? "" : Entries[Math.Min(index, Entries.Count - 1)].Id;
        }

        public string Serialize()
        {
            List<object> entries = new List<object>();
            foreach (SubscriptionEntry e in Entries)
                entries.Add(Profiles.Map("id", e.Id, "name", e.Name, "url", e.Url, "selected", e.Selected, "updatedUtc", e.UpdatedUtc.ToUniversalTime().ToString("o"), "proxies", e.Nodes));
            string json = Profiles.Json(Profiles.Map("schemaVersion", 2, "activeId", ActiveId, "entries", entries));
            if (json.Length > 4 * 1024 * 1024)
                throw new InvalidDataException("Слишком большой список серверов. Удалите ненужные подписки.");
            return json;
        }

        public static SubscriptionCatalog Parse(string json)
        {
            Dictionary<string, object> root = Profiles.Parse(json) as Dictionary<string, object>;
            if (root == null || Profiles.Get(root, "schemaVersion", "") != "2" || !(root["entries"] is IList))
                throw new InvalidDataException("Неверный формат списка подписок.");
            SubscriptionCatalog c = new SubscriptionCatalog();
            HashSet<string> ids = new HashSet<string>();
            foreach (object value in (IList)root["entries"])
            {
                Dictionary<string, object> d = value as Dictionary<string, object>;
                if (d == null)
                    throw new InvalidDataException("Неверная запись подписки.");
                string id = Profiles.Get(d, "id", "");
                if (id.Length == 0 || !ids.Add(id))
                    throw new InvalidDataException("Неверный идентификатор подписки.");
                List<Dictionary<string, object>> nodes = UsableNodes(Profiles.Import(Profiles.Json(d)));
                DateTime time;
                DateTime.TryParse(Profiles.Get(d, "updatedUtc", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time);
                c.Entries.Add(new SubscriptionEntry { Id = id, Name = CleanName(Profiles.Get(d, "name", ""), "Подписка"), Url = Profiles.Get(d, "url", ""), Nodes = nodes, UpdatedUtc = time, Selected = Profiles.Get(d, "selected", "") });
                if (c.Entries.Count > 30)
                    throw new InvalidDataException("Слишком много подписок.");
            }

            c.ActiveId = Profiles.Get(root, "activeId", "");
            if (c.Active == null)
                c.ActiveId = c.Entries.Count == 0 ? "" : c.Entries[0].Id;
            return c;
        }

        public static SubscriptionCatalog FromLegacy(string json)
        {
            Dictionary<string, object> root = Profiles.Parse(json) as Dictionary<string, object>;
            if (root == null)
                throw new InvalidDataException("Неверный старый профиль.");
            DateTime time;
            DateTime.TryParse(Profiles.Get(root, "updatedUtc", ""), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time);
            SubscriptionCatalog c = new SubscriptionCatalog();
            SubscriptionEntry e = c.Add(new SubscriptionResult { Url = Profiles.Get(root, "url", ""), Nodes = Profiles.Import(json), UpdatedUtc = time }, "Моя подписка");
            string previous = Profiles.Get(root, "selected", "");
            int selected = Subscriptions.Selection(e.Nodes, previous);
            e.Selected = Subscriptions.NodeKey(e.Nodes[selected]);
            return c;
        }
    }
}
