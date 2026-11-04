using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.ComponentModel;

namespace MihomoDesk
{
    sealed partial class MainForm
    {
        SubscriptionCatalog book = new SubscriptionCatalog();
        bool catalogReadOnly = false, rebuilding = false;
        FlowLayoutPanel cardsHost = new FlowLayoutPanel();
        Button addSubscriptionButton = new Button();
        Label emptySubscriptions = new Label(), notice = new Label();
        Dictionary<string, Label> latencyLabels = new Dictionary<string, Label>();
        Dictionary<string, long> latencies = new Dictionary<string, long>();
        List<Control> catalogActions = new List<Control>();
        string connectedEntryId = "", connectedNodeKey = "";
        string CollectionFile
        {
            get
            {
                return Path.Combine(Program.Home, "subscriptions.dat");
            }
        }

        void LoadCatalog()
        {
            try
            {
                book = CatalogRepository.Load();
            }
            catch (Exception e)
            {
                catalogReadOnly = true;
                Diagnostics.Failure("subscriptions.load", e);
                notice.Text = "Не удалось прочитать настройки. Сохраните журнал для помощи.";
            }

            ApplyActive();
            RenderCards();
            Diagnostics.Write("subscriptions.loaded", "entries=" + book.Entries.Count);
        }

        void ApplyActive()
        {
            SubscriptionEntry active = book.Active;
            nodes = active == null ? new List<Dictionary<string, object>>() : active.Nodes;
            subscriptionUrl = active == null ? "" : active.Url;
            subscriptionUpdated = active == null ? DateTime.MinValue : active.UpdatedUtc;
            selection = active == null ? "" : active.Selected;
        }

        bool CommitCatalog(SubscriptionCatalog candidate)
        {
            if (catalogReadOnly)
            {
                Error("Настройки не прочитаны. Чтобы не потерять их, изменения заблокированы. Сохраните журнал.");
                return false;
            }

            try
            {
                CatalogRepository.Save(candidate);
                book = candidate;
                ApplyActive();
                RenderCards();
                UpdateButtons();
                return true;
            }
            catch (Exception e)
            {
                Diagnostics.Failure("subscriptions.save", e);
                Error("Не удалось сохранить изменения. Прежние подписки остаются на месте.");
                return false;
            }
        }

        void ShowAddDialog(string initial)
        {
            using (ImportDialog dialog = new ImportDialog(initial))
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    AcceptSubscription(dialog.Downloaded);
        }

        void AcceptSubscription(SubscriptionResult result)
        {
            SubscriptionCatalog next = book.Copy();
            string active = next.ActiveId;
            try
            {
                next.Add(result, result.Name);
                if (core != null)
                    next.ActiveId = active;
                if (CommitCatalog(next))
                {
                    notice.Text = "Подключение добавлено. Выберите сервер и нажмите кнопку подключения.";
                    Diagnostics.Write("subscription.added", "entries=" + book.Entries.Count);
                }
            }
            catch (Exception e)
            {
                Diagnostics.Failure("subscription.add", e);
                Error(e.Message);
            }
        }

        void SwitchSelection(string id, int index)
        {
            if (rebuilding || busy)
                return;
            SubscriptionEntry entry = book.Find(id);
            if (entry == null)
                return;
            string key = Subscriptions.NodeKey(entry.Nodes[index]);
            if (book.ActiveId == id && entry.Selected == key)
                return;
            bool reconnect = core != null;
            SubscriptionCatalog next = book.Copy();
            next.Select(id, index);
            if (!CommitCatalog(next))
                return;
            if (reconnect)
            {
                Disconnect();
                Connect();
            }
            else
                SetStatus("Готово к подключению", Muted);
        }

