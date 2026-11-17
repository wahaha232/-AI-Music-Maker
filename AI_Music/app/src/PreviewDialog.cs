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
        private readonly TrackBar seek = new TrackBar();
        private readonly Button btnPlay = new Button();
        private readonly Button btnVideo = new Button();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly string videoPath;
        private bool scrubbing;

        public PreviewDialog(string audioPath, string imagePath, string videoPath)
        {
            this.videoPath = videoPath;
            Text = "音樂與影片封面預覽";
            Font = new Font("Microsoft JhengHei UI", 11F);
            ClientSize = new Size(780, 680);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(18, 19, 24);
            ForeColor = Color.White;

            picture.SetBounds(20, 20, 740, 420);
            picture.BackColor = Color.FromArgb(10, 11, 14);
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

            lblTitle.SetBounds(20, 452, 740, 26);
            lblTitle.Text = Path.GetFileName(audioPath);
            lblTitle.AutoEllipsis = true;

            seek.SetBounds(20, 484, 740, 40);
            seek.Minimum = 0;
            seek.Maximum = 1000;
            seek.TickStyle = TickStyle.None;
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
                try
                {
                    reader.CurrentTime = TimeSpan.FromSeconds(
                        reader.TotalTime.TotalSeconds * seek.Value / 1000.0);
                }
                catch (Exception)
                {
                }
            };

            lblTime.SetBounds(20, 526, 740, 24);
            lblTime.Text = "00:00 / 00:00";

            btnPlay.SetBounds(20, 560, 120, 40);
            btnPlay.Text = "播放";
            btnPlay.FlatStyle = FlatStyle.Flat;
            btnPlay.BackColor = Color.FromArgb(108, 92, 231);
            btnPlay.ForeColor = Color.White;
            btnPlay.Click += OnPlayPause;

            btnVideo.SetBounds(152, 560, 150, 40);
            btnVideo.Text = "開啟 MP4";
            btnVideo.FlatStyle = FlatStyle.Flat;
            btnVideo.BackColor = Color.FromArgb(55, 58, 70);
            btnVideo.ForeColor = Color.White;
            btnVideo.Enabled = videoPath.Length > 0 && File.Exists(videoPath);
            btnVideo.Click += delegate
            {
                if (videoPath.Length > 0 && File.Exists(videoPath))
                    Process.Start(new ProcessStartInfo(videoPath) { UseShellExecute = true });
            };

            Button close = new Button();
            close.SetBounds(660, 560, 100, 40);
            close.Text = "關閉";
            close.FlatStyle = FlatStyle.Flat;
            close.BackColor = Color.FromArgb(55, 58, 70);
            close.ForeColor = Color.White;
            close.Click += delegate { Close(); };

            Controls.AddRange(new Control[] { picture, lblTitle, seek, lblTime, btnPlay, btnVideo, close });
            ThemeManager.ApplyDialog(this);
            ThemeManager.StylePrimary(btnPlay);

            try
            {
                reader = AudioTools.OpenReader(audioPath);
                output.Init(reader);
                timer.Interval = 200;
                timer.Tick += OnTimer;
                timer.Start();
                output.Play();
                btnPlay.Text = "暫停";
            }
            catch (Exception ex)
            {
                lblTitle.Text = "無法載入音訊: " + ex.Message;
                btnPlay.Enabled = false;
            }
            FormClosed += delegate { DisposeAudio(); };
        }

        private void OnPlayPause(object sender, EventArgs e)
        {
            if (reader == null) return;
            if (output.PlaybackState == PlaybackState.Playing)
            {
                output.Pause();
                btnPlay.Text = "播放";
            }
            else
            {
                output.Play();
                btnPlay.Text = "暫停";
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
