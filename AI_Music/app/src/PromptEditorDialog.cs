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
            UiBuild.DialogParts parts = UiBuild.Dialog(this, "編輯提示詞",
                "大型編輯視窗；套用後會取代主畫面的提示詞", "maximize-2", 1000, 720, true);
            MinimumSize = Dpi.S(640, 480);

            editor.WordWrap = true;
            editor.ScrollBars = RichTextBoxScrollBars.Vertical;
            editor.AcceptsTab = true;
            editor.DetectUrls = false;
            editor.Text = text ?? "";
            UiBuild.AddFull(parts.Body, UiBuild.TextArea(editor, Dpi.Font(12F, FontStyle.Regular)),
                new RowStyle(SizeType.Percent, 100F));

            ModernButton ok = UiBuild.Button(new ModernButton(), "套用", "check", ButtonKind.Primary);
            ok.DialogResult = DialogResult.OK;
            ModernButton cancel = UiBuild.Button(new ModernButton(), "取消", "", ButtonKind.Secondary);
            cancel.DialogResult = DialogResult.Cancel;
            UiBuild.AddFooterButton(parts.Footer, ok);
            UiBuild.AddFooterButton(parts.Footer, cancel);
            CancelButton = cancel;

            ThemeManager.ApplyDialog(this);
        }
    }
}