        void DeleteSubscription(string id)
        {
            if (busy)
                return;
            SubscriptionEntry entry = book.Find(id);
            if (entry == null)
                return;
            if (MessageBox.Show(this, "Удалить «" + entry.Name + "» и её серверы? Остальные подписки останутся.", "Удалить подписку", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            bool stop = core != null && connectedEntryId == id;
            SubscriptionCatalog next = book.Copy();
            next.Remove(id);
            if (CommitCatalog(next))
            {
                if (stop)
                    Disconnect();
                notice.Text = "Подписка удалена.";
                Diagnostics.Write("subscription.removed", "entries=" + book.Entries.Count);
            }
        }

        void RefreshSubscription(bool quiet, Action after)
        {
            if (book.Active == null)
            {
                if (after != null)
                    after();
                return;
            }

            RefreshEntry(book.ActiveId, quiet, after);
        }

        void RefreshEntry(string id, bool quiet, Action after)
        {
            SubscriptionEntry entry = book.Find(id);
            if (entry == null || entry.Url == "" || busy)
            {
                if (after != null)
                    after();
                return;
            }

            bool reconnect = core != null && connectedEntryId == id;
            if (reconnect)
                Disconnect();
            busy = true;
            UpdateButtons();
            notice.Text = "Обновляем серверы «" + entry.Name + "»…";
            string url = entry.Url;
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
                    notice.Text = "Обновить не удалось. Сохранённые серверы доступны.";
                    if (!quiet)
                        Error("Не удалось обновить эту подписку. Остальные данные не изменены.");
                }
                else
                {
                    try
                    {
                        SubscriptionCatalog next = book.Copy();
                        next.Update(id, (SubscriptionResult)e.Result);
                        latencies.Clear();
                        if (CommitCatalog(next))
                        {
                            notice.Text = "Серверы обновлены.";
                            Diagnostics.Write("subscription.refreshed", "nodes=" + ((SubscriptionResult)e.Result).Nodes.Count);
                        }
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Failure("subscription.refresh-save", ex);
                        Error("Не удалось сохранить обновление.");
                    }
                }

                UpdateButtons();
                worker.Dispose();
                if (after != null)
                    after();
                else if (reconnect)
                    ConnectCore();
            };
            worker.RunWorkerAsync();
        }

        void SaveSubscription()
        {
            if (book.Active == null || catalogReadOnly)
                return;
            SubscriptionCatalog next = book.Copy();
            next.Find(book.ActiveId).Selected = selection;
            CommitCatalog(next);
        }

        void Fill()
        {
            RenderCards();
        }

        void Import(string text)
        {
            try
            {
                AcceptSubscription(Subscriptions.ImportInput(text));
            }
            catch (Exception e)
            {
                Diagnostics.Failure("import", e);
                Error("Не удалось прочитать настройки. Проверьте файл или ссылку.");
            }
        }

        void UpdateButtons()
        {
            bool running = core != null;
            connectButton.Enabled = running || (!busy && nodes.Count > 0);
            connectButton.Text = running ? "Отключить" : "Подключить";
            connectButton.Connected = running;
            connectButton.Working = busy && !running;
            connectButton.Invalidate();
            addSubscriptionButton.Enabled = importButton.Enabled = fileButton.Enabled = !busy && !catalogReadOnly;
            disconnectButton.Enabled = running;
            refreshButton.Enabled = !busy && book.Active != null && book.Active.Url != "" && !catalogReadOnly;
            removeButton.Enabled = !busy && book.Active != null && !catalogReadOnly;
            foreach (Control c in catalogActions)
                c.Enabled = !busy && !catalogReadOnly;
            foreach (Control c in catalogActions)
            {
                Button b = c as Button;
                if (b != null && b.Tag is SubscriptionEntry)
                {
                    SubscriptionEntry entry = (SubscriptionEntry)b.Tag;
                    if (b.AccessibleName == "Обновить подписку")
                        b.Enabled = !busy && entry.Url != "" && !catalogReadOnly;
                }
            }

            RefreshTrayText();
        }

