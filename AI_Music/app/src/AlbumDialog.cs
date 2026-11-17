using System;
using System.Drawing;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    internal sealed class AlbumDialog : Form
    {
        private readonly TextBox txtTitle = new TextBox();
        private readonly TextBox txtArtist = new TextBox();
        private readonly TextBox txtTracks = new TextBox();
        private readonly NumericUpDown numDuration = new NumericUpDown();

        public string AlbumTitle { get; private set; }
        public string Artist { get; private set; }
        public string TrackText { get; private set; }
        public int TrackDuration { get; private set; }

        public AlbumDialog(string defaultPrompt, int duration)
        {
            UiBuild.DialogParts parts = UiBuild.Dialog(this, "製作整張專輯",
                "每行一首：歌名 | 提示詞 | 秒數（後兩者可省略，省略時沿用預設）", "disc-3", 780, 600, true);
            TableLayoutPanel body = parts.Body;
            body.ColumnCount = 4;
            body.ColumnStyles.Clear();
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));

            txtTitle.Text = "My Album";
            txtArtist.Text = "AI Music Maker";
            numDuration.Minimum = 10;
            numDuration.Maximum = 7200;
            numDuration.Value = Math.Max(10, Math.Min(7200, duration));

            UiBuild.AddSection(body, "專輯資訊", false);
            UiBuild.AddWide(body, "專輯名稱", UiBuild.TextField(txtTitle));
            UiBuild.AddPair(body, "藝人", UiBuild.TextField(txtArtist), "每首秒數", UiBuild.Stepper(numDuration, 0));

            UiBuild.AddSection(body, "曲目", true);
            txtTracks.ScrollBars = ScrollBars.Vertical;
            txtTracks.WordWrap = false;
            txtTracks.AcceptsReturn = true;
            txtTracks.Text = "Track 01 | " + (defaultPrompt ?? "").Replace("\r", " ").Replace("\n", " ") + " | 180";
            UiBuild.AddFull(body, UiBuild.TextArea(txtTracks, ThemeManager.MonoFont), new RowStyle(SizeType.Percent, 100F));
            UiBuild.AddFull(body, UiBuild.Note("範例：Midnight Rain | warm lo-fi piano, soft rain ambience | 180"),
                new RowStyle(SizeType.AutoSize));

            ModernButton ok = UiBuild.Button(new ModernButton(), "開始製作", "disc-3", ButtonKind.Primary);
            ok.Click += delegate
            {
                AlbumTitle = txtTitle.Text.Trim();
                Artist = txtArtist.Text.Trim();
                TrackText = txtTracks.Text.Trim();
                TrackDuration = (int)numDuration.Value;
                if (AlbumTitle.Length == 0 || TrackText.Length == 0)
                {
                    MessageBox.Show(this, "專輯名稱與曲目不可為空。", "缺少資料");
                    return;
                }
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
