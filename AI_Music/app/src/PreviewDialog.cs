using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using NAudio.Wave;

namespace AIMusicGenerator
{
    internal sealed class PreviewDialog : Form
    {
        private readonly WaveStream reader;
        private readonly WaveOutEvent output = new WaveOutEvent();
        private readonly PictureBox picture = new PictureBox();
        private readonly Label lblTitle = new Label();
        private readonly Label lblTime = new Label();
        private readonly SeekSlider seek = new SeekSlider();
        private readonly ModernButton btnPlay = new ModernButton();
        private readonly ModernButton btnVideo = new ModernButton();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly string videoPath;
        private bool scrubbing;

        public PreviewDialog(string audioPath, string imagePath, string videoPath)
        {
            this.videoPath = videoPath;
            UiBuild.DialogParts parts = UiBuild.Dialog(this, "音樂與影片封面預覽",
                "確認封面與音訊；有同名 MP4 時可直接開啟影片", "image", 780, 720, false);
            TableLayoutPanel body = parts.Body;

            picture.Dock = DockStyle.Fill;
            picture.Margin = Padding.Empty;
            picture.SizeMode = PictureBoxSizeMode.Zoom;
            try
            {
                if (imagePath.Length > 0 && File.Exists(imagePath))
                {
                    using (Image source = Image.FromFile(imagePath))
                        picture.Image = new Bitmap(source);
                }
            }
            catch (Exception)
            {
            }
            InputHost frame = UiBuild.Host(picture, Dpi.S(6, 6, 6, 6));
            frame.Cursor = Cursors.Default;
            frame.Margin = new Padding(0, 0, 0, Dpi.S(12));
            UiBuild.AddFull(body, frame, new RowStyle(SizeType.Percent, 100F));

            CardPanel player = UiBuild.NewCard("", "", "");
            player.Dock = DockStyle.Fill;
            player.Margin = Padding.Empty;
            player.Padding = Dpi.S(14, 10, 18, 10);
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.Margin = Padding.Empty;
            grid.ColumnCount = 2;
            grid.RowCount = 2;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Dpi.S(68)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            UiBuild.Button(btnPlay, "播放", "play", ButtonKind.Primary);
            btnPlay.IconOnly = true;
            btnPlay.Size = Dpi.S(52, 52);
            btnPlay.Radius = Dpi.S(26);
            btnPlay.IconSize = Dpi.S(22);
            btnPlay.Anchor = AnchorStyles.Left;
            btnPlay.Click += OnPlayPause;
            grid.Controls.Add(btnPlay, 0, 0);
            grid.SetRowSpan(btnPlay, 2);

            TableLayoutPanel info = new TableLayoutPanel();
            info.Dock = DockStyle.Fill;
            info.Margin = Padding.Empty;
            info.ColumnCount = 2;
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            info.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            lblTitle.Text = Path.GetFileName(audioPath);
            lblTitle.AutoEllipsis = true;
            lblTitle.Dock = DockStyle.Fill;
            lblTitle.Tag = "title";
            lblTitle.Font = ThemeManager.HeadingFont;
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblTitle.Margin = Padding.Empty;
            lblTime.Text = "00:00 / 00:00";
            lblTime.Font = ThemeManager.MonoFont;
            lblTime.AutoSize = true;
            lblTime.Anchor = AnchorStyles.Right;
            lblTime.Margin = Padding.Empty;
            info.Controls.Add(lblTitle, 0, 0);
            info.Controls.Add(lblTime, 1, 0);
            grid.Controls.Add(info, 1, 0);

            seek.Minimum = 0;
            seek.Maximum = 1000;
            seek.Dock = DockStyle.Fill;
            seek.Margin = new Padding(0, Dpi.S(4), 0, Dpi.S(4));
            seek.Scroll += delegate
            {
                if (reader == null || scrubbing) return;
                try { reader.CurrentTime = TimeSpan.FromSeconds(reader.TotalTime.TotalSeconds * seek.Value / 1000.0); }
                catch (Exception) { }
            };
            seek.MouseDown += delegate { scrubbing = true; };
            seek.MouseUp += delegate
            {
                scrubbing = false;
                if (reader == null) return;
                try { reader.CurrentTime = TimeSpan.FromSeconds(reader.TotalTime.TotalSeconds * seek.Value / 1000.0); }
                catch (Exception) { }
            };
            grid.Controls.Add(seek, 1, 1);
            player.Controls.Add(grid);
            UiBuild.AddFull(body, player, new RowStyle(SizeType.Absolute, Dpi.S(96)));

            UiBuild.Button(btnVideo, "開啟 MP4", "video", ButtonKind.Secondary);
            btnVideo.Enabled = videoPath.Length > 0 && File.Exists(videoPath);
            btnVideo.Click += delegate
            {
                if (videoPath.Length > 0 && File.Exists(videoPath))
                    Process.Start(new ProcessStartInfo(videoPath) { UseShellExecute = true });
            };
            ModernButton close = UiBuild.Button(new ModernButton(), "關閉", "", ButtonKind.Secondary);
            close.Click += delegate { Close(); };
            UiBuild.AddFooterButton(parts.Footer, close);
            UiBuild.AddFooterButton(parts.Footer, btnVideo);
            CancelButton = close;

            ThemeManager.ApplyDialog(this);

            try
            {
                reader = AudioTools.OpenReader(audioPath);
                output.Init(reader);
                timer.Interval = 200;
                timer.Tick += OnTimer;
                timer.Start();
                output.Play();
                SetPlayButton(true);
            }
            catch (Exception ex)
            {
                lblTitle.Text = "無法載入音訊: " + ex.Message;
                btnPlay.Enabled = false;
            }
            FormClosed += delegate { DisposeAudio(); };
        }

        private void SetPlayButton(bool playing)
        {
            btnPlay.Text = playing ? "暫停" : "播放";
            btnPlay.IconName = playing ? "pause" : "play";
        }

        private void OnPlayPause(object sender, EventArgs e)
        {
            if (reader == null) return;
            if (output.PlaybackState == PlaybackState.Playing)
            {
                output.Pause();
                SetPlayButton(false);
            }
            else
            {
                output.Play();
                SetPlayButton(true);
            }
        }

        private void OnTimer(object sender, EventArgs e)
        {
            if (reader == null || scrubbing) return;
            double total = reader.TotalTime.TotalSeconds;
            double current = reader.CurrentTime.TotalSeconds;
            if (total > 0)
                seek.Value = Math.Max(0, Math.Min(1000, (int)(current / total * 1000)));
            lblTime.Text = FormatTime(reader.CurrentTime) + " / " + FormatTime(reader.TotalTime);
        }

        private static string FormatTime(TimeSpan value)
        {
            return ((int)value.TotalMinutes).ToString("00") + ":" + value.Seconds.ToString("00");
        }

        private void DisposeAudio()
        {
            timer.Stop();
            try { output.Stop(); } catch (Exception) { }
            try { output.Dispose(); } catch (Exception) { }
            try { if (reader != null) reader.Dispose(); } catch (Exception) { }
        }
    }
}
