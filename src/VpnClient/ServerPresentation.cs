using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MihomoDesk
{
    public static class ServerPresentation
    {
        static readonly Dictionary<string, string> Countries = new Dictionary<string, string>
        {
            {
                "DE",
                "Германия"
            },
            {
                "NL",
                "Нидерланды"
            },
            {
                "FI",
                "Финляндия"
            },
            {
                "FR",
                "Франция"
            },
            {
                "GB",
                "Великобритания"
            },
            {
                "US",
                "США"
            },
            {
                "RU",
                "Россия"
            },
            {
                "SE",
                "Швеция"
            },
            {
                "CH",
                "Швейцария"
            },
            {
                "PL",
                "Польша"
            },
            {
                "TR",
                "Турция"
            },
            {
                "JP",
                "Япония"
            },
            {
                "SG",
                "Сингапур"
            },
            {
                "CA",
                "Канада"
            },
            {
                "LV",
                "Латвия"
            },
            {
                "EE",
                "Эстония"
            },
            {
                "LT",
                "Литва"
            },
            {
                "KZ",
                "Казахстан"
            },
            {
                "AM",
                "Армения"
            },
            {
                "GE",
                "Грузия"
            },
            {
                "ES",
                "Испания"
            },
            {
                "IT",
                "Италия"
            },
            {
                "AT",
                "Австрия"
            },
            {
                "CZ",
                "Чехия"
            },
            {
                "AU",
                "Австралия"
            },
            {
                "HK",
                "Гонконг"
            },
            {
                "KR",
                "Южная Корея"
            },
            {
                "IL",
                "Израиль"
            },
            {
                "AE",
                "ОАЭ"
            },
            {
                "NO",
                "Норвегия"
            },
            {
                "DK",
                "Дания"
            },
            {
                "RO",
                "Румыния"
            },
            {
                "BG",
                "Болгария"
            },
            {
                "PT",
                "Португалия"
            }
        };
        public static string Location(Dictionary<string, object> node)
        {
            string name = Profiles.Get(node, "name", "Сервер");
            string code = "";
            StringBuilder clean = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                int scalar = Char.IsSurrogatePair(name, i) ? Char.ConvertToUtf32(name, i) : name[i];
                if (scalar >= 0x1F1E6 && scalar <= 0x1F1FF && i + 3 < name.Length)
                {
                    int next = Char.IsSurrogatePair(name, i + 2) ? Char.ConvertToUtf32(name, i + 2) : name[i + 2];
                    if (next >= 0x1F1E6 && next <= 0x1F1FF)
                    {
                        if (code == "")
                            code = "" + (char)('A' + scalar - 0x1F1E6) + (char)('A' + next - 0x1F1E6);
                        i += 3;
                        continue;
                    }
                }

                if (Char.IsSurrogatePair(name, i))
                {
                    clean.Append(name[i]);
                    clean.Append(name[++i]);
                }
                else
                    clean.Append(Char.IsSurrogate(name[i]) ? '?' : name[i]);
            }

            string label = clean.ToString().Trim(' ', '·', '-');
            if (code == "")
                foreach (string key in Countries.Keys)
                    if (Regex.IsMatch(label, @"(^|[^A-Za-z])" + key + @"([^A-Za-z]|$)"))
                    {
                        code = key;
                        break;
                    }

            string country;
            if (Countries.TryGetValue(code, out country))
                return label == "" || label.Equals(code, StringComparison.OrdinalIgnoreCase) ? country : label.IndexOf(country, StringComparison.OrdinalIgnoreCase) >= 0 ? label : country + " · " + label;
            return label == "" ? "Сервер" : label;
        }

        public static string Protocol(Dictionary<string, object> node)
        {
            string type = Profiles.Get(node, "type", "").ToUpperInvariant();
            string network = Profiles.Get(node, "network", "").ToUpperInvariant();
            List<string> parts = new List<string>();
            parts.Add(type == "SS" ? "Shadowsocks" : type == "HYSTERIA2" ? "Hysteria 2" : type);
            if (network != "")
                parts.Add(network);
            if (node.ContainsKey("reality-opts"))
                parts.Add("REALITY");
            else if (Profiles.Get(node, "tls", "false").Equals("true", StringComparison.OrdinalIgnoreCase) || type == "TROJAN")
                parts.Add("TLS");
            if (Profiles.Get(node, "flow", "").Contains("vision"))
                parts.Add("Vision");
            return String.Join(" · ", parts.ToArray());
        }
    }
}
