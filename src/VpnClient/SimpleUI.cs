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
        Stopwatch sessionClock = new Stopwatch();
        System.Windows.Forms.Timer clockTimer = new System.Windows.Forms.Timer();
        MenuItem trayToggle;
        string statusText = "Не подключено", downText = "—", upText = "—";
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);
        static Color Blue = Color.FromArgb(38, 94, 232), Ink = Color.FromArgb(27, 39, 63), Muted = Color.FromArgb(105, 117, 137);
        void BuildSimpleUI()
        {
            Text = "VPN";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            ClientSize = new Size(620, Math.Max(620, Math.Min(740, Screen.PrimaryScreen.WorkingArea.Height - 70)));
            MinimumSize = new Size(620, 660);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9);
            BackColor = SystemColors.Control;
            TableLayoutPanel p = new TableLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.Padding = new Padding(12);
            p.ColumnCount = 1;
            p.RowCount = 6;
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            p.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            Panel content = new Panel();
            content.Dock = DockStyle.Fill;
            Controls.Add(content);
            p.Dock = DockStyle.None;
            content.Controls.Add(p);
            EventHandler layoutContent = delegate
            {
                int width = Math.Min(content.ClientSize.Width, 600);
                p.SetBounds((content.ClientSize.Width - width) / 2, 0, width, content.ClientSize.Height);
            };
            content.Resize += layoutContent;
            Load += layoutContent;
            Shown += layoutContent;
            layoutContent(null, EventArgs.Empty);
            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.ColumnCount = 2;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            p.Controls.Add(header);
            Label title = new Label();
            title.Text = "VPN";
            title.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            title.ForeColor = Ink;
            title.AutoSize = true;
            header.Controls.Add(title, 0, 0);
            Setup(settingsButton, "Настройки", 120);
            settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            settingsButton.FlatStyle = FlatStyle.System;
            settingsButton.ForeColor = Muted;
            header.Controls.Add(settingsButton, 1, 0);
            Panel hero = new Panel();
            hero.Dock = DockStyle.Fill;
            hero.Margin = new Padding(0);
            connectButton.Size = new Size(180, 52);
            hero.Controls.Add(connectButton);
            hero.Layout += delegate
            {
                connectButton.Location = new Point((hero.ClientSize.Width - connectButton.Width) / 2, 0);
            };
            state.Text = "Не подключено";
            state.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            state.ForeColor = Muted;
            state.Dock = DockStyle.Bottom;
            state.Height = 30;
            state.TextAlign = ContentAlignment.MiddleCenter;
            hero.Controls.Add(state);
            p.Controls.Add(hero);
            TableLayoutPanel section = new TableLayoutPanel();
            section.Dock = DockStyle.Fill;
            section.ColumnCount = 2;
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            Label caption = new Label();
            caption.Text = "Подписки";
            caption.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            caption.ForeColor = Ink;
            caption.AutoSize = true;
            caption.Anchor = AnchorStyles.Left;
            section.Controls.Add(caption, 0, 0);
            Setup(addSubscriptionButton, "+ Добавить", 125);
            addSubscriptionButton.Anchor = AnchorStyles.Right;
            addSubscriptionButton.ForeColor = Blue;
            addSubscriptionButton.FlatStyle = FlatStyle.System;
            addSubscriptionButton.Click += delegate
            {
                ShowAddDialog("");
            };
            section.Controls.Add(addSubscriptionButton, 1, 0);
            p.Controls.Add(section);
            cardsHost.Dock = DockStyle.Fill;
            cardsHost.AutoScroll = true;
            cardsHost.WrapContents = false;
            cardsHost.FlowDirection = FlowDirection.TopDown;
            cardsHost.Margin = new Padding(0, 0, 0, 8);
            cardsHost.Resize += delegate
            {
                foreach (Control card in cardsHost.Controls)
                    card.Width = Math.Max(300, cardsHost.ClientSize.Width - 22);
            };
            p.Controls.Add(cardsHost);
            Panel trafficCard = new Panel();
            trafficCard.Dock = DockStyle.Fill;
            trafficCard.Padding = new Padding(8, 4, 8, 4);
            TableLayoutPanel traffic = new TableLayoutPanel();
            traffic.Dock = DockStyle.Fill;
            traffic.ColumnCount = 2;
            traffic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            traffic.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            traffic.RowCount = 3;
            trafficCard.Controls.Add(traffic);
            p.Controls.Add(trafficCard);
            Label received = new Label();
            received.Text = "↓ Получено";
            received.AutoSize = true;
            received.ForeColor = Muted;
            Label sent = new Label();
            sent.Text = "↑ Отправлено";
            sent.AutoSize = true;
            sent.ForeColor = Muted;
            traffic.Controls.Add(received, 0, 0);
            traffic.Controls.Add(sent, 1, 0);
            foreach (Label label in new Label[]
            {
                downRate,
                upRate
            }

            )
            {
                label.Text = "—";
                label.Font = new Font("Segoe UI", 10, FontStyle.Bold);
                label.ForeColor = Ink;
                label.AutoSize = true;
            }

            traffic.Controls.Add(downRate, 0, 1);
            traffic.Controls.Add(upRate, 1, 1);
            foreach (Label label in new Label[]
            {
                downTotal,
                upTotal
            }

            )
            {
                label.Text = "За сеанс: —";
                label.Font = new Font("Segoe UI", 8);
                label.ForeColor = Muted;
                label.AutoSize = true;
            }

            traffic.Controls.Add(downTotal, 0, 2);
            traffic.Controls.Add(upTotal, 1, 2);
            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = new Padding(0);
            footer.RowCount = 1;
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footer.ColumnCount = 2;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Setup(exportButton, "Сохранить журнал", 175);
            exportButton.Margin = new Padding(0);
            exportButton.Anchor = AnchorStyles.Left;
            exportButton.Font = new Font("Segoe UI", 9);
            exportButton.FlatStyle = FlatStyle.System;
            footer.Controls.Add(exportButton, 0, 0);
            notice.Text = "Закрытие окна сворачивает VPN к часам.";
            notice.ForeColor = Muted;
            notice.Font = new Font("Segoe UI", 8);
            notice.Dock = DockStyle.Fill;
            notice.TextAlign = ContentAlignment.MiddleLeft;
            footer.Controls.Add(notice, 1, 0);
            p.Controls.Add(footer);
            systemProxy.Checked = true;
            log.Multiline = true;
            log.ReadOnly = true;
            clockTimer.Interval = 1000;
            clockTimer.Tick += delegate
            {
                UpdateClock();
            };
            clockTimer.Start();
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
                clockTimer.Stop();
                clockTimer.Dispose();
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
            if (text == "Подключено" && core != null && !sessionClock.IsRunning)
                sessionClock.Start();
            connectButton.Healthy = text == "Подключено";
            connectButton.Invalidate();
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
                popup.UpdateStatus(statusText, downText, upText, core != null, !busy && (core != null || nodes.Count > 0), ClockText());
        }

        string ClockText()
        {
            TimeSpan t = sessionClock.Elapsed;
            return ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
        }

        void UpdateClock()
        {
            connectButton.Elapsed = ClockText();
            connectButton.Invalidate();
            RefreshTrayText();
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
                title.Text = "Настройки и помощь";
                title.Font = new Font("Segoe UI", 15, FontStyle.Bold);
                title.AutoSize = true;
                p.Controls.Add(title);
                Setup(importButton, "Добавить подписку или сервер", 350);
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
                version.Text = "VPN 0.10.3 · настройки сохраняются на этом компьютере";
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
                d.FileName = "VPN-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".log";
                d.DefaultExt = "log";
                d.AddExtension = true;
                d.OverwritePrompt = true;
                if (d.ShowDialog(this) != DialogResult.OK)
                    return;
                try
                {
                    Diagnostics.ExportLog(d.FileName, "0.10.3");
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
        Label status = new Label(), traffic = new Label(), elapsed = new Label();
        Button toggle = new Button();
        public StatusPopup(Action action, Action open)
        {
            Text = "VPN";
            Size = new Size(320, 245);
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
            status.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            status.AutoSize = true;
            p.Controls.Add(status);
            elapsed.AutoSize = true;
            elapsed.ForeColor = Color.DimGray;
            elapsed.Margin = new Padding(0, 6, 0, 0);
            p.Controls.Add(elapsed);
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

        public void UpdateStatus(string s, string down, string up, bool connected, bool enabled, string duration)
        {
            status.Text = s;
            elapsed.Text = "Время подключения: " + duration;
            traffic.Text = "↓ " + down + "     ↑ " + up;
            toggle.Text = connected ? "Отключить" : "Подключить";
            toggle.Enabled = enabled;
        }
    }
}