        void RenderCards()
        {
            rebuilding = true;
            try
            {
                cardsHost.SuspendLayout();
                foreach (Control child in new List<Control>(Children(cardsHost)))
                    child.Dispose();
                cardsHost.Controls.Clear();
                catalogActions.Clear();
                latencyLabels.Clear();
                servers = new ComboBox();
                if (book.Entries.Count == 0)
                {
                    emptySubscriptions = new Label();
                    emptySubscriptions.Text = "Добавьте подписку или настройки сервера.\nВсе подключения будут собраны здесь.";
                    emptySubscriptions.ForeColor = Muted;
                    emptySubscriptions.AutoSize = false;
                    emptySubscriptions.Height = 90;
                    emptySubscriptions.Width = Math.Max(250, cardsHost.ClientSize.Width - 22);
                    emptySubscriptions.Padding = new Padding(16, 15, 8, 0);
                    cardsHost.Controls.Add(emptySubscriptions);
                }

                foreach (SubscriptionEntry entry in book.Entries)
                {
                    string id = entry.Id;
                    RoundedPanel card = new RoundedPanel();
                    card.Selected = id == book.ActiveId;
                    card.Height = 110;
                    card.Padding = new Padding(12, 6, 12, 6);
                    card.Width = Math.Max(300, cardsHost.ClientSize.Width - 22);
                    card.Margin = new Padding(0, 0, 0, 6);
                    card.Tag = id;
                    TableLayoutPanel table = new TableLayoutPanel();
                    table.Dock = DockStyle.Fill;
                    table.ColumnCount = 1;
                    table.RowCount = 3;
                    table.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
                    table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
                    table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                    card.Controls.Add(table);
                    Button title = new Button();
                    title.Text = (card.Selected ? "●  " : "○  ") + entry.Name + "    ·    " + entry.Nodes.Count;
                    title.TextAlign = ContentAlignment.MiddleLeft;
                    title.FlatStyle = FlatStyle.Flat;
                    title.FlatAppearance.BorderSize = 0;
                    title.BackColor = Color.White;
                    title.ForeColor = card.Selected ? Blue : Ink;
                    title.Font = new Font("Segoe UI", 10, FontStyle.Bold);
                    title.Dock = DockStyle.Fill;
                    title.Margin = new Padding(0);
                    title.Click += delegate
                    {
                        SwitchSelection(id, Subscriptions.Selection(book.Find(id).Nodes, book.Find(id).Selected));
                    };
                    table.Controls.Add(title);
                    catalogActions.Add(title);
                    ComboBox combo = new ComboBox();
                    combo.DropDownStyle = ComboBoxStyle.DropDownList;
                    combo.DrawMode = DrawMode.Normal;
                    combo.ItemHeight = 40;
                    combo.DropDownWidth = Math.Max(420, card.Width - 28);
                    combo.Dock = DockStyle.Top;
                    for (int i = 0; i < entry.Nodes.Count; i++)
                        combo.Items.Add(new ServerChoice { Index = i, Caption = ServerPresentation.Location(entry.Nodes[i]), Protocol = ServerPresentation.Protocol(entry.Nodes[i]) });
                    combo.SelectedIndex = Subscriptions.Selection(entry.Nodes, entry.Selected);
                    if (card.Selected)
                        servers = combo;
                    combo.SelectedIndexChanged += delegate
                    {
                        if (combo.SelectedIndex >= 0)
                            SwitchSelection(id, combo.SelectedIndex);
                    };
                    Panel serverRow = new Panel();
                    serverRow.Dock = DockStyle.Fill;
                    serverRow.Margin = new Padding(0);
                    serverRow.Controls.Add(combo);
                    Label location = new Label();
                    location.Text = ((ServerChoice)combo.SelectedItem).Caption;
                    location.BackColor = Color.White;
                    location.ForeColor = Ink;
                    location.Font = new Font("Segoe UI", 10, FontStyle.Bold);
                    location.AutoEllipsis = true;
                    location.Location = new Point(6, 2);
                    location.Height = 21;
                    location.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                    location.Cursor = Cursors.Hand;
                    location.Click += delegate
                    {
                        if (combo.Enabled)
                            combo.DroppedDown = true;
                    };
                    serverRow.Controls.Add(location);
                    location.BringToFront();
                    serverRow.Resize += delegate
                    {
                        location.Width = Math.Max(10, serverRow.ClientSize.Width - 32);
                    };
                    serverRow.Layout += delegate
                    {
                        location.Width = Math.Max(10, serverRow.ClientSize.Width - 32);
                    };
                    Label protocol = new Label();
                    protocol.Text = ((ServerChoice)combo.SelectedItem).Protocol;
                    protocol.Dock = DockStyle.Bottom;
                    protocol.Height = 17;
                    protocol.Font = new Font("Segoe UI", 8);
                    protocol.ForeColor = Muted;
                    protocol.AutoEllipsis = true;
                    serverRow.Controls.Add(protocol);
                    table.Controls.Add(serverRow);
                    catalogActions.Add(combo);
                    FlowLayoutPanel buttons = new FlowLayoutPanel();
                    buttons.Dock = DockStyle.Fill;
                    buttons.Margin = new Padding(0);
                    buttons.WrapContents = false;
                    table.Controls.Add(buttons);
                    Button update = new Button();
                    CardButton(update, "Обновить", 85);
                    update.AccessibleName = "Обновить подписку";
                    update.Tag = entry;
                    update.Click += delegate
                    {
                        RefreshEntry(id, false, null);
                    };
                    buttons.Controls.Add(update);
                    catalogActions.Add(update);
                    Button probe = new Button();
                    CardButton(probe, "Проверить", 90);
                    probe.Click += delegate
                    {
                        CheckLatency(id, combo.SelectedIndex);
                    };
                    buttons.Controls.Add(probe);
                    catalogActions.Add(probe);
                    Label latency = new Label();
                    latency.Width = 90;
                    latency.Height = 28;
                    latency.TextAlign = ContentAlignment.MiddleLeft;
                    string latencyKey = id + ":" + Subscriptions.NodeKey(entry.Nodes[combo.SelectedIndex]);
                    long ms;
                    latency.Text = latencies.TryGetValue(latencyKey, out ms) ? ms < 0 ? "Нет ответа" : ms + " мс" : "— мс";
                    latency.ForeColor = Muted;
                    buttons.Controls.Add(latency);
                    latencyLabels[id] = latency;
                    Button remove = new Button();
                    CardButton(remove, "Удалить", 75);
                    remove.ForeColor = Color.FromArgb(164, 69, 79);
                    remove.Click += delegate
                    {
                        DeleteSubscription(id);
                    };
                    buttons.Controls.Add(remove);
                    catalogActions.Add(remove);
                    cardsHost.Controls.Add(card);
                }
            }
            finally
            {
                cardsHost.ResumeLayout();
                rebuilding = false;
            }

            UpdateButtons();
        }

