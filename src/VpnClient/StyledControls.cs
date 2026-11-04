using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MihomoDesk
{
    sealed class RoundedPanel : Panel
    {
        public bool Selected;
        public RoundedPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.White;
            Padding = new Padding(14);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Color.FromArgb(244, 247, 252) : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Round(new Rectangle(0, 0, Math.Max(18, Width - 1), Math.Max(18, Height - 1)), 8))
            using (Brush fill = new SolidBrush(Color.White))
                e.Graphics.FillPath(fill, path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(new Rectangle(1, 1, Width - 3, Height - 3), 8))
            using (Pen pen = new Pen(Selected ? Color.FromArgb(80, 118, 235) : Color.FromArgb(225, 230, 240), Selected ? 2 : 1))
                e.Graphics.DrawPath(pen, p);
        }

        static GraphicsPath Round(Rectangle r, int d)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    sealed class PowerButton : Button
    {
        public bool Connected, Working, Healthy;
        public string Elapsed = "00:00:00";
        bool hover;
        public PowerButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Size = new Size(196, 196);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
                Region old = Region;
                Region = new Region(p);
                if (old != null)
                    old.Dispose();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float size = Math.Min(Width, Height);
            RectangleF outer = new RectangleF(3, 3, size - 6, size - 6), body = new RectangleF(12, 12, size - 24, size - 24);
            Color blue = Healthy ? Color.FromArgb(20, 151, 120) : Color.FromArgb(57, 92, 229);
            if (!Enabled)
                blue = Color.FromArgb(146, 157, 183);
            using (Brush b = new SolidBrush(Connected ? Color.FromArgb(220, 245, 237) : Color.FromArgb(229, 235, 255)))
                g.FillEllipse(b, outer);
            using (Brush b = new SolidBrush(hover ? ControlPaint.Light(blue, .08f) : blue))
                g.FillEllipse(b, body);
            float c = size / 2f;
            using (Pen pen = new Pen(Color.White, 4))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawArc(pen, c - 23, c - 53, 46, 46, -50, 280);
                g.DrawLine(pen, c, c - 58, c, c - 31);
            }

            using (Font caption = new Font("Segoe UI", 11, FontStyle.Bold))
            using (Font clock = new Font("Consolas", 12))
            using (Brush white = new SolidBrush(Color.White))
            using (StringFormat f = new StringFormat())
            {
                f.Alignment = StringAlignment.Center;
                f.LineAlignment = StringAlignment.Center;
                g.DrawString(Working ? "Подключение…" : Connected ? "Отключить" : "Подключить", caption, white, new RectangleF(17, c + 3, size - 34, 28), f);
                g.DrawString(Elapsed, clock, white, new RectangleF(17, c + 32, size - 34, 25), f);
            }

            if (Focused)
                using (Pen pen = new Pen(Color.FromArgb(160, Color.White), 1))
                    g.DrawEllipse(pen, 20, 20, size - 40, size - 40);
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
