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
                f.Show();
                Application.DoEvents();
                Draw(f, "vpn-main.png");
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
