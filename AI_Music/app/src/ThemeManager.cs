using System;
using System.Drawing;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    /// <summary>
    /// Colour tokens and recursive styling.  Palettes are the official
    /// Catppuccin Mocha (dark) and Latte (light) colours (MIT licence).
    /// </summary>
    internal static class ThemeManager
    {
        public static bool Dark = true;

        public static Color FormBack;      // window background
        public static Color CardBack;      // card surface
        public static Color PanelBack;     // secondary buttons / chips
        public static Color InputBack;     // text boxes, lists
        public static Color LogBack;       // log console
        public static Color Text;
        public static Color SubText;
        public static Color Muted;
        public static Color Border;
        public static Color BorderStrong;
        public static Color Accent;
        public static Color AccentHover;
        public static Color AccentDown;
        public static Color Accent2;       // gradient partner of Accent
        public static Color OnAccent;      // text drawn on Accent
        public static Color Success;
        public static Color Danger;
        public static Color Warning;
        public static Color Hover;         // ghost button hover
        public static Color Selection;

        // Fonts are pixel-sized through Dpi.Font so they scale together with the layout.
        public static readonly Font UiFont = Dpi.Font(10F, FontStyle.Regular);
        public static readonly Font SmallFont = Dpi.Font(9F, FontStyle.Regular);
        public static readonly Font HeadingFont = Dpi.Font(10.5F, FontStyle.Bold);
        public static readonly Font TitleFont = Dpi.Font(15F, FontStyle.Bold);
        public static readonly Font ButtonFont = Dpi.Font(10F, FontStyle.Bold);
        public static readonly Font BigButtonFont = Dpi.Font(12F, FontStyle.Bold);
        public static readonly Font SectionFont = Dpi.Font(8.5F, FontStyle.Bold);
        public static readonly Font EditorFont = Dpi.Font(11F, FontStyle.Regular);
        public static readonly Font MonoFont = CreateMonoFont();

        static ThemeManager() { SetTheme(true); }

        private static Font CreateMonoFont()
        {
            foreach (string name in new string[] { "Cascadia Mono", "Consolas" })
            {
                using (Font probe = new Font(name, 9.5F))
                {
                    if (string.Equals(probe.Name, name, StringComparison.OrdinalIgnoreCase))
                        return Dpi.Font(name, 9.5F, FontStyle.Regular);
                }
            }
            return Dpi.Font(FontFamily.GenericMonospace.Name, 9.5F, FontStyle.Regular);
        }

        private static Color Hex(int rgb)
        {
            return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }

        public static void SetTheme(bool dark)
        {
            Dark = dark;
            if (dark)
            {
                // Catppuccin Mocha
                FormBack = Hex(0x11111B);      // crust
                CardBack = Hex(0x1E1E2E);      // base
                PanelBack = Hex(0x313244);     // surface0
                InputBack = Hex(0x181825);     // mantle
                LogBack = Hex(0x11111B);       // crust
                Text = Hex(0xCDD6F4);
                SubText = Hex(0xA6ADC8);       // subtext0
                Muted = Hex(0x7F849C);         // overlay1
                Border = Hex(0x313244);        // surface0
                BorderStrong = Hex(0x45475A);  // surface1
                Accent = Hex(0xCBA6F7);        // mauve
                AccentHover = Hex(0xD9BFFA);
                AccentDown = Hex(0xB48EE8);
                Accent2 = Hex(0x89B4FA);       // blue
                OnAccent = Hex(0x11111B);
                Success = Hex(0xA6E3A1);       // green
                Danger = Hex(0xF38BA8);        // red
                Warning = Hex(0xFAB387);       // peach
                Hover = Hex(0x313244);
                Selection = Color.FromArgb(70, 0xCB, 0xA6, 0xF7);
            }
            else
            {
                // Catppuccin Latte
                FormBack = Hex(0xDCE0E8);      // crust
                CardBack = Hex(0xEFF1F5);      // base
                PanelBack = Hex(0xE6E9EF);     // mantle
                InputBack = Hex(0xFFFFFF);
                LogBack = Hex(0xE6E9EF);
                Text = Hex(0x4C4F69);
                SubText = Hex(0x5C5F77);       // subtext1
                Muted = Hex(0x8C8FA1);         // overlay1
                Border = Hex(0xCCD0DA);        // surface0
                BorderStrong = Hex(0xBCC0CC);  // surface1
                Accent = Hex(0x8839EF);        // mauve
                AccentHover = Hex(0x9A55F2);
                AccentDown = Hex(0x7428DA);
                Accent2 = Hex(0x1E66F5);       // blue
                OnAccent = Color.White;
                Success = Hex(0x40A02B);
                Danger = Hex(0xD20F39);
                Warning = Hex(0xFE640B);
                Hover = Hex(0xDCE0E8);
                Selection = Color.FromArgb(48, 0x88, 0x39, 0xEF);
            }
        }

        /// <summary>Text colour to use on top of a filled colour.</summary>
        public static Color OnFill(Color fill)
        {
            double luminance = (0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B) / 255.0;
            return luminance > 0.6 ? Hex(0x11111B) : Color.White;
        }

        public static Color Blend(Color a, Color b, double amount)
        {
            amount = Math.Max(0.0, Math.Min(1.0, amount));
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * amount),
                (int)Math.Round(a.G + (b.G - a.G) * amount),
                (int)Math.Round(a.B + (b.B - a.B) * amount));
        }

        public static void Apply(Form form)
        {
            form.BackColor = FormBack;
            form.ForeColor = Text;
            foreach (Control control in form.Controls) ApplyControl(control, FormBack);
            form.Invalidate(true);
        }

        public static void ApplyDialog(Form form)
        {
            Apply(form);
            form.Shown -= OnDialogShown;
            form.Shown += OnDialogShown;
        }

        private static void OnDialogShown(object sender, EventArgs e)
        {
            ApplyNativeScrollBars((Control)sender);
        }

        /// <summary>Dark/light native scroll bars on every scrollable child that already has a handle.</summary>
        public static void ApplyNativeScrollBars(Control root)
        {
            if (root is TextBoxBase || root is ListBox || root is ComboBox ||
                (root is ScrollableControl && ((ScrollableControl)root).AutoScroll))
                NativeTheme.ApplyScrollBars(root);
            foreach (Control child in root.Controls) ApplyNativeScrollBars(child);
        }

        /// <summary>Recursively styles a control placed on a surface of colour <paramref name="surface"/>.</summary>
        public static void ApplyControl(Control control, Color surface)
        {
            if (control is CardPanel)
            {
                control.BackColor = CardBack;
                control.ForeColor = Text;
                foreach (Control child in control.Controls) ApplyControl(child, CardBack);
                control.Invalidate();
                return;
            }
            if (control is InputHost)
            {
                Color back = control.Tag as string == "log" ? LogBack : InputBack;
                control.BackColor = back;
                foreach (Control child in control.Controls) ApplyControl(child, back);
                control.Invalidate();
                return;
            }
            if (control is ModernButton || control is ToggleSwitch || control is SlimProgressBar
                || control is SeekSlider || control is WaveformPanel)
            {
                control.BackColor = surface;
                control.ForeColor = Text;
                control.Invalidate();
                return;
            }
            if (control is TableLayoutPanel || control is FlowLayoutPanel || control is SplitContainer
                || control is SplitterPanel || control is Panel)
            {
                control.BackColor = surface;
                control.ForeColor = Text;
                foreach (Control child in control.Controls) ApplyControl(child, surface);
                return;
            }
            if (control is Label)
            {
                control.BackColor = Color.Transparent;
                string role = control.Tag as string;
                if (role == "title") control.ForeColor = Text;
                else if (role == "muted") control.ForeColor = Muted;
                else if (role == "accent") control.ForeColor = Accent;
                else control.ForeColor = SubText;
                return;
            }
            if (control is TextBoxBase)
            {
                control.BackColor = control.Tag as string == "log" ? LogBack : InputBack;
                control.ForeColor = Text;
                return;
            }
            if (control is ListBox)
            {
                control.BackColor = InputBack;
                control.ForeColor = Text;
                control.Invalidate();
                return;
            }
            if (control is ThemedComboBox)
            {
                control.BackColor = InputBack;
                control.ForeColor = Text;
                control.Invalidate();
                return;
            }
            if (control is ComboBox)
            {
                ComboBox combo = (ComboBox)control;
                combo.BackColor = InputBack;
                combo.ForeColor = Text;
                combo.FlatStyle = FlatStyle.Flat;
                if (combo.DrawMode == DrawMode.Normal)
                {
                    combo.DrawMode = DrawMode.OwnerDrawFixed;
                    combo.ItemHeight = Math.Max(combo.ItemHeight, combo.Font.Height + 8);
                    combo.DrawItem += DrawComboItem;
                }
                combo.Invalidate();
                return;
            }
            if (control is NumericUpDown)
            {
                control.BackColor = InputBack;
                control.ForeColor = Text;
                return;
            }
            if (control is CheckBox || control is RadioButton)
            {
                control.BackColor = Color.Transparent;
                control.ForeColor = Text;
                return;
            }
            if (control is TrackBar)
            {
                control.BackColor = surface;
                return;
            }
            if (control is PictureBox)
            {
                control.BackColor = InputBack;
                return;
            }
            if (control is Button)
            {
                StyleButton((Button)control);
                return;
            }
            foreach (Control child in control.Controls) ApplyControl(child, surface);
        }

        private static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            ComboBox combo = (ComboBox)sender;
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0 &&
                            (e.State & DrawItemState.ComboBoxEdit) == 0;
            Color back = selected ? Blend(InputBack, Accent, Dark ? 0.35 : 0.18) : InputBack;
            using (SolidBrush brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
            string text = combo.GetItemText(combo.Items[e.Index]);
            Color fore = combo.Enabled ? Text : Muted;
            Rectangle bounds = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, combo.Font, bounds, fore, back,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        public static void StyleButton(Button button)
        {
            ModernButton modern = button as ModernButton;
            if (modern != null)
            {
                modern.Invalidate();
                return;
            }
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = BorderStrong;
            button.BackColor = PanelBack;
            button.ForeColor = Text;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.MouseOverBackColor = Hover;
            button.FlatAppearance.MouseDownBackColor = BorderStrong;
        }

        public static void StylePrimary(Button button)
        {
            SetKind(button, ButtonKind.Primary);
        }

        public static void StyleSuccess(Button button)
        {
            SetKind(button, ButtonKind.Success);
        }

        public static void StyleDanger(Button button)
        {
            SetKind(button, ButtonKind.Danger);
        }

        private static void SetKind(Button button, ButtonKind kind)
        {
            ModernButton modern = button as ModernButton;
            if (modern != null)
            {
                modern.Kind = kind;
                modern.Invalidate();
                return;
            }
            Color fill = kind == ButtonKind.Danger ? Danger : (kind == ButtonKind.Success ? Success : Accent);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = fill;
            button.ForeColor = OnFill(fill);
            button.Cursor = Cursors.Hand;
        }
    }
}
