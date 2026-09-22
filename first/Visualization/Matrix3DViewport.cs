using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace first
{
    public sealed class Matrix3DViewport : Control
    {
        public delegate bool ProjectDelegate(Vec3 p, out PointF pt, out double depth);

        public double CameraYaw = 40.0;
        public double CameraPitch = 28.0;
        public double CameraDistance = 2.4;
        public Vec3 CameraTarget = new Vec3(0.5, 0.25, 0.5);
        public double CameraFov = 52.0;
        public ViewportMode Mode = ViewportMode.ShadedWireframe;

        private double[,] _data;
        private int _stepM = 10;
        private int _stepN = 10;
        private string _unitName = "мс";
        private double _minT = 0, _maxT = 1;

        private bool _isMouseDown;
        private MouseButtons _mouseButton;
        private Point _lastMouse;
        private Point _hoverMouse;

        public Matrix3DViewport()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Color.FromArgb(28, 32, 38);
            Cursor = Cursors.Default;
        }

        public void ResetCamera()
        {
            CameraYaw = 40.0;
            CameraPitch = 28.0;
            CameraDistance = 2.4;
            CameraTarget = new Vec3(0.5, 0.25, 0.5);
            Invalidate();
        }

        public void SetData(double[,] times, int stepM, int stepN, string unit)
        {
            _data = times;
            _stepM = stepM > 0 ? stepM : 10;
            _stepN = stepN > 0 ? stepN : 10;
            _unitName = unit ?? "мс";

            if (_data != null)
            {
                _minT = double.MaxValue;
                _maxT = double.MinValue;
                int rLen = _data.GetLength(0);
                int cLen = _data.GetLength(1);
                for (int r = 0; r < rLen; r++)
                {
                    for (int c = 0; c < cLen; c++)
                    {
                        if (_data[r, c] < _minT) _minT = _data[r, c];
                        if (_data[r, c] > _maxT) _maxT = _data[r, c];
                    }
                }
                if (_minT > _maxT) { _minT = 0; _maxT = 1; }
            }
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            _isMouseDown = true;
            _mouseButton = e.Button;
            _lastMouse = e.Location;
            Cursor = (e.Button == MouseButtons.Right || e.Button == MouseButtons.Middle)
                ? Cursors.SizeAll
                : Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Focus();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _isMouseDown = false;
            Cursor = Cursors.Default;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _hoverMouse = e.Location;

            if (_isMouseDown)
            {
                double dx = e.X - _lastMouse.X;
                double dy = e.Y - _lastMouse.Y;

                if (_mouseButton == MouseButtons.Left && (ModifierKeys & Keys.Shift) == 0)
                {
                    // Вращение камеры (Orbit)
                    CameraYaw = (CameraYaw + dx * 0.45) % 360.0;
                    CameraPitch = Math.Clamp(CameraPitch - dy * 0.45, -89.0, 89.0);
                }
                else if (_mouseButton == MouseButtons.Right || _mouseButton == MouseButtons.Middle ||
                         (_mouseButton == MouseButtons.Left && (ModifierKeys & Keys.Shift) != 0))
                {
                    // Панорамирование (Pan)
                    double radYaw = CameraYaw * Math.PI / 180.0;
                    double radPitch = CameraPitch * Math.PI / 180.0;
                    Vec3 offset = new Vec3(
                        CameraDistance * Math.Cos(radPitch) * Math.Sin(radYaw),
                        CameraDistance * Math.Sin(radPitch),
                        CameraDistance * Math.Cos(radPitch) * Math.Cos(radYaw)
                    );
                    Vec3 eye = CameraTarget + offset;
                    Vec3 forward = (CameraTarget - eye).Normalized();
                    Vec3 worldUp = Math.Abs(forward.Y) > 0.999 ? new Vec3(0, 0, 1) : new Vec3(0, 1, 0);
                    Vec3 right = forward.Cross(worldUp).Normalized();
                    Vec3 up = right.Cross(forward).Normalized();

                    double panSpeed = CameraDistance * 0.0018;
                    CameraTarget = CameraTarget + (-right * dx + up * dy) * panSpeed;
                }

                _lastMouse = e.Location;
                Invalidate();
            }
            else
            {
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            double zoomFactor = e.Delta > 0 ? 0.88 : 1.14;
            CameraDistance = Math.Clamp(CameraDistance * zoomFactor, 0.25, 25.0);
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.W:
                case Keys.A:
                case Keys.S:
                case Keys.D:
                case Keys.Q:
                case Keys.E:
                case Keys.Space:
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            double radYaw = CameraYaw * Math.PI / 180.0;
            double radPitch = CameraPitch * Math.PI / 180.0;
            Vec3 offset = new Vec3(
                CameraDistance * Math.Cos(radPitch) * Math.Sin(radYaw),
                CameraDistance * Math.Sin(radPitch),
                CameraDistance * Math.Cos(radPitch) * Math.Cos(radYaw)
            );
            Vec3 eye = CameraTarget + offset;
            Vec3 forward = (CameraTarget - eye).Normalized();
            Vec3 groundFwd = new Vec3(forward.X, 0, forward.Z).Normalized();
            Vec3 worldUp = Math.Abs(forward.Y) > 0.999 ? new Vec3(0, 0, 1) : new Vec3(0, 1, 0);
            Vec3 right = forward.Cross(worldUp).Normalized();
            Vec3 groundRight = new Vec3(right.X, 0, right.Z).Normalized();

            double moveSpeed = CameraDistance * 0.05;

            switch (e.KeyCode)
            {
                case Keys.W:
                case Keys.Up:
                    CameraTarget = CameraTarget + groundFwd * moveSpeed;
                    Invalidate();
                    break;
                case Keys.S:
                case Keys.Down:
                    CameraTarget = CameraTarget - groundFwd * moveSpeed;
                    Invalidate();
                    break;
                case Keys.A:
                case Keys.Left:
                    CameraTarget = CameraTarget - groundRight * moveSpeed;
                    Invalidate();
                    break;
                case Keys.D:
                case Keys.Right:
                    CameraTarget = CameraTarget + groundRight * moveSpeed;
                    Invalidate();
                    break;
                case Keys.Q:
                    CameraTarget = CameraTarget + new Vec3(0, -moveSpeed, 0);
                    Invalidate();
                    break;
                case Keys.E:
                    CameraTarget = CameraTarget + new Vec3(0, moveSpeed, 0);
                    Invalidate();
                    break;
                case Keys.Space:
                    ResetCamera();
                    break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            RenderScene(g, Width, Height);
        }

        private class QuadFace
        {
            public PointF[] Pts;
            public double Depth;
            public Color FillColor;
            public int R, C;
        }

        public static Color GetTurboColor(double t)
        {
            t = Math.Max(0.0, Math.Min(1.0, t));
            if (t < 0.25)
            {
                double k = t / 0.25;
                return Color.FromArgb((int)(30 + 10 * k), (int)(60 + 160 * k), (int)(180 + 75 * k));
            }
            else if (t < 0.5)
            {
                double k = (t - 0.25) / 0.25;
                return Color.FromArgb((int)(40 + 20 * k), (int)(220 - 20 * k), (int)(255 - 190 * k));
            }
            else if (t < 0.75)
            {
                double k = (t - 0.5) / 0.25;
                return Color.FromArgb((int)(60 + 195 * k), (int)(200 + 15 * k), (int)(65 - 50 * k));
            }
            else
            {
                double k = (t - 0.75) / 0.25;
                return Color.FromArgb(255, (int)(215 - 165 * k), (int)(15 + 15 * k));
            }
        }

        public void RenderScene(Graphics g, int width, int height, bool isExport = false)
        {
            using (var bgBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, width, height),
                Color.FromArgb(32, 36, 44),
                Color.FromArgb(18, 20, 24),
                System.Drawing.Drawing2D.LinearGradientMode.Vertical))
            {
                g.FillRectangle(bgBrush, 0, 0, width, height);
            }

            if (_data == null)
            {
                using var f = new Font("Segoe UI", 12f, FontStyle.Bold);
                using var subFont = new Font("Segoe UI", 9.5f);
                using var b = new SolidBrush(Color.FromArgb(220, 225, 235));
                using var gray = new SolidBrush(Color.FromArgb(150, 160, 175));
                g.DrawString("3D Viewport — Матричное умножение T × M × N", f, b, 24, 28);
                g.DrawString("Для построения графика отметьте «Матричное умножение A×B» в таблице слева и нажмите «▶ Запустить эксперимент».",
                    subFont, gray, 24, 56);
                return;
            }

            int rowsM = _data.GetLength(0);
            int colsN = _data.GetLength(1);
            int maxM = rowsM * _stepM;
            int maxN = colsN * _stepN;
            double rangeT = Math.Max(1e-12, _maxT - _minT);

            if (Mode == ViewportMode.Heatmap2D)
            {
                Render2DHeatmap(g, width, height, rowsM, colsN, maxM, maxN);
                return;
            }

            double radYaw = CameraYaw * Math.PI / 180.0;
            double radPitch = CameraPitch * Math.PI / 180.0;
            Vec3 offset = new Vec3(
                CameraDistance * Math.Cos(radPitch) * Math.Sin(radYaw),
                CameraDistance * Math.Sin(radPitch),
                CameraDistance * Math.Cos(radPitch) * Math.Cos(radYaw)
            );
            Vec3 eye = CameraTarget + offset;
            Vec3 forward = (CameraTarget - eye).Normalized();
            Vec3 worldUp = Math.Abs(forward.Y) > 0.999 ? new Vec3(0, 0, 1) : new Vec3(0, 1, 0);
            Vec3 right = forward.Cross(worldUp).Normalized();
            Vec3 up = right.Cross(forward).Normalized();

            double fovRad = CameraFov * Math.PI / 180.0;
            double fLens = (height * 0.5) / Math.Tan(fovRad * 0.5);
            float cx = width * 0.5f;
            float cy = height * 0.5f;

            bool Project(Vec3 p, out PointF pt, out double depth)
            {
                Vec3 d = p - eye;
                double dz = d.Dot(forward);
                depth = dz;
                if (dz <= 0.05)
                {
                    pt = PointF.Empty;
                    return false;
                }
                double dx = d.Dot(right);
                double dy = d.Dot(up);
                float sx = (float)(cx + (dx / dz) * fLens);
                float sy = (float)(cy - (dy / dz) * fLens);
                pt = new PointF(sx, sy);
                return true;
            }

            // 1. Сетка пола (Floor Grid) на Y = 0
            using (var floorPen = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
            {
                int ticks = 5;
                for (int i = 0; i <= ticks; i++)
                {
                    double u = (double)i / ticks;
                    if (Project(new Vec3(u, 0, 0), out PointF p1, out _) &&
                        Project(new Vec3(u, 0, 1), out PointF p2, out _))
                    {
                        g.DrawLine(floorPen, p1, p2);
                    }
                }
                for (int j = 0; j <= ticks; j++)
                {
                    double v = (double)j / ticks;
                    if (Project(new Vec3(0, 0, v), out PointF p1, out _) &&
                        Project(new Vec3(1, 0, v), out PointF p2, out _))
                    {
                        g.DrawLine(floorPen, p1, p2);
                    }
                }
            }

            // 2. Пунктирная линия сброса от максимума к полу
            if (rowsM > 0 && colsN > 0)
            {
                double hMax = (_data[rowsM - 1, colsN - 1] - _minT) / rangeT * 0.75;
                if (Project(new Vec3(1, hMax, 1), out PointF topPt, out _) &&
                    Project(new Vec3(1, 0, 1), out PointF btmPt, out _))
                {
                    using var dropPen = new Pen(Color.FromArgb(160, 231, 76, 60), 1.2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                    g.DrawLine(dropPen, topPt, btmPt);
                }
            }

            // 3. Расчёт и сортировка полигонов
            var quads = new List<QuadFace>();
            Vec3 lightDir = new Vec3(0.4, 0.9, 0.5).Normalized();

            for (int r = 0; r < rowsM - 1; r++)
            {
                double u0 = (double)r / (rowsM - 1);
                double u1 = (double)(r + 1) / (rowsM - 1);
                for (int c = 0; c < colsN - 1; c++)
                {
                    double v0 = (double)c / (colsN - 1);
                    double v1 = (double)(c + 1) / (colsN - 1);

                    double h00 = (_data[r, c] - _minT) / rangeT * 0.75;
                    double h10 = (_data[r + 1, c] - _minT) / rangeT * 0.75;
                    double h11 = (_data[r + 1, c + 1] - _minT) / rangeT * 0.75;
                    double h01 = (_data[r, c + 1] - _minT) / rangeT * 0.75;

                    Vec3 p00 = new Vec3(u0, h00, v0);
                    Vec3 p10 = new Vec3(u1, h10, v0);
                    Vec3 p11 = new Vec3(u1, h11, v1);
                    Vec3 p01 = new Vec3(u0, h01, v1);

                    if (Project(p00, out PointF s00, out double d00) &&
                        Project(p10, out PointF s10, out double d10) &&
                        Project(p11, out PointF s11, out double d11) &&
                        Project(p01, out PointF s01, out double d01))
                    {
                        Vec3 centroid = (p00 + p10 + p11 + p01) * 0.25;
                        double depth = (centroid - eye).Dot(forward);

                        Vec3 n1 = (p10 - p00).Cross(p01 - p00).Normalized();
                        if (n1.Y < 0) n1 = n1 * -1;
                        double diff = Math.Clamp(Math.Abs(n1.Dot(lightDir)), 0.35, 1.0);
                        double avgH = (h00 + h10 + h11 + h01) * 0.25 / 0.75;
                        Color baseCol = GetTurboColor(avgH);
                        Color litCol = Color.FromArgb(
                            Math.Clamp((int)(baseCol.R * diff), 0, 255),
                            Math.Clamp((int)(baseCol.G * diff), 0, 255),
                            Math.Clamp((int)(baseCol.B * diff), 0, 255)
                        );

                        quads.Add(new QuadFace {
                            Pts = new[] { s00, s10, s11, s01 },
                            Depth = depth,
                            FillColor = litCol,
                            R = r, C = c
                        });
                    }
                }
            }

            quads.Sort((a, b) => b.Depth.CompareTo(a.Depth));

            if (Mode == ViewportMode.ShadedWireframe)
            {
                using var wirePen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f);
                foreach (var q in quads)
                {
                    using var b = new SolidBrush(q.FillColor);
                    g.FillPolygon(b, q.Pts);
                    g.DrawPolygon(wirePen, q.Pts);
                }
            }
            else if (Mode == ViewportMode.WireframeOnly)
            {
                foreach (var q in quads)
                {
                    using var p = new Pen(q.FillColor, 1.4f);
                    g.DrawPolygon(p, q.Pts);
                }
            }

            // 4. Поиск ближайшей точки под курсором
            (int r, int c)? closestVertex = null;
            double minDistSq = 20 * 20;
            PointF hoverScreenPt = PointF.Empty;

            using var fontTicks = new Font("Segoe UI", 8f);
            using var fontAxes = new Font("Segoe UI", 9.5f, FontStyle.Bold);

            for (int r = 0; r < rowsM; r++)
            {
                double u = rowsM > 1 ? (double)r / (rowsM - 1) : 0;
                for (int c = 0; c < colsN; c++)
                {
                    double v = colsN > 1 ? (double)c / (colsN - 1) : 0;
                    double h = (_data[r, c] - _minT) / rangeT * 0.75;
                    if (Project(new Vec3(u, h, v), out PointF pt, out _))
                    {
                        if (Mode == ViewportMode.PointCloud)
                        {
                            Color nodeCol = GetTurboColor(h / 0.75);
                            using var b = new SolidBrush(nodeCol);
                            g.FillEllipse(b, pt.X - 3.5f, pt.Y - 3.5f, 7f, 7f);
                        }

                        if (!isExport)
                        {
                            double dsq = (pt.X - _hoverMouse.X) * (pt.X - _hoverMouse.X) +
                                         (pt.Y - _hoverMouse.Y) * (pt.Y - _hoverMouse.Y);
                            if (dsq < minDistSq)
                            {
                                minDistSq = dsq;
                                closestVertex = (r, c);
                                hoverScreenPt = pt;
                            }
                        }
                    }
                }
            }

            // 5. Координатные оси (T, M, N)
            DrawAxes(g, Project, width, height, maxM, maxN, rangeT, fontAxes, fontTicks);

            // 6. Подсветка точки под курсором и всплывающий инспектор
            if (closestVertex.HasValue && !isExport)
            {
                int hr = closestVertex.Value.r;
                int hc = closestVertex.Value.c;
                int mVal = (hr + 1) * _stepM;
                int nVal = (hc + 1) * _stepN;
                double tVal = _data[hr, hc];
                long ops = (long)mVal * mVal * nVal;

                using (var haloPen = new Pen(Color.FromArgb(255, 255, 255, 255), 2.5f))
                {
                    g.DrawEllipse(haloPen, hoverScreenPt.X - 7, hoverScreenPt.Y - 7, 14, 14);
                }
                using (var centerDot = new SolidBrush(Color.FromArgb(255, 231, 76, 60)))
                {
                    g.FillEllipse(centerDot, hoverScreenPt.X - 3, hoverScreenPt.Y - 3, 6, 6);
                }

                DrawInspectorTooltip(g, hoverScreenPt, mVal, nVal, tVal, ops);
            }

            // 7. 3D Orientation Gizmo
            DrawUnityGizmo(g, width, right, up);

            // 8. HUD оверлей
            DrawHUD(g, width, height, rowsM, colsN, maxM, maxN);
        }

        private void DrawAxes(
            Graphics g,
            ProjectDelegate project,
            int width,
            int height,
            int maxM,
            int maxN,
            double rangeT,
            Font fontAxes,
            Font fontTicks)
        {
            if (!project(new Vec3(0, 0, 0), out var p0, out _)) return;

            // Ось T (Red, Up)
            if (project(new Vec3(0, 0.88, 0), out var ptEnd, out _))
            {
                using var penT = new Pen(Color.FromArgb(231, 76, 60), 2.5f);
                using var brushT = new SolidBrush(Color.FromArgb(231, 76, 60));
                g.DrawLine(penT, p0, ptEnd);
                DrawArrowHead(g, p0, ptEnd, penT, brushT);
                g.DrawString($"Ось T ({_unitName})", fontAxes, brushT, ptEnd.X - 10, ptEnd.Y - 22);

                for (int k = 1; k <= 4; k++)
                {
                    double h = (double)k / 4.0 * 0.75;
                    double valT = _minT + (double)k / 4.0 * rangeT;
                    if (project(new Vec3(0, h, 0), out var pTick, out _) &&
                        project(new Vec3(-0.03, h, 0), out var pTickOut, out _))
                    {
                        g.DrawLine(penT, pTick, pTickOut);
                        g.DrawString($"{valT:F2}", fontTicks, brushT, pTickOut.X - 35, pTickOut.Y - 7);
                    }
                }
            }

            // Ось M (Green, Right)
            if (project(new Vec3(1.14, 0, 0), out var pmEnd, out _))
            {
                using var penM = new Pen(Color.FromArgb(46, 204, 113), 2.5f);
                using var brushM = new SolidBrush(Color.FromArgb(46, 204, 113));
                g.DrawLine(penM, p0, pmEnd);
                DrawArrowHead(g, p0, pmEnd, penM, brushM);
                g.DrawString("Ось M (строки A)", fontAxes, brushM, pmEnd.X + 8, pmEnd.Y - 8);

                for (int k = 1; k <= 5; k++)
                {
                    double u = (double)k / 5.0;
                    int valM = (int)Math.Round(u * maxM);
                    if (project(new Vec3(u, 0, 0), out var pTick, out _) &&
                        project(new Vec3(u, 0, -0.04), out var pTickOut, out _))
                    {
                        g.DrawLine(penM, pTick, pTickOut);
                        g.DrawString($"{valM}", fontTicks, brushM, pTickOut.X - 10, pTickOut.Y + 4);
                    }
                }
            }

            // Ось N (Blue, Forward)
            if (project(new Vec3(0, 0, 1.14), out var pnEnd, out _))
            {
                using var penN = new Pen(Color.FromArgb(52, 152, 219), 2.5f);
                using var brushN = new SolidBrush(Color.FromArgb(52, 152, 219));
                g.DrawLine(penN, p0, pnEnd);
                DrawArrowHead(g, p0, pnEnd, penN, brushN);
                g.DrawString("Ось N (столбцы A)", fontAxes, brushN, pnEnd.X - 120, pnEnd.Y + 6);

                for (int k = 1; k <= 5; k++)
                {
                    double v = (double)k / 5.0;
                    int valN = (int)Math.Round(v * maxN);
                    if (project(new Vec3(0, 0, v), out var pTick, out _) &&
                        project(new Vec3(-0.04, 0, v), out var pTickOut, out _))
                    {
                        g.DrawLine(penN, pTick, pTickOut);
                        g.DrawString($"{valN}", fontTicks, brushN, pTickOut.X - 28, pTickOut.Y - 7);
                    }
                }
            }
        }

        private static void DrawArrowHead(Graphics g, PointF pFrom, PointF pTo, Pen pen, Brush brush)
        {
            float dx = pTo.X - pFrom.X;
            float dy = pTo.Y - pFrom.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return;
            float ux = dx / len;
            float uy = dy / len;
            float size = 9f;
            float width = 5f;

            PointF tip = pTo;
            PointF b1 = new PointF(pTo.X - ux * size + uy * width, pTo.Y - uy * size - ux * width);
            PointF b2 = new PointF(pTo.X - ux * size - uy * width, pTo.Y - uy * size + ux * width);

            g.FillPolygon(brush, new[] { tip, b1, b2 });
        }

        private void DrawUnityGizmo(Graphics g, int width, Vec3 right, Vec3 up)
        {
            float gx = width - 65;
            float gy = 65;

            using (var bgBrush = new SolidBrush(Color.FromArgb(160, 24, 28, 34)))
            using (var borderPen = new Pen(Color.FromArgb(60, 70, 85), 1.5f))
            {
                g.FillEllipse(bgBrush, gx - 36, gy - 36, 72, 72);
                g.DrawEllipse(borderPen, gx - 36, gy - 36, 72, 72);
            }

            using var fontGizmo = new Font("Segoe UI", 8f, FontStyle.Bold);

            void DrawAxisPin(Vec3 dir, string label, Color col)
            {
                double ax = dir.Dot(right);
                double ay = dir.Dot(up);
                float ex = (float)(gx + ax * 26);
                float ey = (float)(gy - ay * 26);

                using var p = new Pen(col, 2.2f);
                using var b = new SolidBrush(col);
                using var textB = new SolidBrush(Color.White);
                g.DrawLine(p, gx, gy, ex, ey);
                g.FillEllipse(b, ex - 6, ey - 6, 12, 12);
                g.DrawString(label, fontGizmo, textB, ex - 4.5f, ey - 5.5f);
            }

            DrawAxisPin(new Vec3(0, 1, 0), "T", Color.FromArgb(231, 76, 60));
            DrawAxisPin(new Vec3(1, 0, 0), "M", Color.FromArgb(46, 204, 113));
            DrawAxisPin(new Vec3(0, 0, 1), "N", Color.FromArgb(52, 152, 219));

            using var centerB = new SolidBrush(Color.FromArgb(200, 200, 200));
            g.FillEllipse(centerB, gx - 3, gy - 3, 6, 6);
        }

        private void DrawInspectorTooltip(Graphics g, PointF pt, int m, int n, double tVal, long ops)
        {
            using var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var bodyFont = new Font("Segoe UI", 8.5f);

            string line1 = $"M = {m},  N = {n}";
            string line2 = $"Время T = {Bench.FormatTime(tVal)}";
            string line3 = $"A({m}×{n}) × B({n}×{m}) → C({m}×{m})";
            string line4 = $"O(M²·N) = {ops:N0} оп.";

            int boxW = 205;
            int boxH = 80;
            float bx = pt.X + 16;
            float by = pt.Y - boxH / 2f;
            if (bx + boxW > Width - 10) bx = pt.X - boxW - 16;
            if (by < 10) by = 10;
            if (by + boxH > Height - 10) by = Height - boxH - 10;

            using (var shadowBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
                g.FillRectangle(shadowBrush, bx + 3, by + 3, boxW, boxH);

            using (var boxBrush = new SolidBrush(Color.FromArgb(240, 26, 30, 36)))
            using (var borderPen = new Pen(Color.FromArgb(231, 76, 60), 1.5f))
            {
                g.FillRectangle(boxBrush, bx, by, boxW, boxH);
                g.DrawRectangle(borderPen, bx, by, boxW, boxH);
            }

            using var whiteBrush = new SolidBrush(Color.White);
            using var textM = new SolidBrush(Color.FromArgb(46, 204, 113));
            using var textT = new SolidBrush(Color.FromArgb(241, 196, 15));
            using var grayBrush = new SolidBrush(Color.FromArgb(170, 180, 195));

            g.DrawString(line1, titleFont, textM, bx + 8, by + 6);
            g.DrawString(line2, titleFont, textT, bx + 8, by + 24);
            g.DrawString(line3, bodyFont, whiteBrush, bx + 8, by + 42);
            g.DrawString(line4, bodyFont, grayBrush, bx + 8, by + 58);
        }

        private void DrawHUD(Graphics g, int width, int height, int rowsM, int colsN, int maxM, int maxN)
        {
            using var boldFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var smallFont = new Font("Segoe UI", 8.2f);
            using var whiteBrush = new SolidBrush(Color.FromArgb(230, 235, 245));
            using var goldBrush = new SolidBrush(Color.FromArgb(241, 196, 15));
            using var cyanBrush = new SolidBrush(Color.FromArgb(52, 152, 219));
            using var bgBadge = new SolidBrush(Color.FromArgb(190, 20, 24, 30));
            using var borderBadge = new Pen(Color.FromArgb(50, 60, 75), 1f);

            // Top-left HUD badge
            string hudTitle = "3D Viewport — Матричное умножение T × M × N";
            string hudCam = $"Камера: Yaw = {CameraYaw:F1}°,  Pitch = {CameraPitch:F1}°,  Дистанция = {CameraDistance:F2} | Режим: {Mode}";
            string hudStats = $"Размерности: M_max = {maxM}, N_max = {maxN}  |  Сетка: {rowsM}×{colsN}  |  Max T = {_maxT:F3} {_unitName}";

            g.FillRectangle(bgBadge, 12, 12, 450, 64);
            g.DrawRectangle(borderBadge, 12, 12, 450, 64);
            g.DrawString(hudTitle, boldFont, goldBrush, 20, 16);
            g.DrawString(hudCam, smallFont, cyanBrush, 20, 35);
            g.DrawString(hudStats, smallFont, whiteBrush, 20, 52);

            // Bottom-left Controls helper badge
            int ctrlW = 490;
            int ctrlH = 76;
            float cy = height - ctrlH - 12;
            g.FillRectangle(bgBadge, 12, cy, ctrlW, ctrlH);
            g.DrawRectangle(borderBadge, 12, cy, ctrlW, ctrlH);

            g.DrawString("▶ Управление камерой (Unity Scene View):", boldFont, goldBrush, 20, cy + 5);
            g.DrawString("• ЛКМ + Перетаскивание: Вращение камеры вокруг объекта (Orbit)", smallFont, whiteBrush, 20, cy + 22);
            g.DrawString("• ПКМ / СКМ + Перетаскивание: Сдвиг сцены (Pan)  |  Колёсико: Масштаб (Zoom)", smallFont, whiteBrush, 20, cy + 38);
            g.DrawString("• Клавиши WASD / QE: Полёт по сцене  |  Пробел: Сбросить камеру", smallFont, whiteBrush, 20, cy + 54);
        }

        private void Render2DHeatmap(Graphics g, int width, int height, int rowsM, int colsN, int maxM, int maxN)
        {
            int marginX = 80;
            int marginY = 80;
            int plotW = width - marginX * 2 - 40;
            int plotH = height - marginY * 2;
            if (plotW <= 10 || plotH <= 10) return;

            double rangeT = Math.Max(1e-12, _maxT - _minT);
            float cellW = (float)plotW / rowsM;
            float cellH = (float)plotH / colsN;

            using var fontTitle = new Font("Segoe UI", 11f, FontStyle.Bold);
            using var fontLabel = new Font("Segoe UI", 9f);
            using var fontTick = new Font("Segoe UI", 8f);
            using var whiteBrush = new SolidBrush(Color.White);
            using var linePen = new Pen(Color.FromArgb(40, 48, 60), 1f);

            g.DrawString($"Тепловая карта (Heatmap 2D): T(M, N) от {_minT:F2} до {_maxT:F2} {_unitName}", fontTitle, whiteBrush, marginX, 25);

            for (int r = 0; r < rowsM; r++)
            {
                for (int c = 0; c < colsN; c++)
                {
                    float x = marginX + r * cellW;
                    float y = marginY + (colsN - 1 - c) * cellH;
                    double h = (_data[r, c] - _minT) / rangeT;
                    Color col = GetTurboColor(h);
                    using var b = new SolidBrush(col);
                    g.FillRectangle(b, x, y, cellW, cellH);
                    g.DrawRectangle(linePen, x, y, cellW, cellH);
                }
            }

            using var axisPen = new Pen(Color.FromArgb(200, 200, 200), 2f);
            g.DrawLine(axisPen, marginX, marginY + plotH, marginX + plotW, marginY + plotH);
            g.DrawLine(axisPen, marginX, marginY, marginX, marginY + plotH);

            g.DrawString("Размерность M (строки A)", fontLabel, whiteBrush, marginX + plotW / 2 - 60, marginY + plotH + 25);
            g.DrawString("Размерность N (столбцы A)", fontLabel, whiteBrush, marginX - 70, marginY - 20);

            for (int r = 0; r < rowsM; r += Math.Max(1, rowsM / 5))
            {
                float x = marginX + r * cellW + cellW * 0.5f;
                int m = (r + 1) * _stepM;
                g.DrawString($"{m}", fontTick, whiteBrush, x - 10, marginY + plotH + 5);
            }
            for (int c = 0; c < colsN; c += Math.Max(1, colsN / 5))
            {
                float y = marginY + (colsN - 1 - c) * cellH + cellH * 0.5f;
                int n = (c + 1) * _stepN;
                g.DrawString($"{n}", fontTick, whiteBrush, marginX - 30, y - 6);
            }
        }

        public static Bitmap RenderOffline(
            double[,] times,
            int stepM,
            int stepN,
            string unitName = "мс",
            int width = 1200,
            int height = 750,
            double yaw = 40.0,
            double pitch = 28.0,
            double distance = 2.4,
            ViewportMode mode = ViewportMode.ShadedWireframe)
        {
            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                var vp = new Matrix3DViewport();
                vp.CameraYaw = yaw;
                vp.CameraPitch = pitch;
                vp.CameraDistance = distance;
                vp.Mode = mode;
                vp.SetData(times, stepM, stepN, unitName);
                vp.RenderScene(g, width, height, isExport: true);
            }
            return bmp;
        }

        public Bitmap RenderToBitmap(int width, int height)
        {
            var bmp = new Bitmap(width, height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                RenderScene(g, width, height, isExport: true);
            }
            return bmp;
        }
    }
}
