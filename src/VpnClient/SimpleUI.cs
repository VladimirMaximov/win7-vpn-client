using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;

namespace MihomoDesk
{
    sealed partial class MainForm
    {
        NotifyIcon tray;
        Icon trayIcon;
        StatusPopup popup;
        Form settingsForm;
        Button settingsButton = new Button(), exportButton = new Button();
        Label downRate = new Label(), upRate = new Label(), downTotal = new Label(), upTotal = new Label();
        bool exitRequested = false, monitorBusy = false;
        int generation = 0, apiPort;
        string apiSecret = "";
        DateTime lastProbe = DateTime.MinValue, lastSample = DateTime.MinValue;
        long lastUp = 0, lastDown = 0;
        bool metricsErrorLogged = false;
        EventWaitHandle showSignal;
        MenuItem trayToggle;
        string statusText = "Не подключено", downText = "—", upText = "—";
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);
        static Color Blue = Color.FromArgb(38, 94, 232), Ink = Color.FromArgb(27, 39, 63), Muted = Color.FromArgb(105, 117, 137);
        void BuildSimpleUI()
        {
            Text = "VPN";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            ClientSize = new Size(540, 510);
            MinimumSize = new Size(540, 550);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10);
            BackColor = Color.FromArgb(244, 247, 252);
            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.Padding = new Padding(30, 22, 30, 16);
            p.ColumnCount = 1;
            p.RowCount = 9;
            foreach (int h in new int[]
            {
                40,
                54,
                25,
                42,
                76,
                110,
                60,
                42
            }

            )
                p.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(p);
            Label title = new Label();
            title.Text = "VPN";
            title.Font = new Font("Segoe UI", 21, FontStyle.Bold);
            title.ForeColor = Ink;
            title.AutoSize = true;
            p.Controls.Add(title);
            state.Text = "Не подключено";
            state.Font = new Font("Segoe UI", 19, FontStyle.Bold);
            state.ForeColor = Muted;
            state.Dock = DockStyle.Fill;
            state.TextAlign = ContentAlignment.MiddleLeft;
            p.Controls.Add(state);
            Label serverLabel = new Label();
            serverLabel.Text = "Сервер";
            serverLabel.ForeColor = Muted;
            serverLabel.Dock = DockStyle.Fill;
            p.Controls.Add(serverLabel);
            servers.DropDownStyle = ComboBoxStyle.DropDownList;
            servers.Dock = DockStyle.Top;
            servers.DropDownWidth = 480;
            p.Controls.Add(servers);
            Setup(connectButton, "Подключить", 450);
            connectButton.Dock = DockStyle.Top;
            connectButton.Height = 58;
            connectButton.Font = new Font("Segoe UI", 14, FontStyle.Bold);
            connectButton.BackColor = Blue;
            connectButton.ForeColor = Color.White;
            connectButton.FlatAppearance.BorderSize = 0;
            p.Controls.Add(connectButton);
            TableLayoutPanel traffic = new TableLayoutPanel();
            traffic.BackColor = Color.White;
            traffic.Dock = DockStyle.Fill;
            traffic.Padding = new Padding(15, 10, 15, 10);
            traffic.ColumnCount = 2;
            traffic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            traffic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            traffic.RowCount = 3;
            Label received = new Label();
            received.Text = "↓ Входящий";
            received.ForeColor = Muted;
            received.AutoSize = true;
            traffic.Controls.Add(received, 0, 0);
            Label sent = new Label();
            sent.Text = "↑ Исходящий";
            sent.ForeColor = Muted;
            sent.AutoSize = true;
            traffic.Controls.Add(sent, 1, 0);
            foreach (Label l in new Label[]
            {
                downRate,
                upRate
            }

            )
            {
                l.Text = "—";
                l.Font = new Font("Segoe UI", 16, FontStyle.Bold);
                l.ForeColor = Ink;
                l.AutoSize = true;
            }

            traffic.Controls.Add(downRate, 0, 1);
            traffic.Controls.Add(upRate, 1, 1);
            foreach (Label l in new Label[]
            {
                downTotal,
                upTotal
            }

            )
            {
                l.Text = "За сеанс: —";
                l.Font = new Font("Segoe UI", 8);
                l.ForeColor = Muted;
                l.AutoSize = true;
            }

