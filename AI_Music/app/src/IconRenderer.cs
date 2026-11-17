using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace AIMusicGenerator
{
    /// <summary>
    /// Draws Lucide icons (24x24 stroke icons, see LucideIcons.cs) as vectors, so they stay
    /// sharp at any DPI and follow the theme colour.
    /// </summary>
    internal static class IconRenderer
    {
        private static readonly Dictionary<string, GraphicsPath> Cache = new Dictionary<string, GraphicsPath>();
        private static readonly object Gate = new object();

        public static bool Has(string name)
        {
            return !string.IsNullOrEmpty(name) && LucideIcons.Paths.ContainsKey(name);
        }

        public static void Draw(Graphics g, string name, RectangleF bounds, Color color)
        {
            Draw(g, name, bounds, color, 2F);
        }

        public static void Draw(Graphics g, string name, RectangleF bounds, Color color, float strokeWidth)
        {
            GraphicsPath source = GetPath(name);
            if (source == null || bounds.Width <= 0 || bounds.Height <= 0) return;
            float size = Math.Min(bounds.Width, bounds.Height);
            float scale = size / 24F;
            using (GraphicsPath path = (GraphicsPath)source.Clone())
            using (Matrix matrix = new Matrix())
            {
                matrix.Translate(bounds.X + (bounds.Width - size) / 2F, bounds.Y + (bounds.Height - size) / 2F);
                matrix.Scale(scale, scale);
                path.Transform(matrix);
                SmoothingMode oldMode = g.SmoothingMode;
                PixelOffsetMode oldOffset = g.PixelOffsetMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                using (Pen pen = new Pen(color, Math.Max(1F, strokeWidth * scale)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, path);
                }
                g.SmoothingMode = oldMode;
                g.PixelOffsetMode = oldOffset;
            }
        }

        public static Bitmap ToBitmap(string name, int size, Color color)
        {
            Bitmap bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                Draw(g, name, new RectangleF(0, 0, size, size), color);
            }
            return bitmap;
        }

        public static GraphicsPath GetPath(string name)
        {
            if (!Has(name)) return null;
            lock (Gate)
            {
                GraphicsPath path;
                if (Cache.TryGetValue(name, out path)) return path;
                path = SvgPathParser.Parse(LucideIcons.Paths[name]);
                Cache[name] = path;
                return path;
            }
        }
    }

    /// <summary>Minimal SVG path-data parser (M L H V C S Q T A Z, absolute and relative).</summary>
    internal static class SvgPathParser
    {
        public static GraphicsPath Parse(string data)
        {
            GraphicsPath path = new GraphicsPath(FillMode.Winding);
            Scanner s = new Scanner(data);
            PointF current = PointF.Empty;
            PointF start = PointF.Empty;
            PointF lastControl = PointF.Empty;
            char lastCommand = ' ';
            bool open = false;
            char command = ' ';
            while (true)
            {
                s.SkipSeparators();
                if (s.AtEnd) break;
                if (s.PeekIsElementBreak())
                {
                    // '|' separates SVG elements: each one starts from the origin again,
                    // so a leading relative "m" is measured from (0,0) as in the SVG spec.
                    s.ReadChar();
                    current = start = lastControl = PointF.Empty;
                    lastCommand = command = ' ';
                    open = false;
                    continue;
                }
                if (s.PeekIsCommand()) command = s.ReadChar();
                else if (command == ' ') throw new FormatException("path data must start with a command");
                else if (command == 'M') command = 'L';
                else if (command == 'm') command = 'l';

                bool rel = char.IsLower(command);
                char upper = char.ToUpperInvariant(command);
                switch (upper)
                {
                    case 'M':
                    {
                        PointF p = s.ReadPoint();
                        if (rel && lastCommand != ' ') p = Add(current, p);
                        path.StartFigure();
                        open = false;
                        current = start = p;
                        break;
                    }
                    case 'L':
                    {
                        PointF p = s.ReadPoint();
                        if (rel) p = Add(current, p);
                        path.AddLine(current, p);
                        open = true;
                        current = p;
                        break;
                    }
                    case 'H':
                    {
                        float x = s.ReadNumber();
                        PointF p = new PointF(rel ? current.X + x : x, current.Y);
                        path.AddLine(current, p);
                        open = true;
                        current = p;
                        break;
                    }
                    case 'V':
                    {
                        float y = s.ReadNumber();
                        PointF p = new PointF(current.X, rel ? current.Y + y : y);
                        path.AddLine(current, p);
                        open = true;
                        current = p;
                        break;
                    }
                    case 'C':
                    {
                        PointF c1 = s.ReadPoint(), c2 = s.ReadPoint(), p = s.ReadPoint();
                        if (rel) { c1 = Add(current, c1); c2 = Add(current, c2); p = Add(current, p); }
                        path.AddBezier(current, c1, c2, p);
                        open = true;
                        lastControl = c2;
                        current = p;
                        break;
                    }
                    case 'S':
                    {
                        PointF c2 = s.ReadPoint(), p = s.ReadPoint();
                        if (rel) { c2 = Add(current, c2); p = Add(current, p); }
                        char lc = char.ToUpperInvariant(lastCommand);
                        PointF c1 = (lc == 'C' || lc == 'S') ? Reflect(lastControl, current) : current;
                        path.AddBezier(current, c1, c2, p);
                        open = true;
                        lastControl = c2;
                        current = p;
                        break;
                    }
                    case 'Q':
                    {
                        PointF q = s.ReadPoint(), p = s.ReadPoint();
                        if (rel) { q = Add(current, q); p = Add(current, p); }
                        AddQuadratic(path, current, q, p);
                        open = true;
                        lastControl = q;
                        current = p;
                        break;
                    }
                    case 'T':
                    {
                        PointF p = s.ReadPoint();
                        if (rel) p = Add(current, p);
                        char lc = char.ToUpperInvariant(lastCommand);
                        PointF q = (lc == 'Q' || lc == 'T') ? Reflect(lastControl, current) : current;
                        AddQuadratic(path, current, q, p);
                        open = true;
                        lastControl = q;
                        current = p;
                        break;
                    }
                    case 'A':
                    {
                        float rx = s.ReadNumber(), ry = s.ReadNumber(), angle = s.ReadNumber();
                        bool large = s.ReadFlag(), sweep = s.ReadFlag();
                        PointF p = s.ReadPoint();
                        if (rel) p = Add(current, p);
                        AddArc(path, current, rx, ry, angle, large, sweep, p);
                        open = true;
                        current = p;
                        break;
                    }
                    case 'Z':
                    {
                        if (open) path.CloseFigure();
                        open = false;
                        current = start;
                        break;
                    }
                    default:
                        throw new FormatException("unsupported path command: " + command);
                }
                lastCommand = command;
            }
            return path;
        }

        private static PointF Add(PointF a, PointF b) { return new PointF(a.X + b.X, a.Y + b.Y); }

        private static PointF Reflect(PointF control, PointF around)
        {
            return new PointF(2 * around.X - control.X, 2 * around.Y - control.Y);
        }

        private static void AddQuadratic(GraphicsPath path, PointF p0, PointF q, PointF p)
        {
            PointF c1 = new PointF(p0.X + 2F / 3F * (q.X - p0.X), p0.Y + 2F / 3F * (q.Y - p0.Y));
            PointF c2 = new PointF(p.X + 2F / 3F * (q.X - p.X), p.Y + 2F / 3F * (q.Y - p.Y));
            path.AddBezier(p0, c1, c2, p);
        }

        // SVG endpoint arc -> cubic beziers (SVG 1.1 implementation notes, F.6.5).
        private static void AddArc(GraphicsPath path, PointF p0, float rxIn, float ryIn, float angleDeg,
            bool large, bool sweep, PointF p1)
        {
            if (p0 == p1) return;
            double rx = Math.Abs(rxIn), ry = Math.Abs(ryIn);
            if (rx < 1e-6 || ry < 1e-6) { path.AddLine(p0, p1); return; }
            double phi = angleDeg * Math.PI / 180.0;
            double cos = Math.Cos(phi), sin = Math.Sin(phi);
            double dx = (p0.X - p1.X) / 2.0, dy = (p0.Y - p1.Y) / 2.0;
            double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;
            double lambda = (x1 * x1) / (rx * rx) + (y1 * y1) / (ry * ry);
            if (lambda > 1) { double k = Math.Sqrt(lambda); rx *= k; ry *= k; }
            double num = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
            double den = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            double coef = den == 0 ? 0 : Math.Sqrt(Math.Max(0, num / den));
            if (large == sweep) coef = -coef;
            double cx1 = coef * rx * y1 / ry, cy1 = -coef * ry * x1 / rx;
            double cx = cos * cx1 - sin * cy1 + (p0.X + p1.X) / 2.0;
            double cy = sin * cx1 + cos * cy1 + (p0.Y + p1.Y) / 2.0;
            double theta1 = Angle(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry);
            double delta = Angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);
            if (!sweep && delta > 0) delta -= 2 * Math.PI;
            else if (sweep && delta < 0) delta += 2 * Math.PI;

            int segments = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2) - 1e-9);
            if (segments < 1) segments = 1;
            double step = delta / segments;
            double t = 4.0 / 3.0 * Math.Tan(step / 4);
            PointF from = p0;
            double a = theta1;
            for (int i = 0; i < segments; i++)
            {
                double b = a + step;
                double cosA = Math.Cos(a), sinA = Math.Sin(a), cosB = Math.Cos(b), sinB = Math.Sin(b);
                PointF c1 = Map(cx, cy, rx, ry, cos, sin, cosA - t * sinA, sinA + t * cosA);
                PointF c2 = Map(cx, cy, rx, ry, cos, sin, cosB + t * sinB, sinB - t * cosB);
                PointF to = i == segments - 1 ? p1 : Map(cx, cy, rx, ry, cos, sin, cosB, sinB);
                path.AddBezier(from, c1, c2, to);
                from = to;
                a = b;
            }
        }

        private static PointF Map(double cx, double cy, double rx, double ry, double cos, double sin, double x, double y)
        {
            x *= rx; y *= ry;
            return new PointF((float)(cos * x - sin * y + cx), (float)(sin * x + cos * y + cy));
        }

        private static double Angle(double ux, double uy, double vx, double vy)
        {
            return Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
        }

        private sealed class Scanner
        {
            private readonly string text;
            private int pos;

            public Scanner(string text) { this.text = text ?? ""; }

            public bool AtEnd { get { return pos >= text.Length; } }

            public void SkipSeparators()
            {
                while (pos < text.Length && (char.IsWhiteSpace(text[pos]) || text[pos] == ',')) pos++;
            }

            public bool PeekIsElementBreak()
            {
                return pos < text.Length && text[pos] == '|';
            }

            public bool PeekIsCommand()
            {
                if (pos >= text.Length) return false;
                char c = text[pos];
                return char.IsLetter(c) && c != 'e' && c != 'E';
            }

            public char ReadChar() { return text[pos++]; }

            public bool ReadFlag()
            {
                SkipSeparators();
                if (pos >= text.Length) throw new FormatException("missing arc flag");
                char c = text[pos++];
                if (c != '0' && c != '1') throw new FormatException("invalid arc flag");
                return c == '1';
            }

            public PointF ReadPoint()
            {
                float x = ReadNumber();
                float y = ReadNumber();
                return new PointF(x, y);
            }

            public float ReadNumber()
            {
                SkipSeparators();
                int begin = pos;
                if (pos < text.Length && (text[pos] == '-' || text[pos] == '+')) pos++;
                bool dot = false;
                while (pos < text.Length)
                {
                    char c = text[pos];
                    if (char.IsDigit(c)) { pos++; continue; }
                    if (c == '.' && !dot) { dot = true; pos++; continue; }
                    break;
                }
                if (pos < text.Length && (text[pos] == 'e' || text[pos] == 'E'))
                {
                    pos++;
                    if (pos < text.Length && (text[pos] == '-' || text[pos] == '+')) pos++;
                    while (pos < text.Length && char.IsDigit(text[pos])) pos++;
                }
                if (pos == begin) throw new FormatException("number expected at " + begin);
                return float.Parse(text.Substring(begin, pos - begin), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
