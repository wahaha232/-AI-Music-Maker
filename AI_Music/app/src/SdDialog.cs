using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    internal sealed class SdDialog : Form
    {
        private readonly CheckBox chkEnable = new ToggleSwitch();
        private readonly TextBox txtUrl = new TextBox();
        private readonly TextBox txtPath = new TextBox();
        private readonly ComboBox comboModel = new ThemedComboBox();
        private readonly ModernButton btnRefresh = new ModernButton();
        private readonly NumericUpDown numSteps = new NumericUpDown();
        private readonly ComboBox comboSize = new ThemedComboBox();
        private readonly Label lblStatus = new Label();

        public bool EnabledForCover { get; private set; }
        public string ApiUrl { get; private set; }
        public string InstallPath { get; private set; }
        public string ModelTitle { get; private set; }
        public int Steps { get; private set; }
        public int CoverSize { get; private set; }

        public SdDialog(bool enabled, string url, string path, string model, int steps, int size)
        {
            UiBuild.DialogParts parts = UiBuild.Dialog(this, "Stable Diffusion 封面",
                "音樂完成後呼叫本機 WebUI API 產生專輯封面", "image", 640, 520, false);
            TableLayoutPanel body = parts.Body;
            body.ColumnCount = 4;
            body.ColumnStyles.Clear();
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            chkEnable.Text = "音樂生成後，自動用 Stable Diffusion 產生封面";
            chkEnable.Checked = enabled;
            chkEnable.Margin = new Padding(0, Dpi.S(2), 0, Dpi.S(6));
            UiBuild.AddFull(body, chkEnable, new RowStyle(SizeType.AutoSize));

            txtUrl.Text = url;
            txtPath.Text = path;
            UiBuild.AddSection(body, "WebUI", true);
            UiBuild.AddWide(body, "API 網址", UiBuild.TextField(txtUrl));
            UiBuild.AddWide(body, "安裝路徑", UiBuild.TextField(txtPath));

            comboModel.Items.Add("(尚未取得模型清單)");
            comboModel.SelectedIndex = 0;
            if (!string.IsNullOrEmpty(model))
            {
                comboModel.Items.Add(model);
                comboModel.SelectedItem = model;
            }
            UiBuild.Button(btnRefresh, "重新整理", "refresh-cw", ButtonKind.Secondary);
            btnRefresh.Click += OnRefresh;
            Control modelCombo = UiBuild.Combo(comboModel);
            UiBuild.AddSection(body, "模型與品質", true);
            UiBuild.AddWide(body, "模型", UiBuild.Fill(modelCombo, btnRefresh));

            numSteps.Minimum = 8;
            numSteps.Maximum = 40;
            numSteps.Value = Math.Max(8, Math.Min(40, steps));
            comboSize.Items.AddRange(new object[] { "512", "640", "768" });
            comboSize.SelectedItem = size.ToString();
            if (comboSize.SelectedIndex < 0) comboSize.SelectedIndex = 0;
            UiBuild.AddPair(body, "Steps", UiBuild.Stepper(numSteps, 0), "尺寸", UiBuild.Combo(comboSize));

            lblStatus.Text = "提示：第一次啟動 WebUI 約需 1–3 分鐘。";
            lblStatus.Tag = "accent";
            lblStatus.AutoSize = true;
            lblStatus.MaximumSize = new Size(Dpi.S(580), 0);
            lblStatus.Margin = new Padding(0, Dpi.S(12), 0, 0);
            UiBuild.AddFull(body, lblStatus, new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.RowCount++;

            ModernButton ok = UiBuild.Button(new ModernButton(), "套用", "check", ButtonKind.Primary);
            ok.Click += delegate
            {
                EnabledForCover = chkEnable.Checked;
                ApiUrl = txtUrl.Text.Trim();
                InstallPath = txtPath.Text.Trim();
                ModelTitle = Convert.ToString(comboModel.SelectedItem);
                if (ModelTitle.StartsWith("(")) ModelTitle = "";
                Steps = (int)numSteps.Value;
                CoverSize = int.Parse(Convert.ToString(comboSize.SelectedItem));
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

        private void OnRefresh(object sender, EventArgs e)
        {
            btnRefresh.Enabled = false;
            lblStatus.Text = "啟動／讀取 SD WebUI 中...";
            string url = txtUrl.Text.Trim();
            string path = txtPath.Text.Trim();
            Thread worker = new Thread(delegate()
            {
                try
                {
                    if (!SdCoverClient.EnsureApi(url, path, delegate(string message)
                    {
                        PostStatus(message);
                    }))
                    {
                        PostStatus("SD API 無法啟動，請確認路徑與 WebUI 可正常執行。");
                        return;
                    }
                    List<string> models = SdCoverClient.GetModels(url);
                    try
                    {
                        BeginInvoke(new Action(delegate()
                        {
                            comboModel.Items.Clear();
                            foreach (string title in models) comboModel.Items.Add(title);
                            if (comboModel.Items.Count > 0) comboModel.SelectedIndex = 0;
                            lblStatus.Text = "已取得 " + models.Count + " 個模型。";
                            btnRefresh.Enabled = true;
                        }));
                    }
                    catch (InvalidOperationException) { }
                }
                catch (Exception ex)
                {
                    PostStatus("讀取模型失敗: " + ex.Message);
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void PostStatus(string message)
        {
            try
            {
                BeginInvoke(new Action(delegate()
                {
                    lblStatus.Text = message;
                    btnRefresh.Enabled = true;
                }));
            }
            catch (InvalidOperationException) { }
        }
    }
}
