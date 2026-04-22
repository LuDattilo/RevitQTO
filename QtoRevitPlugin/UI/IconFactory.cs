using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace QtoRevitPlugin.UI
{
    /// <summary>
    /// Genera le icone del ribbon QTO in-memory. Approccio parametrico: nessun PNG binario
    /// nel repository, icone modificabili in un punto solo.
    /// Chiamato da QtoApplication.CreateRibbon in OnStartup.
    /// </summary>
    internal static class IconFactory
    {
        private static readonly Brush BrandPrimary = new SolidColorBrush(Color.FromRgb(0x1F, 0x4E, 0x79));
        private static readonly Brush BrandAccent = new SolidColorBrush(Color.FromRgb(0xFF, 0xC0, 0x00));
        private static readonly Brush HealthGreen = new SolidColorBrush(Color.FromRgb(0x3C, 0xAA, 0x4B));
        private static readonly Brush White = Brushes.White;

        /// <summary>"Avvia QTO": lista con barre orizzontali + accento giallo = listino prezzi.</summary>
        public static BitmapSource CreateLaunchIcon(int size)
        {
            return Render(size, dc =>
            {
                double s = size;
                double r = s * 0.12; // corner radius
                var bg = new Rect(0, 0, s, s);

                // Sfondo blu brand
                dc.DrawRoundedRectangle(BrandPrimary, null, bg, r, r);

                // 3 righe "listino" bianche
                double pad = s * 0.18;
                double lineH = s * 0.085;
                double gap = s * 0.08;
                double lineX = pad;
                double firstY = s * 0.26;
                double lineW = s - pad * 2;

                for (int i = 0; i < 3; i++)
                {
                    double y = firstY + i * (lineH + gap);
                    dc.DrawRoundedRectangle(White, null,
                        new Rect(lineX, y, lineW, lineH), lineH / 2, lineH / 2);
                }

                // Quadrato giallo accento (come pulsante "play" / badge) in alto a destra
                double badgeSize = s * 0.32;
                dc.DrawRoundedRectangle(BrandAccent, null,
                    new Rect(s - badgeSize - s * 0.08, s * 0.08, badgeSize, badgeSize),
                    s * 0.05, s * 0.05);
            });
        }

        /// <summary>"Health Check": cerchio verde con spunta bianca.</summary>
        public static BitmapSource CreateHealthCheckIcon(int size)
        {
            return Render(size, dc =>
            {
                double s = size;
                var center = new Point(s / 2, s / 2);

                // Cerchio verde
                dc.DrawEllipse(HealthGreen, null, center, s * 0.42, s * 0.42);

                // Spunta bianca (polilinea)
                var pen = new Pen(White, s * 0.12)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                pen.Freeze();

                var geom = new StreamGeometry();
                using (var ctx = geom.Open())
                {
                    ctx.BeginFigure(new Point(s * 0.30, s * 0.52), false, false);
                    ctx.LineTo(new Point(s * 0.46, s * 0.68), true, true);
                    ctx.LineTo(new Point(s * 0.72, s * 0.38), true, true);
                }
                geom.Freeze();

                dc.DrawGeometry(null, pen, geom);
            });
        }

        // =====================================================================
        // Primitive di rendering
        // =====================================================================

        private static BitmapSource Render(int size, System.Action<DrawingContext> draw)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                draw(dc);
            }

            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
    }
}
