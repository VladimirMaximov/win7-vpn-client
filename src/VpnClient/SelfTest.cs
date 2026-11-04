using System;
using System.IO;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace MihomoDesk
{
    static class SelfTest
    {
        public static void Run()
        {
            foreach (string name in new string[]
            {
                "selftest.txt",
                "selftest-error.txt"
            }

            )
            {
                string file = Path.Combine(Program.Home, name);
                if (File.Exists(file))
                    File.Delete(file);
            }

            try
            {
                CheckedRun();
            }
            catch (Exception e)
            {
                File.WriteAllText(Path.Combine(Program.Home, "selftest-error.txt"), e.ToString());
                throw;
            }
        }

        static void CheckedRun()
        {
            Assets.Get("mihomo.exe");
            Assets.Get("xray.exe");
            Profiles.Parse("{\"ready\":true}");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (MainForm f = new MainForm())
            {
                SubscriptionCatalog preview = new SubscriptionCatalog();
                var demo = Profiles.Import("vless://11111111-1111-4111-8111-111111111111@127.0.0.1:443?security=reality&type=tcp&flow=xtls-rprx-vision&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&sni=example.com#%F0%9F%87%A9%F0%9F%87%AA%20Frankfurt\n" + "vless://11111111-1111-4111-8111-111111111111@127.0.0.2:443?security=reality&type=tcp&pbk=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&sni=example.com#%F0%9F%87%B3%F0%9F%87%B1%20Amsterdam");
                var first = preview.Add(new SubscriptionResult { Url = "https://provider.invalid/home", Nodes = demo, UpdatedUtc = DateTime.UtcNow }, "Семейная подписка");
                preview.Add(new SubscriptionResult { Url = "https://provider.invalid/backup", Nodes = Profiles.Import("hysteria2://fake@127.0.0.3:443?sni=example.com#%F0%9F%87%AB%F0%9F%87%B7%20Paris"), UpdatedUtc = DateTime.UtcNow }, "Резервное подключение");
                preview.ActiveId = first.Id;
                typeof(MainForm).GetField("book", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(f, preview);
                typeof(MainForm).GetField("catalogReadOnly", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(f, false);
                typeof(MainForm).GetMethod("ApplyActive", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f, null);
                typeof(MainForm).GetMethod("RenderCards", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f, null);
                ((Label)typeof(MainForm).GetField("notice", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f)).Text = "Предпросмотр · примеры подписок";
                f.Show();
                Application.DoEvents();
                Control content = f.Controls[0];
                Control column = content.Controls[0];
                if (column.Height != content.ClientSize.Height || column.Width < 500 || column.Right > content.ClientSize.Width)
                    throw new Exception("Initial window layout failed");
                PowerButton power = (PowerButton)typeof(MainForm).GetField("connectButton", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f);
                if (Math.Abs(power.Left - (power.Parent.ClientSize.Width - power.Width) / 2) > 1)
                    throw new Exception("Initial connect button alignment failed");
                Draw(f, "vpn-main.png");
                Size originalSize = f.ClientSize;
                f.ClientSize = new Size(960, originalSize.Height);
                Application.DoEvents();
                if (column.Width > 650 || Math.Abs(column.Left - (content.ClientSize.Width - column.Width) / 2) > 1)
                    throw new Exception("Resized content column layout failed");
                Draw(f, "vpn-wide.png");
                f.ClientSize = originalSize;
                Application.DoEvents();
                MethodInfo settings = typeof(MainForm).GetMethod("OpenSettings", BindingFlags.Instance | BindingFlags.NonPublic);
                settings.Invoke(f, null);
                Application.DoEvents();
                Form window = (Form)typeof(MainForm).GetField("settingsForm", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f);
                Draw(window, "vpn-settings.png");
                window.Hide();
                typeof(MainForm).GetMethod("ShowPopup", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f, null);
                Application.DoEvents();
                Form popup = (Form)typeof(MainForm).GetField("popup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f);
                Draw(popup, "vpn-tray.png");
                popup.Close();
                typeof(MainForm).GetMethod("ShowPopup", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f, null);
                Application.DoEvents();
                popup = (Form)typeof(MainForm).GetField("popup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f);
                if (popup.IsDisposed)
                    throw new Exception("Tray reopen failed");
                ((NotifyIcon)typeof(MainForm).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f)).Dispose();
                popup.Dispose();
                window.Dispose();
                ((System.Windows.Forms.Timer)typeof(MainForm).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(f)).Stop();
            }

            using (ImportDialog input = new ImportDialog())
            {
                input.Show();
                Application.DoEvents();
                Draw(input, "vpn-import.png");
            }

            File.WriteAllText(Path.Combine(Program.Home, "selftest.txt"), "OK: single executable; embedded dependencies; Windows Forms; main/settings/tray rendered.");
        }

        static void Draw(Form f, string name)
        {
            using (Bitmap b = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height));
                b.Save(Path.Combine(Program.Home, name), System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}
