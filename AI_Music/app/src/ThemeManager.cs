using System;
using System.Drawing;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    internal static class ThemeManager
    {
        public static bool Dark = true;

        public static Color FormBack;
        public static Color PanelBack;
        public static Color InputBack;
        public static Color Text;
        public static Color SubText;
        public static Color Border;
        public static Color Accent;
        public static Color AccentHover;
        public static Color AccentDown;
        public static Color LogBack;

        static ThemeManager() { SetTheme(true); }

        public static void SetTheme(bool dark)
        {
            Dark = dark;
            if (dark)
            {
                FormBack = Color.FromArgb(30, 30, 46);
                PanelBack = Color.FromArgb(49, 50, 68);
                InputBack = Color.FromArgb(24, 24, 37);
                Text = Color.FromArgb(205, 214, 244);
                SubText = Color.FromArgb(166, 173, 200);
                Border = Color.FromArgb(69, 71, 90);
                Accent = Color.FromArgb(203, 166, 247);
                AccentHover = Color.FromArgb(180, 190, 254);
                AccentDown = Color.FromArgb(136, 57, 239);
                LogBack = Color.FromArgb(24, 24, 37);
            }
            else
            {
                FormBack = Color.FromArgb(239, 241, 245);
                PanelBack = Color.FromArgb(255, 255, 255);
                InputBack = Color.FromArgb(255, 255, 255);
                Text = Color.FromArgb(76, 79, 105);
                SubText = Color.FromArgb(108, 111, 133);
                Border = Color.FromArgb(204, 208, 218);
                Accent = Color.FromArgb(136, 57, 239);
                AccentHover = Color.FromArgb(114, 135, 253);
                AccentDown = Color.FromArgb(108, 92, 231);
                LogBack = Color.FromArgb(255, 255, 255);
            }
        }

        public static void Apply(Form form)
        {
            form.BackColor = FormBack;
            form.ForeColor = Text;
            foreach (Control control in form.Controls) ApplyControl(control);
        }

        private static void ApplyControl(Control control)
        {
            if (control is FlowLayoutPanel || control is TableLayoutPanel || control is Panel)
            {
                control.BackColor = FormBack;
                foreach (Control child in control.Controls) ApplyControl(child);
                return;
            }
            if (control is Label)
            {
                control.BackColor = Color.Transparent;
                control.ForeColor = SubText;
                return;
            }
            if (control is RichTextBox || control is TextBox)
            {
                control.BackColor = InputBack;
                control.ForeColor = Text;
                return;
            }
            if (control is ListBox)
            {
                control.BackColor = InputBack;
                control.ForeColor = Text;
                return;
            }
            if (control is ComboBox)
            {
                control.BackColor = InputBack;
                control.ForeColor = Text;
                ((ComboBox)control).FlatStyle = FlatStyle.Flat;
                return;
            }
            if (control is NumericUpDown)
            {
                control.BackColor = InputBack;
                control.ForeColor = Text;
                return;
            }
            if (control is CheckBox)
            {
                control.BackColor = Color.Transparent;
                control.ForeColor = Text;
                return;
            }
            if (control is TrackBar)
            {
                control.BackColor = FormBack;
                control.ForeColor = SubText;
                return;
            }
            if (control is WaveformPanel)
            {
                ((WaveformPanel)control).ThemeDark = Dark;
                ((WaveformPanel)control).Invalidate();
                return;
            }
            if (control is Button)
            {
                StyleButton((Button)control);
                return;
            }
        }

        public static void StyleButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Border;
            button.BackColor = PanelBack;
            button.ForeColor = Text;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.MouseOverBackColor = Dark
                ? Color.FromArgb(42, 45, 56) : Color.FromArgb(235, 235, 240);
            button.FlatAppearance.MouseDownBackColor = Dark
                ? Color.FromArgb(36, 38, 48) : Color.FromArgb(225, 225, 230);
            RoundButton(button);
        }

        public static void StylePrimary(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Accent;
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.MouseOverBackColor = AccentHover;
            button.FlatAppearance.MouseDownBackColor = AccentDown;
            RoundButton(button);
        }

        private static void RoundButton(Button button)
        {
            if (button.Width <= 0 || button.Height <= 0) return;
            int radius = 8;
            Rectangle bounds = new Rectangle(0, 0, button.Width, button.Height);
            using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                int d = radius * 2;
                path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
                path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
                path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
                path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                button.Region = new Region(path);
            }
            if (button.Tag == null)
            {
                button.Tag = "rounded";
                button.SizeChanged += delegate { RoundButton(button); };
            }
        }

        public static void ApplyDialog(Form form)
        {
            form.BackColor = FormBack;
            form.ForeColor = Text;
            Apply(form);
        }
    }
}
