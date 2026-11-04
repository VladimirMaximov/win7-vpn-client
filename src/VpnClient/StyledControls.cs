using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MihomoDesk
{
    sealed class PowerButton : Button
    {
        bool connected, working;
        string elapsed = "00:00:00";
        public bool Healthy;
        public bool Connected { get { return connected; } set { connected = value; UpdateCaption(); } }
        public bool Working { get { return working; } set { working = value; UpdateCaption(); } }
        public string Elapsed { get { return elapsed; } set { elapsed = value; UpdateCaption(); } }
        public PowerButton()
        {
            FlatStyle = FlatStyle.System;
            UseVisualStyleBackColor = true;
            Size = new Size(180, 52);
            UpdateCaption();
        }
        void UpdateCaption()
        {
            Text = (working ? "Подключение…" : connected ? "Отключить" : "Подключить") + Environment.NewLine + elapsed;
        }
    }

    sealed class ServerChoice
    {
        public string Caption, Protocol;
        public int Index;
        public override string ToString()
        {
            return Caption;
        }

        public static void Draw(object sender, DrawItemEventArgs e)
        {
            ComboBox box = (ComboBox)sender;
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= box.Items.Count)
                return;
            ServerChoice item = (ServerChoice)box.Items[e.Index];
            Color ink = (e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(25, 58, 133) : Color.FromArgb(31, 43, 66);
            using (Font title = new Font("Segoe UI", 10, FontStyle.Bold))
            using (Font subtitle = new Font("Segoe UI", 8))
            {
                TextRenderer.DrawText(e.Graphics, item.Caption, title, new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 2, e.Bounds.Width - 16, 20), ink, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(e.Graphics, item.Protocol, subtitle, new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 22, e.Bounds.Width - 16, 16), Color.FromArgb(108, 120, 143), TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            }

            e.DrawFocusRectangle();
        }
    }
}
