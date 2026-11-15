using System;
using System.Drawing;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    internal sealed class AdvancedDialog : Form
    {
        private readonly TextBox txtNegative = new TextBox();
        private readonly NumericUpDown numFadeIn = new NumericUpDown();
        private readonly NumericUpDown numFadeOut = new NumericUpDown();
        private readonly NumericUpDown numShift = new NumericUpDown();
        private readonly ComboBox comboBitrate = new ComboBox();
        private readonly ComboBox comboRate = new ComboBox();

        public string NegativePrompt { get; private set; }
        public decimal FadeIn { get; private set; }
        public decimal FadeOut { get; private set; }
        public decimal Shift { get; private set; }
        public string Mp3Bitrate { get; private set; }
        public int Mp3SampleRate { get; private set; }

        public AdvancedDialog(string negative, decimal fadeIn, decimal fadeOut,
            decimal shift, string bitrate, int sampleRate)
        {
            Text = "進階音樂設定";
            Font = new Font("Microsoft JhengHei UI", 11F);
            ClientSize = new Size(560, 470);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(24, 25, 31);
            ForeColor = Color.FromArgb(232, 234, 241);

            Label l1 = MakeLabel("Negative prompt（避免事項）", 12, 12);
            txtNegative.SetBounds(12, 40, 536, 150);
            txtNegative.Multiline = true;
            txtNegative.ScrollBars = ScrollBars.Vertical;
            txtNegative.BackColor = Color.FromArgb(34, 36, 44);
            txtNegative.ForeColor = Color.White;
            txtNegative.Text = negative ?? "";

            Label l2 = MakeLabel("淡入（秒）", 12, 208);
            numFadeIn.SetBounds(12, 234, 90, 28);
            numFadeIn.DecimalPlaces = 1;
            numFadeIn.Increment = 1m;
            numFadeIn.Minimum = 0m;
            numFadeIn.Maximum = 30m;
            numFadeIn.Value = Math.Max(0m, Math.Min(30m, fadeIn));

            Label l3 = MakeLabel("淡出（秒）", 124, 208);
            numFadeOut.SetBounds(124, 234, 90, 28);
            numFadeOut.DecimalPlaces = 1;
            numFadeOut.Increment = 1m;
            numFadeOut.Minimum = 0m;
            numFadeOut.Maximum = 30m;
            numFadeOut.Value = Math.Max(0m, Math.Min(30m, fadeOut));

            Label l4 = MakeLabel("Shift（Turbo）", 236, 208);
            numShift.SetBounds(236, 234, 90, 28);
            numShift.DecimalPlaces = 1;
            numShift.Increment = 0.5m;
            numShift.Minimum = 0.5m;
            numShift.Maximum = 10m;
            numShift.Value = Math.Max(0.5m, Math.Min(10m, shift));

            Label l5 = MakeLabel("MP3 bitrate", 12, 282);
            comboBitrate.SetBounds(12, 308, 150, 28);
            comboBitrate.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBitrate.Items.AddRange(new object[] { "128k", "192k", "256k", "320k" });
            comboBitrate.SelectedItem = bitrate;
            if (comboBitrate.SelectedIndex < 0) comboBitrate.SelectedIndex = 0;

            Label l6 = MakeLabel("MP3 sample rate", 184, 282);
            comboRate.SetBounds(184, 308, 150, 28);
            comboRate.DropDownStyle = ComboBoxStyle.DropDownList;
            comboRate.Items.AddRange(new object[] { "44100", "48000" });
            comboRate.SelectedItem = sampleRate.ToString();
            if (comboRate.SelectedIndex < 0) comboRate.SelectedIndex = 1;

            Button ok = new Button();
            ok.SetBounds(300, 400, 110, 38);
            ok.Text = "套用";
            ok.FlatStyle = FlatStyle.Flat;
            ok.BackColor = Color.FromArgb(108, 92, 231);
            ok.ForeColor = Color.White;
            ok.Click += delegate
            {
                NegativePrompt = txtNegative.Text.Trim();
                FadeIn = numFadeIn.Value;
                FadeOut = numFadeOut.Value;
                Shift = numShift.Value;
                Mp3Bitrate = Convert.ToString(comboBitrate.SelectedItem);
                Mp3SampleRate = int.Parse(Convert.ToString(comboRate.SelectedItem));
                DialogResult = DialogResult.OK;
                Close();
            };

            Button cancel = new Button();
            cancel.SetBounds(420, 400, 110, 38);
            cancel.Text = "取消";
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Color.FromArgb(48, 50, 60);
            cancel.ForeColor = Color.White;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[]
            {
                l1, txtNegative, l2, numFadeIn, l3, numFadeOut, l4, numShift,
                l5, comboBitrate, l6, comboRate, ok, cancel
            });
            ThemeManager.ApplyDialog(this);
            ThemeManager.StylePrimary(ok);
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Location = new Point(x, y);
            label.ForeColor = Color.FromArgb(200, 205, 218);
            return label;
        }
    }
}
