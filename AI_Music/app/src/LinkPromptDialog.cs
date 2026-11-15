using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    internal sealed class LinkPromptDialog : Form
    {
        private readonly TextBox txtUrl = new TextBox();
        private readonly Button btnParse = new Button();
        private readonly ComboBox comboOutputLanguage = new ComboBox();
        private LinkInfo lastInfo;
        private readonly Label lblStatus = new Label();
        private readonly Label lblMeta = new Label();
        private readonly TextBox txtPreview = new TextBox();
        private readonly Button btnApply = new Button();
        private readonly Button btnCancel = new Button();

        public string SuggestedPrompt { get; private set; }
        private string suggestionPrompt = "";

        public LinkPromptDialog()
        {
            Text = "參考連結 → 提示詞";
            Font = new Font("Microsoft JhengHei UI", 10F);
            ClientSize = new Size(720, 500);
            MinimumSize = new Size(720, 500);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(18, 19, 24);
            ForeColor = Color.FromArgb(232, 234, 241);
            BuildUi();
            ThemeManager.ApplyDialog(this);
            ThemeManager.StylePrimary(btnParse);
            ThemeManager.StylePrimary(btnApply);
        }

        private void BuildUi()
        {
            Label hint = MakeLabel("貼上 YouTube / Spotify 連結（只讀 metadata，不下載音訊）", 12, 12);
            hint.ForeColor = Color.FromArgb(188, 194, 208);

            txtUrl.SetBounds(12, 40, 580, 28);
            txtUrl.BackColor = Color.FromArgb(30, 32, 40);
            txtUrl.ForeColor = Color.FromArgb(240, 242, 247);
            txtUrl.BorderStyle = BorderStyle.FixedSingle;
            txtUrl.Text = "https://www.youtube.com/watch?v=";

            Label lblLanguage = MakeLabel("輸出語言", 470, 12);
            comboOutputLanguage.SetBounds(538, 8, 170, 28);
            comboOutputLanguage.DropDownStyle = ComboBoxStyle.DropDownList;
            comboOutputLanguage.Items.AddRange(new object[] { "English", "繁體中文" });
            comboOutputLanguage.SelectedIndex = 0;
            comboOutputLanguage.BackColor = Color.FromArgb(30, 32, 40);
            comboOutputLanguage.ForeColor = Color.FromArgb(240, 242, 247);
            comboOutputLanguage.SelectedIndexChanged += delegate { RefreshLanguage(); };

            btnParse.SetBounds(600, 40, 96, 28);
            btnParse.Text = "解析";
            StyleButton(btnParse, true);
            btnParse.Click += OnParse;

            lblStatus.SetBounds(12, 76, 684, 22);
            lblStatus.ForeColor = Color.FromArgb(148, 205, 255);

            lblMeta.SetBounds(12, 104, 684, 44);
            lblMeta.ForeColor = Color.FromArgb(188, 194, 208);

            txtPreview.SetBounds(12, 154, 684, 276);
            txtPreview.Multiline = true;
            txtPreview.ReadOnly = true;
            txtPreview.ScrollBars = ScrollBars.Vertical;
            txtPreview.BackColor = Color.FromArgb(12, 13, 18);
            txtPreview.ForeColor = Color.FromArgb(226, 229, 238);
            txtPreview.Font = new Font("Consolas", 9F);

            btnApply.SetBounds(480, 448, 110, 34);
            btnApply.Text = "套用提示詞";
            btnApply.Enabled = false;
            StyleButton(btnApply, true);
            btnApply.Click += delegate
            {
                SuggestedPrompt = suggestionPrompt.Length > 0 ? suggestionPrompt : txtPreview.Text.Trim();
                if (SuggestedPrompt.Length == 0) return;
                DialogResult = DialogResult.OK;
                Close();
            };

            btnCancel.SetBounds(600, 448, 96, 34);
            btnCancel.Text = "取消";
            StyleButton(btnCancel, false);
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[]
            {
                hint, lblLanguage, comboOutputLanguage, txtUrl, btnParse,
                lblStatus, lblMeta, txtPreview, btnApply, btnCancel
            });
        }

        private static Label MakeLabel(string text, int x, int y)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Location = new Point(x, y);
            label.BackColor = Color.Transparent;
            return label;
        }

        private static void StyleButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = primary
                ? Color.FromArgb(124, 92, 255)
                : Color.FromArgb(58, 62, 75);
            button.BackColor = primary
                ? Color.FromArgb(108, 92, 231)
                : Color.FromArgb(38, 41, 51);
            button.ForeColor = Color.White;
            button.Cursor = Cursors.Hand;
        }

        private void RefreshLanguage()
        {
            if (lastInfo == null || !btnApply.Enabled) return;
            bool chinese = comboOutputLanguage.SelectedIndex == 1;
            ApplySuggestion(LinkAnalyzer.Suggest(lastInfo, true, chinese), chinese);
        }

        private void ApplySuggestion(PromptSuggestion result, bool chinese)
        {
            suggestionPrompt = result.Prompt;
            txtPreview.Text = result.Prompt + "\r\n\r\n[建議 BPM] " + result.Bpm +
                              "\r\n[負向提示，僅供參考] " + result.Negative;
            lblStatus.Text = "推測曲風: " + result.Genre + "（僅供參考，可手動修改）";
            btnApply.Enabled = true;
        }

        private void OnParse(object sender, EventArgs e)
        {
            string url = txtUrl.Text.Trim();
            if (url.Length == 0)
            {
                lblStatus.Text = "請先貼上連結。";
                return;
            }
            btnParse.Enabled = false;
            btnApply.Enabled = false;
            lblStatus.Text = "讀取 metadata 中...";
            lblMeta.Text = "";
            txtPreview.Clear();

            Thread worker = new Thread(delegate()
            {
                try
                {
                    LinkInfo info = LinkAnalyzer.Analyze(url);
                    bool chinese = comboOutputLanguage.SelectedIndex == 1;
                    PromptSuggestion result = LinkAnalyzer.Suggest(info, true, chinese);
                    try
                    {
                        BeginInvoke(new Action(delegate()
                        {
                            lastInfo = info;
                            string meta = info.Source + " | " + info.Artist;
                            if (info.Title.Length > 0) meta += " - " + info.Title;
                            if (info.Album.Length > 0) meta += "\r\n專輯: " + info.Album;
                            if (info.Year.Length > 0) meta += "  年份: " + info.Year;
                            lblMeta.Text = meta;
                            txtPreview.Text = result.Prompt + "\r\n\r\n[建議 BPM] " + result.Bpm +
                                              "\r\n[負向提示，僅供參考] " + result.Negative;
                            lblStatus.Text = "推測曲風: " + result.Genre + "（僅供參考，可手動修改）";
                            suggestionPrompt = result.Prompt;
                            btnApply.Enabled = true;
                            btnParse.Enabled = true;
                        }));
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        BeginInvoke(new Action(delegate()
                        {
                            lblStatus.Text = "解析失敗: " + ex.Message;
                            btnParse.Enabled = true;
                        }));
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            txtUrl.Focus();
            txtUrl.SelectAll();
        }
    }
}
