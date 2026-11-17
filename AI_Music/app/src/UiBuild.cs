using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    /// <summary>Shared layout builders for the main window and the dialogs (all sizes DPI-scaled).</summary>
    internal static class UiBuild
    {
        public static int S(int value) { return Dpi.S(value); }

        public static int FieldHeight { get { return S(36); } }

        public static CardPanel NewCard(string title, string icon, string subtitle)
        {
            CardPanel card = new CardPanel();
            card.Dock = DockStyle.Fill;
            card.Title = title;
            card.IconName = icon;
            card.Subtitle = subtitle;
            return card;
        }

        public static InputHost Host(Control inner, Padding padding)
        {
            InputHost host = new InputHost(inner, padding);
            host.Dock = DockStyle.Fill;
            host.Margin = Padding.Empty;
            return host;
        }

        /// <summary>Single-line text box in a rounded frame, vertically centred.</summary>
        public static InputHost TextField(TextBox box)
        {
            box.Font = ThemeManager.UiFont;
            InputHost host = new InputHost(box, TextPadding(box));
            host.Height = FieldHeight;
            host.Dock = DockStyle.Fill;
            host.Margin = new Padding(0, S(3), 0, S(3));
            return host;
        }

        /// <summary>Padding that vertically centres a single-line text box in a field-height frame.</summary>
        public static Padding TextPadding(Control box)
        {
            int top = Math.Max(S(4), (FieldHeight - box.Font.Height) / 2);
            return new Padding(S(10), top, S(8), 0);
        }

        /// <summary>Multi-line text area in a rounded frame.</summary>
        public static InputHost TextArea(TextBoxBase box, Font font)
        {
            box.Font = font;
            box.Multiline = true;
            InputHost host = new InputHost(box, Dpi.S(12, 10, 6, 8));
            host.Dock = DockStyle.Fill;
            host.Margin = new Padding(0, S(3), 0, S(3));
            return host;
        }

        /// <summary>Number field with themed −/+ buttons (the native spin buttons are clipped away).</summary>
        public static Control Stepper(NumericUpDown number, int width)
        {
            number.Font = ThemeManager.UiFont;
            number.TextAlign = HorizontalAlignment.Center;
            number.BorderStyle = BorderStyle.None;
            int height = FieldHeight;

            // The native spin buttons cannot be themed, so the NumericUpDown is made wider than
            // its clip panel and the buttons fall outside the visible area.
            Panel clip = new Panel();
            clip.Margin = Padding.Empty;
            clip.Controls.Add(number);
            int spin = SystemInformation.VerticalScrollBarWidth + S(4);
            EventHandler fit = delegate
            {
                number.Location = new Point(0, Math.Max(0, (clip.Height - number.Height) / 2));
                number.Width = clip.Width + spin;
            };
            clip.Resize += fit;

            InputHost host = new InputHost(clip, new Padding(S(2), S(2), S(2), S(2)));
            host.Size = new Size(width > 0 ? width : S(120), height);
            host.Margin = Padding.Empty;
            if (width <= 0) host.Dock = DockStyle.Fill;
            ModernButton minus = StepButton("minus", "減少", delegate { number.DownButton(); });
            ModernButton plus = StepButton("plus", "增加", delegate { number.UpButton(); });
            minus.Dock = DockStyle.Left;
            plus.Dock = DockStyle.Right;
            host.Controls.Add(minus);
            host.Controls.Add(plus);
            clip.BringToFront();
            number.GotFocus += delegate { host.Invalidate(); };
            number.LostFocus += delegate { host.Invalidate(); };
            number.EnabledChanged += delegate
            {
                minus.Enabled = number.Enabled;
                plus.Enabled = number.Enabled;
                host.Invalidate();
            };
            fit(null, EventArgs.Empty);
            return host;
        }

        private static ModernButton StepButton(string icon, string text, EventHandler click)
        {
            ModernButton button = new ModernButton();
            button.Kind = ButtonKind.Ghost;
            button.Text = text;
            button.IconName = icon;
            button.IconOnly = true;
            button.IconSize = S(14);
            button.Width = S(24);
            button.Radius = S(6);
            button.TabStop = false;
            button.Margin = Padding.Empty;
            button.Click += click;
            return button;
        }

        public static Control Combo(ComboBox combo)
        {
            combo.Dock = DockStyle.Fill;
            combo.Margin = Padding.Empty;
            Panel wrap = new Panel();
            wrap.Height = FieldHeight;
            wrap.Dock = DockStyle.Fill;
            wrap.Margin = new Padding(0, S(3), 0, S(3));
            wrap.Padding = new Padding(0, Math.Max(0, (FieldHeight - combo.Height) / 2), 0, 0);
            wrap.Controls.Add(combo);
            return wrap;
        }

        public static Control ComboFixed(ComboBox combo, int width)
        {
            combo.Width = width;
            combo.Margin = new Padding(0, Math.Max(0, (FieldHeight - combo.Height) / 2), 0, 0);
            return combo;
        }

        public static Control Row(params Control[] items)
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.WrapContents = false;
            row.AutoSize = true;
            row.Margin = new Padding(0, S(3), 0, S(3));
            row.Padding = Padding.Empty;
            foreach (Control item in items)
            {
                if (!(item is Label) && item.Margin == Padding.Empty) item.Margin = new Padding(0, 0, S(8), 0);
                if (item is ToggleSwitch) item.Margin = new Padding(S(4), (FieldHeight - item.Height) / 2, 0, 0);
                row.Controls.Add(item);
            }
            return row;
        }

        /// <summary>A row where the first control stretches and the second keeps its size.</summary>
        public static Control Fill(Control stretch, Control fixedRight)
        {
            TableLayoutPanel row = new TableLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.Height = S(40);
            row.Margin = new Padding(0, S(2), 0, S(2));
            row.ColumnCount = 2;
            row.RowCount = 1;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, fixedRight.Width + S(6)));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            stretch.Margin = new Padding(0, S(2), S(6), S(2));
            stretch.Dock = DockStyle.Fill;
            fixedRight.Margin = Padding.Empty;
            fixedRight.Anchor = AnchorStyles.Right;
            row.Controls.Add(stretch, 0, 0);
            row.Controls.Add(fixedRight, 1, 0);
            return row;
        }

        public static Label Unit(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Tag = "muted";
            label.Font = ThemeManager.SmallFont;
            label.Margin = new Padding(0, (FieldHeight - ThemeManager.SmallFont.Height) / 2, S(10), 0);
            return label;
        }

        public static Label Caption(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Font = ThemeManager.UiFont;
            label.Margin = new Padding(0, S(3) + (FieldHeight - ThemeManager.UiFont.Height) / 2, S(10), 0);
            return label;
        }

        public static Label Note(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Tag = "muted";
            label.Font = ThemeManager.SmallFont;
            label.Margin = new Padding(0, S(2), 0, S(2));
            return label;
        }

        public static Label SectionLabel(string text, bool gapAbove)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = ThemeManager.SectionFont;
            label.Tag = "accent";
            label.AutoSize = true;
            label.Margin = new Padding(0, gapAbove ? S(14) : S(2), 0, S(2));
            return label;
        }

        public static void AddSection(TableLayoutPanel grid, string text, bool gapAbove)
        {
            Label label = SectionLabel(text, gapAbove);
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(label, 0, row);
            grid.SetColumnSpan(label, Math.Max(1, grid.ColumnCount));
        }

        public static void AddWide(TableLayoutPanel grid, string caption, Control control)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(Caption(caption), 0, row);
            if (control.Dock != DockStyle.Fill) control.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            grid.Controls.Add(control, 1, row);
            if (grid.ColumnCount > 2) grid.SetColumnSpan(control, grid.ColumnCount - 1);
        }

        public static void AddPair(TableLayoutPanel grid, string caption1, Control control1, string caption2, Control control2)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.Controls.Add(Caption(caption1), 0, row);
            control1.Margin = new Padding(0, S(3), S(12), S(3));
            grid.Controls.Add(control1, 1, row);
            grid.Controls.Add(Caption(caption2), 2, row);
            control2.Margin = new Padding(0, S(3), 0, S(3));
            grid.Controls.Add(control2, 3, row);
        }

        /// <summary>Adds a full-width control in a new row (row style given by caller).</summary>
        public static void AddFull(TableLayoutPanel grid, Control control, RowStyle style)
        {
            int row = grid.RowCount++;
            grid.RowStyles.Add(style);
            grid.Controls.Add(control, 0, row);
            grid.SetColumnSpan(control, Math.Max(1, grid.ColumnCount));
        }

        public static void AddHeaderTools(CardPanel card, params Control[] tools)
        {
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.AutoSize = true;
            bar.WrapContents = false;
            bar.Margin = Padding.Empty;
            bar.Padding = Padding.Empty;
            int rowHeight = S(32);
            foreach (Control tool in tools)
            {
                int top = Math.Max(0, (rowHeight - tool.Height) / 2);
                tool.Margin = new Padding(S(8), top, 0, 0);
                bar.Controls.Add(tool);
            }
            Size preferred = bar.GetPreferredSize(Size.Empty);
            bar.AutoSize = false;
            bar.Size = new Size(preferred.Width, rowHeight);
            card.Controls.Add(bar);
            bar.BringToFront();
            EventHandler place = delegate
            {
                bar.Location = new Point(Math.Max(0, card.Width - bar.Width - S(12)), S(5));
                card.HeaderReserve = bar.Width + S(12);
                card.Invalidate();
            };
            card.Resize += place;
            bar.SizeChanged += place;
            place(null, EventArgs.Empty);
        }

        public static ModernButton Button(ModernButton button, string text, string icon, ButtonKind kind)
        {
            button.Text = text;
            button.IconName = icon;
            button.Kind = kind;
            button.Margin = Padding.Empty;
            button.Size = button.GetPreferredSize(Size.Empty);
            return button;
        }

        // ------------------------------------------------------------------ dialogs

        internal sealed class DialogParts
        {
            public TableLayoutPanel Body;
            public FlowLayoutPanel Footer;
        }

        /// <summary>
        /// Standard dialog frame: header (icon tile, title, subtitle), a body grid and a
        /// right-aligned footer.  Buttons added to Footer appear right-to-left.
        /// </summary>
        public static DialogParts Dialog(Form form, string title, string subtitle, string icon,
            int width, int height, bool resizable)
        {
            form.AutoScaleMode = AutoScaleMode.None;
            form.Font = ThemeManager.UiFont;
            form.Text = title;
            form.ClientSize = Dpi.S(width, height);
            form.StartPosition = FormStartPosition.CenterParent;
            form.FormBorderStyle = resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
            form.MaximizeBox = resizable;
            form.MinimizeBox = false;
            form.ShowInTaskbar = false;
            if (resizable) form.MinimumSize = form.Size;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Margin = Padding.Empty;
            root.Padding = Dpi.S(20, 16, 20, 16);
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            TableLayoutPanel header = new TableLayoutPanel();
            header.AutoSize = true;
            header.Dock = DockStyle.Fill;
            header.Margin = new Padding(0, 0, 0, S(12));
            header.ColumnCount = 2;
            header.RowCount = 2;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(52)));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            IconTile tile = new IconTile(icon);
            tile.Margin = new Padding(0, S(2), 0, 0);
            header.Controls.Add(tile, 0, 0);
            header.SetRowSpan(tile, 2);
            Label titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Font = Dpi.Font(13F, FontStyle.Bold);
            titleLabel.Tag = "title";
            titleLabel.AutoSize = true;
            titleLabel.Margin = Padding.Empty;
            header.Controls.Add(titleLabel, 1, 0);
            Label sub = Note(subtitle);
            sub.Margin = new Padding(S(1), S(2), 0, 0);
            header.Controls.Add(sub, 1, 1);
            root.Controls.Add(header, 0, 0);

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.Margin = Padding.Empty;
            body.ColumnCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.Controls.Add(body, 0, 1);

            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.FlowDirection = FlowDirection.RightToLeft;
            footer.WrapContents = false;
            footer.AutoSize = true;
            footer.Dock = DockStyle.Fill;
            footer.Margin = new Padding(0, S(14), 0, 0);
            root.Controls.Add(footer, 0, 2);

            form.Controls.Add(root);
            form.HandleCreated += delegate { DarkTitleBar(form); };

            DialogParts parts = new DialogParts();
            parts.Body = body;
            parts.Footer = footer;
            return parts;
        }

        public static void AddFooterButton(FlowLayoutPanel footer, ModernButton button)
        {
            button.Height = S(38);
            button.Size = new Size(Math.Max(S(104), button.GetPreferredSize(Size.Empty).Width), S(38));
            button.Margin = new Padding(S(10), 0, 0, 0);
            footer.Controls.Add(button);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void DarkTitleBar(Form form)
        {
            if (!form.IsHandleCreated) return;
            try
            {
                int enabled = ThemeManager.Dark ? 1 : 0;
                DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int));
                DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>Small rounded tile with a Lucide icon (dialog headers).</summary>
    internal class IconTile : Control
    {
        private readonly string icon;

        public IconTile(string icon)
        {
            this.icon = icon;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = Dpi.S(40, 40);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color surface = Shapes.SurfaceOf(this, ThemeManager.FormBack);
            g.Clear(surface);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            RectangleF rect = new RectangleF(0, 0, Width - 1, Height - 1);
            using (System.Drawing.Drawing2D.GraphicsPath path = Shapes.RoundedRect(rect, Dpi.S(10)))
            using (SolidBrush brush = new SolidBrush(ThemeManager.Blend(surface, ThemeManager.Accent, ThemeManager.Dark ? 0.20 : 0.14)))
                g.FillPath(brush, path);
            float size = Dpi.S(20F);
            IconRenderer.Draw(g, icon, new RectangleF((Width - size) / 2F, (Height - size) / 2F, size, size), ThemeManager.Accent);
        }
    }
}
