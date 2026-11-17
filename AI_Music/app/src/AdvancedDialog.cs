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
        private readonly ComboBox comboBitrate = new ThemedComboBox();
        private readonly ComboBox comboRate = new ThemedComboBox();

        public string NegativePrompt { get; private set; }
        public decimal FadeIn { get; private set; }
        public decimal FadeOut { get; private set; }
        public decimal Shift { get; private set; }
        public string Mp3Bitrate { get; private set; }
        public int Mp3SampleRate { get; private set; }

        public AdvancedDialog(string negative, decimal fadeIn, decimal fadeOut,
            decimal shift, string bitrate, int sampleRate)
        {
            UiBuild.DialogParts parts = UiBuild.Dialog(this, "進階音樂設定",
                "負向提示、淡入淡出、Turbo Shift 與 MP3 輸出參數", "settings-2", 580, 560, false);
            TableLayoutPanel body = parts.Body;
            body.ColumnCount = 4;
            body.ColumnStyles.Clear();
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            UiBuild.AddSection(body, "負向提示（避免事項）", false);
            txtNegative.ScrollBars = ScrollBars.Vertical;
            txtNegative.Text = negative ?? "";
            UiBuild.AddFull(body, UiBuild.TextArea(txtNegative, ThemeManager.UiFont), new RowStyle(SizeType.Percent, 100F));
            UiBuild.AddFull(body, UiBuild.Note("例如：vocals, harsh noise, distorted guitar（給 LM 參考，不保證完全避免）"),
                new RowStyle(SizeType.AutoSize));

            numFadeIn.DecimalPlaces = 1;
            numFadeIn.Increment = 0.5m;
            numFadeIn.Minimum = 0m;
            numFadeIn.Maximum = 30m;
            numFadeIn.Value = Math.Max(0m, Math.Min(30m, fadeIn));
            numFadeOut.DecimalPlaces = 1;
            numFadeOut.Increment = 0.5m;
            numFadeOut.Minimum = 0m;
            numFadeOut.Maximum = 30m;
            numFadeOut.Value = Math.Max(0m, Math.Min(30m, fadeOut));
            numShift.DecimalPlaces = 1;
            numShift.Increment = 0.5m;
            numShift.Minimum = 0.5m;
            numShift.Maximum = 10m;
            numShift.Value = Math.Max(0.5m, Math.Min(10m, shift));

            UiBuild.AddSection(body, "淡入淡出與 Shift", true);
            UiBuild.AddPair(body, "淡入（秒）", UiBuild.Stepper(numFadeIn, 0), "淡出（秒）", UiBuild.Stepper(numFadeOut, 0));
            Label spacer = new Label();
            spacer.AutoSize = true;
            UiBuild.AddPair(body, "Shift", UiBuild.Stepper(numShift, 0), "", spacer);

            comboBitrate.Items.AddRange(new object[] { "128k", "192k", "256k", "320k" });
            comboBitrate.SelectedItem = bitrate;
            if (comboBitrate.SelectedIndex < 0) comboBitrate.SelectedIndex = 0;
            comboRate.Items.AddRange(new object[] { "44100", "48000" });
            comboRate.SelectedItem = sampleRate.ToString();
            if (comboRate.SelectedIndex < 0) comboRate.SelectedIndex = 1;
            UiBuild.AddSection(body, "MP3 輸出", true);
            UiBuild.AddPair(body, "位元率", UiBuild.Combo(comboBitrate), "取樣率", UiBuild.Combo(comboRate));

            ModernButton ok = UiBuild.Button(new ModernButton(), "套用", "check", ButtonKind.Primary);
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
            ModernButton cancel = UiBuild.Button(new ModernButton(), "取消", "", ButtonKind.Secondary);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            UiBuild.AddFooterButton(parts.Footer, ok);
            UiBuild.AddFooterButton(parts.Footer, cancel);
            CancelButton = cancel;

            ThemeManager.ApplyDialog(this);
        }
    }
}
