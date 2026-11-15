using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    internal sealed class SdDialog : Form
    {
        private readonly CheckBox chkEnable = new CheckBox();
        private readonly TextBox txtUrl = new TextBox();
        private readonly TextBox txtPath = new TextBox();
        private readonly ComboBox comboModel = new ComboBox();
        private readonly Button btnRefresh = new Button();
        private readonly NumericUpDown numSteps = new NumericUpDown();
        private readonly ComboBox comboSize = new ComboBox();
        private readonly Label lblStatus = new Label();

        public bool EnabledForCover { get; private set; }
        public string ApiUrl { get; private set; }
        public string InstallPath { get; private set; }
        public string ModelTitle { get; private set; }
        public int Steps { get; private set; }
        public int CoverSize { get; private set; }

        public SdDialog(bool enabled, string url, string path, string model, int steps, int size)
        {
            Text = "Stable Diffusion 封面";
            Font = new Font("Microsoft JhengHei UI", 11F);
            ClientSize = new Size(620, 430);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(24, 25, 31);
            ForeColor = Color.FromArgb(232, 234, 241);

            chkEnable.SetBounds(12, 12, 580, 28);
            chkEnable.Text = "音樂生成後，自動用 Stable Diffusion 產生封面";
            chkEnable.Checked = enabled;

            Label l1 = MakeLabel("WebUI URL", 12, 52);
            txtUrl.SetBounds(12, 78, 580, 28);
            txtUrl.BackColor = Color.FromArgb(34, 36, 44);
            txtUrl.ForeColor = Color.White;
            txtUrl.Text = url;

            Label l2 = MakeLabel("WebUI 安裝路徑", 12, 116);
            txtPath.SetBounds(12, 142, 580, 28);
            txtPath.BackColor = Color.FromArgb(34, 36, 44);
            txtPath.ForeColor = Color.White;
            txtPath.Text = path;

            Label l3 = MakeLabel("Model", 12, 180);
            comboModel.SetBounds(12, 206, 470, 28);
            comboModel.DropDownStyle = ComboBoxStyle.DropDownList;
            comboModel.Items.Add("(尚未取得模型清單)");
            comboModel.SelectedIndex = 0;
            if (!string.IsNullOrEmpty(model))
            {
                comboModel.Items.Add(model);
                comboModel.SelectedItem = model;
            }

            btnRefresh.SetBounds(492, 205, 100, 30);
            btnRefresh.Text = "重新整理";
            btnRefresh.FlatStyle = FlatStyle.Flat;
            btnRefresh.BackColor = Color.FromArgb(48, 50, 60);
            btnRefresh.ForeColor = Color.White;
            btnRefresh.Click += OnRefresh;

            Label l4 = MakeLabel("Steps", 12, 246);
            numSteps.SetBounds(12, 272, 80, 28);
            numSteps.Minimum = 8;
            numSteps.Maximum = 40;
            numSteps.Value = Math.Max(8, Math.Min(40, steps));

            Label l5 = MakeLabel("尺寸", 112, 246);
            comboSize.SetBounds(112, 272, 100, 28);
            comboSize.DropDownStyle = ComboBoxStyle.DropDownList;
            comboSize.Items.AddRange(new object[] { "512", "640", "768" });
            comboSize.SelectedItem = size.ToString();
            if (comboSize.SelectedIndex < 0) comboSize.SelectedIndex = 0;

            lblStatus.SetBounds(12, 312, 580, 40);
            lblStatus.ForeColor = Color.FromArgb(160, 200, 235);
            lblStatus.Text = "提示：第一次啟動 WebUI 約需 1–3 分鐘。";

            Button ok = new Button();
            ok.SetBounds(360, 370, 110, 38);
            ok.Text = "套用";
            ok.FlatStyle = FlatStyle.Flat;
            ok.BackColor = Color.FromArgb(108, 92, 231);
            ok.ForeColor = Color.White;
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

            Button cancel = new Button();
            cancel.SetBounds(480, 370, 110, 38);
            cancel.Text = "取消";
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Color.FromArgb(48, 50, 60);
            cancel.ForeColor = Color.White;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[]
            {
                chkEnable, l1, txtUrl, l2, txtPath, l3, comboModel, btnRefresh,
                l4, numSteps, l5, comboSize, lblStatus, ok, cancel
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
