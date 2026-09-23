using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace first
{
    public sealed class Matrix3DViewport : Control
    {
        public delegate bool ProjectDelegate(Vec3 p, out SKPoint pt, out double depth);

        public double CameraYaw = 40.0;
        public double CameraPitch = 28.0;
        public double CameraDistance = 2.4;
        public Vec3 CameraTarget = new Vec3(0.5, 0.25, 0.5);
        public double CameraFov = 52.0;
        public ViewportMode Mode = ViewportMode.ShadedWireframe;
        public bool IsPanMode { get; set; } = false;

        private double[,] _data;
        private int _stepM = 10;
        private int _stepN = 10;
        private string _unitName = "мс";
        private double _minT = 0, _maxT = 1;

        private bool _isMouseDown;
        private bool _isRightOrMiddle;
        private Point _lastMouse;
        private Point _hoverMouse;

        public Matrix3DViewport()
        {
            Focusable = true;
            ClipToBounds = true;
        }

        public void ResetCamera()
        {
            CameraYaw = 40.0;
            CameraPitch = 28.0;
            CameraDistance = 2.4;
            CameraTarget = new Vec3(0.5, 0.25, 0.5);
            InvalidateVisual();
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
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            var props = e.GetCurrentPoint(this).Properties;
            _isMouseDown = true;
            _isRightOrMiddle = props.IsRightButtonPressed || props.IsMiddleButtonPressed;
            _lastMouse = e.GetPosition(this);
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _isMouseDown = false;
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var pos = e.GetPosition(this);

            if (_isMouseDown)
            {
                double dx = pos.X - _lastMouse.X;
                double dy = pos.Y - _lastMouse.Y;

                bool shouldPan = _isRightOrMiddle || (e.KeyModifiers & KeyModifiers.Shift) != 0 || IsPanMode || Mode == ViewportMode.Heatmap2D;
                if (!shouldPan)
                {
                    // Вращение камеры (Orbit)
                    CameraYaw = (CameraYaw + dx * 0.45) % 360.0;
                    CameraPitch = Math.Clamp(CameraPitch - dy * 0.45, -89.0, 89.0);
                }
                else
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

                _lastMouse = pos;
                InvalidateVisual();
                e.Handled = true;
            }
            else
            {
                double d = Math.Abs(pos.X - _hoverMouse.X) + Math.Abs(pos.Y - _hoverMouse.Y);
                if (d > 4.0)
                {
                    _hoverMouse = pos;
                    InvalidateVisual();
                }
            }
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            double zoomFactor = e.Delta.Y > 0 ? 0.88 : 1.14;
            CameraDistance = Math.Clamp(CameraDistance * zoomFactor, 0.25, 25.0);
            InvalidateVisual();
            e.Handled = true;
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

            switch (e.Key)
            {
                case Key.W:
                case Key.Up:
                    CameraTarget = CameraTarget + groundFwd * moveSpeed;
                    InvalidateVisual();
                    break;
                case Key.S:
                case Key.Down:
                    CameraTarget = CameraTarget - groundFwd * moveSpeed;
                    InvalidateVisual();
                    break;
                case Key.A:
                case Key.Left:
                    CameraTarget = CameraTarget - groundRight * moveSpeed;
                    InvalidateVisual();
                    break;
                case Key.D:
                case Key.Right:
                    CameraTarget = CameraTarget + groundRight * moveSpeed;
                    InvalidateVisual();
                    break;
                case Key.Q:
                    CameraTarget = CameraTarget + new Vec3(0, -moveSpeed, 0);
                    InvalidateVisual();
                    break;
                case Key.E:
                    CameraTarget = CameraTarget + new Vec3(0, moveSpeed, 0);
                    InvalidateVisual();
                    break;
                case Key.Space:
                    ResetCamera();
                    break;
            }
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            int width = (int)Bounds.Width;
            int height = (int)Bounds.Height;
            if (width < 10 || height < 10) return;

            var wb = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var fb = wb.Lock())
            {
                var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var surface = SKSurface.Create(info, fb.Address, fb.RowBytes);
                if (surface != null)
                {
                    RenderScene(surface.Canvas, width, height, isExport: false);
                }
            }

            context.DrawImage(wb, new Rect(0, 0, width, height));
        }

        private class QuadFace
        {
            public SKPoint[] Pts;
            public double Depth;
            public SKColor FillColor;
            public int R, C;
        }

        public static SKColor GetTurboColor(double t)
        {
            t = Math.Max(0.0, Math.Min(1.0, t));
            if (t < 0.25)
            {
                double k = t / 0.25;
                return new SKColor((byte)(30 + 10 * k), (byte)(60 + 160 * k), (byte)(180 + 75 * k));
            }
            else if (t < 0.5)
            {
                double k = (t - 0.25) / 0.25;
                return new SKColor((byte)(40 + 20 * k), (byte)(220 - 20 * k), (byte)(255 - 190 * k));
            }
            else if (t < 0.75)
            {
                double k = (t - 0.5) / 0.25;
                return new SKColor((byte)(60 + 195 * k), (byte)(200 + 15 * k), (byte)(65 - 50 * k));
            }
            else
            {
                double k = (t - 0.75) / 0.25;
                return new SKColor(255, (byte)(215 - 165 * k), (byte)(15 + 15 * k));
            }
        }

        public void RenderScene(SKCanvas canvas, int width, int height, bool isExport = false)
        {
            // Background gradient
            using (var paintBg = new SKPaint())
            {
                paintBg.Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(0, height),
                    new[] { new SKColor(32, 36, 44), new SKColor(18, 20, 24) },
                    null,
                    SKShaderTileMode.Clamp
                );
                canvas.DrawRect(0, 0, width, height, paintBg);
            }

            if (_data == null)
            {
                using var pText = new SKPaint { Color = new SKColor(220, 225, 235), TextSize = 18, IsAntialias = true, FakeBoldText = true };
                using var pSub = new SKPaint { Color = new SKColor(150, 160, 175), TextSize = 13, IsAntialias = true };
                canvas.DrawText("3D Движок Viewport — Матричное умножение T × M × N", 24, 38, pText);
                canvas.DrawText("Для отображения отметьте «Матричное умножение A×B» слева и нажмите «▶ ЗАПУСТИТЬ ЗАМЕРЫ».", 24, 66, pSub);
                return;
            }

            int rowsM = _data.GetLength(0);
            int colsN = _data.GetLength(1);
            int maxM = rowsM * _stepM;
            int maxN = colsN * _stepN;
            double rangeT = Math.Max(1e-12, _maxT - _minT);

            if (Mode == ViewportMode.Heatmap2D)
            {
                Render2DHeatmap(canvas, width, height, rowsM, colsN, maxM, maxN);
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

            bool Project(Vec3 p, out SKPoint pt, out double depth)
            {
                Vec3 d = p - eye;
                double dz = d.Dot(forward);
                depth = dz;
                if (dz <= 0.05)
                {
                    pt = SKPoint.Empty;
                    return false;
                }
                double dx = d.Dot(right);
                double dy = d.Dot(up);
                float sx = (float)(cx + (dx / dz) * fLens);
                float sy = (float)(cy - (dy / dz) * fLens);
                pt = new SKPoint(sx, sy);
                return true;
            }

            // 1. Сетка пола (Floor Grid) на Y = 0
            using (var floorPaint = new SKPaint { Color = new SKColor(255, 255, 255, 35), StrokeWidth = 1, IsAntialias = true })
            {
                int ticks = 5;
                for (int i = 0; i <= ticks; i++)
                {
                    double u = (double)i / ticks;
                    if (Project(new Vec3(u, 0, 0), out var p1, out _) &&
                        Project(new Vec3(u, 0, 1), out var p2, out _))
                    {
                        canvas.DrawLine(p1, p2, floorPaint);
                    }
                }
                for (int j = 0; j <= ticks; j++)
                {
                    double v = (double)j / ticks;
                    if (Project(new Vec3(0, 0, v), out var p1, out _) &&
                        Project(new Vec3(1, 0, v), out var p2, out _))
                    {
                        canvas.DrawLine(p1, p2, floorPaint);
                    }
                }
            }

            // 2. Линия сброса от максимальной вершины к полу
            if (rowsM > 0 && colsN > 0)
            {
                double hMax = (_data[rowsM - 1, colsN - 1] - _minT) / rangeT * 0.75;
                if (Project(new Vec3(1, hMax, 1), out var topPt, out _) &&
                    Project(new Vec3(1, 0, 1), out var btmPt, out _))
                {
                    using var dropPaint = new SKPaint
                    {
                        Color = new SKColor(231, 76, 60, 160),
                        StrokeWidth = 1.4f,
                        PathEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0),
                        IsAntialias = true
                    };
                    canvas.DrawLine(topPt, btmPt, dropPaint);
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

                    if (Project(p00, out var s00, out double d00) &&
                        Project(p10, out var s10, out double d10) &&
                        Project(p11, out var s11, out double d11) &&
                        Project(p01, out var s01, out double d01))
                    {
                        Vec3 centroid = (p00 + p10 + p11 + p01) * 0.25;
                        double depth = (centroid - eye).Dot(forward);

                        Vec3 n1 = (p10 - p00).Cross(p01 - p00).Normalized();
                        if (n1.Y < 0) n1 = -n1;
                        double diff = Math.Clamp(Math.Abs(n1.Dot(lightDir)), 0.35, 1.0);
                        double avgH = (h00 + h10 + h11 + h01) * 0.25 / 0.75;
                        SKColor baseCol = GetTurboColor(avgH);
                        SKColor litCol = new SKColor(
                            (byte)Math.Clamp((int)(baseCol.Red * diff), 0, 255),
                            (byte)Math.Clamp((int)(baseCol.Green * diff), 0, 255),
                            (byte)Math.Clamp((int)(baseCol.Blue * diff), 0, 255)
                        );

                        quads.Add(new QuadFace
                        {
                            Pts = new[] { s00, s10, s11, s01 },
                            Depth = depth,
                            FillColor = litCol,
                            R = r,
                            C = c
                        });
                    }
                }
            }

            quads.Sort((a, b) => b.Depth.CompareTo(a.Depth));

            using var pFill = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
            using var pWire = new SKPaint { Style = SKPaintStyle.Stroke, Color = new SKColor(255, 255, 255, 70), StrokeWidth = 1f, IsAntialias = true };

            if (Mode == ViewportMode.ShadedWireframe)
            {
                foreach (var q in quads)
                {
                    using var path = new SKPath();
                    path.MoveTo(q.Pts[0]);
                    path.LineTo(q.Pts[1]);
                    path.LineTo(q.Pts[2]);
                    path.LineTo(q.Pts[3]);
                    path.Close();

                    pFill.Color = q.FillColor;
                    canvas.DrawPath(path, pFill);
                    canvas.DrawPath(path, pWire);
                }
            }
            else if (Mode == ViewportMode.WireframeOnly)
            {
                pWire.Color = new SKColor(96, 165, 250);
                pWire.StrokeWidth = 1.3f;
                foreach (var q in quads)
                {
                    using var path = new SKPath();
                    path.MoveTo(q.Pts[0]);
                    path.LineTo(q.Pts[1]);
                    path.LineTo(q.Pts[2]);
                    path.LineTo(q.Pts[3]);
                    path.Close();
                    canvas.DrawPath(path, pWire);
                }
            }

            // 4. Вершины и поиск ближайшей точки под курсором
            (int r, int c)? closestVertex = null;
            double minDistSq = 20 * 20;
            SKPoint hoverScreenPt = SKPoint.Empty;

            using var pNode = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };

            for (int r = 0; r < rowsM; r++)
            {
                double u = rowsM > 1 ? (double)r / (rowsM - 1) : 0;
                for (int c = 0; c < colsN; c++)
                {
                    double v = colsN > 1 ? (double)c / (colsN - 1) : 0;
                    double h = (_data[r, c] - _minT) / rangeT * 0.75;
                    if (Project(new Vec3(u, h, v), out var pt, out _))
                    {
                        if (Mode == ViewportMode.PointCloud)
                        {
                            pNode.Color = GetTurboColor(h / 0.75);
                            canvas.DrawCircle(pt, 3.5f, pNode);
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
            DrawAxes(canvas, Project, width, height, maxM, maxN, rangeT);

            // 6. Подсветка точки под курсором и всплывающий инспектор
            if (closestVertex.HasValue && !isExport)
            {
                int hr = closestVertex.Value.r;
                int hc = closestVertex.Value.c;
                int mVal = (hr + 1) * _stepM;
                int nVal = (hc + 1) * _stepN;
                double tVal = _data[hr, hc];
                long ops = (long)mVal * mVal * nVal;

                using var pHalo = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.White, StrokeWidth = 2.5f, IsAntialias = true };
                canvas.DrawCircle(hoverScreenPt, 7f, pHalo);

                using var pDot = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(231, 76, 60), IsAntialias = true };
                canvas.DrawCircle(hoverScreenPt, 4.5f, pDot);

                // Карточка инспектора
                float boxW = 320, boxH = 105;
                float bx = hoverScreenPt.X + 16;
                float by = hoverScreenPt.Y - boxH - 12;
                if (bx + boxW > width - 10) bx = hoverScreenPt.X - boxW - 16;
                if (by < 10) by = hoverScreenPt.Y + 16;

                using var pCardBg = new SKPaint { Color = new SKColor(20, 24, 34, 245), Style = SKPaintStyle.Fill };
                using var pCardBorder = new SKPaint { Color = new SKColor(96, 165, 250), Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, IsAntialias = true };
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(bx, by, bx + boxW, by + boxH), 8, 8), pCardBg);
                canvas.DrawRoundRect(new SKRoundRect(new SKRect(bx, by, bx + boxW, by + boxH), 8, 8), pCardBorder);

                using var pTitle = new SKPaint { Color = new SKColor(251, 191, 36), TextSize = 13, FakeBoldText = true, IsAntialias = true };
                using var pBody = new SKPaint { Color = SKColors.White, TextSize = 12, IsAntialias = true };
                using var pMuted = new SKPaint { Color = new SKColor(156, 163, 175), TextSize = 11, IsAntialias = true };

                string tFmt = (tVal >= 100) ? $"{tVal:F1} {_unitName}" : (tVal >= 1.0 ? $"{tVal:F3} {_unitName}" : $"{tVal:F4} {_unitName}");
                canvas.DrawText($"Узел матрицы: M = {mVal}, N = {nVal}", bx + 12, by + 20, pTitle);
                canvas.DrawText($"• Время T: {tFmt}", bx + 12, by + 40, pBody);
                canvas.DrawText($"• Сложность: {ops:N0} операций (M²·N)", bx + 12, by + 60, pBody);
                canvas.DrawText($"• Сетка: строка {hr + 1}/{rowsM}, столбец {hc + 1}/{colsN}", bx + 12, by + 80, pMuted);
            }

            // 7. HUD
            DrawHUD(canvas, width, height, rowsM, colsN, maxM, maxN);
        }

        private void DrawAxes(
            SKCanvas canvas,
            ProjectDelegate project,
            int width,
            int height,
            int maxM,
            int maxN,
            double rangeT)
        {
            if (!project(new Vec3(0, 0, 0), out var p0, out _)) return;

            using var pFontAxes = new SKPaint { TextSize = 14, FakeBoldText = true, IsAntialias = true };
            using var pFontTicks = new SKPaint { TextSize = 12, FakeBoldText = true, IsAntialias = true };

            // Ось T (Красная, Вверх)
            if (project(new Vec3(0, 0.88, 0), out var ptEnd, out _))
            {
                using var penT = new SKPaint { Color = new SKColor(231, 76, 60), StrokeWidth = 2.5f, IsAntialias = true };
                canvas.DrawLine(p0, ptEnd, penT);
                DrawArrowHead(canvas, p0, ptEnd, penT);
                pFontAxes.Color = new SKColor(231, 76, 60);
                canvas.DrawText($"Ось T ({_unitName})", ptEnd.X - 15, ptEnd.Y - 14, pFontAxes);

                for (int k = 1; k <= 4; k++)
                {
                    double h = (double)k / 4.0 * 0.75;
                    double valT = _minT + (double)k / 4.0 * rangeT;
                    if (project(new Vec3(0, h, 0), out var pTick, out _) &&
                        project(new Vec3(-0.03, h, 0), out var pTickOut, out _))
                    {
                        canvas.DrawLine(pTick, pTickOut, penT);
                        pFontTicks.Color = new SKColor(231, 76, 60);
                        string valTStr = rangeT >= 10 ? $"{valT:F1}" : (rangeT >= 1.0 ? $"{valT:F2}" : $"{valT:F3}");
                        canvas.DrawText(valTStr, pTickOut.X - 44, pTickOut.Y + 4, pFontTicks);
                    }
                }
            }

            // Ось M (Зелёная, Вправо)
            if (project(new Vec3(1.14, 0, 0), out var pmEnd, out _))
            {
                using var penM = new SKPaint { Color = new SKColor(46, 204, 113), StrokeWidth = 2.5f, IsAntialias = true };
                canvas.DrawLine(p0, pmEnd, penM);
                DrawArrowHead(canvas, p0, pmEnd, penM);
                pFontAxes.Color = new SKColor(46, 204, 113);
                canvas.DrawText("Ось M (строки A)", pmEnd.X + 8, pmEnd.Y + 5, pFontAxes);

                for (int k = 1; k <= 5; k++)
                {
                    double u = (double)k / 5.0;
                    int valM = (int)Math.Round(u * maxM);
                    if (project(new Vec3(u, 0, 0), out var pTick, out _) &&
                        project(new Vec3(u, 0, -0.04), out var pTickOut, out _))
                    {
                        canvas.DrawLine(pTick, pTickOut, penM);
                        pFontTicks.Color = new SKColor(46, 204, 113);
                        canvas.DrawText($"{valM}", pTickOut.X - 10, pTickOut.Y + 16, pFontTicks);
                    }
                }
            }

            // Ось N (Синяя, Вперёд)
            if (project(new Vec3(0, 0, 1.14), out var pnEnd, out _))
            {
                using var penN = new SKPaint { Color = new SKColor(52, 152, 219), StrokeWidth = 2.5f, IsAntialias = true };
                canvas.DrawLine(p0, pnEnd, penN);
                DrawArrowHead(canvas, p0, pnEnd, penN);
                pFontAxes.Color = new SKColor(52, 152, 219);
                canvas.DrawText("Ось N (столбцы A)", pnEnd.X - 130, pnEnd.Y + 18, pFontAxes);

                for (int k = 1; k <= 5; k++)
                {
                    double v = (double)k / 5.0;
                    int valN = (int)Math.Round(v * maxN);
                    if (project(new Vec3(0, 0, v), out var pTick, out _) &&
                        project(new Vec3(-0.04, 0, v), out var pTickOut, out _))
                    {
                        canvas.DrawLine(pTick, pTickOut, penN);
                        pFontTicks.Color = new SKColor(52, 152, 219);
                        canvas.DrawText($"{valN}", pTickOut.X - 32, pTickOut.Y + 4, pFontTicks);
                    }
                }
            }
        }

        private static void DrawArrowHead(SKCanvas canvas, SKPoint pFrom, SKPoint pTo, SKPaint paint)
        {
            float dx = pTo.X - pFrom.X;
            float dy = pTo.Y - pFrom.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f) return;

            float ux = dx / len;
            float uy = dy / len;
            float arrowSize = 10f;
            float perpX = -uy * (arrowSize * 0.45f);
            float perpY = ux * (arrowSize * 0.45f);

            var a1 = new SKPoint(pTo.X - ux * arrowSize + perpX, pTo.Y - uy * arrowSize + perpY);
            var a2 = new SKPoint(pTo.X - ux * arrowSize - perpX, pTo.Y - uy * arrowSize - perpY);

            using var path = new SKPath();
            path.MoveTo(pTo);
            path.LineTo(a1);
            path.LineTo(a2);
            path.Close();

            using var fill = new SKPaint { Color = paint.Color, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawPath(path, fill);
        }

        private void DrawHUD(SKCanvas canvas, int width, int height, int rowsM, int colsN, int maxM, int maxN)
        {
            // Верхняя плашка параметров
            float infoW = 390, infoH = 88;
            using var bgBadge = new SKPaint { Color = new SKColor(20, 24, 34, 235), Style = SKPaintStyle.Fill };
            using var borderBadge = new SKPaint { Color = new SKColor(60, 70, 85), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };

            canvas.DrawRoundRect(new SKRoundRect(new SKRect(12, 12, 12 + infoW, 12 + infoH), 6, 6), bgBadge);
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(12, 12, 12 + infoW, 12 + infoH), 6, 6), borderBadge);

            using var pTitle = new SKPaint { Color = new SKColor(251, 191, 36), TextSize = 13, FakeBoldText = true, IsAntialias = true };
            using var pWhite = new SKPaint { Color = SKColors.White, TextSize = 12, IsAntialias = true };
            using var pCyan = new SKPaint { Color = new SKColor(56, 189, 248), TextSize = 12, IsAntialias = true };

            canvas.DrawText("▶ 3D Пространственная сложность T(M, N):", 20, 32, pTitle);
            canvas.DrawText($"• Сетка: {rowsM} × {colsN} узлов (шаг M={_stepM}, N={_stepN})", 20, 52, pWhite);
            string maxTFmt = (_maxT >= 100) ? $"{_maxT:F1} {_unitName}" : (_maxT >= 1.0 ? $"{_maxT:F3} {_unitName}" : $"{_maxT:F4} {_unitName}");
            canvas.DrawText($"• Max M = {maxM}, Max N = {maxN}, T_max = {maxTFmt}", 20, 70, pWhite);
            canvas.DrawText($"• Камера: Yaw={CameraYaw:F0}°, Pitch={CameraPitch:F0}°, Дистанция={CameraDistance:F1}", 20, 88, pCyan);

            // Нижняя плашка подсказки управления
            float ctrlW = 540, ctrlH = 72;
            float cy = height - ctrlH - 12;

            canvas.DrawRoundRect(new SKRoundRect(new SKRect(12, cy, 12 + ctrlW, cy + ctrlH), 6, 6), bgBadge);
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(12, cy, 12 + ctrlW, cy + ctrlH), 6, 6), borderBadge);

            using var pHint = new SKPaint { Color = new SKColor(229, 231, 235), TextSize = 11, IsAntialias = true };
            canvas.DrawText("▶ Управление камерой и просмотр точек:", 20, cy + 20, pTitle);
            canvas.DrawText("• ЛКМ: Вращение камеры (Orbit)  |  ПКМ / Shift+ЛКМ: Перемещение сцены (Pan)", 20, cy + 38, pHint);
            canvas.DrawText("• Колёсико мыши: Масштаб (Zoom)  |  Наведение на узлы: Просмотр точных значений", 20, cy + 54, pHint);
        }

        private void Render2DHeatmap(SKCanvas canvas, int width, int height, int rowsM, int colsN, int maxM, int maxN)
        {
            float padLeft = 80, padBottom = 60, padTop = 50, padRight = 130;
            float plotW = width - padLeft - padRight;
            float plotH = height - padTop - padBottom;
            if (plotW <= 10 || plotH <= 10) return;

            float cellW = plotW / colsN;
            float cellH = plotH / rowsM;
            double rangeT = Math.Max(1e-12, _maxT - _minT);

            using var pCell = new SKPaint { Style = SKPaintStyle.Fill };
            using var pBorder = new SKPaint { Style = SKPaintStyle.Stroke, Color = new SKColor(20, 24, 30), StrokeWidth = 1 };

            for (int r = 0; r < rowsM; r++)
            {
                for (int c = 0; c < colsN; c++)
                {
                    double normT = (_data[r, c] - _minT) / rangeT;
                    pCell.Color = GetTurboColor(normT);

                    float x = padLeft + c * cellW;
                    float y = padTop + (rowsM - 1 - r) * cellH;
                    var rc = new SKRect(x, y, x + cellW, y + cellH);
                    canvas.DrawRect(rc, pCell);
                    canvas.DrawRect(rc, pBorder);
                }
            }

            // Подписи осей сетки
            using var pGridText = new SKPaint { Color = new SKColor(209, 213, 219), TextSize = 11, IsAntialias = true };
            for (int c = 0; c < colsN; c += Math.Max(1, colsN / 5))
            {
                int nVal = (c + 1) * _stepN;
                float x = padLeft + c * cellW + cellW * 0.5f;
                canvas.DrawText($"{nVal}", x - 10, padTop + plotH + 18, pGridText);
            }
            for (int r = 0; r < rowsM; r += Math.Max(1, rowsM / 5))
            {
                int mVal = (r + 1) * _stepM;
                float y = padTop + (rowsM - 1 - r) * cellH + cellH * 0.5f;
                canvas.DrawText($"{mVal}", padLeft - 32, y + 4, pGridText);
            }

            using var pAxisLabel = new SKPaint { Color = new SKColor(156, 163, 175), TextSize = 12, FakeBoldText = true, IsAntialias = true };
            canvas.DrawText("Столбцы N", padLeft + plotW * 0.5f - 30, padTop + plotH + 38, pAxisLabel);
            canvas.DrawText("Строки M", padLeft - 72, padTop - 15, pAxisLabel);

            // Шкала цветов (ColorBar)
            float barX = width - padRight + 25;
            float barY = padTop;
            float barW = 22;
            float barH = plotH;
            int gradSteps = 60;
            float stepH = barH / gradSteps;

            for (int i = 0; i < gradSteps; i++)
            {
                double normT = (double)(gradSteps - 1 - i) / (gradSteps - 1);
                pCell.Color = GetTurboColor(normT);
                canvas.DrawRect(barX, barY + i * stepH, barW, stepH + 1, pCell);
            }

            using var pBarBorder = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.White, StrokeWidth = 1 };
            canvas.DrawRect(barX, barY, barW, barH, pBarBorder);

            using var pText = new SKPaint { Color = SKColors.White, TextSize = 12, FakeBoldText = true, IsAntialias = true };
            string topFmt = (_maxT >= 10) ? $"{_maxT:F1}" : $"{_maxT:F2}";
            string midFmt = ((_minT + _maxT) * 0.5 >= 10) ? $"{(_minT + _maxT) * 0.5:F1}" : $"{(_minT + _maxT) * 0.5:F2}";
            string botFmt = (_minT >= 10) ? $"{_minT:F1}" : $"{_minT:F2}";

            canvas.DrawText($"{topFmt} {_unitName}", barX + barW + 8, barY + 12, pText);
            canvas.DrawText($"{midFmt}", barX + barW + 8, barY + barH * 0.5f + 4, pText);
            canvas.DrawText($"{botFmt}", barX + barW + 8, barY + barH, pText);

            using var pTitle = new SKPaint { Color = new SKColor(251, 191, 36), TextSize = 14, FakeBoldText = true, IsAntialias = true };
            canvas.DrawText($"2D Тепловая карта T(M, N): время в {_unitName}", padLeft, padTop - 16, pTitle);

            // Инспектор точки при наведении на ячейку в 2D Heatmap
            if (_hoverMouse.X >= padLeft && _hoverMouse.X <= padLeft + plotW &&
                _hoverMouse.Y >= padTop && _hoverMouse.Y <= padTop + plotH)
            {
                int hoverC = (int)((_hoverMouse.X - padLeft) / cellW);
                int hoverR = rowsM - 1 - (int)((_hoverMouse.Y - padTop) / cellH);
                if (hoverR >= 0 && hoverR < rowsM && hoverC >= 0 && hoverC < colsN)
                {
                    float hx = padLeft + hoverC * cellW;
                    float hy = padTop + (rowsM - 1 - hoverR) * cellH;
                    using var pHoverBorder = new SKPaint { Style = SKPaintStyle.Stroke, Color = new SKColor(251, 191, 36), StrokeWidth = 2.5f, IsAntialias = true };
                    canvas.DrawRect(new SKRect(hx, hy, hx + cellW, hy + cellH), pHoverBorder);

                    int hmVal = (hoverR + 1) * _stepM;
                    int hnVal = (hoverC + 1) * _stepN;
                    double htVal = _data[hoverR, hoverC];
                    long hops = (long)hmVal * hmVal * hnVal;

                    float boxW = 320, boxH = 105;
                    float bx = (float)_hoverMouse.X + 16;
                    float by = (float)_hoverMouse.Y - boxH - 12;
                    if (bx + boxW > width - 10) bx = (float)_hoverMouse.X - boxW - 16;
                    if (by < 10) by = (float)_hoverMouse.Y + 16;

                    using var pCardBg = new SKPaint { Color = new SKColor(20, 24, 34, 245), Style = SKPaintStyle.Fill };
                    using var pCardBorder = new SKPaint { Color = new SKColor(96, 165, 250), Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f, IsAntialias = true };
                    canvas.DrawRoundRect(new SKRoundRect(new SKRect(bx, by, bx + boxW, by + boxH), 8, 8), pCardBg);
                    canvas.DrawRoundRect(new SKRoundRect(new SKRect(bx, by, bx + boxW, by + boxH), 8, 8), pCardBorder);

                    using var pCardTitle = new SKPaint { Color = new SKColor(251, 191, 36), TextSize = 13, FakeBoldText = true, IsAntialias = true };
                    using var pBody = new SKPaint { Color = SKColors.White, TextSize = 12, IsAntialias = true };
                    using var pMuted = new SKPaint { Color = new SKColor(156, 163, 175), TextSize = 11, IsAntialias = true };

                    string tFmt = (htVal >= 100) ? $"{htVal:F1} {_unitName}" : (htVal >= 1.0 ? $"{htVal:F3} {_unitName}" : $"{htVal:F4} {_unitName}");
                    canvas.DrawText($"Ячейка матрицы: M = {hmVal}, N = {hnVal}", bx + 12, by + 20, pCardTitle);
                    canvas.DrawText($"• Время T: {tFmt}", bx + 12, by + 40, pBody);
                    canvas.DrawText($"• Операций: {hops:N0} (M²·N)", bx + 12, by + 60, pBody);
                    canvas.DrawText($"• Сетка: строка {hoverR + 1}/{rowsM}, столбец {hoverC + 1}/{colsN}", bx + 12, by + 80, pMuted);
                }
            }
        }

        public static void RenderOffline(
            double[,] times,
            int stepM,
            int stepN,
            string unit,
            string outPath,
            int width = 1200,
            int height = 750,
            ViewportMode mode = ViewportMode.ShadedWireframe)
        {
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var vp = new Matrix3DViewport();
            vp.SetData(times, stepM, stepN, unit);
            vp.Mode = mode;
            vp.RenderScene(surface.Canvas, width, height, isExport: true);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            using var stream = File.OpenWrite(outPath);
            data.SaveTo(stream);
        }
    }
}
