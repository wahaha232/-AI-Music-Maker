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
            Text = "製作整張專輯";
            Font = new Font("Microsoft JhengHei UI", 11F);
            ClientSize = new Size(760, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(24, 25, 31);
            ForeColor = Color.FromArgb(232, 234, 241);

            Label l0 = MakeLabel("專輯名稱", 12, 12);
            txtTitle.SetBounds(12, 38, 720, 28);
            txtTitle.BackColor = Color.FromArgb(34, 36, 44);
            txtTitle.ForeColor = Color.White;
            txtTitle.Text = "My Album";

            Label l1 = MakeLabel("藝人名稱", 12, 76);
            txtArtist.SetBounds(12, 102, 350, 28);
            txtArtist.BackColor = Color.FromArgb(34, 36, 44);
            txtArtist.ForeColor = Color.White;
            txtArtist.Text = "AI Music Maker";

            Label l2 = MakeLabel("每首長度（秒）", 380, 76);
            numDuration.SetBounds(380, 102, 100, 28);
            numDuration.Minimum = 10;
            numDuration.Maximum = 7200;
            numDuration.Value = Math.Max(10, Math.Min(7200, duration));

            Label l3 = MakeLabel("曲目（每行一首；格式：歌名 | 提示詞 | 秒數，後兩者可省略）", 12, 144);
            txtTracks.SetBounds(12, 172, 720, 300);
            txtTracks.Multiline = true;
            txtTracks.ScrollBars = ScrollBars.Vertical;
            txtTracks.BackColor = Color.FromArgb(16, 17, 22);
            txtTracks.ForeColor = Color.FromArgb(230, 233, 240);
            txtTracks.Font = new Font("Consolas", 10F);
            txtTracks.Text = "Track 01 | " + (defaultPrompt ?? "") + " | 180";

            Button ok = new Button();
            ok.SetBounds(490, 492, 110, 40);
            ok.Text = "開始製作";
            ok.FlatStyle = FlatStyle.Flat;
            ok.BackColor = Color.FromArgb(108, 92, 231);
            ok.ForeColor = Color.White;
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

            Button cancel = new Button();
            cancel.SetBounds(610, 492, 110, 40);
            cancel.Text = "取消";
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Color.FromArgb(48, 50, 60);
            cancel.ForeColor = Color.White;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[]
            {
                l0, txtTitle, l1, txtArtist, l2, numDuration, l3, txtTracks, ok, cancel
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
