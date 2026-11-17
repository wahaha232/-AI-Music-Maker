using System;
using System.Drawing;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    internal sealed class PromptEditorDialog : Form
    {
        private readonly RichTextBox editor = new RichTextBox();

        public string PromptText
        {
            get { return editor.Text; }
        }

        public PromptEditorDialog(string text)
        {
            Text = "編輯提示詞";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1000, 760);
            MinimumSize = new Size(640, 480);
            Font = new Font("Microsoft JhengHei UI", 11F);
            BackColor = Color.FromArgb(18, 19, 24);
            ForeColor = Color.White;

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 56;
            bottom.BackColor = Color.FromArgb(18, 19, 24);

            Button ok = new Button();
            ok.Text = "套用";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(764, 8, 110, 40);
            ok.FlatStyle = FlatStyle.Flat;
            ok.BackColor = Color.FromArgb(67, 56, 202);
            ok.ForeColor = Color.White;
            bottom.Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(880, 8, 110, 40);
            cancel.FlatStyle = FlatStyle.Flat;
            cancel.BackColor = Color.FromArgb(55, 58, 70);
            cancel.ForeColor = Color.White;
            bottom.Controls.Add(cancel);

            editor.Dock = DockStyle.Fill;
            editor.BorderStyle = BorderStyle.None;
            editor.BackColor = Color.FromArgb(26, 27, 35);
            editor.ForeColor = Color.FromArgb(230, 233, 240);
            editor.Font = new Font("Microsoft JhengHei UI", 12F);
            editor.WordWrap = true;
            editor.ScrollBars = RichTextBoxScrollBars.Vertical;
            editor.AcceptsTab = true;
            editor.Text = text ?? "";

            Controls.Add(editor);
            Controls.Add(bottom);
            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
