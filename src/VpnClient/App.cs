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
            if (args.Length == 1 && args[0] == "--migration-check")
            {
                try
                {
                    SubscriptionCatalog migrated = CatalogRepository.Load();
                    File.WriteAllText(Path.Combine(Home, "migration-result.txt"), "OK subscriptions=" + migrated.Entries.Count);
                }
                catch (Exception e)
                {
                    Diagnostics.Failure("migration.check", e);
                    File.WriteAllText(Path.Combine(Home, "migration-result.txt"), "ERROR " + e.GetType().Name);
                }

                return;
            }

            if (args.Length == 2 && args[0] == "--probe-selftest")
            {
                try
                {
                    var choices = Subscriptions.ImportInput(File.ReadAllText(args[1])).Nodes;
                    int index = choices.FindIndex(delegate (Dictionary<string, object> n)
                    {
                        return Profiles.Get(n, "type", "") == "vless" && Profiles.Get(n, "network", "") == "tcp" && n.ContainsKey("reality-opts");
                    });
                    long delay = ProbeRunner.Measure(choices[index < 0 ? 0 : index]);
                    File.WriteAllText(Path.Combine(Home, "probe-result.txt"), "OK latencyMs=" + delay);
                }
                catch (Exception e)
                {
                    Diagnostics.Failure("probe.selftest", e);
                    File.WriteAllText(Path.Combine(Home, "probe-result.txt"), "ERROR " + e.GetType().Name);
                }

                return;
            }

            if (args.Length == 6 && args[0] == "--watch-probe")
            {
                ProbeRunner.Supervise(args);
                return;
            }

            if (args.Length == 1 && args[0] == "--selftest")
            {
                SelfTest.Run();
                return;
            }

            Diagnostics.Write("app.start", "version=0.10.3 clr=" + Environment.Version + " os=" + Environment.OSVersion.Version + " mode=" + (args.Length == 0 ? "ui" : "maintenance"));
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

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try { Recovery.Restore(owner); return; }
                    catch (Exception e)
                    {
                        Diagnostics.Failure("watchdog.restore", e);
                        Thread.Sleep(1000);
                    }
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
        public static void Arm(Process core, int port, bool proxy)
        {
            if (File.Exists(State))
                throw new IOException("Previous proxy recovery is still pending");
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

                d["wininet"] = WindowsProxy.Capture();
                Program.SaveSecret(State, Profiles.Json(d));
                Diagnostics.Write("proxy.snapshot", "saved=true");
            }

            Process self = Process.GetCurrentProcess();
            ProcessStartInfo s = new ProcessStartInfo(Application.ExecutablePath, "--watch " + self.Id + " " + self.StartTime.ToUniversalTime().Ticks + " " + core.Id + " " + core.StartTime.ToUniversalTime().Ticks);
            s.UseShellExecute = false;
            s.CreateNoWindow = true;
            Process.Start(s).Dispose();
            if (proxy)
            {
                Dictionary<string, object> enabled = Profiles.Map("flags", 3, "server", "127.0.0.1:" + port, "bypass", "<local>", "pac", "");
                WindowsProxy.Apply(enabled);
                if (!WindowsProxy.Equivalent(enabled, WindowsProxy.Capture()))
                    throw new IOException("Windows proxy activation verification failed");
                Diagnostics.Write("proxy.enabled", "port=" + port + " verified=True");
            }
        }

        public static void Restore()
        {
            Restore(null);
        }

        public static void RestoreForPort(int port)
        {
            if (File.Exists(State)) Restore(null);
            else Diagnostics.Write("proxy.restore.missing-snapshot", "disabledOwnedProxy=" + WindowsProxy.DisableOwned(port));
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
                            // Do not replace settings changed by another app while we were running.
                            Dictionary<string, object> current = WindowsProxy.Capture();
                            string port = Profiles.Get(d, "port", "");
                            if (WindowsProxy.UsesPort(Convert.ToString(k.GetValue("ProxyServer", "")), port) || WindowsProxy.UsesPort(Profiles.Get(current, "server", ""), port))
                            {
                                object native;
                                Dictionary<string, object> previous = d.TryGetValue("wininet", out native) ? native as Dictionary<string, object> : null;
                                if (previous == null)
                                {
                                    int flags = Convert.ToInt32(d["ProxyEnable"] ?? 0) != 0 ? 3 : 1;
                                    if (!String.IsNullOrEmpty(Convert.ToString(d["AutoConfigURL"]))) flags |= 4;
                                    // Legacy snapshots did not record automatic discovery; retain its current setting.
                                    flags |= Int32.Parse(Profiles.Get(current, "flags", "1")) & 8;
                                    previous = Profiles.Map("flags", flags, "server", Convert.ToString(d["ProxyServer"]), "bypass", Convert.ToString(d["ProxyOverride"]), "pac", Convert.ToString(d["AutoConfigURL"]));
                                }
                                WindowsProxy.Apply(previous);
                                if (!WindowsProxy.Equivalent(previous, WindowsProxy.Capture()))
                                    throw new IOException("Windows proxy restoration verification failed");
                                Diagnostics.Write("proxy.restore.verified", "success=True");
                            }
                            else Diagnostics.Write("proxy.restore.skipped", "reason=changed-by-another-application");
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
        PowerButton connectButton = new PowerButton();
        Button refreshButton = new Button(), logsButton = new Button(), importButton = new Button(), fileButton = new Button(), disconnectButton = new Button(), removeButton = new Button();
        Label state = new Label(), detail = new Label();
        CheckBox systemProxy = new CheckBox();
        TextBox log = new TextBox();
        List<Dictionary<string, object>> nodes = new List<Dictionary<string, object>>();
        Process core;
        bool busy = false, closing = false;
        int localPort;
        bool activeXray = false;
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
                ShowAddDialog("");
            };
            logsButton.Click += delegate
            {
                Diagnostics.Write("ui.logs.open", "");
                Process.Start("notepad.exe", Quote(Diagnostics.FilePath));
            };
            refreshButton.Click += delegate
            {
                RefreshSubscription(false, null);
            };
            removeButton.Click += delegate
            {
                if (book.Active != null)
                    DeleteSubscription(book.ActiveId);
            };
            fileButton.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Filter = "Настройки VPN|*.json;*.yaml;*.yml;*.txt|Все файлы|*.*";
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        return;
                    try
                    {
                        if (new FileInfo(dialog.FileName).Length > 4 * 1024 * 1024)
                            throw new IOException();
                        ShowAddDialog(File.ReadAllText(dialog.FileName, Encoding.UTF8));
                    }
                    catch (Exception e)
                    {
                        Diagnostics.Failure("import.file", e);
                        Error("Не удалось открыть файл настроек.");
                    }
                }
            };
            connectButton.Click += delegate
            {
                if (core != null)
                    Disconnect();
                else if (nodes.Count > 0 && !busy)
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

                if (busy && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    exitRequested = false;
                    MessageBox.Show(this, "Дождитесь завершения операции. Окно можно свернуть.", "VPN");
                    return;
                }

                closing = true;
                ProbeRunner.CancelAll();
                try
                {
                    if (!Disconnect())
                    {
                        e.Cancel = true;
                        closing = false;
                    }
                }
                catch (Exception ex)
                {
                    e.Cancel = true;
                    closing = false;
                    Diagnostics.Failure("shutdown", ex);
                    Error("Не удалось восстановить настройки сети. Сохраните журнал.");
                }
            };
            LoadCatalog();
            timer.Interval = 3000;
            timer.Tick += delegate
            {
                PollStatus();
                if (core != null && !busy && core.HasExited)
                {
                    Diagnostics.Write("core.exit", "exit=" + core.ExitCode);
                    if (Disconnect()) SetStatus("Соединение завершено", Color.Firebrick);
                }
            };
            timer.Start();
            Diagnostics.Write("ui.ready", "subscriptions=" + book.Entries.Count);
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
                    connectedEntryId = book.ActiveId;
                    connectedNodeKey = selection;
                    sessionClock.Reset();
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

        bool Disconnect()
        {
            Diagnostics.Write("disconnect.begin", "");
            try { Recovery.RestoreForPort(localPort); }
            catch (Exception e)
            {
                Diagnostics.Failure("proxy.restore", e);
                SetStatus("Не удалось восстановить прокси", Color.Firebrick);
                Error("Windows не восстановила настройки прокси. Повторите отключение и сохраните журнал.");
                return false;
            }
            sessionClock.Reset();
            connectButton.Elapsed = "00:00:00";
            connectedEntryId = "";
            connectedNodeKey = "";
            generation++;
            downText = upText = "—";
            downRate.Text = upRate.Text = "—";
            downTotal.Text = upTotal.Text = "За сеанс: —";
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
            Say("Отключено. Настройки прокси Windows восстановлены, если их не меняла другая программа.");
            UpdateButtons();
            return true;
        }
    }

    sealed class ImportDialog : Form
    {
        TextBox url = new TextBox(), name = new TextBox();
        Button save = new Button(), cancel = new Button(), paste = new Button(), file = new Button();
        Label hint = new Label();
        bool loading;
        public SubscriptionResult Downloaded;
        public ImportDialog() : this("")
        {
        }

        public ImportDialog(string initial)
        {
            Text = "Добавить подключение";
            ClientSize = new Size(610, 425);
            MinimumSize = new Size(620, 465);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10);
            BackColor = Color.FromArgb(247, 249, 253);
            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.Padding = new Padding(20);
            p.ColumnCount = 1;
            p.RowCount = 7;
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            Controls.Add(p);
            Label title = new Label();
            title.Text = "Название · необязательно";
            title.AutoSize = true;
            title.ForeColor = Color.DimGray;
            p.Controls.Add(title);
            name.Dock = DockStyle.Top;
            p.Controls.Add(name);
            hint.Text = "Вставьте ссылку подписки, ссылку сервера или JSON-настройки. Несколько ссылок — по одной на строку.";
            hint.Dock = DockStyle.Fill;
            p.Controls.Add(hint);
            FlowLayoutPanel helpers = new FlowLayoutPanel();
            helpers.Dock = DockStyle.Fill;
            paste.Text = "Вставить";
            paste.Size = new Size(110, 29);
            file.Text = "Открыть файл…";
            file.Size = new Size(145, 29);
            helpers.Controls.AddRange(new Control[] { paste, file });
            p.Controls.Add(helpers);
            url.Multiline = true;
            url.AcceptsReturn = true;
            url.ScrollBars = ScrollBars.Vertical;
            url.Dock = DockStyle.Fill;
            url.Text = initial;
            p.Controls.Add(url);
            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.Padding = new Padding(0, 5, 0, 0);
            save.Text = "Добавить";
            save.Size = new Size(150, 33);
            save.BackColor = Color.FromArgb(57, 92, 229);
            save.ForeColor = Color.White;
            save.FlatStyle = FlatStyle.Flat;
            save.FlatAppearance.BorderSize = 0;
            cancel.Text = "Отмена";
            cancel.Size = new Size(110, 33);
            buttons.Controls.AddRange(new Control[] { save, cancel });
            p.Controls.Add(buttons);
            Label privacy = new Label();
            privacy.Text = "Настройки сохраняются зашифрованно. Ошибка добавления не затронет другие подключения. Пароли и ключи не попадают в журнал.";
            privacy.Font = new Font("Segoe UI", 8);
            privacy.ForeColor = Color.DimGray;
            privacy.Dock = DockStyle.Fill;
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
            paste.Click += delegate
            {
                try
                {
                    if (Clipboard.ContainsText())
                        url.Text = Clipboard.GetText();
                }
                catch
                {
                    MessageBox.Show(this, "Не удалось прочитать буфер обмена. Вставьте текст с помощью Ctrl+V.");
                }
            };
            file.Click += delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Filter = "Настройки VPN|*.json;*.yaml;*.yml;*.txt|Все файлы|*.*";
                    if (d.ShowDialog(this) != DialogResult.OK)
                        return;
                    try
                    {
                        if (new FileInfo(d.FileName).Length > 4 * 1024 * 1024)
                            throw new IOException();
                        url.Text = File.ReadAllText(d.FileName, Encoding.UTF8);
                        if (name.Text.Trim() == "")
                            name.Text = Path.GetFileNameWithoutExtension(d.FileName);
                    }
                    catch
                    {
                        MessageBox.Show(this, "Не удалось прочитать файл настроек.");
                    }
                }
            };
            FormClosing += delegate (object sender, FormClosingEventArgs e)
            {
                if (loading)
                    e.Cancel = true;
            };
        }

        void BeginDownload()
        {
            if (String.IsNullOrEmpty(url.Text.Trim()))
            {
                MessageBox.Show(this, "Вставьте ссылку или откройте файл настроек.", "Добавить подключение");
                return;
            }

            try
            {
                if (Subscriptions.IsSubscriptionLink(url.Text))
                {
                    Uri source = Subscriptions.Validate(url.Text);
                    if (source.Scheme == "http" && MessageBox.Show(this, "Эта подписка передаётся без шифрования. Продолжить?", "HTTP-подписка", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message);
                return;
            }

            string input = url.Text, title = name.Text.Trim();
            loading = true;
            save.Enabled = cancel.Enabled = url.Enabled = name.Enabled = paste.Enabled = file.Enabled = false;
            hint.Text = "Проверяем настройки…";
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate (object sender, DoWorkEventArgs e)
            {
                SubscriptionResult result = Subscriptions.ImportInput(input);
                result.Name = title;
                if (result.Name == "")
                {
                    Uri u;
                    if (Subscriptions.IsSubscriptionLink(input) && Uri.TryCreate(input.Trim(), UriKind.Absolute, out u))
                        result.Name = u.Fragment.Length > 1 ? Uri.UnescapeDataString(u.Fragment.Substring(1)) : u.Host;
                    else
                        result.Name = "Мои серверы";
                }

                e.Result = result;
            };
            worker.RunWorkerCompleted += delegate (object sender, RunWorkerCompletedEventArgs e)
            {
                loading = false;
                save.Enabled = cancel.Enabled = url.Enabled = name.Enabled = paste.Enabled = file.Enabled = true;
                if (e.Error != null)
                {
                    Diagnostics.Failure("subscription.add", e.Error);
                    hint.Text = "Не удалось добавить подключение. Остальные подписки не изменены.";
                    MessageBox.Show(this, "Не удалось прочитать настройки или загрузить серверы. Проверьте ссылку. Подробности доступны в журнале.", "Добавить подключение");
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
