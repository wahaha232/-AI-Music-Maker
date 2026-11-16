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
                FormBack = Color.FromArgb(18, 18, 22);
                PanelBack = Color.FromArgb(26, 27, 35);
                InputBack = Color.FromArgb(13, 14, 18);
                Text = Color.FromArgb(243, 244, 246);
                SubText = Color.FromArgb(156, 163, 175);
                Border = Color.FromArgb(46, 49, 66);
                Accent = Color.FromArgb(79, 70, 229);
                AccentHover = Color.FromArgb(99, 102, 241);
                AccentDown = Color.FromArgb(67, 56, 202);
                LogBack = Color.FromArgb(13, 14, 18);
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
        }

        public static void StyleSuccess(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(5, 150, 105);
            button.BackColor = Color.FromArgb(5, 150, 105);
            button.ForeColor = Color.White;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(16, 185, 129);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(4, 120, 87);
        }

        public static void StyleDanger(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(220, 38, 38);
            button.BackColor = Color.FromArgb(220, 38, 38);
            button.ForeColor = Color.White;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(239, 68, 68);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(185, 28, 28);
        }

        public static void ApplyDialog(Form form)
        {
            form.BackColor = FormBack;
            form.ForeColor = Text;
            Apply(form);
        }
    }
}
