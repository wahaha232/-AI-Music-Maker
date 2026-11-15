using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    internal sealed class WaveformPanel : Control
    {
        public float[] Peaks;
        public double Duration;
        public double Position;
        public bool ThemeDark = true;
        public event Action<double> SeekRequested;

        public WaveformPanel()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(22, 24, 31);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ThemeDark ? Color.FromArgb(18, 19, 24) : Color.FromArgb(250, 250, 252));

            int width = Width;
            int height = Height;
            int center = height / 2;

            if (Peaks == null || Peaks.Length == 0)
            {
                using (Brush hint = new SolidBrush(ThemeDark ? Color.FromArgb(120, 126, 140) : Color.FromArgb(120, 124, 134)))
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    g.DrawString("載入音檔後顯示波形", Font, hint,
                        new RectangleF(0, 0, width, height), format);
                }
                return;
            }

            int playedX = (int)Math.Round(Position * width);
            using (Pen played = new Pen(ThemeDark ? Color.FromArgb(0, 194, 168) : Color.FromArgb(0, 150, 136), 1f))
            using (Pen pending = new Pen(ThemeDark ? Color.FromArgb(108, 92, 231) : Color.FromArgb(0, 122, 255), 1f))
            {
                for (int x = 0; x < width; x++)
                {
                    int index = (int)((long)x * Peaks.Length / Math.Max(1, width));
                    if (index >= Peaks.Length) index = Peaks.Length - 1;
                    int amplitude = (int)Math.Round(Peaks[index] * (height - 6));
                    if (amplitude < 1) amplitude = 1;
                    Pen pen = x <= playedX ? played : pending;
                    g.DrawLine(pen, x, center - amplitude / 2, x, center + amplitude / 2);
                }
                using (Pen cursor = new Pen(ThemeDark ? Color.FromArgb(235, 238, 245) : Color.FromArgb(40, 44, 52), 1f))
                {
                    g.DrawLine(cursor, playedX, 1, playedX, height - 2);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Peaks == null || Width <= 0) return;
            double ratio = Math.Max(0.0, Math.Min(1.0, (double)e.X / Width));
            if (SeekRequested != null) SeekRequested(ratio);
        }
    }
}
