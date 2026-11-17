using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    /// <summary>Rounded bar-style waveform with played/pending colouring and click-to-seek.</summary>
    internal sealed class WaveformPanel : Control
    {
        public float[] Peaks;
        public double Duration;
        public double Position;
        public bool ThemeDark = true;
        public event Action<double> SeekRequested;
        private int hoverX = -1;

        public WaveformPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color surface = Parent != null ? Parent.BackColor : ThemeManager.CardBack;
            g.Clear(surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF frame = new RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F);
            using (GraphicsPath path = Shapes.RoundedRect(frame, 10))
            using (SolidBrush brush = new SolidBrush(ThemeManager.InputBack))
                g.FillPath(brush, path);

            int width = Width;
            int height = Height;
            if (Peaks == null || Peaks.Length == 0)
            {
                IconRenderer.Draw(g, "audio-waveform", new RectangleF(width / 2F - 92, height / 2F - 9, 18, 18), ThemeManager.Muted);
                TextRenderer.DrawText(g, "載入音檔後顯示波形，點擊可跳轉", ThemeManager.SmallFont,
                    new Rectangle(width / 2 - 68, 0, width / 2 + 68, height), ThemeManager.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                return;
            }

            const int bar = 3;
            const int gap = 2;
            int inner = Math.Max(1, width - 20);
            int count = Math.Max(1, inner / (bar + gap));
            float center = height / 2F;
            float maxAmp = (height - 14) / 2F;
            float playedX = 10 + (float)(Position * inner);
            using (SolidBrush played = new SolidBrush(ThemeManager.Accent))
            using (SolidBrush pending = new SolidBrush(ThemeManager.Blend(ThemeManager.InputBack, ThemeManager.SubText, 0.45)))
            using (SolidBrush hovered = new SolidBrush(ThemeManager.Blend(ThemeManager.InputBack, ThemeManager.Accent, 0.55)))
            {
                for (int i = 0; i < count; i++)
                {
                    int start = (int)((long)i * Peaks.Length / count);
                    int end = Math.Max(start + 1, (int)((long)(i + 1) * Peaks.Length / count));
                    float peak = 0F;
                    for (int j = start; j < end && j < Peaks.Length; j++) peak = Math.Max(peak, Peaks[j]);
                    float amp = Math.Max(1.5F, (float)Math.Sqrt(Math.Min(1F, peak)) * maxAmp);
                    float x = 10 + i * (bar + gap);
                    Brush brush = x <= playedX ? played : (hoverX >= 0 && x <= hoverX ? hovered : pending);
                    using (GraphicsPath path = Shapes.RoundedRect(new RectangleF(x, center - amp, bar, amp * 2), 1.5F))
                        g.FillPath(brush, path);
                }
            }
            using (Pen cursor = new Pen(ThemeManager.Text, 1.5F))
                g.DrawLine(cursor, playedX, 6, playedX, height - 6);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Peaks == null) return;
            hoverX = e.X;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverX = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Peaks == null || Width <= 20) return;
            double ratio = Math.Max(0.0, Math.Min(1.0, (e.X - 10.0) / (Width - 20.0)));
            if (SeekRequested != null) SeekRequested(ratio);
        }
    }
}
