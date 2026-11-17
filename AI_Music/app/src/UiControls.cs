using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    /// <summary>
    /// Explicit DPI scaling.  The main window uses AutoScaleMode.None and every size goes
    /// through S(), so layouts look the same at 100 %, 125 %, 150 % ...
    /// AIMUSIC_UI_SCALE (e.g. "1" or "1.5") overrides the factor for screenshot tests.
    /// </summary>
    internal static class Dpi
    {
        public static readonly float Factor = Detect();

        private static float Detect()
        {
            string forced = Environment.GetEnvironmentVariable("AIMUSIC_UI_SCALE");
            float value;
            if (!string.IsNullOrEmpty(forced) &&
                float.TryParse(forced, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                value >= 0.75F && value <= 4F)
                return value;
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1F, g.DpiX / 96F);
            }
            catch (Exception)
            {
                return 1F;
            }
        }

        public static int S(int value) { return (int)Math.Round(value * Factor); }
        public static float S(float value) { return value * Factor; }
        public static Size S(int width, int height) { return new Size(S(width), S(height)); }
        public static Padding S(int left, int top, int right, int bottom)
        {
            return new Padding(S(left), S(top), S(right), S(bottom));
        }

        /// <summary>Font sized in points at 96 DPI, scaled by Factor (pixel units, so it ignores system DPI).</summary>
        public static Font Font(float points, FontStyle style)
        {
            return Font("Microsoft JhengHei UI", points, style);
        }

        public static Font Font(string family, float points, FontStyle style)
        {
            return new Font(family, points * 96F / 72F * Factor, style, GraphicsUnit.Pixel);
        }
    }

    internal static class NativeTheme
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        /// <summary>Dark or light native scroll bars for a scrollable control.</summary>
        public static void ApplyScrollBars(Control control)
        {
            if (control == null || !control.IsHandleCreated) return;
            try { SetWindowTheme(control.Handle, ThemeManager.Dark ? "DarkMode_Explorer" : "Explorer", null); }
            catch (Exception) { }
        }
    }

    internal enum ButtonKind
    {
        Secondary,
        Primary,
        Ghost,
        Success,
        Danger,
        DangerOutline
    }

    internal static class Shapes
    {
        public static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float r = Math.Max(0F, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2F));
            if (r < 0.5F)
            {
                path.AddRectangle(rect);
                return path;
            }
            float d = r * 2F;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color SurfaceOf(Control control, Color fallback)
        {
            Control parent = control.Parent;
            while (parent != null && parent.BackColor.A < 255) parent = parent.Parent;
            return parent != null ? parent.BackColor : fallback;
        }

        public static void DrawChevronDown(Graphics g, RectangleF box, Color color)
        {
            float w = Math.Min(box.Width, box.Height) * 0.36F;
            float cx = box.X + box.Width / 2F, cy = box.Y + box.Height / 2F;
            using (Pen pen = new Pen(color, Dpi.S(1.6F)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new PointF[]
                {
                    new PointF(cx - w, cy - w / 2F), new PointF(cx, cy + w / 2F), new PointF(cx + w, cy - w / 2F)
                });
            }
        }
    }

    /// <summary>Rounded, theme-aware button with an optional Lucide icon.</summary>
    internal class ModernButton : Button
    {
        private ButtonKind kind = ButtonKind.Secondary;
        private string iconName = "";
        private bool hover;
        private bool pressed;

        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Font = ThemeManager.ButtonFont;
            Radius = Dpi.S(8);
            IconSize = Dpi.S(18);
            Height = Dpi.S(36);
        }

        [DefaultValue(ButtonKind.Secondary)]
        public ButtonKind Kind
        {
            get { return kind; }
            set { kind = value; Invalidate(); }
        }

        public string IconName
        {
            get { return iconName; }
            set { iconName = value ?? ""; Invalidate(); }
        }

        /// <summary>Draw only the icon; Text is still used for the tooltip / accessibility.</summary>
        public bool IconOnly { get; set; }
        public int Radius { get; set; }
        public int IconSize { get; set; }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        public override Size GetPreferredSize(Size proposedSize)
        {
            if (IconOnly) return new Size(Height, Height);
            int icon = IconRenderer.Has(iconName) ? IconSize + Dpi.S(7) : 0;
            Size text = TextRenderer.MeasureText(Text ?? "", Font, Size.Empty, TextFormatFlags.NoPadding);
            return new Size(Math.Max(text.Width + icon + Dpi.S(28), Dpi.S(56)), Height);
        }

        private void GetColors(Color surface, out Color fill, out Color border, out Color fore)
        {
            switch (kind)
            {
                case ButtonKind.Primary:
                    fill = pressed ? ThemeManager.AccentDown : (hover ? ThemeManager.AccentHover : ThemeManager.Accent);
                    border = fill;
                    fore = ThemeManager.OnAccent;
                    break;
                case ButtonKind.Success:
                case ButtonKind.Danger:
                    fill = kind == ButtonKind.Success ? ThemeManager.Success : ThemeManager.Danger;
                    if (hover) fill = ThemeManager.Blend(fill, Color.White, 0.15);
                    if (pressed) fill = ThemeManager.Blend(fill, Color.Black, 0.12);
                    border = fill;
                    fore = ThemeManager.OnFill(fill);
                    break;
                case ButtonKind.DangerOutline:
                    fill = hover ? ThemeManager.Blend(surface, ThemeManager.Danger, ThemeManager.Dark ? 0.16 : 0.10) : surface;
                    border = ThemeManager.Blend(surface, ThemeManager.Danger, 0.75);
                    fore = ThemeManager.Danger;
                    break;
                case ButtonKind.Ghost:
                    fill = pressed ? ThemeManager.BorderStrong : (hover ? ThemeManager.Hover : surface);
                    border = fill;
                    fore = hover ? ThemeManager.Text : ThemeManager.SubText;
                    break;
                default:
                    fill = pressed ? ThemeManager.BorderStrong : (hover ? ThemeManager.Hover : ThemeManager.PanelBack);
                    border = ThemeManager.Dark ? ThemeManager.BorderStrong : ThemeManager.Border;
                    fore = ThemeManager.Text;
                    break;
            }
            if (!Enabled)
            {
                bool flat = kind == ButtonKind.Ghost || kind == ButtonKind.DangerOutline;
                fill = flat ? surface : ThemeManager.Blend(surface, fill, 0.35);
                border = ThemeManager.Blend(surface, border, 0.45);
                fore = ThemeManager.Muted;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color surface = Shapes.SurfaceOf(this, ThemeManager.CardBack);
            g.Clear(surface);
            Color fill, border, fore;
            GetColors(surface, out fill, out border, out fore);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF rect = new RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F);
            using (GraphicsPath path = Shapes.RoundedRect(rect, Radius))
            {
                using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if (border != fill)
                    using (Pen pen = new Pen(border)) g.DrawPath(pen, path);
            }
            if (Focused && ShowFocusCues)
            {
                float inset = Dpi.S(2.5F);
                using (GraphicsPath ring = Shapes.RoundedRect(new RectangleF(inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1),
                    Math.Max(1, Radius - inset)))
                using (Pen pen = new Pen(Color.FromArgb(170, kind == ButtonKind.Primary ? ThemeManager.OnAccent : ThemeManager.Accent), Dpi.S(1.5F)))
                    g.DrawPath(pen, ring);
            }

            bool hasIcon = IconRenderer.Has(iconName);
            int iconSize = Math.Min(IconSize, Height - Dpi.S(10));
            if (IconOnly || string.IsNullOrEmpty(Text))
            {
                if (hasIcon)
                    IconRenderer.Draw(g, iconName, new RectangleF((Width - iconSize) / 2F, (Height - iconSize) / 2F,
                        iconSize, iconSize), fore);
                return;
            }
            Size textSize = TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            int gap = hasIcon ? Dpi.S(7) : 0;
            int total = textSize.Width + (hasIcon ? iconSize + gap : 0);
            int x = Math.Max(Dpi.S(8), (Width - total) / 2);
            if (hasIcon)
            {
                IconRenderer.Draw(g, iconName, new RectangleF(x, (Height - iconSize) / 2F, iconSize, iconSize), fore);
                x += iconSize + gap;
            }
            Rectangle textRect = new Rectangle(x, 0, Math.Max(0, Width - x - Dpi.S(6)), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Rounded card surface with an optional header (icon + title + subtitle).</summary>
    internal class CardPanel : Panel
    {
        private string title = "";
        private string subtitle = "";
        private string iconName = "";

        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Radius = Dpi.S(12);
            UpdatePadding();
        }

        public static int HeaderHeight { get { return Dpi.S(42); } }

        public int Radius { get; set; }

        /// <summary>Width kept free on the right of the header for tool controls.</summary>
        public int HeaderReserve { get; set; }

        public string Title
        {
            get { return title; }
            set { title = value ?? ""; UpdatePadding(); Invalidate(); }
        }

        public string Subtitle
        {
            get { return subtitle; }
            set { subtitle = value ?? ""; Invalidate(); }
        }

        public string IconName
        {
            get { return iconName; }
            set { iconName = value ?? ""; Invalidate(); }
        }

        private void UpdatePadding()
        {
            Padding = new Padding(Dpi.S(16), title.Length > 0 ? HeaderHeight + Dpi.S(4) : Dpi.S(14), Dpi.S(16), Dpi.S(14));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Shapes.SurfaceOf(this, ThemeManager.FormBack));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF rect = new RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F);
            using (GraphicsPath path = Shapes.RoundedRect(rect, Radius))
            {
                using (SolidBrush brush = new SolidBrush(ThemeManager.CardBack)) g.FillPath(brush, path);
                using (Pen pen = new Pen(ThemeManager.Border)) g.DrawPath(pen, path);
            }
            if (title.Length == 0) return;
            int x = Dpi.S(16);
            int top = Dpi.S(10);
            int tileSize = Dpi.S(26);
            if (IconRenderer.Has(iconName))
            {
                RectangleF tile = new RectangleF(x, top, tileSize, tileSize);
                using (GraphicsPath tilePath = Shapes.RoundedRect(tile, Dpi.S(7)))
                using (SolidBrush brush = new SolidBrush(ThemeManager.Blend(ThemeManager.CardBack, ThemeManager.Accent,
                    ThemeManager.Dark ? 0.18 : 0.12)))
                    g.FillPath(brush, tilePath);
                float inner = Dpi.S(16);
                IconRenderer.Draw(g, iconName, new RectangleF(x + (tileSize - inner) / 2F, top + (tileSize - inner) / 2F, inner, inner),
                    ThemeManager.Accent);
                x += tileSize + Dpi.S(10);
            }
            int available = Math.Max(0, Width - x - Dpi.S(16) - HeaderReserve);
            Rectangle titleRect = new Rectangle(x, top, available, tileSize);
            TextRenderer.DrawText(g, title, ThemeManager.HeadingFont, titleRect, ThemeManager.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (subtitle.Length > 0)
            {
                Size titleSize = TextRenderer.MeasureText(g, title, ThemeManager.HeadingFont, Size.Empty, TextFormatFlags.NoPadding);
                int sx = x + titleSize.Width + Dpi.S(10);
                int width = available - titleSize.Width - Dpi.S(10);
                if (width > Dpi.S(40))
                    TextRenderer.DrawText(g, subtitle, ThemeManager.SmallFont, new Rectangle(sx, top, width, tileSize),
                        ThemeManager.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>Rounded frame around a borderless input; highlights while the input has focus.</summary>
    internal class InputHost : Panel
    {
        private readonly Control inner;

        public InputHost(Control inner) : this(inner, Dpi.S(10, 7, 8, 5)) { }

        public InputHost(Control inner, Padding padding)
        {
            this.inner = inner;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Padding = padding;
            TextBoxBase box = inner as TextBoxBase;
            if (box != null) box.BorderStyle = BorderStyle.None;
            NumericUpDown number = inner as NumericUpDown;
            if (number != null) number.BorderStyle = BorderStyle.None;
            ListBox list = inner as ListBox;
            if (list != null) list.BorderStyle = BorderStyle.None;
            inner.Dock = DockStyle.Fill;
            inner.Margin = Padding.Empty;
            Controls.Add(inner);
            inner.GotFocus += delegate { Invalidate(); };
            inner.LostFocus += delegate { Invalidate(); };
            inner.EnabledChanged += delegate { Invalidate(); };
            Cursor = Cursors.IBeam;
        }

        public Control Inner { get { return inner; } }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            inner.Focus();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Shapes.SurfaceOf(this, ThemeManager.CardBack));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool focused = inner.ContainsFocus;
            RectangleF rect = new RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F);
            using (GraphicsPath path = Shapes.RoundedRect(rect, Dpi.S(8)))
            {
                using (SolidBrush brush = new SolidBrush(BackColor)) g.FillPath(brush, path);
                Color border = focused ? ThemeManager.Accent : (ThemeManager.Dark ? ThemeManager.BorderStrong : ThemeManager.Border);
                if (!inner.Enabled) border = ThemeManager.Border;
                using (Pen pen = new Pen(border, focused ? Dpi.S(1.5F) : 1F)) g.DrawPath(pen, path);
            }
        }
    }

    /// <summary>DropDownList combo box with themed item list, border and chevron.</summary>
    internal class ThemedComboBox : ComboBox
    {
        private const int WM_PAINT = 0x000F;

        public ThemedComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = Dpi.S(26);
            Font = ThemeManager.UiFont;
            DoubleBuffered = true;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            bool selected = (e.State & DrawItemState.Selected) != 0 && !edit;
            Color back = selected
                ? ThemeManager.Blend(ThemeManager.InputBack, ThemeManager.Accent, ThemeManager.Dark ? 0.32 : 0.16)
                : ThemeManager.InputBack;
            using (SolidBrush brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
            Rectangle bounds = new Rectangle(e.Bounds.X + Dpi.S(edit ? 6 : 8), e.Bounds.Y, e.Bounds.Width - Dpi.S(10), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, bounds,
                Enabled ? ThemeManager.Text : ThemeManager.Muted, back,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT && IsHandleCreated) PaintChrome();
        }

        private void PaintChrome()
        {
            using (Graphics g = Graphics.FromHwnd(Handle))
            {
                Rectangle r = ClientRectangle;
                Color surface = Shapes.SurfaceOf(this, ThemeManager.CardBack);
                int button = SystemInformation.VerticalScrollBarWidth + Dpi.S(2);
                using (SolidBrush brush = new SolidBrush(ThemeManager.InputBack))
                    g.FillRectangle(brush, r.Right - button - 1, r.Top + 1, button, r.Height - 2);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Shapes.DrawChevronDown(g, new RectangleF(r.Right - button - 2, r.Top, button, r.Height),
                    Enabled ? ThemeManager.SubText : ThemeManager.Muted);
                RectangleF frame = new RectangleF(0.5F, 0.5F, r.Width - 1.5F, r.Height - 1.5F);
                using (GraphicsPath path = Shapes.RoundedRect(frame, Dpi.S(7)))
                using (Region outside = new Region(new Rectangle(0, 0, r.Width, r.Height)))
                {
                    outside.Exclude(path);
                    using (SolidBrush brush = new SolidBrush(surface)) g.FillRegion(brush, outside);
                    bool active = Focused || DroppedDown;
                    Color border = active ? ThemeManager.Accent : (ThemeManager.Dark ? ThemeManager.BorderStrong : ThemeManager.Border);
                    using (Pen pen = new Pen(border, active ? Dpi.S(1.5F) : 1F)) g.DrawPath(pen, path);
                }
            }
        }
    }

    /// <summary>CheckBox drawn as a toggle switch; keeps the normal CheckBox API.</summary>
    internal class ToggleSwitch : CheckBox
    {
        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            AutoSize = true;
        }

        private static int TrackWidth { get { return Dpi.S(36); } }
        private static int TrackHeight { get { return Dpi.S(20); } }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text ?? "", Font, Size.Empty, TextFormatFlags.NoPadding);
            return new Size(TrackWidth + Dpi.S(10) + text.Width + Dpi.S(4), Math.Max(Dpi.S(28), text.Height + Dpi.S(6)));
        }

        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color surface = Shapes.SurfaceOf(this, ThemeManager.CardBack);
            g.Clear(surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float top = (Height - TrackHeight) / 2F;
            RectangleF track = new RectangleF(1, top, TrackWidth, TrackHeight);
            Color trackColor = Checked ? ThemeManager.Accent : ThemeManager.BorderStrong;
            if (!Enabled) trackColor = ThemeManager.Blend(surface, trackColor, 0.45);
            using (GraphicsPath path = Shapes.RoundedRect(track, TrackHeight / 2F))
            using (SolidBrush brush = new SolidBrush(trackColor))
                g.FillPath(brush, path);
            float pad = Dpi.S(3F);
            float knob = TrackHeight - pad * 2;
            float knobX = Checked ? track.Right - knob - pad : track.X + pad;
            Color knobColor = Checked ? ThemeManager.OnAccent : Color.White;
            using (SolidBrush brush = new SolidBrush(knobColor))
                g.FillEllipse(brush, knobX, top + pad, knob, knob);
            if (Focused && ShowFocusCues)
                using (GraphicsPath ring = Shapes.RoundedRect(new RectangleF(0, top - 1, TrackWidth + 2, TrackHeight + 2), TrackHeight / 2F + 1))
                using (Pen pen = new Pen(Color.FromArgb(150, ThemeManager.Accent), Dpi.S(1.5F)))
                    g.DrawPath(pen, ring);
            int textX = TrackWidth + Dpi.S(10);
            Rectangle textRect = new Rectangle(textX, 0, Math.Max(0, Width - textX), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ThemeManager.Text : ThemeManager.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Thin rounded progress bar; API mirrors ProgressBar (Minimum/Maximum/Value/Style).</summary>
    internal class SlimProgressBar : Control
    {
        private readonly Timer marquee = new Timer();
        private ProgressBarStyle style = ProgressBarStyle.Blocks;
        private int minimum;
        private int maximum = 100;
        private int value;
        private float phase;

        public SlimProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Dpi.S(8);
            marquee.Interval = 30;
            marquee.Tick += delegate { phase += 0.012F; if (phase > 1.2F) phase = -0.35F; Invalidate(); };
        }

        public ProgressBarStyle Style
        {
            get { return style; }
            set
            {
                if (style == value) return;
                style = value;
                if (style == ProgressBarStyle.Marquee) { phase = -0.35F; marquee.Start(); }
                else marquee.Stop();
                Invalidate();
            }
        }

        public int Minimum { get { return minimum; } set { minimum = value; Invalidate(); } }
        public int Maximum { get { return maximum; } set { maximum = value; Invalidate(); } }

        public int Value
        {
            get { return value; }
            set { this.value = Math.Max(minimum, Math.Min(maximum, value)); Invalidate(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) marquee.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Shapes.SurfaceOf(this, ThemeManager.CardBack));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF track = new RectangleF(0, 0, Width - 1, Height - 1);
            float radius = Height / 2F;
            using (GraphicsPath path = Shapes.RoundedRect(track, radius))
            using (SolidBrush brush = new SolidBrush(ThemeManager.Dark ? ThemeManager.Border : ThemeManager.BorderStrong))
                g.FillPath(brush, path);

            RectangleF fill;
            if (style == ProgressBarStyle.Marquee)
            {
                float w = track.Width * 0.3F;
                fill = RectangleF.Intersect(new RectangleF(track.X + phase * track.Width, 0, w, track.Height), track);
            }
            else
            {
                float ratio = maximum > minimum ? (value - minimum) / (float)(maximum - minimum) : 0F;
                fill = new RectangleF(0, 0, track.Width * ratio, track.Height);
            }
            if (fill.Width < 1F) return;
            if (fill.Width < Height) fill.Width = Math.Min(track.Width, Height);
            using (GraphicsPath path = Shapes.RoundedRect(fill, radius))
            using (LinearGradientBrush brush = new LinearGradientBrush(
                new RectangleF(-1, 0, Math.Max(2, Width + 2), Height), ThemeManager.Accent, ThemeManager.Accent2, 0F))
                g.FillPath(brush, path);
        }
    }

    /// <summary>Slim seek slider; API mirrors the parts of TrackBar the player uses.</summary>
    internal class SeekSlider : Control
    {
        private int minimum;
        private int maximum = 1000;
        private int value;
        private bool dragging;
        private bool hover;

        public event EventHandler Scroll;

        public SeekSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Height = Dpi.S(18);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public int Minimum { get { return minimum; } set { minimum = value; Invalidate(); } }
        public int Maximum { get { return maximum; } set { maximum = value; Invalidate(); } }
        public TickStyle TickStyle { get; set; }

        public int Value
        {
            get { return value; }
            set
            {
                int clamped = Math.Max(minimum, Math.Min(maximum, value));
                if (clamped == this.value) return;
                this.value = clamped;
                Invalidate();
            }
        }

        private void RaiseScroll()
        {
            EventHandler handler = Scroll;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private void SetFromX(int x)
        {
            float usable = Math.Max(1, Width - Height);
            float ratio = Math.Max(0F, Math.Min(1F, (x - Height / 2F) / usable));
            Value = minimum + (int)Math.Round(ratio * (maximum - minimum));
            RaiseScroll();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Focus();
            dragging = true;
            Capture = true;
            SetFromX(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) SetFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            Capture = false;
            base.OnMouseUp(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = Math.Max(1, (maximum - minimum) / 50);
            if (e.KeyCode == Keys.Left) { Value -= step; RaiseScroll(); }
            if (e.KeyCode == Keys.Right) { Value += step; RaiseScroll(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Shapes.SurfaceOf(this, ThemeManager.CardBack));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float trackHeight = Dpi.S(4F);
            float left = Height / 2F, right = Width - Height / 2F;
            float y = (Height - trackHeight) / 2F;
            float ratio = maximum > minimum ? (value - minimum) / (float)(maximum - minimum) : 0F;
            float x = left + (right - left) * ratio;
            using (GraphicsPath path = Shapes.RoundedRect(new RectangleF(left, y, Math.Max(1, right - left), trackHeight), trackHeight / 2))
            using (SolidBrush brush = new SolidBrush(ThemeManager.Dark ? ThemeManager.Border : ThemeManager.BorderStrong))
                g.FillPath(brush, path);
            Color active = Enabled ? ThemeManager.Accent : ThemeManager.Muted;
            if (x - left > 1)
                using (GraphicsPath path = Shapes.RoundedRect(new RectangleF(left, y, x - left, trackHeight), trackHeight / 2))
                using (SolidBrush brush = new SolidBrush(active))
                    g.FillPath(brush, path);
            float size = (hover || dragging || Focused) ? Height - Dpi.S(4) : Height - Dpi.S(8);
            using (SolidBrush brush = new SolidBrush(active))
                g.FillEllipse(brush, x - size / 2F, (Height - size) / 2F, size, size);
        }
    }

    /// <summary>Pill-shaped segmented switch (e.g. 提示詞 | 歌詞).</summary>
    internal class SegmentedControl : Control
    {
        private readonly string[] items;
        private readonly string[] icons;
        private int selected;
        private int hover = -1;

        public event EventHandler SelectedIndexChanged;

        public SegmentedControl(string[] items, string[] icons)
        {
            this.items = items;
            this.icons = icons;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            Font = ThemeManager.ButtonFont;
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = GetPreferredSize(Size.Empty);
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                int clamped = Math.Max(0, Math.Min(items.Length - 1, value));
                if (clamped == selected) return;
                selected = clamped;
                Invalidate();
                EventHandler handler = SelectedIndexChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private int SegmentWidth(int index)
        {
            int icon = icons != null && IconRenderer.Has(icons[index]) ? Dpi.S(16) + Dpi.S(6) : 0;
            return TextRenderer.MeasureText(items[index], Font, Size.Empty, TextFormatFlags.NoPadding).Width + icon + Dpi.S(26);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = Dpi.S(6);
            for (int i = 0; i < items.Length; i++) width += SegmentWidth(i);
            return new Size(width, Dpi.S(32));
        }

        private Rectangle SegmentBounds(int index)
        {
            int x = Dpi.S(3);
            for (int i = 0; i < index; i++) x += SegmentWidth(i);
            return new Rectangle(x, Dpi.S(3), SegmentWidth(index), Height - Dpi.S(6));
        }

        private int HitTest(Point point)
        {
            for (int i = 0; i < items.Length; i++)
                if (SegmentBounds(i).Contains(point)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = HitTest(e.Location);
            if (index != hover) { hover = index; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int index = HitTest(e.Location);
            if (index >= 0) { Focus(); SelectedIndex = index; }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) SelectedIndex = selected - 1;
            if (e.KeyCode == Keys.Right) SelectedIndex = selected + 1;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color surface = Shapes.SurfaceOf(this, ThemeManager.CardBack);
            g.Clear(surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF frame = new RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F);
            using (GraphicsPath path = Shapes.RoundedRect(frame, Height / 2F))
            {
                using (SolidBrush brush = new SolidBrush(ThemeManager.InputBack)) g.FillPath(brush, path);
                Color border = Focused && ShowFocusCues ? ThemeManager.Accent
                    : (ThemeManager.Dark ? ThemeManager.BorderStrong : ThemeManager.Border);
                using (Pen pen = new Pen(border)) g.DrawPath(pen, path);
            }
            for (int i = 0; i < items.Length; i++)
            {
                Rectangle r = SegmentBounds(i);
                bool active = i == selected;
                if (active || i == hover)
                {
                    Color fill = active ? ThemeManager.Accent : ThemeManager.Hover;
                    using (GraphicsPath path = Shapes.RoundedRect(r, r.Height / 2F))
                    using (SolidBrush brush = new SolidBrush(fill))
                        g.FillPath(brush, path);
                }
                Color fore = active ? ThemeManager.OnAccent : ThemeManager.SubText;
                bool hasIcon = icons != null && IconRenderer.Has(icons[i]);
                int iconSize = Dpi.S(16);
                Size text = TextRenderer.MeasureText(g, items[i], Font, Size.Empty, TextFormatFlags.NoPadding);
                int total = text.Width + (hasIcon ? iconSize + Dpi.S(6) : 0);
                int x = r.X + (r.Width - total) / 2;
                if (hasIcon)
                {
                    IconRenderer.Draw(g, icons[i], new RectangleF(x, r.Y + (r.Height - iconSize) / 2F, iconSize, iconSize), fore);
                    x += iconSize + Dpi.S(6);
                }
                TextRenderer.DrawText(g, items[i], Font, new Rectangle(x, r.Y, r.Right - x, r.Height), fore,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>App logo: gradient tile with the Lucide "audio-lines" glyph.</summary>
    internal class LogoTile : Control
    {
        public LogoTile()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = Dpi.S(40, 40);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Shapes.SurfaceOf(this, ThemeManager.FormBack));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Draw(g, new RectangleF(0, 0, Width - 1, Height - 1));
        }

        public static void Draw(Graphics g, RectangleF rect)
        {
            using (GraphicsPath path = Shapes.RoundedRect(rect, rect.Width * 0.26F))
            using (LinearGradientBrush brush = new LinearGradientBrush(rect, ThemeManager.Accent, ThemeManager.Accent2, 45F))
                g.FillPath(brush, path);
            float pad = rect.Width * 0.22F;
            IconRenderer.Draw(g, "audio-lines", new RectangleF(rect.X + pad, rect.Y + pad, rect.Width - pad * 2,
                rect.Height - pad * 2), ThemeManager.OnAccent, 2.2F);
        }
    }
}