        static IEnumerable<Control> Children(Control parent)
        {
            foreach (Control c in parent.Controls)
                yield return c;
        }

        static void CardButton(Button b, string text, int width)
        {
            b.Text = text;
            b.Size = new Size(width, 27);
            b.Font = new Font("Segoe UI", 8);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(224, 229, 240);
            b.BackColor = Color.White;
            b.Margin = new Padding(0, 0, 7, 0);
        }

        void CheckLatency(string id, int index)
        {
            if (busy)
                return;
            SubscriptionEntry entry = book.Find(id);
            if (entry == null || index < 0 || index >= entry.Nodes.Count)
                return;
            Dictionary<string, object> node = entry.Nodes[index];
            string key = id + ":" + Subscriptions.NodeKey(node);
            bool running = core != null && connectedEntryId == id && connectedNodeKey == Subscriptions.NodeKey(node);
            int port = localPort;
            busy = true;
            UpdateButtons();
            if (latencyLabels.ContainsKey(id))
                latencyLabels[id].Text = "Проверяем…";
            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate (object sender, DoWorkEventArgs e)
            {
                e.Result = running ? ProbeRunner.MeasureProxy(port) : ProbeRunner.Measure(node);
            };
            worker.RunWorkerCompleted += delegate (object sender, RunWorkerCompletedEventArgs e)
            {
                busy = false;
                worker.Dispose();
                long ms = e.Error == null ? (long)e.Result : -1;
                latencies[key] = ms;
                if (e.Error != null)
                    Diagnostics.Failure("server.probe", e.Error);
                Diagnostics.Write("server.probe-result", "success=" + (ms >= 0) + " elapsedMs=" + ms);
                if (latencyLabels.ContainsKey(id))
                {
                    latencyLabels[id].Text = ms < 0 ? "Нет ответа" : ms + " мс";
                    latencyLabels[id].ForeColor = ms < 0 ? Color.FromArgb(177, 80, 69) : Color.FromArgb(24, 139, 105);
                }

                UpdateButtons();
            };
            worker.RunWorkerAsync();
        }
    }
}
