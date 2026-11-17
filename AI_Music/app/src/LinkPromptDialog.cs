using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    internal sealed class LinkPromptDialog : Form
    {
        private readonly TextBox txtUrl = new TextBox();
        private readonly ModernButton btnParse = new ModernButton();
        private readonly ComboBox comboOutputLanguage = new ThemedComboBox();
        private LinkInfo lastInfo;
        private readonly Label lblStatus = new Label();
        private readonly Label lblMeta = new Label();
        private readonly TextBox txtPreview = new TextBox();
        private readonly ModernButton btnApply = new ModernButton();
        private readonly ModernButton btnCancel = new ModernButton();

        public string SuggestedPrompt { get; private set; }
        private string suggestionPrompt = "";

        public LinkPromptDialog()
        {
            UiBuild.DialogParts parts = UiBuild.Dialog(this, "參考連結 → 提示詞",
                "貼上 YouTube／Spotify 連結；只讀取 metadata，不下載音訊", "link", 760, 580, false);
            BuildUi(parts);
            ThemeManager.ApplyDialog(this);
        }

        private void BuildUi(UiBuild.DialogParts parts)
        {
            TableLayoutPanel body = parts.Body;
            body.ColumnCount = 2;
            body.ColumnStyles.Clear();
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            txtUrl.Text = "https://www.youtube.com/watch?v=";
            UiBuild.Button(btnParse, "解析", "sparkles", ButtonKind.Primary);
            btnParse.Click += OnParse;
            UiBuild.AddWide(body, "連結", UiBuild.Fill(UiBuild.TextField(txtUrl), btnParse));

            comboOutputLanguage.Items.AddRange(new object[] { "English", "繁體中文" });
            comboOutputLanguage.SelectedIndex = 0;
            comboOutputLanguage.SelectedIndexChanged += delegate { RefreshLanguage(); };
            UiBuild.AddWide(body, "輸出語言", UiBuild.Row(UiBuild.ComboFixed(comboOutputLanguage, Dpi.S(160)),
                UiBuild.Unit("建議提示詞使用英文，模型理解較好")));

            lblStatus.AutoSize = true;
            lblStatus.Tag = "accent";
            lblStatus.Font = ThemeManager.HeadingFont;
            lblStatus.Margin = new Padding(0, Dpi.S(12), 0, Dpi.S(2));
            UiBuild.AddFull(body, lblStatus, new RowStyle(SizeType.AutoSize));
            lblMeta.AutoSize = true;
            lblMeta.Tag = "muted";
            lblMeta.MaximumSize = new Size(Dpi.S(700), 0);
            lblMeta.Margin = new Padding(0, 0, 0, Dpi.S(6));
            UiBuild.AddFull(body, lblMeta, new RowStyle(SizeType.AutoSize));

            txtPreview.ReadOnly = true;
            txtPreview.ScrollBars = ScrollBars.Vertical;
            UiBuild.AddFull(body, UiBuild.TextArea(txtPreview, ThemeManager.MonoFont), new RowStyle(SizeType.Percent, 100F));

            UiBuild.Button(btnApply, "套用提示詞", "check", ButtonKind.Primary);
            btnApply.Enabled = false;
            btnApply.Click += delegate
            {
                SuggestedPrompt = suggestionPrompt.Length > 0 ? suggestionPrompt : txtPreview.Text.Trim();
                if (SuggestedPrompt.Length == 0) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            UiBuild.Button(btnCancel, "取消", "", ButtonKind.Secondary);
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            UiBuild.AddFooterButton(parts.Footer, btnApply);
            UiBuild.AddFooterButton(parts.Footer, btnCancel);
            CancelButton = btnCancel;
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
