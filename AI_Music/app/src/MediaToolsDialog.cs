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
        private readonly ModernButton btnFade = new ModernButton();
        private readonly ModernButton btnMerge = new ModernButton();
        private readonly ModernButton btnNormalize = new ModernButton();
        private readonly ModernButton btnPreview = new ModernButton();
        private readonly ModernButton btnRefresh = new ModernButton();
        private readonly ModernButton btnSort = new ModernButton();
        private readonly ModernButton btnClose = new ModernButton();
        private readonly TextBox txtLog = new TextBox();
        private readonly Label lblStatus = new Label();
        private bool busy;
        private bool sortAscending;

        public MediaToolsDialog(string outputDir, Action onChanged)
        {
            this.outputDir = outputDir;
            this.onChanged = onChanged;
            UiBuild.DialogParts parts = UiBuild.Dialog(this, "媒體工具",
                "淡入淡出、音量正規化、合併與預覽；處理結果另存新檔，不覆蓋原檔", "wrench", 1000, 700, true);
            TableLayoutPanel body = parts.Body;
            body.ColumnCount = 2;
            body.ColumnStyles.Clear();
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            body.RowCount = 3;
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.Absolute, Dpi.S(118)));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // left: files
            CardPanel filesCard = UiBuild.NewCard("音檔", "list-music", "可按 Ctrl／Shift 多選");
            filesCard.Margin = new Padding(0, 0, Dpi.S(6), Dpi.S(12));
            lstFiles.SelectionMode = SelectionMode.MultiExtended;
            lstFiles.IntegralHeight = false;
            lstFiles.Font = ThemeManager.UiFont;
            lstFiles.DrawMode = DrawMode.OwnerDrawFixed;
            lstFiles.ItemHeight = Dpi.S(32);
            lstFiles.DrawItem += OnDrawFile;
            InputHost listHost = UiBuild.Host(lstFiles, Dpi.S(4, 4, 2, 4));
            listHost.Cursor = Cursors.Default;
            filesCard.Controls.Add(listHost);
            UiBuild.Button(btnRefresh, "重新整理", "refresh-cw", ButtonKind.Secondary);
            btnRefresh.Height = Dpi.S(30);
            btnRefresh.IconSize = Dpi.S(15);
            btnRefresh.Width = btnRefresh.GetPreferredSize(Size.Empty).Width;
            btnRefresh.Click += delegate { RefreshFiles(); };
            UiBuild.Button(btnSort, "新→舊", "list-music", ButtonKind.Secondary);
            btnSort.Height = Dpi.S(30);
            btnSort.IconSize = Dpi.S(15);
            btnSort.Width = btnSort.GetPreferredSize(Size.Empty).Width;
            btnSort.Click += delegate
            {
                sortAscending = !sortAscending;
                btnSort.Text = sortAscending ? "舊→新" : "新→舊";
                RefreshFiles();
            };
            UiBuild.AddHeaderTools(filesCard, btnSort, btnRefresh);
            body.Controls.Add(filesCard, 0, 0);

            // right: actions
            CardPanel actionsCard = UiBuild.NewCard("處理", "wrench", "");
            actionsCard.Margin = new Padding(Dpi.S(6), 0, 0, Dpi.S(12));
            TableLayoutPanel actions = new TableLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.Margin = Padding.Empty;
            actions.ColumnCount = 2;
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            numFadeIn.DecimalPlaces = 1;
            numFadeIn.Increment = 0.5m;
            numFadeIn.Minimum = 0m;
            numFadeIn.Maximum = 30m;
            numFadeIn.Value = 2m;
            numFadeOut.DecimalPlaces = 1;
            numFadeOut.Increment = 0.5m;
            numFadeOut.Minimum = 0m;
            numFadeOut.Maximum = 30m;
            numFadeOut.Value = 3m;

            UiBuild.AddSection(actions, "淡入淡出", false);
            UiBuild.AddWide(actions, "淡入（秒）", UiBuild.Stepper(numFadeIn, Dpi.S(150)));
            UiBuild.AddWide(actions, "淡出（秒）", UiBuild.Stepper(numFadeOut, Dpi.S(150)));
            AddAction(actions, btnFade, "套用淡入淡出", "audio-waveform", ButtonKind.Secondary, OnFade);
            UiBuild.AddSection(actions, "音量", true);
            AddAction(actions, btnNormalize, "音量正規化（-14 LUFS）", "volume-2", ButtonKind.Secondary, OnNormalize);
            UiBuild.AddSection(actions, "合併與預覽", true);
            AddAction(actions, btnMerge, "合併選取（2 首以上）", "git-merge", ButtonKind.Secondary, OnMerge);
            AddAction(actions, btnPreview, "預覽選取（1 首）", "play", ButtonKind.Primary, OnPreview);
            Label hint = UiBuild.Note("合併輸出為 merged_日期_時間.mp3；預覽會顯示封面並播放，有同名 MP4 時可開啟影片。");
            hint.MaximumSize = new Size(Dpi.S(360), 0);
            hint.Margin = new Padding(0, Dpi.S(10), 0, 0);
            UiBuild.AddFull(actions, hint, new RowStyle(SizeType.AutoSize));
            actionsCard.Controls.Add(actions);
            body.Controls.Add(actionsCard, 1, 0);

            // bottom: log + status
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Tag = "log";
            InputHost logHost = UiBuild.TextArea(txtLog, ThemeManager.MonoFont);
            logHost.Tag = "log";
            logHost.Margin = Padding.Empty;
            body.Controls.Add(logHost, 0, 1);
            body.SetColumnSpan(logHost, 2);
            lblStatus.Text = "就緒";
            lblStatus.AutoSize = true;
            lblStatus.Tag = "muted";
            lblStatus.Margin = new Padding(0, Dpi.S(10), 0, 0);
            body.Controls.Add(lblStatus, 0, 2);
            body.SetColumnSpan(lblStatus, 2);

            UiBuild.Button(btnClose, "關閉", "", ButtonKind.Secondary);
            btnClose.Click += delegate { Close(); };
            UiBuild.AddFooterButton(parts.Footer, btnClose);
            CancelButton = btnClose;

            ThemeManager.ApplyDialog(this);
            Load += delegate { RefreshFiles(); };
        }

        private static void AddAction(TableLayoutPanel grid, ModernButton button, string text, string icon,
            ButtonKind kind, EventHandler handler)
        {
            UiBuild.Button(button, text, icon, kind);
            button.Dock = DockStyle.Fill;
            button.Height = Dpi.S(38);
            button.Margin = new Padding(0, Dpi.S(4), 0, Dpi.S(4));
            button.Click += handler;
            UiBuild.AddFull(grid, button, new RowStyle(SizeType.Absolute, Dpi.S(46)));
        }

        private void OnDrawFile(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color back = ThemeManager.InputBack;
            using (SolidBrush brush = new SolidBrush(back)) g.FillRectangle(brush, e.Bounds);
            Rectangle row = new Rectangle(e.Bounds.X + Dpi.S(2), e.Bounds.Y + Dpi.S(1), e.Bounds.Width - Dpi.S(4), e.Bounds.Height - Dpi.S(2));
            if (selected)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (System.Drawing.Drawing2D.GraphicsPath path = Shapes.RoundedRect(row, Dpi.S(6)))
                using (SolidBrush brush = new SolidBrush(ThemeManager.Blend(back, ThemeManager.Accent, ThemeManager.Dark ? 0.24 : 0.14)))
                    g.FillPath(brush, path);
            }
            int icon = Dpi.S(16);
            IconRenderer.Draw(g, "music", new RectangleF(row.X + Dpi.S(8), row.Y + (row.Height - icon) / 2F, icon, icon),
                selected ? ThemeManager.Text : ThemeManager.Accent);
            Rectangle text = new Rectangle(row.X + Dpi.S(32), row.Y, row.Width - Dpi.S(36), row.Height);
            TextRenderer.DrawText(g, lstFiles.Items[e.Index].ToString(), lstFiles.Font, text, ThemeManager.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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
                    if (sortAscending) files.Reverse();
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
            btnSort.Enabled = !value;
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
