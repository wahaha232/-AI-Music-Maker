using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    /// <summary>
    /// Layout, theming and drawing of the main window.  Everything that reacts to the
    /// generation pipeline stays in MainForm.cs; this file only builds and paints the UI.
    /// All sizes go through S() (see Dpi) because the form uses AutoScaleMode.None.
    /// </summary>
    internal sealed partial class MainForm
    {
        private readonly ModernButton btnTheme = new ModernButton();
        private readonly ModernButton btnRandomSeed = new ModernButton();
        private readonly Label lblNowPlaying = new Label();
        private readonly SplitContainer mainSplit = new SplitContainer();
        private readonly ToolTip toolTip = new ToolTip();
        private double savedSplitRatio = 0.5;
        private readonly SegmentedControl editorTabs =
            new SegmentedControl(new string[] { "提示詞", "歌詞", "封面", "影片" },
                new string[] { "wand-sparkles", "music", "image", "video" });
        private CardPanel editorCard;
        private InputHost promptHost;
        private InputHost lyricsHost;
        private InputHost coverHost;
        private InputHost videoHost;
        private const string PromptHint = "描述曲風、樂器與氛圍（英文效果較佳）";
        private const string LyricsHint = "純音樂保留 [Instrumental] 即可";
        private const string CoverHint = "SD 封面提示詞；留空則依歌曲自動產生";
        private const string VideoHint = "影片關鍵字：ambient／kenburns／waveform／blur／cover";
        private bool splitApplied;

        private static int S(int value) { return Dpi.S(value); }

        private static int Gap { get { return S(12); } }
        private static int ActionHeight { get { return S(76); } }
        private static int PlayerHeight { get { return S(98); } }

        // ------------------------------------------------------------------ build

        private void BuildUi()
        {
            SuspendLayout();
            ConfigureInputs();

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Margin = Padding.Empty;
            root.Padding = Dpi.S(16, 8, 16, 16);
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(60)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(BuildHeader(), 0, 0);

            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.Margin = Padding.Empty;
            body.ColumnCount = 2;
            body.RowCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(404)));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.Controls.Add(BuildMainArea(), 0, 0);
            body.Controls.Add(BuildSidebar(), 1, 0);
            root.Controls.Add(body, 0, 1);

            Controls.Add(root);
            ResumeLayout(true);

            FormClosing += OnFormClosing;
            Shown += delegate
            {
                ApplySplitRatio();
                ThemeManager.ApplyNativeScrollBars(this);
            };
            Resize += delegate
            {
                if (WindowState != FormWindowState.Minimized && !splitApplied) ApplySplitRatio();
            };
        }

        private void ConfigureInputs()
        {
            txtCaption.WordWrap = true;
            txtCaption.DetectUrls = false;
            txtCaption.ScrollBars = RichTextBoxScrollBars.Vertical;
            txtCaption.Font = ThemeManager.EditorFont;
            txtCaption.HandleCreated += delegate
            {
                txtCaption.ScrollBars = RichTextBoxScrollBars.Vertical;
                txtCaption.WordWrap = true;
            };

            txtLyrics.WordWrap = true;
            txtLyrics.DetectUrls = false;
            txtLyrics.ScrollBars = RichTextBoxScrollBars.Vertical;
            txtLyrics.Font = ThemeManager.EditorFont;

            txtCover.WordWrap = true;
            txtCover.DetectUrls = false;
            txtCover.ScrollBars = RichTextBoxScrollBars.Vertical;
            txtCover.Font = ThemeManager.EditorFont;

            txtVideo.WordWrap = true;
            txtVideo.DetectUrls = false;
            txtVideo.ScrollBars = RichTextBoxScrollBars.Vertical;
            txtVideo.Font = ThemeManager.EditorFont;

            numBpm.Minimum = 30;
            numBpm.Maximum = 300;
            numBpm.Value = 78;
            chkAutoBpm.Text = "自動";
            chkAutoBpm.Checked = true;
            chkAutoBpm.CheckedChanged += OnAutoBpmChanged;

            numHours.Minimum = 0;
            numHours.Maximum = 2;
            numHours.Value = 0;
            numHours.ValueChanged += OnDurationPartsChanged;
            numMinutes.Minimum = 0;
            numMinutes.Maximum = 59;
            numMinutes.Value = 1;
            numMinutes.ValueChanged += OnDurationPartsChanged;
            numSeconds.Minimum = 0;
            numSeconds.Maximum = 59;
            numSeconds.Value = 0;
            numSeconds.ValueChanged += OnDurationPartsChanged;
            numDuration.Minimum = 10;
            numDuration.Maximum = 7200;
            numDuration.Value = 60;

            numSteps.Minimum = 1;
            numSteps.Maximum = 100;
            numSteps.Value = 8;

            numGuidance.DecimalPlaces = 2;
            numGuidance.Increment = 0.1m;
            numGuidance.Minimum = 0.1m;
            numGuidance.Maximum = 20m;
            numGuidance.Value = 1.0m;

            txtSeed.Font = ThemeManager.UiFont;
            txtSeed.HandleCreated += delegate
            {
                SendMessage(txtSeed.Handle, EM_SETCUEBANNER, (IntPtr)1, "空白 = 隨機");
            };
            txtOutDir.Font = ThemeManager.UiFont;

            chkInstrumental.Text = "純音樂（無人聲）";
            chkInstrumental.Checked = true;
            chkThinking.Text = "LM 思考（較慢，編排較完整）";
            chkThinking.Checked = true;
            chkThinking.CheckedChanged += OnThinkingChanged;
            chkKeepModel.Text = "取消時保留模型（背景跑完）";
            chkAutoVideo.Text = "完成後自動轉 MP4 影片";
            chkBatch.Text = "批次";

            foreach (StyleTemplate template in StyleTemplates.All) comboStyleTemplate.Items.Add(template);
            if (comboStyleTemplate.Items.Count > 0) comboStyleTemplate.SelectedIndex = 0;

            comboKey.Items.AddRange(new object[]
            {
                "自動", "C Major", "C Minor", "C# Major", "C# Minor",
                "D Major", "D Minor", "D# Major", "D# Minor",
                "E Major", "E Minor", "F Major", "F Minor",
                "F# Major", "F# Minor", "G Major", "G Minor",
                "G# Major", "G# Minor", "A Major", "A Minor",
                "A# Major", "A# Minor", "B Major", "B Minor"
            });
            comboKey.SelectedIndex = 0;
            comboKey.MaxDropDownItems = 14;

            comboSignature.Items.AddRange(new object[] { "自動", "4", "3", "6" });
            comboSignature.SelectedIndex = 0;

            comboLanguage.Items.AddRange(new object[]
            {
                "unknown", "en", "zh", "yue", "ja", "ko", "es", "fr",
                "de", "it", "pt", "ru", "ar", "hi", "th", "vi", "id", "tr"
            });
            comboLanguage.SelectedIndex = 0;
            comboLanguage.MaxDropDownItems = 12;

            comboFormat.Items.AddRange(new object[] { "wav", "flac", "mp3" });
            comboFormat.SelectedIndex = 0;

            comboOffload.Items.AddRange(new object[] { "auto", "save_vram" });
            comboOffload.SelectedIndex = 0;

            comboTheme.Items.AddRange(new object[] { "Mocha 深色", "Latte 淺色" });
            comboTheme.SelectedIndex = 0;

            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.WordWrap = true;
            txtLog.Font = ThemeManager.MonoFont;
            txtLog.Tag = "log";

            lstHistory.IntegralHeight = false;
            lstHistory.DrawMode = DrawMode.OwnerDrawFixed;
            lstHistory.ItemHeight = S(50);
            lstHistory.Font = ThemeManager.UiFont;
            lstHistory.DrawItem += OnHistoryDrawItem;
            lstHistory.SelectedIndexChanged += OnHistorySelected;
            lstHistory.DoubleClick += delegate(object sender, EventArgs e) { PlaySelected(); };

            seekBar.Minimum = 0;
            seekBar.Maximum = 1000;
            seekBar.Scroll += OnSeekScroll;
            seekBar.MouseDown += delegate(object sender, MouseEventArgs e) { scrubbing = true; };
            seekBar.MouseUp += delegate(object sender, MouseEventArgs e) { scrubbing = false; SeekTo(seekBar.Value / 1000.0); };
            waveform.SeekRequested += OnWaveformSeek;

            lblStatus.Text = "待命 · 首次載入模型約需 1–5 分鐘，載入後會保持常駐";
            lblStatus.AutoEllipsis = true;
            lblStatus.Tag = "title";
            lblStatus.Font = ThemeManager.HeadingFont;
            toolTip.SetToolTip(editorTabs, "切換音樂提示詞、歌詞、封面提示詞、影片提示詞");

            lblTime.Text = "00:00 / 00:00";
            lblTime.Font = ThemeManager.MonoFont;
            lblTime.TextAlign = ContentAlignment.MiddleRight;
            lblNowPlaying.Text = "尚未載入音檔";
            lblNowPlaying.AutoEllipsis = true;
            lblNowPlaying.Tag = "title";
            lblNowPlaying.Font = ThemeManager.HeadingFont;

            SetupButton(btnGenerate, "生成音樂", "sparkles", ButtonKind.Primary, OnGenerate);
            btnGenerate.Font = ThemeManager.BigButtonFont;
            btnGenerate.IconSize = S(22);
            btnGenerate.Radius = S(12);
            SetupButton(btnCancel, "取消", "x", ButtonKind.DangerOutline, OnCancel);
            btnCancel.Radius = S(12);
            btnCancel.Enabled = false;
            SetupButton(btnOpen, "開啟資料夾", "folder-open", ButtonKind.Secondary, OnOpenFolder);
            btnOpen.Enabled = false;
            SetupButton(btnPlay, "播放", "play", ButtonKind.Secondary, OnPlay);
            btnPlay.Height = S(30);
            btnPlay.IconSize = S(15);
            btnPlay.Width = btnPlay.GetPreferredSize(Size.Empty).Width;
            btnPlay.Enabled = false;
            SetupButton(btnBrowse, "選擇資料夾", "folder", ButtonKind.Secondary, OnBrowse);
            btnBrowse.IconOnly = true;
            btnBrowse.Size = new Size(S(38), S(38));
            SetupButton(btnCover, "封面圖片", "image", ButtonKind.Secondary, OnCover);
            SetupButton(btnApplyTemplate, "套用", "wand-sparkles", ButtonKind.Secondary, OnApplyStyleTemplate);
            btnApplyTemplate.Height = S(32);
            btnApplyTemplate.IconSize = S(15);
            btnApplyTemplate.Width = btnApplyTemplate.GetPreferredSize(Size.Empty).Width;
            SetupButton(btnExpandPrompt, "放大編輯", "maximize-2", ButtonKind.Ghost, OnExpandPrompt);
            btnExpandPrompt.IconOnly = true;
            btnExpandPrompt.Height = S(32);
            btnExpandPrompt.Width = S(32);

            SetupButton(btnPlayPause, "播放", "play", ButtonKind.Primary, OnPlayPauseClick);
            btnPlayPause.IconOnly = true;
            btnPlayPause.Size = new Size(S(52), S(52));
            btnPlayPause.Radius = S(26);
            btnPlayPause.IconSize = S(22);
            SetupButton(btnStop, "停止", "square", ButtonKind.Ghost, OnStopClick);
            btnStop.IconOnly = true;
            btnStop.Size = new Size(S(40), S(40));
            btnStop.Radius = S(20);
            btnStop.IconSize = S(16);

            SetupButton(btnLink, "參考連結", "link", ButtonKind.Ghost, OnLinkPrompt);
            SetupButton(btnAlbum, "專輯", "disc-3", ButtonKind.Ghost, OnAlbum);
            SetupButton(btnMediaTools, "媒體工具", "wrench", ButtonKind.Ghost, OnMediaTools);
            SetupButton(btnSd, "SD 封面", "image", ButtonKind.Ghost, OnSd);
            SetupButton(btnAdvanced, "進階設定", "settings-2", ButtonKind.Ghost, OnAdvanced);
            SetupButton(btnTelegram, "Telegram", "send", ButtonKind.Ghost, OnTelegram);
            SetupButton(btnTheme, "切換深色／淺色", "sun", ButtonKind.Ghost, OnToggleTheme);
            btnTheme.IconOnly = true;
            btnTheme.Size = new Size(S(38), S(38));

            SetupButton(btnRandomSeed, "清除種子（改用隨機）", "dices", ButtonKind.Ghost, delegate { txtSeed.Text = ""; });
            btnRandomSeed.IconOnly = true;
            btnRandomSeed.Size = new Size(S(36), S(36));

            toolTip.SetToolTip(btnLink, "貼上 YouTube／Spotify 連結，依 metadata 推薦提示詞");
            toolTip.SetToolTip(btnAlbum, "一次製作多首曲目的專輯");
            toolTip.SetToolTip(btnMediaTools, "淡入淡出、合併、音量正規化、預覽");
            toolTip.SetToolTip(btnSd, "Stable Diffusion 自動封面設定");
            toolTip.SetToolTip(btnAdvanced, "負向提示、淡入淡出、Shift、MP3 參數");
            toolTip.SetToolTip(btnTelegram, "啟動 Telegram 遠端控制");
            toolTip.SetToolTip(btnTheme, "切換深色／淺色主題");
            toolTip.SetToolTip(btnExpandPrompt, "開啟大型提示詞編輯視窗");
            toolTip.SetToolTip(comboOffload, "GPU 鎖僅涵蓋本程式、Telegram 與 SD 封面；外部模型或程式不受保護");
            toolTip.SetToolTip(btnBrowse, "選擇輸出資料夾");
            toolTip.SetToolTip(btnRandomSeed, "清除種子（每次隨機）");
            toolTip.SetToolTip(btnPlayPause, "播放／暫停");
            toolTip.SetToolTip(btnStop, "停止");
            toolTip.SetToolTip(btnPlay, "播放選取或最新的輸出");
            toolTip.SetToolTip(chkBatch, "開啟後，提示詞每一行各生成一首");
        }

        private static void SetupButton(ModernButton button, string text, string icon, ButtonKind kind, EventHandler handler)
        {
            button.Text = text;
            button.IconName = icon;
            button.Kind = kind;
            button.Margin = Padding.Empty;
            if (handler != null) button.Click += handler;
            button.Size = button.GetPreferredSize(Size.Empty);
        }

        private Control BuildHeader()
        {
            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.Margin = Dpi.S(0, 0, 0, 6);
            header.ColumnCount = 3;
            header.RowCount = 1;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(54)));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            LogoTile logo = new LogoTile();
            logo.Anchor = AnchorStyles.Left;
            logo.Margin = Padding.Empty;
            header.Controls.Add(logo, 0, 0);

            TableLayoutPanel titles = new TableLayoutPanel();
            titles.Anchor = AnchorStyles.Left;
            titles.AutoSize = true;
            titles.Margin = Padding.Empty;
            titles.ColumnCount = 1;
            titles.RowCount = 2;
            Label title = new Label();
            title.Text = "AI Music Maker";
            title.Font = ThemeManager.TitleFont;
            title.Tag = "title";
            title.AutoSize = true;
            title.Margin = Padding.Empty;
            Label subtitle = new Label();
            subtitle.Text = "ACE-Step 1.5 · 本機離線生成";
            subtitle.Font = ThemeManager.SmallFont;
            subtitle.Tag = "muted";
            subtitle.AutoSize = true;
            subtitle.Margin = Dpi.S(2, 0, 0, 0);
            titles.Controls.Add(title, 0, 0);
            titles.Controls.Add(subtitle, 0, 1);
            header.Controls.Add(titles, 1, 0);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.AutoSize = true;
            actions.WrapContents = false;
            actions.Anchor = AnchorStyles.Right;
            actions.Margin = Padding.Empty;
            foreach (ModernButton button in new ModernButton[] { btnLink, btnAlbum, btnMediaTools, btnSd, btnAdvanced, btnTelegram })
            {
                button.Margin = Dpi.S(2, 0, 0, 0);
                actions.Controls.Add(button);
            }
            Panel divider = new Panel();
            divider.Size = new Size(Math.Max(1, S(1)), S(22));
            divider.Margin = Dpi.S(10, 8, 8, 0);
            divider.Paint += delegate(object sender, PaintEventArgs e) { e.Graphics.Clear(ThemeManager.BorderStrong); };
            actions.Controls.Add(divider);
            btnTheme.Margin = Padding.Empty;
            actions.Controls.Add(btnTheme);
            header.Controls.Add(actions, 2, 0);
            return header;
        }

        private Control BuildMainArea()
        {
            mainSplit.Dock = DockStyle.Fill;
            mainSplit.Orientation = Orientation.Horizontal;
            mainSplit.Margin = new Padding(0, 0, Gap, 0);
            mainSplit.SplitterWidth = Gap;
            mainSplit.TabStop = false;

            // top: one editor card that switches between prompt and lyrics
            editorCard = UiBuild.NewCard("創作", "wand-sparkles", PromptHint);
            editorCard.Margin = Padding.Empty;
            Panel editors = new Panel();
            editors.Dock = DockStyle.Fill;
            editors.Margin = Padding.Empty;
            promptHost = UiBuild.Host(txtCaption, Dpi.S(12, 10, 6, 8));
            lyricsHost = UiBuild.Host(txtLyrics, Dpi.S(12, 10, 6, 8));
            coverHost = UiBuild.Host(txtCover, Dpi.S(12, 10, 6, 8));
            videoHost = UiBuild.Host(txtVideo, Dpi.S(12, 10, 6, 8));
            lyricsHost.Visible = false;
            coverHost.Visible = false;
            videoHost.Visible = false;
            editors.Controls.Add(videoHost);
            editors.Controls.Add(coverHost);
            editors.Controls.Add(lyricsHost);
            editors.Controls.Add(promptHost);
            editorCard.Controls.Add(editors);
            editorTabs.SelectedIndexChanged += OnEditorTabChanged;
            comboStyleTemplate.Width = S(168);
            UiBuild.AddHeaderTools(editorCard, editorTabs, chkBatch, comboStyleTemplate, btnApplyTemplate, btnExpandPrompt);
            mainSplit.Panel1.Controls.Add(editorCard);

            // bottom: action bar, player, history + log
            TableLayoutPanel lower = new TableLayoutPanel();
            lower.Dock = DockStyle.Fill;
            lower.Margin = Padding.Empty;
            lower.ColumnCount = 2;
            lower.RowCount = 3;
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            lower.RowStyles.Add(new RowStyle(SizeType.Absolute, ActionHeight + Gap));
            lower.RowStyles.Add(new RowStyle(SizeType.Absolute, PlayerHeight + Gap));
            lower.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Control action = BuildActionCard();
            lower.Controls.Add(action, 0, 0);
            lower.SetColumnSpan(action, 2);
            Control player = BuildPlayerCard();
            lower.Controls.Add(player, 0, 1);
            lower.SetColumnSpan(player, 2);

            CardPanel historyCard = UiBuild.NewCard("歷史輸出", "list-music", "雙擊播放");
            historyCard.Margin = new Padding(0, 0, Gap / 2, 0);
            InputHost historyHost = UiBuild.Host(lstHistory, Dpi.S(4, 4, 2, 4));
            historyHost.Cursor = Cursors.Default;
            historyCard.Controls.Add(historyHost);
            UiBuild.AddHeaderTools(historyCard, btnPlay);
            lower.Controls.Add(historyCard, 0, 2);

            CardPanel logCard = UiBuild.NewCard("執行日誌", "scroll-text", "");
            logCard.Margin = new Padding(Gap / 2, 0, 0, 0);
            InputHost logHost = UiBuild.Host(txtLog, Dpi.S(10, 8, 2, 6));
            logHost.Tag = "log";
            logCard.Controls.Add(logHost);
            lower.Controls.Add(logCard, 1, 2);

            mainSplit.Panel2.Controls.Add(lower);
            return mainSplit;
        }

        private Control BuildActionCard()
        {
            CardPanel card = UiBuild.NewCard("", "", "");
            card.Margin = new Padding(0, 0, 0, Gap);
            card.Padding = Dpi.S(14, 12, 18, 12);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.Margin = Padding.Empty;
            grid.ColumnCount = 3;
            grid.RowCount = 1;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(188)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(118)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            btnGenerate.Dock = DockStyle.Fill;
            btnGenerate.Margin = new Padding(0, 0, S(10), 0);
            grid.Controls.Add(btnGenerate, 0, 0);
            btnCancel.Dock = DockStyle.Fill;
            btnCancel.Margin = new Padding(0, 0, S(20), 0);
            grid.Controls.Add(btnCancel, 1, 0);

            TableLayoutPanel status = new TableLayoutPanel();
            status.Dock = DockStyle.Fill;
            status.Margin = Padding.Empty;
            status.ColumnCount = 1;
            status.RowCount = 2;
            status.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, S(14)));
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.Margin = Padding.Empty;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            bar.Dock = DockStyle.Top;
            bar.Height = S(8);
            bar.Margin = Padding.Empty;
            status.Controls.Add(lblStatus, 0, 0);
            status.Controls.Add(bar, 0, 1);
            grid.Controls.Add(status, 2, 0);
            card.Controls.Add(grid);
            return card;
        }

        private Control BuildPlayerCard()
        {
            CardPanel card = UiBuild.NewCard("", "", "");
            card.Margin = new Padding(0, 0, 0, Gap);
            card.Padding = Dpi.S(14, 8, 18, 8);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.Margin = Padding.Empty;
            grid.ColumnCount = 2;
            grid.RowCount = 1;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(112)));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            FlowLayoutPanel transport = new FlowLayoutPanel();
            transport.WrapContents = false;
            transport.Anchor = AnchorStyles.Left;
            transport.AutoSize = true;
            transport.Margin = Padding.Empty;
            btnPlayPause.Margin = new Padding(0, 0, S(8), 0);
            btnStop.Margin = new Padding(0, S(6), 0, 0);
            transport.Controls.Add(btnPlayPause);
            transport.Controls.Add(btnStop);
            grid.Controls.Add(transport, 0, 0);

            TableLayoutPanel center = new TableLayoutPanel();
            center.Dock = DockStyle.Fill;
            center.Margin = Padding.Empty;
            center.ColumnCount = 2;
            center.RowCount = 3;
            center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            center.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(140)));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, S(24)));
            center.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            center.RowStyles.Add(new RowStyle(SizeType.Absolute, S(18)));
            lblNowPlaying.Dock = DockStyle.Fill;
            lblNowPlaying.TextAlign = ContentAlignment.MiddleLeft;
            lblNowPlaying.Margin = Padding.Empty;
            center.Controls.Add(lblNowPlaying, 0, 0);
            lblTime.Dock = DockStyle.Fill;
            lblTime.Margin = Padding.Empty;
            center.Controls.Add(lblTime, 1, 0);
            waveform.Dock = DockStyle.Fill;
            waveform.Margin = new Padding(0, S(4), 0, S(2));
            center.Controls.Add(waveform, 0, 1);
            center.SetColumnSpan(waveform, 2);
            seekBar.Dock = DockStyle.Fill;
            seekBar.Margin = Padding.Empty;
            center.Controls.Add(seekBar, 0, 2);
            center.SetColumnSpan(seekBar, 2);
            grid.Controls.Add(center, 1, 0);
            card.Controls.Add(grid);
            return card;
        }

        private Control BuildSidebar()
        {
            TableLayoutPanel side = new TableLayoutPanel();
            side.Dock = DockStyle.Fill;
            side.Margin = Padding.Empty;
            side.ColumnCount = 1;
            side.RowCount = 2;
            side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            side.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            side.RowStyles.Add(new RowStyle(SizeType.Absolute, S(150)));

            CardPanel paramsCard = UiBuild.NewCard("生成參數", "sliders-horizontal", "");
            paramsCard.Margin = new Padding(0, 0, 0, Gap);
            paramsCard.Padding = new Padding(S(16), CardPanel.HeaderHeight, S(4), S(10));
            Panel scroller = new Panel();
            scroller.Dock = DockStyle.Fill;
            scroller.AutoScroll = true;
            scroller.Margin = Padding.Empty;

            // 4 columns: caption | field | caption | field
            TableLayoutPanel grid = new TableLayoutPanel();
            grid.AutoSize = true;
            grid.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            grid.Dock = DockStyle.Top;
            grid.Margin = Padding.Empty;
            grid.Padding = new Padding(0, 0, S(12), S(4));
            grid.ColumnCount = 4;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            UiBuild.AddSection(grid, "節奏與長度", false);
            UiBuild.AddWide(grid, "BPM", UiBuild.Row(UiBuild.Stepper(numBpm, S(120)), chkAutoBpm));
            // 時／分／秒 needs the full card width: caption on its own line, fields below.
            UiBuild.AddFull(grid, UiBuild.Caption("時長"), new RowStyle(SizeType.AutoSize));
            UiBuild.AddFull(grid, UiBuild.Row(
                UiBuild.Stepper(numHours, S(80)), UiBuild.Unit("時"),
                UiBuild.Stepper(numMinutes, S(84)), UiBuild.Unit("分"),
                UiBuild.Stepper(numSeconds, S(84)), UiBuild.Unit("秒")), new RowStyle(SizeType.AutoSize));
            UiBuild.AddSection(grid, "取樣", true);
            UiBuild.AddPair(grid, "步數", UiBuild.Stepper(numSteps, 0), "Guidance", UiBuild.Stepper(numGuidance, 0));
            InputHost seedHost = UiBuild.Host(txtSeed, UiBuild.TextPadding(txtSeed));
            seedHost.Height = S(36);
            UiBuild.AddWide(grid, "種子", UiBuild.Fill(seedHost, btnRandomSeed));
            UiBuild.AddSection(grid, "音樂", true);
            UiBuild.AddPair(grid, "調性", UiBuild.Combo(comboKey), "拍號", UiBuild.Combo(comboSignature));
            UiBuild.AddPair(grid, "語言", UiBuild.Combo(comboLanguage), "格式", UiBuild.Combo(comboFormat));
            UiBuild.AddSection(grid, "模型", true);
            UiBuild.AddWide(grid, "Offload", UiBuild.Row(UiBuild.ComboFixed(comboOffload, S(130)), UiBuild.Unit("下次載入模型時生效")));
            UiBuild.AddSection(grid, "選項", true);
            foreach (ToggleSwitch toggle in new ToggleSwitch[] { chkInstrumental, chkThinking, chkAutoVideo, chkKeepModel })
            {
                toggle.Margin = new Padding(0, S(1), 0, S(1));
                int row = grid.RowCount++;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                grid.Controls.Add(toggle, 0, row);
                grid.SetColumnSpan(toggle, 4);
            }
            scroller.Controls.Add(grid);
            paramsCard.Controls.Add(scroller);
            side.Controls.Add(paramsCard, 0, 0);

            CardPanel output = UiBuild.NewCard("輸出", "folder", "");
            output.Margin = Padding.Empty;
            TableLayoutPanel outGrid = new TableLayoutPanel();
            outGrid.Dock = DockStyle.Fill;
            outGrid.Margin = Padding.Empty;
            outGrid.ColumnCount = 2;
            outGrid.RowCount = 2;
            outGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            outGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            outGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, S(44)));
            outGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            InputHost dirHost = UiBuild.Host(txtOutDir, UiBuild.TextPadding(txtOutDir));
            Control dirRow = UiBuild.Fill(dirHost, btnBrowse);
            outGrid.Controls.Add(dirRow, 0, 0);
            outGrid.SetColumnSpan(dirRow, 2);
            btnOpen.Dock = DockStyle.Fill;
            btnOpen.Margin = new Padding(0, S(4), S(5), 0);
            btnCover.Dock = DockStyle.Fill;
            btnCover.Margin = new Padding(S(5), S(4), 0, 0);
            outGrid.Controls.Add(btnOpen, 0, 1);
            outGrid.Controls.Add(btnCover, 1, 1);
            output.Controls.Add(outGrid);
            side.Controls.Add(output, 0, 1);
            return side;
        }

        private void OnEditorTabChanged(object sender, EventArgs e)
        {
            int tab = editorTabs.SelectedIndex;
            bool lyrics = tab == 1;
            bool cover = tab == 2;
            bool video = tab == 3;
            promptHost.Visible = tab == 0;
            lyricsHost.Visible = lyrics;
            coverHost.Visible = cover;
            videoHost.Visible = video;
            if (lyrics) editorCard.Subtitle = LyricsHint;
            else if (cover) editorCard.Subtitle = CoverHint;
            else if (video) editorCard.Subtitle = VideoHint;
            else editorCard.Subtitle = PromptHint;
            // template / batch / expand only act on the music prompt
            chkBatch.Visible = tab == 0;
            comboStyleTemplate.Visible = tab == 0;
            btnApplyTemplate.Visible = tab == 0;
            btnExpandPrompt.Visible = tab == 0;
            Control target = tab == 1 ? (Control)txtLyrics
                : tab == 2 ? (Control)txtCover
                : tab == 3 ? (Control)txtVideo
                : txtCaption;
            target.Focus();
        }

        // ------------------------------------------------------------------ theme & window chrome

        private void TryLoadIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(iconPath)) Icon = new Icon(iconPath);
            }
            catch (Exception)
            {
            }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTitleBarTheme();
        }

        private void ApplyTitleBarTheme()
        {
            if (!IsHandleCreated) return;
            try
            {
                int enabled = ThemeManager.Dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
                DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
            }
            catch (Exception)
            {
            }
        }

        private void ApplyTheme()
        {
            ThemeManager.Apply(this);
            ThemeManager.StylePrimary(btnGenerate);
            ThemeManager.StylePrimary(btnPlayPause);
            btnCancel.Kind = ButtonKind.DangerOutline;
            btnTheme.IconName = ThemeManager.Dark ? "sun" : "moon";
            mainSplit.BackColor = ThemeManager.FormBack;
            waveform.ThemeDark = ThemeManager.Dark;
            comboTheme.SelectedIndex = ThemeManager.Dark ? 0 : 1;
            ApplyTitleBarTheme();
            ThemeManager.ApplyNativeScrollBars(this);
            Invalidate(true);
        }

        private void OnToggleTheme(object sender, EventArgs e)
        {
            ThemeManager.SetTheme(!ThemeManager.Dark);
            ApplyTheme();
            SaveSettings();
        }

        // ------------------------------------------------------------------ split ratio

        private void ApplySplitRatio()
        {
            int height = mainSplit.Height;
            if (height < S(200)) return;
            int lowerMin = ActionHeight + PlayerHeight + Gap * 2 + S(120);
            mainSplit.Panel1MinSize = Math.Min(S(240), Math.Max(S(120), height - lowerMin - Gap));
            mainSplit.Panel2MinSize = Math.Max(S(100), Math.Min(lowerMin, height - mainSplit.Panel1MinSize - Gap));
            int distance = (int)Math.Round(height * savedSplitRatio);
            int max = height - mainSplit.Panel2MinSize - mainSplit.SplitterWidth;
            distance = Math.Max(mainSplit.Panel1MinSize, Math.Min(max, distance));
            try { mainSplit.SplitterDistance = distance; } catch (Exception) { }
            splitApplied = true;
        }

        private double CurrentSplitRatio()
        {
            if (!splitApplied || mainSplit.Height <= 0) return savedSplitRatio;
            return Math.Round(mainSplit.SplitterDistance / (double)mainSplit.Height, 3);
        }

        // ------------------------------------------------------------------ player & history drawing

        private void UpdatePlaybackUi()
        {
            bool playing = audioOutput != null && audioOutput.PlaybackState == NAudio.Wave.PlaybackState.Playing;
            btnPlayPause.Text = playing ? "暫停" : "播放";
            btnPlayPause.IconName = playing ? "pause" : "play";
        }

        private void SetNowPlaying(string path)
        {
            lblNowPlaying.Text = string.IsNullOrEmpty(path) ? "尚未載入音檔" : Path.GetFileName(path);
        }

        private void OnHistoryDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Color back = ThemeManager.InputBack;
            using (SolidBrush brush = new SolidBrush(back)) g.FillRectangle(brush, e.Bounds);
            Rectangle row = new Rectangle(e.Bounds.X + S(2), e.Bounds.Y + S(2), e.Bounds.Width - S(4), e.Bounds.Height - S(4));
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (selected)
            {
                using (System.Drawing.Drawing2D.GraphicsPath path = Shapes.RoundedRect(row, S(8)))
                using (SolidBrush brush = new SolidBrush(ThemeManager.Blend(back, ThemeManager.Accent, ThemeManager.Dark ? 0.22 : 0.14)))
                    g.FillPath(brush, path);
            }
            int tileSize = S(32);
            Rectangle tile = new Rectangle(row.X + S(8), row.Y + (row.Height - tileSize) / 2, tileSize, tileSize);
            using (System.Drawing.Drawing2D.GraphicsPath path = Shapes.RoundedRect(tile, S(8)))
            using (SolidBrush brush = new SolidBrush(ThemeManager.Blend(back, ThemeManager.Accent, selected ? 0.45 : 0.16)))
                g.FillPath(brush, path);
            int icon = S(16);
            IconRenderer.Draw(g, "music", new RectangleF(tile.X + (tileSize - icon) / 2F, tile.Y + (tileSize - icon) / 2F, icon, icon),
                selected ? ThemeManager.Text : ThemeManager.Accent);

            string text = lstHistory.Items[e.Index].ToString();
            string[] parts = text.Split(new string[] { "  |  " }, StringSplitOptions.None);
            string name = parts[0];
            string detail = parts.Length > 1 ? string.Join("  ·  ", parts, 1, parts.Length - 1) : "";
            int textX = tile.Right + S(10);
            int lineHeight = row.Height / 2;
            Rectangle nameRect = new Rectangle(textX, row.Y + S(2), row.Right - textX - S(8), lineHeight);
            TextRenderer.DrawText(g, name, ThemeManager.UiFont, nameRect, ThemeManager.Text,
                TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (detail.Length > 0)
            {
                Rectangle detailRect = new Rectangle(textX, row.Y + lineHeight + S(1), row.Right - textX - S(8), lineHeight - S(2));
                TextRenderer.DrawText(g, detail, ThemeManager.SmallFont, detailRect, ThemeManager.Muted,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        // ------------------------------------------------------------------ self tests

        internal string UiSelfTest()
        {
            StringBuilder report = new StringBuilder();
            try
            {
                Size = Dpi.S(1440, 900);
                PerformLayout();
                ThemeManager.SetTheme(false);
                ApplyTheme();
                report.AppendLine("light_form=" + BackColor.ToArgb() +
                                  " card=" + ThemeManager.CardBack.ToArgb() +
                                  " input=" + txtCaption.BackColor.ToArgb());
                ThemeManager.SetTheme(true);
                ApplyTheme();
                report.AppendLine("dark_form=" + BackColor.ToArgb() +
                                  " card=" + ThemeManager.CardBack.ToArgb() +
                                  " input=" + txtCaption.BackColor.ToArgb());
                report.AppendLine("dpi_factor=" + Dpi.Factor);
                int missing = 0;
                foreach (string name in new string[] { "sparkles", "x", "play", "pause", "square", "folder", "folder-open",
                    "link", "disc-3", "wrench", "image", "settings-2", "send", "sun", "moon", "dices", "music",
                    "list-music", "scroll-text", "sliders-horizontal", "wand-sparkles", "maximize-2", "audio-lines",
                    "audio-waveform" })
                {
                    if (IconRenderer.GetPath(name) == null) { missing++; report.AppendLine("icon_missing=" + name); }
                }
                report.AppendLine("icons_missing=" + missing);
                int oldSeconds = (int)numDuration.Value;
                SetDurationFromSeconds(7200);
                report.AppendLine("duration_max=" + numHours.Value + "h" + numMinutes.Value +
                                  "m" + numSeconds.Value + "s total=" + numDuration.Value);
                SetDurationFromSeconds(30);
                report.AppendLine("duration_30s=" + numHours.Value + "h" + numMinutes.Value +
                                  "m" + numSeconds.Value + "s total=" + numDuration.Value);
                SetDurationFromSeconds(oldSeconds);
                report.AppendLine("style_templates=" + comboStyleTemplate.Items.Count);
                comboStyleTemplate.SelectedIndex = 0;
                OnApplyStyleTemplate(this, EventArgs.Empty);
                report.AppendLine("template_apply=" + numBpm.Value + "|" +
                                  Convert.ToString(comboKey.SelectedItem) + "|" + txtCaption.TextLength + " chars");
                report.AppendLine("editor_tabs=" + editorTabs.Count +
                                  " cover_chars=" + txtCover.TextLength +
                                  " video_style=" + VideoStyleFromPrompt("ambient rain") +
                                  " cover_override=" + (CoverPromptFor("missing.mp3").Length > 0));
                report.AppendLine("auto_video=" + chkAutoVideo.Text + " checked=" + chkAutoVideo.Checked);
                using (MediaToolsDialog tools = new MediaToolsDialog(Paths.Generated, null))
                {
                    tools.CreateControl();
                    report.AppendLine("media_tools_controls=" + tools.Controls.Count);
                }
            }
            catch (Exception ex)
            {
                report.AppendLine("error=" + ex);
            }
            return report.ToString();
        }

        /// <summary>Fills the window with demo content for --ui-screenshot.</summary>
        internal void PrepareScreenshot(bool dark, string demoDir)
        {
            string size = Environment.GetEnvironmentVariable("AIMUSIC_SHOT_SIZE");
            string screen = Environment.GetEnvironmentVariable("AIMUSIC_SHOT_SCREEN");
            WindowState = FormWindowState.Normal;
            StartPosition = FormStartPosition.Manual;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int index;
            if (!string.IsNullOrEmpty(screen) && int.TryParse(screen, out index) && index < Screen.AllScreens.Length)
                area = Screen.AllScreens[index].WorkingArea;
            Size target = new Size(Math.Min(area.Width, S(1440)), Math.Min(area.Height, S(900)));
            if (size == "max") target = area.Size;
            Location = area.Location;
            Size = target;
            ThemeManager.SetTheme(dark);
            ApplyTheme();
            if (!string.IsNullOrEmpty(demoDir))
            {
                txtOutDir.Text = demoDir;
                RefreshHistory();
                if (lstHistory.Items.Count > 1) lstHistory.SelectedIndex = 1;
            }
            txtCaption.Text = "Warm lo-fi hip hop, dusty boom bap drums, mellow Rhodes piano, jazzy guitar licks, " +
                              "upright bass, vinyl crackle and tape hiss, relaxed late-night study atmosphere, " +
                              "instrumental only, no vocals.";
            float[] peaks = new float[1024];
            Random random = new Random(7);
            for (int i = 0; i < peaks.Length; i++)
            {
                double envelope = 0.35 + 0.45 * Math.Abs(Math.Sin(i / 90.0)) + 0.15 * Math.Sin(i / 13.0);
                peaks[i] = (float)Math.Max(0.05, Math.Min(1.0, envelope * (0.7 + 0.3 * random.NextDouble())));
            }
            waveform.Peaks = peaks;
            waveform.Position = 0.37;
            seekBar.Value = 370;
            lblNowPlaying.Text = "ace_20261117_021553_seed1706262108.mp3";
            lblTime.Text = "02:57 / 08:00";
            lblStatus.Text = "生成音樂中（480s, 8 steps）... 42%  [已耗時 03:12]";
            bar.Style = ProgressBarStyle.Blocks;
            bar.Value = 42;
            btnCancel.Enabled = true;
            AppendLog("=== 啟動模型常駐程序（載入後保持，offload=auto）===");
            AppendLog("[gpu] NVIDIA GeForce RTX 3060 - VRAM free 10.84/12.00 GB");
            AppendLog("[stage] 載入 DiT 主模型 (acestep-v15-turbo)...");
            AppendLog("[info] DiT 載入 41.2s ok=True");
            AppendLog("=== 模型就緒 ===");
            AppendLog("[stage] 生成音樂中（480s, 8 steps）...");
        }
    }
}