            traffic.Controls.Add(downTotal, 0, 2);
            traffic.Controls.Add(upTotal, 1, 2);
            p.Controls.Add(traffic);
            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.Padding = new Padding(0, 9, 0, 0);
            Setup(settingsButton, "Настройки", 120);
            Setup(exportButton, "Сохранить журнал", 190);
            actions.Controls.AddRange(new Control[] { settingsButton, exportButton });
            p.Controls.Add(actions);
            detail.Text = "Закрытие окна сворачивает VPN к часам. Для выхода используйте меню значка.";
            detail.Font = new Font("Segoe UI", 8);
            detail.ForeColor = Muted;
            detail.Dock = DockStyle.Fill;
            p.Controls.Add(detail);
            systemProxy.Checked = true;
            log.Multiline = true;
            log.ReadOnly = true; // Support controls stay out of the main screen.
            settingsButton.Click += delegate
            {
                OpenSettings();
            };
            exportButton.Click += delegate
            {
                ExportLog();
            };
            tray = new NotifyIcon();
            tray.Text = "VPN — не подключено";
            tray.ContextMenu = new ContextMenu();
            tray.ContextMenu.MenuItems.Add("Открыть VPN", delegate
            {
                ShowMain();
            });
            tray.ContextMenu.MenuItems.Add("Статус и трафик", delegate
            {
                ShowPopup();
            });
            trayToggle = new MenuItem("Подключить", delegate
            {
                if (core != null)
                    Disconnect();
                else if (!busy && nodes.Count > 0)
                    Connect();
            });
            tray.ContextMenu.MenuItems.Add(trayToggle);
            tray.ContextMenu.MenuItems.Add("Настройки", delegate
            {
                ShowMain();
                OpenSettings();
            });
            tray.ContextMenu.MenuItems.Add("Сохранить журнал", delegate
            {
                ExportLog();
            });
            tray.ContextMenu.MenuItems.Add("-");
            tray.ContextMenu.MenuItems.Add("Выход", delegate
            {
                exitRequested = true;
                Close();
            });
            tray.MouseClick += delegate (object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                    ShowPopup();
            };
            tray.MouseDoubleClick += delegate
            {
                ShowMain();
            };
            tray.Visible = true;
            SetStatus("Не подключено", Muted);
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized)
                    Hide();
            };
            showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\VPN.ShowWindow");
            FormClosed += delegate
            {
                timer.Stop();
                showSignal.Close();
                if (tray != null)
                {
                    tray.Visible = false;
                    tray.Dispose();
                }

                if (trayIcon != null)
                    trayIcon.Dispose();
                if (popup != null)
                    popup.Dispose();
                if (settingsForm != null)
                    settingsForm.Dispose();
            };
        }

        void ShowMain()
        {
            if (popup != null)
                popup.Hide();
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void SetStatus(string text, Color color)
        {
            statusText = text;
            state.Text = text;
            state.ForeColor = color;
            if (tray == null)
                return;
            using (Bitmap bitmap = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (Brush b = new SolidBrush(color))
                        g.FillEllipse(b, 2, 2, 28, 28);
                    using (Pen pen = new Pen(Color.White, 3))
                    {
                        g.DrawLine(pen, 10, 15, 14, 20);
                        g.DrawLine(pen, 14, 20, 23, 10);
                    }
                }

                IntPtr h = bitmap.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                tray.Icon = icon;
                if (trayIcon != null)
                    trayIcon.Dispose();
                trayIcon = icon;
            }

            RefreshTrayText();
        }

        void RefreshTrayText()
        {
            if (tray == null)
                return;
            string t = "VPN · " + statusText + "\n↓ " + downText + "  ↑ " + upText;
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
            if (trayToggle != null)
            {
                trayToggle.Text = core != null ? "Отключить" : "Подключить";
                trayToggle.Enabled = !busy && (core != null || nodes.Count > 0);
            }

            if (popup != null && !popup.IsDisposed)
                popup.UpdateStatus(statusText, downText, upText, core != null, !busy && (core != null || nodes.Count > 0));
        }

        void ShowPopup()
        {
            if (popup == null || popup.IsDisposed)
            {
                popup = new StatusPopup(delegate
                {
                    if (core != null)
                        Disconnect();
                    else if (!busy && nodes.Count > 0)
                        Connect();
                }, ShowMain);
            }

            RefreshTrayText();
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            popup.Location = new Point(area.Right - popup.Width - 12, area.Bottom - popup.Height - 12);
            popup.Show();
            popup.Activate();
        }

        void OpenSettings()
        {
            if (settingsForm == null || settingsForm.IsDisposed)
            {
                settingsForm = new Form();
                settingsForm.Text = "Настройки VPN";
                settingsForm.ClientSize = new Size(510, 385);
                settingsForm.StartPosition = FormStartPosition.CenterParent;
                settingsForm.Font = Font;
                FlowLayoutPanel p = new FlowLayoutPanel();
                p.Dock = DockStyle.Fill;
                p.Padding = new Padding(22);
                p.FlowDirection = FlowDirection.TopDown;
                p.WrapContents = false;
                settingsForm.Controls.Add(p);
                Label title = new Label();
                title.Text = "Подписка и помощь";
                title.Font = new Font("Segoe UI", 15, FontStyle.Bold);
                title.AutoSize = true;
                p.Controls.Add(title);
                Setup(importButton, "Добавить или заменить подписку", 350);
                Setup(refreshButton, "Обновить серверы", 350);
                Setup(fileButton, "Импортировать файл настроек", 350);
                Setup(removeButton, "Удалить подписку", 350);
                Setup(logsButton, "Открыть журнал", 350);
                foreach (Button b in new Button[]
                {
                    importButton,
                    refreshButton,
                    fileButton,
                    removeButton,
                    logsButton
                }

                )
                {
                    b.Margin = new Padding(0, 5, 0, 0);
                    p.Controls.Add(b);
                }

                Button licenses = new Button();
                Setup(licenses, "Лицензии компонентов", 350);
                licenses.Margin = new Padding(0, 5, 0, 0);
                licenses.Click += delegate
                {
                    string folder = Path.Combine(Program.Home, "licenses");
                    Directory.CreateDirectory(folder);
                    foreach (string name in new string[]
                    {
                        "LICENSE.txt",
                        "LICENSE-mihomo.txt",
                        "LICENSE-Xray.txt",
                        "LICENSE-YamlDotNet.txt"
                    }

                    )
                        File.Copy(Assets.Get(name), Path.Combine(folder, name), true);
                    Process.Start("explorer.exe", folder);
                };
                p.Controls.Add(licenses);
                Label version = new Label();
                version.Text = "VPN 0.7 · настройки сохраняются на этом компьютере";
                version.Font = new Font("Segoe UI", 8);
                version.AutoSize = true;
                version.Margin = new Padding(0, 12, 0, 0);
                p.Controls.Add(version);
                settingsForm.FormClosing += delegate (object sender, FormClosingEventArgs e)
                {
                    if (!exitRequested)
                    {
                        e.Cancel = true;
                        settingsForm.Hide();
                    }
                };
            }

            if (Environment.UserInteractive)
                settingsForm.Show(this);
            else
                settingsForm.Show();
            settingsForm.Activate();
        }

        void ExportLog()
        {
            if (!Visible)
                ShowMain();
            Diagnostics.Write("diagnostics.export", "");
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Filter = "Журнал диагностики|*.log";
                d.FileName = "VPN-diagnostics.log";
                if (d.ShowDialog(this) != DialogResult.OK)
                    return;
                try
                {
                    using (StreamWriter writer = new StreamWriter(d.FileName, false, System.Text.Encoding.UTF8))
                    {
                        writer.WriteLine("VPN 0.7 · журнал диагностики");
                        for (int i = 2; i >= 0; i--)
                        {
                            string path = Diagnostics.FilePath + (i == 0 ? "" : "." + i);
                            if (File.Exists(path))
                                writer.Write(File.ReadAllText(path));
                        }
                    }

                    MessageBox.Show("Журнал сохранён. Его можно отправить человеку, который помогает с настройкой.", "VPN");
                }
                catch (Exception e)
                {
                    Diagnostics.Failure("diagnostics.export", e);
                    MessageBox.Show("Не удалось сохранить журнал в выбранную папку.", "VPN");
                }
            }
        }

        void PollStatus()
        {
            if (showSignal != null && showSignal.WaitOne(0, false))
                ShowMain();
            if (core == null || busy || monitorBusy)
                return;
            if (core.HasExited)
                return;
            monitorBusy = true;
            int session = generation;
            int port = localPort, statsPort = apiPort;
            string key = apiSecret, binary = engine;
            bool xray = activeXray;
            bool probe = (DateTime.UtcNow - lastProbe).TotalSeconds >= 30;
            if (probe)
                lastProbe = DateTime.UtcNow;
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate (object sender, DoWorkEventArgs e)
            {
                TrafficSample sample;
                try
                {
                    sample = Telemetry.Read(xray, binary, statsPort, key);
                }
                catch (Exception ex)
                {
                    sample = new TrafficSample();
                    if (!metricsErrorLogged)
                    {
                        Diagnostics.Failure("traffic.read", ex);
                        metricsErrorLogged = true;
                    }
                }

                if (probe)
                {
                    sample.Probed = true;
                    sample.Reachable = Telemetry.Probe(port);
                }

                e.Result = sample;
            };
            worker.RunWorkerCompleted += delegate (object sender, RunWorkerCompletedEventArgs e)
            {
                monitorBusy = false;
                worker.Dispose();
                if (IsDisposed || closing || session != generation || core == null)
                    return;
                if (e.Error != null)
                {
                    Diagnostics.Failure("status.poll", e.Error);
                    return;
                }

                TrafficSample sample = (TrafficSample)e.Result;
                if (sample.MetricsAvailable)
                {
                    double seconds = lastSample == DateTime.MinValue ? 0 : (DateTime.UtcNow - lastSample).TotalSeconds;
                    downText = seconds <= 0 ? "0 Б/с" : Telemetry.Bytes(Math.Max(0, sample.Down - lastDown) / seconds) + "/с";
                    upText = seconds <= 0 ? "0 Б/с" : Telemetry.Bytes(Math.Max(0, sample.Up - lastUp) / seconds) + "/с";
                    downRate.Text = downText;
                    upRate.Text = upText;
                    downTotal.Text = "За сеанс: " + Telemetry.Bytes(sample.Down);
                    upTotal.Text = "За сеанс: " + Telemetry.Bytes(sample.Up);
                    lastDown = sample.Down;
                    lastUp = sample.Up;
                    lastSample = DateTime.UtcNow;
                    metricsErrorLogged = false;
                }
                else
                {
                    downText = upText = "нет данных";
                    downRate.Text = upRate.Text = "—";
                }

                if (sample.Probed)
                {
                    SetStatus(sample.Reachable ? "Подключено" : "Проверка не прошла", sample.Reachable ? Color.FromArgb(26, 145, 103) : Color.FromArgb(190, 128, 28));
                    Diagnostics.Write("connectivity.probe", "reachable=" + sample.Reachable);
                }

                RefreshTrayText();
            };
            worker.RunWorkerAsync();
        }
    }

    sealed class StatusPopup : Form
    {
        Label status = new Label(), traffic = new Label();
        Button toggle = new Button();
        public StatusPopup(Action action, Action open)
        {
            Text = "VPN";
            Size = new Size(320, 215);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Segoe UI", 10);
            BackColor = Color.White;
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.Padding = new Padding(18);
            p.FlowDirection = FlowDirection.TopDown;
            p.WrapContents = false;
            Controls.Add(p);
            status.Font = new Font("Segoe UI", 13, FontStyle.Bold);
            status.AutoSize = true;
            p.Controls.Add(status);
            traffic.AutoSize = true;
            traffic.Margin = new Padding(0, 12, 0, 12);
            p.Controls.Add(traffic);
            toggle.Size = new Size(265, 34);
            toggle.FlatStyle = FlatStyle.Flat;
            toggle.BackColor = Color.FromArgb(38, 94, 232);
            toggle.ForeColor = Color.White;
            toggle.FlatAppearance.BorderSize = 0;
            toggle.Click += delegate
            {
                action();
            };
            p.Controls.Add(toggle);
            LinkLabel more = new LinkLabel();
            more.Text = "Открыть приложение";
            more.AutoSize = true;
            more.Margin = new Padding(0, 9, 0, 0);
            more.Click += delegate
            {
                Hide();
                open();
            };
            p.Controls.Add(more);
            Deactivate += delegate
            {
                Hide();
            };
        }

        public void UpdateStatus(string s, string down, string up, bool connected, bool enabled)
        {
            status.Text = s;
            traffic.Text = "↓ " + down + "     ↑ " + up;
            toggle.Text = connected ? "Отключить" : "Подключить";
            toggle.Enabled = enabled;
        }
    }
}
