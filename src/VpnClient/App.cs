using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MihomoDesk
{
    static class Program
    {
        public static readonly string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MihomoDesk");
        public static void Run(string[] args)
        {
            Directory.CreateDirectory(Home);
            if (args.Length == 1 && args[0] == "--selftest")
            {
                SelfTest.Run();
                return;
            }

            Diagnostics.Write("app.start", "version=0.7 clr=" + Environment.Version + " os=" + Environment.OSVersion.Version + " mode=" + (args.Length == 0 ? "ui" : "maintenance"));
            if (args.Length == 1 && args[0] == "--recover")
            {
                try
                {
                    Recovery.Restore(null);
                    MessageBox.Show("Настройки прокси восстановлены, если их не меняла другая программа.", "VPN");
                }
                catch
                {
                    MessageBox.Show("Восстановление не удалось. Проверьте настройки прокси Windows.");
                }

                return;
            }

            if (args.Length == 5 && args[0] == "--watch")
            {
                string owner = args[1] + ":" + args[2] + ":" + args[3] + ":" + args[4];
                try
                {
                    int id = Int32.Parse(args[1]);
                    long ticks = Int64.Parse(args[2]);
                    using (Process p = Process.GetProcessById(id))
                    {
                        if (p.StartTime.ToUniversalTime().Ticks == ticks)
                        {
                            while (!p.WaitForExit(1000))
                            {
                                if (!Recovery.OwnedBy(owner))
                                    return;
                            }
                        }
                    }
                }
                catch
                {
                }

                try
                {
                    Recovery.Restore(owner);
                }
                catch
                {
                }

                return;
            }

            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\MihomoDesk.Main", out first))
            {
                if (!first)
                {
                    try
                    {
                        using (EventWaitHandle signal = EventWaitHandle.OpenExisting("Local\\VPN.ShowWindow"))
                            signal.Set();
                    }
                    catch
                    {
                    }

                    return;
                }

                Application.ThreadException += delegate (object sender, ThreadExceptionEventArgs e)
                {
                    Diagnostics.Failure("ui.unhandled", e.Exception);
                    MessageBox.Show("Ошибка записана в журнал диагностики.", "VPN");
                };
                AppDomain.CurrentDomain.UnhandledException += delegate (object sender, UnhandledExceptionEventArgs e)
                {
                    Exception ex = e.ExceptionObject as Exception;
                    if (ex != null)
                        Diagnostics.Failure("process.unhandled", ex);
                };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    Recovery.Restore();
                    string stale = Path.Combine(Home, "runtime.json");
                    if (File.Exists(stale))
                        File.Delete(stale);
                    Application.Run(new MainForm());
                }
                catch (Exception e)
                {
                    Diagnostics.Failure("startup", e);
                    MessageBox.Show("Не удалось запустить приложение: " + e.Message, "VPN");
                }
            }
        }

        public static void SaveSecret(string path, string text)
        {
            byte[] b = ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser);
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, b);
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }

        public static string ReadSecret(string path)
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser));
        }
    }

    static class Recovery
    {
        const string Key = "Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings";
        static string State
        {
            get
            {
                return Path.Combine(Program.Home, "recovery.dat");
            }
        }

        public static bool OwnedBy(string owner)
        {
            try
            {
                if (!File.Exists(State))
                    return false;
                Dictionary<string, object> d = Profiles.Parse(Program.ReadSecret(State)) as Dictionary<string, object>;
                return d != null && Profiles.Get(d, "owner", "") == owner;
            }
            catch
            {
                return File.Exists(State);
            }
        }

        static string[] Fields = new string[]
        {
            "ProxyEnable",
            "ProxyServer",
            "ProxyOverride",
            "AutoConfigURL"
        };
        [DllImport("wininet.dll", SetLastError = true)]
        static extern bool InternetSetOption(IntPtr h, int option, IntPtr b, int len);
        static void Refresh()
        {
            InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
        }

        public static void Arm(Process core, int port, bool proxy)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key, true))
            {
                if (k == null)
                    throw new Exception("Не удалось открыть настройки прокси Windows.");
                Dictionary<string, object> d = Profiles.Map("owner", Process.GetCurrentProcess().Id + ":" + Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks + ":" + core.Id + ":" + core.StartTime.ToUniversalTime().Ticks, "corePid", core.Id, "coreTicks", core.StartTime.ToUniversalTime().Ticks.ToString(), "proxy", proxy, "port", port);
                foreach (string f in Fields)
                {
                    object val = k.GetValue(f, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    d[f] = val;
                    d[f + "Kind"] = val == null ? 0 : (int)k.GetValueKind(f);
                }

                Program.SaveSecret(State, Profiles.Json(d));
                Diagnostics.Write("proxy.snapshot", "saved=true");
            }

            Process self = Process.GetCurrentProcess();
            ProcessStartInfo s = new ProcessStartInfo(Application.ExecutablePath, "--watch " + self.Id + " " + self.StartTime.ToUniversalTime().Ticks + " " + core.Id + " " + core.StartTime.ToUniversalTime().Ticks);
            s.UseShellExecute = false;
            s.CreateNoWindow = true;
            Process.Start(s).Dispose();
            if (proxy)
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key, true))
                {
                    k.SetValue("ProxyServer", "127.0.0.1:" + port, RegistryValueKind.String);
                    k.SetValue("ProxyOverride", "<local>", RegistryValueKind.String);
                    k.DeleteValue("AutoConfigURL", false);
                    k.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                    Refresh();
                    Diagnostics.Write("proxy.enabled", "port=" + port);
                }
        }

        public static void Restore()
        {
            Restore(null);
        }

        public static void Restore(string owner)
        {
            using (Mutex m = new Mutex(false, "Local\\MihomoDesk.Recovery"))
            {
                try
                {
                    m.WaitOne();
                }
                catch (AbandonedMutexException)
                {
                }

                try
                {
                    if (!File.Exists(State))
                        return;
                    Diagnostics.Write("proxy.restore.begin", "");
                    Dictionary<string, object> d = Profiles.Parse(Program.ReadSecret(State)) as Dictionary<string, object>;
                    if (d == null)
                        throw new Exception("Неверный файл восстановления.");
                    if (owner != null && Profiles.Get(d, "owner", "") != owner)
                        return;
                    if (Profiles.Get(d, "proxy", "False").Equals("True", StringComparison.OrdinalIgnoreCase))
                        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key, true))
                        {
                            string owned = "127.0.0.1:" + Profiles.Get(d, "port", "");
                            // Do not replace settings changed by another app while we were running.
                            if (Convert.ToString(k.GetValue("ProxyServer", "")) == owned)
                            {
                                foreach (string f in Fields)
                                {
                                    object v = d[f];
                                    if (v == null)
                                        k.DeleteValue(f, false);
                                    else
                                    {
                                        RegistryValueKind kind = (RegistryValueKind)Int32.Parse(Profiles.Get(d, f + "Kind", "1"));
                                        if (kind == RegistryValueKind.DWord)
                                            v = Int32.Parse(Convert.ToString(v));
                                        k.SetValue(f, v, kind);
                                    }
                                }

                                Refresh();
                            }
                        }

                    try
                    {
                        Process p = Process.GetProcessById(Int32.Parse(Profiles.Get(d, "corePid", "0")));
                        if (p.StartTime.ToUniversalTime().Ticks.ToString() == Profiles.Get(d, "coreTicks", ""))
                            p.Kill();
                    }
                    catch
                    {
                    }

                    string runtime = Path.Combine(Program.Home, "runtime.json");
                    if (File.Exists(runtime))
                        File.Delete(runtime);
                    File.Delete(State);
                    Diagnostics.Write("proxy.restore.complete", "");
                }
                finally
                {
                    m.ReleaseMutex();
                }
            }
        }
    }

    sealed partial class MainForm : Form
    {
        ComboBox servers = new ComboBox();
        Button refreshButton = new Button(), logsButton = new Button(), importButton = new Button(), fileButton = new Button(), connectButton = new Button(), disconnectButton = new Button(), removeButton = new Button();
        Label state = new Label(), detail = new Label();
        CheckBox systemProxy = new CheckBox();
        TextBox log = new TextBox();
        List<Dictionary<string, object>> nodes = new List<Dictionary<string, object>>();
        Process core;
        bool busy = false, closing = false;
        int localPort;
        bool activeXray = false;
        bool filling = false;
        string subscriptionUrl = "", selection = "";
        DateTime subscriptionUpdated = DateTime.MinValue;
        string SubscriptionFile
        {
            get
            {
                return Path.Combine(Program.Home, "subscription.dat");
            }
        }

        System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        string Saved
        {
            get
            {
                return Path.Combine(Program.Home, "profiles.dat");
            }
        }

        public MainForm()
        {
            BuildSimpleUI();
            importButton.Click += delegate
            {
                using (ImportDialog dialog = new ImportDialog())
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        AcceptSubscription(dialog.Downloaded);
                }
            };
            refreshButton.Click += delegate
            {
                RefreshSubscription(false, null);
            };
            servers.SelectedIndexChanged += delegate
            {
                if (filling || servers.SelectedIndex < 0)
                    return;
                selection = Subscriptions.NodeKey(nodes[servers.SelectedIndex]);
                try
                {
                    SaveSubscription();
                }
                catch (Exception e)
                {
                    Diagnostics.Failure("selection.save", e);
                }
            };
            fileButton.Click += delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Filter = "Профили и ссылки|*.yaml;*.yml;*.json;*.txt|Все файлы|*.*";
                    if (d.ShowDialog(this) == DialogResult.OK)
                        try
                        {
                            if (new FileInfo(d.FileName).Length > 4 * 1024 * 1024)
                                throw new Exception("Файл больше 4 МБ.");
                            Import(File.ReadAllText(d.FileName, Encoding.UTF8));
                        }
                        catch (Exception e)
                        {
                            Error(e.Message);
                        }
                }
            };
            removeButton.Click += delegate
            {
                if (MessageBox.Show(this, "Удалить все сохранённые профили с этого компьютера?", "Профили", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    nodes.Clear();
                    servers.Items.Clear();
                    subscriptionUrl = "";
                    selection = "";
                    subscriptionUpdated = DateTime.MinValue;
                    if (File.Exists(Saved))
                        File.Delete(Saved);
                    if (File.Exists(SubscriptionFile))
                        File.Delete(SubscriptionFile);
                    UpdateButtons();
                }
            };
            connectButton.Click += delegate
            {
                if (core != null)
                    Disconnect();
                else if (nodes.Count == 0)
                {
                    OpenSettings();
                    importButton.PerformClick();
                }
                else
                    Connect();
            };
            disconnectButton.Click += delegate
            {
                Disconnect();
            };
            FormClosing += delegate (object sender, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.UserClosing && !exitRequested)
                {
                    e.Cancel = true;
                    Hide();
                    return;
                }

                if (busy)
                {
                    e.Cancel = true;
                    exitRequested = false;
                    MessageBox.Show(this, "Дождитесь завершения операции. Окно можно свернуть.", "VPN");
                    return;
                }

                closing = true;
                try
                {
                    Disconnect();
                }
                catch (Exception ex)
                {
                    e.Cancel = true;
                    closing = false;
                    Error(ex.Message);
                }
            };
            if (File.Exists(Saved))
                try
                {
                    nodes = Profiles.Import(Program.ReadSecret(Saved));
                    Diagnostics.Nodes(nodes, "saved");
                    Fill();
                    Say("Сохранённые серверы загружены.");
                }
                catch (Exception ex)
                {
                    Diagnostics.Failure("profiles.load", ex);
                    Say("Не удалось прочитать сохранённые профили. Импортируйте их заново.");
                }

            if (File.Exists(SubscriptionFile))
                try
                {
                    string content = Program.ReadSecret(SubscriptionFile);
                    Dictionary<string, object> saved = Profiles.Parse(content) as Dictionary<string, object>;
                    List<Dictionary<string, object>> loaded = Profiles.Import(content);
                    nodes = loaded;
                    subscriptionUrl = Profiles.Get(saved, "url", "");
                    selection = Profiles.Get(saved, "selected", "");
                    DateTime.TryParse(Profiles.Get(saved, "updatedUtc", ""), out subscriptionUpdated);
                    Fill();
                    Diagnostics.Nodes(nodes, "subscription-saved");
                }
                catch (Exception e)
                {
                    Diagnostics.Failure("subscription.load", e);
                    Say("Не удалось прочитать сохранённую подписку; доступен прежний профиль.");
                }

            string pending = Path.Combine(Application.StartupPath, "pending-profile.json");
            if (File.Exists(pending))
                try
                {
                    List<Dictionary<string, object>> imported = Profiles.Import(File.ReadAllText(pending, Encoding.UTF8));
                    imported.RemoveAll(delegate (Dictionary<string, object> n)
                    {
                        return !Profiles.Usable(n);
                    });
                    if (imported.Count == 0)
                        throw new Exception();
                    Program.SaveSecret(Saved, Profiles.Json(Profiles.Map("proxies", imported)));
                    nodes = imported;
                    Fill();
                    Diagnostics.Nodes(nodes, "pending-import");
                    File.Delete(pending);
                    Say("Подготовленный профиль импортирован.");
                }
                catch (Exception ex)
                {
                    Diagnostics.Failure("pending-import", ex);
                }

            timer.Interval = 3000;
            timer.Tick += delegate
            {
                PollStatus();
                if (core != null && !busy && core.HasExited)
                {
                    Diagnostics.Write("core.exit", "exit=" + core.ExitCode);
                    Say("Движок завершил работу. Восстанавливаю настройки Windows.");
                    Disconnect();
                }
            };
            timer.Start();
            Diagnostics.Write("ui.ready", "");
            Say("Добавьте подписку в настройках или используйте сохранённый сервер.");
            UpdateButtons();
        }

        static void Setup(Button b, string title, int width)
        {
            b.Text = title;
            b.Width = width;
            b.Height = 34;
            b.FlatStyle = FlatStyle.Flat;
            b.Margin = new Padding(0, 0, 10, 0);
        }

        void Say(string s)
        {
            if (closing)
                return;
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine);
        }

        void Error(string s)
        {
            MessageBox.Show(this, s, "VPN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void Fill()
        {
            filling = true;
            try
            {
                servers.Items.Clear();
                foreach (Dictionary<string, object> n in nodes)
                    servers.Items.Add(Profiles.Get(n, "name", ""));
                servers.SelectedIndex = Subscriptions.Selection(nodes, selection);
                if (servers.SelectedIndex >= 0)
                    selection = Subscriptions.NodeKey(nodes[servers.SelectedIndex]);
            }
            finally
            {
                filling = false;
            }
        }

        void SaveSubscription()
        {
            if (nodes.Count > 0)
                Program.SaveSecret(SubscriptionFile, Profiles.Json(Subscriptions.Envelope(subscriptionUrl, nodes, selection, subscriptionUpdated)));
        }

        void AcceptSubscription(SubscriptionResult result)
        {
            try
            {
                string chosen = selection;
                int i = Subscriptions.Selection(result.Nodes, chosen);
                if (i >= 0)
                    chosen = Subscriptions.NodeKey(result.Nodes[i]);
                Program.SaveSecret(SubscriptionFile, Profiles.Json(Subscriptions.Envelope(result.Url, result.Nodes, chosen, result.UpdatedUtc)));
                nodes = result.Nodes;
                subscriptionUrl = result.Url;
                subscriptionUpdated = result.UpdatedUtc;
                selection = chosen;
                Fill();
                Diagnostics.Write("subscription.saved", "nodes=" + nodes.Count);
                Say("Подписка сохранена. Серверов: " + nodes.Count + ". Дальше достаточно нажать «Подключить».");
                UpdateButtons();
            }
            catch (Exception e)
            {
                Diagnostics.Failure("subscription.save", e);
                Error("Не удалось сохранить обновление. Прежние данные не удалены.");
            }
        }

        void RefreshSubscription(bool quiet, Action after)
        {
            if (subscriptionUrl == "" || busy || core != null)
            {
                if (after != null)
                    after();
                return;
            }

            busy = true;
            UpdateButtons();
            SetStatus("Обновление серверов…", Muted);
            string url = subscriptionUrl;
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate (object sender, DoWorkEventArgs e)
            {
                e.Result = Subscriptions.Download(url);
            };
            worker.RunWorkerCompleted += delegate (object sender, RunWorkerCompletedEventArgs e)
            {
                busy = false;
                if (e.Error != null)
                {
                    Diagnostics.Failure("subscription.refresh", e.Error);
                    Say("Обновление не удалось. Сохранённые серверы остаются доступны.");
                    if (!quiet)
                        Error("Не удалось обновить подписку. Прежние серверы сохранены; подробности в журнале.");
                }
                else
                    AcceptSubscription((SubscriptionResult)e.Result);
                SetStatus("Не подключено", Muted);
                UpdateButtons();
                worker.Dispose();
                if (after != null)
                    after();
            };
            worker.RunWorkerAsync();
        }

        void Import(string text)
        {
            Diagnostics.Input(text);
            try
            {
                List<Dictionary<string, object>> imported = Profiles.Import(text);
                Diagnostics.Nodes(imported, "import");
                imported.RemoveAll(delegate (Dictionary<string, object> n)
                {
                    return !Profiles.Usable(n);
                });
                if (imported.Count == 0)
                    throw new InvalidDataException("Подписка содержит только заглушку app not supported. Рабочих серверов нет. Загрузите подписку заново.");
                Program.SaveSecret(SubscriptionFile, Profiles.Json(Subscriptions.Envelope("", imported, "", DateTime.MinValue)));
                nodes = imported;
                subscriptionUrl = "";
                subscriptionUpdated = DateTime.MinValue;
                selection = "";
                Fill();
                Say("Импортировано серверов: " + nodes.Count + ". Маршрутизация: весь трафик через выбранный сервер; исходные правила профиля не импортируются.");
                UpdateButtons();
            }
            catch (Exception e)
            {
                Diagnostics.Failure("import", e);
                Error(e.Message);
            }
        }

        void UpdateButtons()
        {
            bool running = core != null;
            connectButton.Enabled = !busy;
            connectButton.Text = running ? "Отключить" : nodes.Count == 0 ? "Добавить подписку" : "Подключить";
            RefreshTrayText();
            disconnectButton.Enabled = !busy && running;
            servers.Enabled = !busy && !running;
            systemProxy.Enabled = !busy && !running;
            importButton.Enabled = fileButton.Enabled = removeButton.Enabled = !busy && !running;
            refreshButton.Enabled = !busy && !running && subscriptionUrl != "";
        }

        static int FreePort()
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        static string Quote(string s)
        {
            if (s.IndexOf('"') >= 0 || s.EndsWith("\\"))
                throw new Exception("Недопустимый путь программы.");
            return "\"" + s + "\"";
        }

        string engine
        {
            get
            {
                return Assets.Get(activeXray ? "xray.exe" : "mihomo.exe");
            }
        }

        Process StartCore(bool test)
        {
            string args = activeXray ? "run " + (test ? "-test " : "") + "-c " + Quote(Path.Combine(Program.Home, "runtime.json")) : (test ? "-t " : "") + "-d " + Quote(Program.Home) + " -f " + Quote(Path.Combine(Program.Home, "runtime.json"));
            ProcessStartInfo info = new ProcessStartInfo(engine, args);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WorkingDirectory = Program.Home;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            Process p = new Process();
            p.StartInfo = info;
            // Record only allowlisted error categories; never persist raw engine lines.
            p.OutputDataReceived += delegate (object sender, DataReceivedEventArgs e)
            {
                Diagnostics.Core(e.Data, test);
            };
            p.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs e)
            {
                Diagnostics.Core(e.Data, test);
            };
            p.Start();
            Diagnostics.Write("core.start", "pid=" + p.Id + " test=" + test);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            return p;
        }

        void Connect()
        {
            if (subscriptionUrl != "" && (DateTime.UtcNow - subscriptionUpdated.ToUniversalTime()).TotalHours >= 6)
            {
                RefreshSubscription(true, ConnectCore);
                return;
            }

            ConnectCore();
        }

        void ConnectCore()
        {
            if (servers.SelectedIndex >= 0 && !Profiles.Usable(nodes[servers.SelectedIndex]))
            {
                Diagnostics.Write("connect.blocked", "reason=placeholder");
                Error("Это служебная заглушка app not supported, а не VPN-сервер. Загрузите подписку заново в обновлённой версии приложения.");
                return;
            }

            activeXray = servers.SelectedIndex >= 0 && Profiles.CanXray(nodes[servers.SelectedIndex]);
            Diagnostics.Write("engine.selected", "engine=" + (activeXray ? "xray" : "mihomo"));
            if (!File.Exists(engine))
            {
                Error("Не удалось подготовить компоненты VPN. Повторите запуск приложения.");
                return;
            }

            busy = true;
            UpdateButtons();
            SetStatus("Подключаемся…", Blue);
            int selected = servers.SelectedIndex;
            bool useProxy = systemProxy.Checked;
            Diagnostics.Write("connect.begin", "selectedIndex=" + selected + " systemProxy=" + useProxy);
            Diagnostics.Nodes(nodes, "connect");
            try
            {
                localPort = FreePort();
                do
                {
                    apiPort = FreePort();
                }
                while (apiPort == localPort);
                apiSecret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                Dictionary<string, object> configuration = activeXray ? Profiles.XrayConfig(nodes, selected, localPort) : Profiles.Config(nodes, selected, localPort);
                Telemetry.Configure(configuration, activeXray, apiPort, apiSecret);
                File.WriteAllText(Path.Combine(Program.Home, "runtime.json"), Profiles.Json(configuration), new UTF8Encoding(false));
            }
            catch
            {
                busy = false;
                SetStatus("Не удалось подключиться", Color.Firebrick);
                UpdateButtons();
                Error("Не удалось создать локальную конфигурацию или выбрать порт. Проверьте права доступа к папке профилей.");
                return;
            }

            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate (object sender, DoWorkEventArgs e)
            {
                Process started = null;
                try
                {
                    using (Process test = StartCore(true))
                    {
                        if (!test.WaitForExit(20000))
                        {
                            test.Kill();
                            throw new Exception("Проверка профиля заняла больше 20 секунд.");
                        }

                        test.WaitForExit();
                        Diagnostics.Write("core.validation", "exit=" + test.ExitCode);
                        if (test.ExitCode != 0)
                            throw new Exception("Не удалось проверить настройки сервера. Сохраните журнал диагностики для помощи.");
                    }

                    started = StartCore(false);
                    bool ready = false;
                    for (int i = 0; i < 100; i++)
                    {
                        if (started.HasExited)
                            throw new Exception("Не удалось запустить подключение. Подробности записаны в журнал.");
                        using (TcpClient c = new TcpClient())
                        {
                            try
                            {
                                IAsyncResult ar = c.BeginConnect(IPAddress.Loopback, localPort, null, null);
                                bool ok = ar.AsyncWaitHandle.WaitOne(100, false);
                                if (ok)
                                {
                                    c.EndConnect(ar);
                                    ready = true;
                                    ar.AsyncWaitHandle.Close();
                                    break;
                                }

                                ar.AsyncWaitHandle.Close();
                            }
                            catch
                            {
                            }
                        }

                        Thread.Sleep(100);
                    }

                    if (!ready)
                        throw new Exception("Локальный прокси не запустился.");
                    Recovery.Arm(started, localPort, useProxy);
                    e.Result = started;
                }
                catch
                {
                    try
                    {
                        Recovery.Restore();
                    }
                    catch
                    {
                    }

                    if (started != null)
                        try
                        {
                            started.Kill();
                        }
                        catch
                        {
                        }

                    throw;
                }
            };
            worker.RunWorkerCompleted += delegate (object sender, RunWorkerCompletedEventArgs e)
            {
                busy = false;
                if (e.Error != null)
                {
                    Diagnostics.Failure("connect", e.Error);
                    SetStatus("Не удалось подключиться", Color.Firebrick);
                    Error(e.Error.Message);
                    string p = Path.Combine(Program.Home, "runtime.json");
                    if (File.Exists(p))
                        File.Delete(p);
                }
                else
                {
                    core = (Process)e.Result;
                    Diagnostics.Write("connect.ready", "port=" + localPort + " pid=" + core.Id);
                    generation++;
                    lastProbe = DateTime.MinValue;
                    lastSample = DateTime.MinValue;
                    lastUp = lastDown = 0;
                    SetStatus("Проверяем соединение…", Blue);
                    Say((activeXray ? "Xray · HTTP-прокси: " : "Mihomo · HTTP/SOCKS: ") + "127.0.0.1:" + localPort + ". " + (useProxy ? "Системный прокси включён." : "Настройте прокси в нужном приложении вручную."));
                    Say("Откройте нужный сайт для проверки: запуск движка ещё не подтверждает доступность VPN-сервера.");
                }

                UpdateButtons();
                worker.Dispose();
            };
            worker.RunWorkerAsync();
        }

        void Disconnect()
        {
            generation++;
            downText = upText = "—";
            downRate.Text = upRate.Text = "—";
            downTotal.Text = upTotal.Text = "За сеанс: —";
            Diagnostics.Write("disconnect.begin", "");
            Recovery.Restore();
            if (core != null)
            {
                try
                {
                    if (!core.HasExited)
                        core.Kill();
                }
                catch
                {
                }

                core.Dispose();
                core = null;
            }

            SetStatus("Не подключено", Muted);
            state.ForeColor = Color.FromArgb(90, 100, 115);
            Say("Отключено. Прежние настройки прокси восстановлены, если их не меняла другая программа.");
            UpdateButtons();
        }
    }

    sealed class ImportDialog : Form
    {
        TextBox url = new TextBox();
        Button save = new Button(), cancel = new Button();
        Label hint = new Label();
        bool loading = false;
        public SubscriptionResult Downloaded;
        public ImportDialog()
        {
            Text = "Добавить или заменить подписку";
            ClientSize = new Size(630, 215);
            MinimumSize = new Size(640, 250);
            MaximumSize = new Size(1000, 300);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10);
            TableLayoutPanel p = new TableLayoutPanel();
            p.Padding = new Padding(18);
            p.Dock = DockStyle.Fill;
            p.RowCount = 4;
            p.ColumnCount = 1;
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(p);
            hint.Text = "Вставьте HTTPS-ссылку подписки вашего VPN-сервиса. Приложение сохранит её и будет обновлять серверы.";
            hint.Dock = DockStyle.Fill;
            p.Controls.Add(hint);
            url.Dock = DockStyle.Top;
            p.Controls.Add(url);
            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            save.Text = "Сохранить";
            save.Size = new Size(150, 34);
            cancel.Text = "Отмена";
            cancel.Size = new Size(110, 34);
            buttons.Controls.AddRange(new Control[] { save, cancel });
            p.Controls.Add(buttons);
            Label privacy = new Label();
            privacy.Text = "Ссылка хранится зашифрованной на этом компьютере и не попадает в журнал. Рабочие серверы сохранятся, если новая ссылка не загрузится.";
            privacy.Dock = DockStyle.Fill;
            privacy.Font = new Font("Segoe UI", 8);
            p.Controls.Add(privacy);
            AcceptButton = save;
            cancel.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
            };
            save.Click += delegate
            {
                BeginDownload();
            };
            FormClosing += delegate (object sender, FormClosingEventArgs e)
            {
                if (loading)
                    e.Cancel = true;
            };
        }

        void BeginDownload()
        {
            try
            {
                Subscriptions.Validate(url.Text);
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message);
                return;
            }

            string input = url.Text;
            loading = true;
            save.Enabled = cancel.Enabled = url.Enabled = false;
            hint.Text = "Загрузка и проверка серверов…";
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate (object sender, DoWorkEventArgs e)
            {
                e.Result = Subscriptions.Download(input);
            };
            worker.RunWorkerCompleted += delegate (object sender, RunWorkerCompletedEventArgs e)
            {
                loading = false;
                save.Enabled = cancel.Enabled = url.Enabled = true;
                if (e.Error != null)
                {
                    Diagnostics.Failure("subscription.add", e.Error);
                    hint.Text = "Не удалось загрузить подписку. Прежние серверы сохранены.";
                    MessageBox.Show(this, "Не удалось получить рабочую подписку. Проверьте ссылку и доступность сервиса. Подробности записаны в журнал.", "Подписка");
                }
                else
                {
                    Downloaded = (SubscriptionResult)e.Result;
                    DialogResult = DialogResult.OK;
                }

                worker.Dispose();
            };
            worker.RunWorkerAsync();
        }
    }
}
