using CodexMonitor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace CodexMonitor_App;

/// <summary>A time-based chart with a smooth, shape-preserving token-rate curve.</summary>
public sealed class SpeedChart : Canvas
{
    private static readonly Brush Accent = Brush(35, 210, 231);
    private static readonly Brush GridBrush = Brush(45, 59, 69);
    private static readonly Brush LabelBrush = Brush(137, 153, 166);
    private IReadOnlyList<SpeedPoint> _points = [];
    private TimeSpan _period = TimeSpan.FromMinutes(5);
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public SpeedChart()
    {
        Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        SizeChanged += (_, _) => Render();
    }

    public void SetData(IReadOnlyList<SpeedPoint> points, TimeSpan period, DateTimeOffset now)
    {
        _points = points;
        _period = period;
        _now = now;
        Render();
    }

    private void Render()
    {
        Children.Clear();
        double width = ActualWidth;
        double height = ActualHeight;
        if (width < 70 || height < 40 || _period <= TimeSpan.Zero) return;

        const double left = 38, right = 5, top = 8, bottom = 18;
        double plotWidth = width - left - right;
        double plotHeight = height - top - bottom;
        if (plotWidth <= 0 || plotHeight <= 0) return;

        for (int i = 0; i <= 2; i++)
        {
            double y = top + i * plotHeight / 2;
            Children.Add(new Line
            {
                X1 = left, Y1 = y, X2 = width - right, Y2 = y,
                Stroke = GridBrush, StrokeThickness = 1
            });
        }

        var since = _now - _period;
        var visible = _points.Where(p => p.At >= since && p.At <= _now &&
                                         double.IsFinite(p.TokensPerSecond) && p.TokensPerSecond >= 0)
                             .OrderBy(p => p.At).ToList();
        double max = visible.Count == 0 ? 1 : Math.Max(1, visible.Max(p => p.TokensPerSecond));
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(max)));
        double ceiling = Math.Ceiling(max / magnitude) * magnitude;

        AddLabel($"{ceiling:0.#}/s", 0, 0);
        AddLabel("0", 25, top + plotHeight - 7);
        AddLabel(UiText.Ago(_period), left, height - 15);
        AddLabel(UiText.Now, width - 31, height - 15);

        if (visible.Count == 0)
        {
            var emptyLabel = AddLabel(UiText.NoSpeedData, left, top + plotHeight / 2 - 8);
            emptyLabel.Width = plotWidth;
            emptyLabel.TextAlignment = TextAlignment.Center;
            return;
        }

        var segment = new List<Point>(visible.Count);
        foreach (var sample in visible)
        {
            double x = left + Math.Clamp((sample.At - since).TotalSeconds / _period.TotalSeconds, 0, 1) * plotWidth;
            double y = top + (1 - Math.Clamp(sample.TokensPerSecond / ceiling, 0, 1)) * plotHeight;
            segment.Add(new Point(x, y));
        }
        DrawCurve(segment);
    }

    private void DrawCurve(List<Point> points)
    {
        if (points.Count > 1)
        {
            // PCHIP-style tangents round sharp joints without inventing peaks
            // above or below the observed token rates.
            var slopes = new double[points.Count];
            var secants = new double[points.Count - 1];
            for (int i = 0; i < secants.Length; i++)
            {
                double dx = Math.Max(0.001, points[i + 1].X - points[i].X);
                secants[i] = (points[i + 1].Y - points[i].Y) / dx;
            }
            slopes[0] = secants[0];
            slopes[^1] = secants[^1];
            for (int i = 1; i < points.Count - 1; i++)
            {
                double before = secants[i - 1], after = secants[i];
                if (before * after <= 0) continue;
                double dxBefore = Math.Max(0.001, points[i].X - points[i - 1].X);
                double dxAfter = Math.Max(0.001, points[i + 1].X - points[i].X);
                double w1 = 2 * dxAfter + dxBefore;
                double w2 = dxAfter + 2 * dxBefore;
                slopes[i] = (w1 + w2) / (w1 / before + w2 / after);
            }

            var figure = new PathFigure { StartPoint = points[0], IsFilled = false };
            for (int i = 0; i < points.Count - 1; i++)
            {
                Point a = points[i], b = points[i + 1];
                double dx = b.X - a.X;
                if (dx <= 0) continue;
                double low = Math.Min(a.Y, b.Y), high = Math.Max(a.Y, b.Y);
                figure.Segments.Add(new BezierSegment
                {
                    Point1 = new Point(a.X + dx / 3, Math.Clamp(a.Y + slopes[i] * dx / 3, low, high)),
                    Point2 = new Point(b.X - dx / 3, Math.Clamp(b.Y - slopes[i + 1] * dx / 3, low, high)),
                    Point3 = b
                });
            }
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry, Stroke = Accent, StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
            });
        }
        else if (points.Count == 1)
        {
            var dot = new Ellipse { Width = 5, Height = 5, Fill = Accent };
            SetLeft(dot, points[0].X - 2.5);
            SetTop(dot, points[0].Y - 2.5);
            Children.Add(dot);
        }
    }

    private TextBlock AddLabel(string value, double x, double y)
    {
        var label = new TextBlock { Text = value, FontSize = 10, Foreground = LabelBrush };
        SetLeft(label, x);
        SetTop(label, y);
        Children.Add(label);
        return label;
    }

    private static Brush Brush(byte r, byte g, byte b) =>
        new SolidColorBrush(Color.FromArgb(255, r, g, b));
}
