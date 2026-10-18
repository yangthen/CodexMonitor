using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CodexMonitor_App;

/// <summary>A quota bar whose colored track uses the full requested height.</summary>
public sealed class QuotaBar : Grid
{
    private readonly Border _fill;
    private double _value;

    public QuotaBar()
    {
        Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 40, 50, 59)),
            CornerRadius = new CornerRadius(5)
        });
        _fill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 35, 210, 231)),
            CornerRadius = new CornerRadius(5),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Children.Add(_fill);
        SizeChanged += (_, _) => UpdateFill();
    }

    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 100);
            UpdateFill();
        }
    }

    private void UpdateFill()
    {
        _fill.Width = ActualWidth * _value / 100;
    }
}
