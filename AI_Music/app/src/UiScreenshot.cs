using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace AIMusicGenerator
{
    /// <summary>Test helper: shows the main window or a dialog with demo content and saves a PNG.</summary>
    internal static class UiScreenshot
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        public static int Run(string target, bool dark, string demoDir, string dialog)
        {
            int code = 1;
            if (dialog == "icons") return IconSheet(target, dark);
            if (dialog == "art") return ArtGenerator.Render(target);
            try
            {
                ThemeManager.SetTheme(dark);
                Form form;
                if (string.IsNullOrEmpty(dialog) || dialog == "main")
                {
                    MainForm main = new MainForm();
                    main.PrepareScreenshot(dark, demoDir);
                    form = main;
                }
                else
                {
                    form = CreateDialog(dialog, demoDir);
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(40, 40);
                }
                form.TopMost = true;
                form.ShowInTaskbar = false;
                Timer timer = new Timer();
                timer.Interval = 1500;
                timer.Tick += delegate
                {
                    timer.Stop();
                    try
                    {
                        Rectangle bounds = form.Bounds;
                        using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
                        {
                            // PrintWindow renders the window itself (works while the session is locked
                            // and never captures other windows or the desktop).
                            bool ok;
                            using (Graphics g = Graphics.FromImage(bitmap))
                            {
                                IntPtr hdc = g.GetHdc();
                                try { ok = PrintWindow(form.Handle, hdc, 2); }
                                finally { g.ReleaseHdc(hdc); }
                            }
                            if (!ok) form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bounds.Size));
                            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)));
                            bitmap.Save(target, ImageFormat.Png);
                        }
                        code = 0;
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex);
                    }
                    form.Close();
                };
                form.Shown += delegate { timer.Start(); };
                Application.Run(form);
            }
            catch (Exception ex)
            {
                File.WriteAllText(target + ".error.txt", ex.ToString());
            }
            return code;
        }

        private static int IconSheet(string target, bool dark)
        {
            ThemeManager.SetTheme(dark);
            int cell = 72, cols = 8;
            System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>(LucideIcons.Paths.Keys);
            names.Sort(StringComparer.Ordinal);
            int rows = (names.Count + cols - 1) / cols;
            using (Bitmap bitmap = new Bitmap(cols * cell, rows * (cell + 16)))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(ThemeManager.CardBack);
                for (int i = 0; i < names.Count; i++)
                {
                    int x = (i % cols) * cell, y = (i / cols) * (cell + 16);
                    IconRenderer.Draw(g, names[i], new RectangleF(x + 18, y + 8, 36, 36), ThemeManager.Text);
                    IconRenderer.Draw(g, names[i], new RectangleF(x + 4, y + 50, 16, 16), ThemeManager.Accent);
                    TextRenderer.DrawText(g, names[i], new Font("Segoe UI", 7F), new Rectangle(x, y + 70, cell, 14),
                        ThemeManager.SubText, TextFormatFlags.HorizontalCenter);
                }
                bitmap.Save(target, ImageFormat.Png);
            }
            return 0;
        }

        private static Form CreateDialog(string name, string demoDir)
        {
            switch (name)
            {
                case "advanced": return new AdvancedDialog("vocals, harsh noise", 2m, 3m, 1.0m, "192k", 48000);
                case "album": return new AlbumDialog("Warm lo-fi chill instrumental, no vocals.", 180);
                case "link": return new LinkPromptDialog();
                case "sd": return new SdDialog(true, "http://127.0.0.1:7860", @"G:\stable-diffusion-webui", "", 20, 512);
                case "media": return new MediaToolsDialog(string.IsNullOrEmpty(demoDir) ? Paths.Generated : demoDir, null);
                case "editor": return new PromptEditorDialog("Warm lo-fi hip hop, dusty boom bap drums, mellow Rhodes piano.");
                case "preview":
                {
                    string[] wavs = Directory.GetFiles(demoDir, "*.wav");
                    string cover = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cover.png");
                    return new PreviewDialog(wavs[0], cover, "");
                }
                default: throw new ArgumentException("unknown dialog: " + name);
            }
        }
    }

    /// <summary>
    /// Renders app.ico and the default cover.png from the Lucide "audio-lines" glyph and the
    /// Catppuccin Mocha palette (both open source), so no third-party bitmap is shipped.
    /// </summary>
    internal static class ArtGenerator
    {
        public static int Render(string folder)
        {
            Directory.CreateDirectory(folder);
            ThemeManager.SetTheme(true);
            WriteIcon(Path.Combine(folder, "app.ico"), new int[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 });
            using (Bitmap cover = RenderCover(1600))
                cover.Save(Path.Combine(folder, "cover.png"), ImageFormat.Png);
            using (Bitmap preview = RenderLogo(512))
                preview.Save(Path.Combine(folder, "app_icon_512.png"), ImageFormat.Png);
            return 0;
        }

        private static Bitmap RenderLogo(int size)
        {
            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float pad = size <= 24 ? 0.5F : size * 0.04F;
                LogoTile.Draw(g, new RectangleF(pad, pad, size - pad * 2 - 1, size - pad * 2 - 1));
            }
            return bitmap;
        }

        private static void WriteIcon(string path, int[] sizes)
        {
            System.Collections.Generic.List<byte[]> images = new System.Collections.Generic.List<byte[]>();
            foreach (int size in sizes)
            {
                using (Bitmap bitmap = RenderLogo(size))
                using (MemoryStream png = new MemoryStream())
                {
                    bitmap.Save(png, ImageFormat.Png);
                    images.Add(png.ToArray());
                }
            }
            using (FileStream stream = File.Create(path))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write((short)0);
                writer.Write((short)1);
                writer.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((short)1);
                    writer.Write((short)32);
                    writer.Write(images[i].Length);
                    writer.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] image in images) writer.Write(image);
            }
        }

        private static Bitmap RenderCover(int size)
        {
            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Rectangle all = new Rectangle(0, 0, size, size);
                using (System.Drawing.Drawing2D.LinearGradientBrush back = new System.Drawing.Drawing2D.LinearGradientBrush(
                    all, Color.FromArgb(0x11, 0x11, 0x1B), Color.FromArgb(0x1E, 0x1E, 0x2E), 60F))
                    g.FillRectangle(back, all);
                Glow(g, size * 0.28F, size * 0.30F, size * 0.55F, Color.FromArgb(0xCB, 0xA6, 0xF7), 70);
                Glow(g, size * 0.74F, size * 0.72F, size * 0.60F, Color.FromArgb(0x89, 0xB4, 0xFA), 60);
                using (Pen ring = new Pen(Color.FromArgb(28, 0xCD, 0xD6, 0xF4), size * 0.004F))
                {
                    for (int i = 1; i <= 4; i++)
                    {
                        float r = size * (0.22F + i * 0.085F);
                        g.DrawEllipse(ring, size / 2F - r, size / 2F - r, r * 2, r * 2);
                    }
                }
                float tile = size * 0.40F;
                RectangleF rect = new RectangleF((size - tile) / 2F, (size - tile) / 2F, tile, tile);
                using (System.Drawing.Drawing2D.GraphicsPath shadow = Shapes.RoundedRect(
                    new RectangleF(rect.X, rect.Y + size * 0.02F, rect.Width, rect.Height), tile * 0.26F))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                    g.FillPath(brush, shadow);
                LogoTile.Draw(g, rect);
            }
            return bitmap;
        }

        private static void Glow(Graphics g, float cx, float cy, float radius, Color color, int alpha)
        {
            using (System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath())
            {
                path.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2);
                using (System.Drawing.Drawing2D.PathGradientBrush brush = new System.Drawing.Drawing2D.PathGradientBrush(path))
                {
                    brush.CenterColor = Color.FromArgb(alpha, color);
                    brush.SurroundColors = new Color[] { Color.FromArgb(0, color) };
                    g.FillPath(brush, path);
                }
            }
        }
    }
}
