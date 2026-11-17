using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    internal sealed class MediaToolsDialog : Form
    {
        private sealed class FileItem
        {
            public readonly string Path;

            public FileItem(string path)
            {
                Path = path;
            }

            public override string ToString()
            {
                return System.IO.Path.GetFileName(Path);
            }
        }

        private readonly string outputDir;
        private readonly Action onChanged;
        private readonly ListBox lstFiles = new ListBox();
        private readonly NumericUpDown numFadeIn = new NumericUpDown();
        private readonly NumericUpDown numFadeOut = new NumericUpDown();
        private readonly Button btnFade = new Button();
        private readonly Button btnMerge = new Button();
        private readonly Button btnNormalize = new Button();
        private readonly Button btnPreview = new Button();
        private readonly Button btnRefresh = new Button();
        private readonly Button btnClose = new Button();
        private readonly TextBox txtLog = new TextBox();
        private readonly Label lblStatus = new Label();
        private bool busy;

        public MediaToolsDialog(string outputDir, Action onChanged)
        {
            this.outputDir = outputDir;
            this.onChanged = onChanged;
            Text = "媒體工具";
            Font = new Font("Microsoft JhengHei UI", 11F);
            ClientSize = new Size(980, 660);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(18, 19, 24);
            ForeColor = Color.White;

            Label title = new Label();
            title.SetBounds(12, 12, 700, 24);
            title.Text = "音訊工具：淡入淡出、合併、音量正規化、封面預覽";
            title.ForeColor = Color.FromArgb(200, 205, 218);

            lstFiles.SetBounds(12, 44, 560, 430);
            lstFiles.SelectionMode = SelectionMode.MultiExtended;
            lstFiles.IntegralHeight = false;
            lstFiles.BackColor = Color.FromArgb(26, 27, 35);
            lstFiles.ForeColor = Color.FromArgb(230, 233, 240);

            btnRefresh.SetBounds(12, 484, 120, 34);
            btnRefresh.Text = "重新整理";
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.BackColor = Color.FromArgb(55, 58, 70);
            btnRefresh.ForeColor = Color.White;
            btnRefresh.Click += delegate { RefreshFiles(); };

            Label lblIn = MakeLabel("淡入(秒)", 590, 44);
            numFadeIn.SetBounds(668, 40, 80, 28);
            numFadeIn.DecimalPlaces = 1;
            numFadeIn.Increment = 0.5m;
            numFadeIn.Minimum = 0m;
            numFadeIn.Maximum = 30m;
            numFadeIn.Value = 2m;

            Label lblOut = MakeLabel("淡出(秒)", 770, 44);
            numFadeOut.SetBounds(848, 40, 80, 28);
            numFadeOut.DecimalPlaces = 1;
            numFadeOut.Increment = 0.5m;
            numFadeOut.Minimum = 0m;
            numFadeOut.Maximum = 30m;
            numFadeOut.Value = 3m;

            SetupButton(btnFade, "套用淡入淡出", 590, 82, OnFade);
            SetupButton(btnNormalize, "音量正規化 (-14 LUFS)", 590, 130, OnNormalize);
            SetupButton(btnMerge, "合併選取（2 首以上）", 590, 178, OnMerge);
            SetupButton(btnPreview, "預覽選取（1 首）", 590, 226, OnPreview);

            Label hint = new Label();
            hint.SetBounds(590, 274, 370, 120);
            hint.Text = "淡入淡出與正規化會建立新檔，不覆蓋原檔。\n" +
                        "合併輸出為 merged_日期_時間.mp3。\n" +
                        "預覽會顯示封面並播放音樂；有 MP4 時可開啟影片。";
            hint.ForeColor = Color.FromArgb(150, 156, 170);

            txtLog.SetBounds(12, 530, 950, 72);
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Color.FromArgb(24, 24, 24);
            txtLog.ForeColor = Color.Gainsboro;
            txtLog.Font = new Font("Consolas", 9F);

            lblStatus.SetBounds(12, 614, 800, 24);
            lblStatus.Text = "就緒";

            btnClose.SetBounds(850, 610, 110, 36);
            btnClose.Text = "關閉";
            btnClose.FlatStyle = FlatStyle.Flat;
            btnClose.BackColor = Color.FromArgb(55, 58, 70);
            btnClose.ForeColor = Color.White;
            btnClose.Click += delegate { Close(); };

            Controls.AddRange(new Control[]
            {
                title, lstFiles, btnRefresh, lblIn, numFadeIn, lblOut, numFadeOut,
                btnFade, btnNormalize, btnMerge, btnPreview, hint, txtLog, lblStatus, btnClose
            });
            ThemeManager.ApplyDialog(this);
            ThemeManager.StylePrimary(btnPreview);
            Load += delegate { RefreshFiles(); };
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

        private static void SetupButton(Button button, string text, int x, int y,
            EventHandler handler)
        {
            button.SetBounds(x, y, 370, 38);
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Color.FromArgb(55, 58, 70);
            button.ForeColor = Color.White;
            button.Click += handler;
        }

        private void RefreshFiles()
        {
            string selected = "";
            if (lstFiles.SelectedItem is FileItem)
                selected = ((FileItem)lstFiles.SelectedItem).Path;
            lstFiles.BeginUpdate();
            lstFiles.Items.Clear();
            try
            {
                if (Directory.Exists(outputDir))
                {
                    List<FileInfo> files = new List<FileInfo>();
                    foreach (FileInfo file in new DirectoryInfo(outputDir).GetFiles())
                    {
                        string ext = file.Extension.ToLowerInvariant();
                        if (ext == ".mp3" || ext == ".wav" || ext == ".flac")
                            files.Add(file);
                    }
                    files.Sort(delegate(FileInfo a, FileInfo b)
                    {
                        return b.LastWriteTime.CompareTo(a.LastWriteTime);
                    });
                    foreach (FileInfo file in files)
                        lstFiles.Items.Add(new FileItem(file.FullName));
                }
            }
            finally
            {
                lstFiles.EndUpdate();
            }
            for (int i = 0; i < lstFiles.Items.Count; i++)
            {
                FileItem item = lstFiles.Items[i] as FileItem;
                if (item != null && item.Path == selected)
                {
                    lstFiles.SelectedIndex = i;
                    break;
                }
            }
            lblStatus.Text = lstFiles.Items.Count + " 個音檔";
        }

        private List<string> SelectedPaths()
        {
            List<string> result = new List<string>();
            List<int> indices = new List<int>();
            foreach (int index in lstFiles.SelectedIndices) indices.Add(index);
            indices.Sort();
            foreach (int index in indices)
            {
                FileItem item = lstFiles.Items[index] as FileItem;
                if (item != null) result.Add(item.Path);
            }
            return result;
        }

        private void OnFade(object sender, EventArgs e)
        {
            if (busy) return;
            List<string> files = SelectedPaths();
            if (files.Count == 0)
            {
                MessageBox.Show(this, "請先選取至少一個音檔。", "媒體工具",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            double fadeIn = (double)numFadeIn.Value;
            double fadeOut = (double)numFadeOut.Value;
            if (fadeIn <= 0 && fadeOut <= 0)
            {
                MessageBox.Show(this, "淡入與淡出不可同時為 0。", "媒體工具",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SetBusy(true);
            Log("=== 套用淡入淡出 ===");
            Thread worker = new Thread(delegate()
            {
                int failed = 0;
                foreach (string input in files)
                {
                    string output = Path.Combine(outputDir,
                        Path.GetFileNameWithoutExtension(input) + "_fade" + Path.GetExtension(input));
                    output = UniqueOutput(output);
                    Log("淡入淡出: " + Path.GetFileName(input));
                    int code = AudioEditRunner.Run(
                        Paths.BuildAudioFadeStartInfo(input, fadeIn, fadeOut, output), Log);
                    if (code != 0) failed++;
                }
                PostUi(delegate()
                {
                    SetBusy(false);
                    lblStatus.Text = failed == 0 ? "淡入淡出完成" : "淡入淡出完成，失敗 " + failed;
                    RefreshFiles();
                    if (onChanged != null) onChanged();
                });
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void OnNormalize(object sender, EventArgs e)
        {
            if (busy) return;
            List<string> files = SelectedPaths();
            if (files.Count == 0)
            {
                MessageBox.Show(this, "請先選取至少一個音檔。", "媒體工具",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SetBusy(true);
            Log("=== 音量正規化 -14 LUFS ===");
            Thread worker = new Thread(delegate()
            {
                int code = AudioEditRunner.Run(
                    Paths.BuildAudioNormalizeStartInfo(files, outputDir), Log);
                PostUi(delegate()
                {
                    SetBusy(false);
                    lblStatus.Text = code == 0 ? "音量正規化完成" : "音量正規化失敗";
                    RefreshFiles();
                    if (onChanged != null) onChanged();
                });
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void OnMerge(object sender, EventArgs e)
        {
            if (busy) return;
            List<string> files = SelectedPaths();
            if (files.Count < 2)
            {
                MessageBox.Show(this, "合併需要選取至少兩個音檔。", "媒體工具",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string output = Path.Combine(outputDir,
                "merged_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp3");
            SetBusy(true);
            Log("=== 合併 " + files.Count + " 個音檔 ===");
            Thread worker = new Thread(delegate()
            {
                int code = AudioEditRunner.Run(
                    Paths.BuildAudioMergeStartInfo(files, output), Log);
                PostUi(delegate()
                {
                    SetBusy(false);
                    lblStatus.Text = code == 0
                        ? "合併完成: " + Path.GetFileName(output)
                        : "合併失敗";
                    RefreshFiles();
                    if (onChanged != null) onChanged();
                });
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void OnPreview(object sender, EventArgs e)
        {
            if (busy) return;
            List<string> files = SelectedPaths();
            if (files.Count != 1)
            {
                MessageBox.Show(this, "預覽請選取一個音檔。", "媒體工具",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string audio = files[0];
            string cover = Path.ChangeExtension(audio, ".cover.png");
            if (!File.Exists(cover)) cover = Paths.CoverFile;
            string video = Path.ChangeExtension(audio, ".mp4");
            using (PreviewDialog dialog = new PreviewDialog(audio, cover, video))
                dialog.ShowDialog(this);
        }

        private void SetBusy(bool value)
        {
            busy = value;
            lstFiles.Enabled = !value;
            numFadeIn.Enabled = !value;
            numFadeOut.Enabled = !value;
            btnFade.Enabled = !value;
            btnMerge.Enabled = !value;
            btnNormalize.Enabled = !value;
            btnPreview.Enabled = !value;
            btnRefresh.Enabled = !value;
        }

        private static string UniqueOutput(string path)
        {
            if (!File.Exists(path)) return path;
            return Path.Combine(Path.GetDirectoryName(path),
                Path.GetFileNameWithoutExtension(path) + "_" +
                DateTime.Now.ToString("HHmmss") + Path.GetExtension(path));
        }

        private void Log(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(Log), text); }
                catch (InvalidOperationException) { }
                return;
            }
            txtLog.AppendText(text + Environment.NewLine);
        }

        private void PostUi(Action action)
        {
            if (IsDisposed) return;
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }
    }
}
