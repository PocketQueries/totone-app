using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Controls;

public partial class LevelMeterControl : UserControl
{
    #region Dependency Properties (入力)

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(LevelMeterControl),
            new PropertyMetadata(""));

    public static readonly DependencyProperty RmsLevelProperty =
        DependencyProperty.Register(nameof(RmsLevel), typeof(double), typeof(LevelMeterControl),
            new PropertyMetadata(0.0, OnLevelChanged));

    public static readonly DependencyProperty PeakLevelProperty =
        DependencyProperty.Register(nameof(PeakLevel), typeof(double), typeof(LevelMeterControl),
            new PropertyMetadata(0.0, OnLevelChanged));

    public static readonly DependencyProperty DbTextProperty =
        DependencyProperty.Register(nameof(DbText), typeof(string), typeof(LevelMeterControl),
            new PropertyMetadata("-∞ dB"));

    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(nameof(StatusText), typeof(string), typeof(LevelMeterControl),
            new PropertyMetadata("待機中"));

    public static readonly DependencyProperty HealthStatusProperty =
        DependencyProperty.Register(nameof(HealthStatus), typeof(HealthStatus), typeof(LevelMeterControl),
            new PropertyMetadata(Models.HealthStatus.Healthy, OnHealthChanged));

    #endregion

    #region Dependency Properties (計算値、内部バインディング用)

    public static readonly DependencyProperty RmsBarWidthProperty =
        DependencyProperty.Register(nameof(RmsBarWidth), typeof(double), typeof(LevelMeterControl),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty PeakMarginProperty =
        DependencyProperty.Register(nameof(PeakMargin), typeof(Thickness), typeof(LevelMeterControl),
            new PropertyMetadata(new Thickness(0)));

    public static readonly DependencyProperty LevelBrushProperty =
        DependencyProperty.Register(nameof(LevelBrush), typeof(Brush), typeof(LevelMeterControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xF5, 0x8F, 0x98))));

    public static readonly DependencyProperty StatusBrushProperty =
        DependencyProperty.Register(nameof(StatusBrush), typeof(Brush), typeof(LevelMeterControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x5C))));

    #endregion

    // フリーズ済みブラシキャッシュ（~30fps で呼ばれる UpdateVisuals でのGC圧力を回避）
    private static readonly SolidColorBrush TokiPinkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0x8F, 0x98)));
    private static readonly SolidColorBrush ErrorRedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)));
    private static readonly SolidColorBrush IdleGreyBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x5C, 0x5C, 0x5C)));
    private static readonly SolidColorBrush SuccessGreenBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)));
    private static readonly SolidColorBrush WarningOrangeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)));

    private static SolidColorBrush Freeze(SolidColorBrush brush) { brush.Freeze(); return brush; }

    #region CLR Properties

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public double RmsLevel
    {
        get => (double)GetValue(RmsLevelProperty);
        set => SetValue(RmsLevelProperty, value);
    }

    public double PeakLevel
    {
        get => (double)GetValue(PeakLevelProperty);
        set => SetValue(PeakLevelProperty, value);
    }

    public string DbText
    {
        get => (string)GetValue(DbTextProperty);
        set => SetValue(DbTextProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public HealthStatus HealthStatus
    {
        get => (HealthStatus)GetValue(HealthStatusProperty);
        set => SetValue(HealthStatusProperty, value);
    }

    public double RmsBarWidth
    {
        get => (double)GetValue(RmsBarWidthProperty);
        set => SetValue(RmsBarWidthProperty, value);
    }

    public Thickness PeakMargin
    {
        get => (Thickness)GetValue(PeakMarginProperty);
        set => SetValue(PeakMarginProperty, value);
    }

    public Brush LevelBrush
    {
        get => (Brush)GetValue(LevelBrushProperty);
        set => SetValue(LevelBrushProperty, value);
    }

    public Brush StatusBrush
    {
        get => (Brush)GetValue(StatusBrushProperty);
        set => SetValue(StatusBrushProperty, value);
    }

    #endregion

    public LevelMeterControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateVisuals();
    }

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LevelMeterControl control)
            control.UpdateVisuals();
    }

    private static void OnHealthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LevelMeterControl control)
            control.UpdateStatusBrush();
    }

    private void UpdateVisuals()
    {
        double containerWidth = ActualWidth * 0.5;
        if (containerWidth <= 0) containerWidth = 200;

        double rms = Math.Clamp(RmsLevel, 0, 1);
        double peak = Math.Clamp(PeakLevel, 0, 1);

        RmsBarWidth = rms * containerWidth;
        PeakMargin = new Thickness(peak * containerWidth, 0, 0, 0);

        // Toki-Pink → Error Red gradient based on level
        if (rms > 0.9)
            LevelBrush = ErrorRedBrush;
        else if (rms > 0.7)
            LevelBrush = WarningOrangeBrush;
        else if (rms > 0.01)
            LevelBrush = TokiPinkBrush;
        else
            LevelBrush = IdleGreyBrush;
    }

    private void UpdateStatusBrush()
    {
        StatusBrush = HealthStatus switch
        {
            Models.HealthStatus.Healthy => SuccessGreenBrush,
            Models.HealthStatus.Silent => WarningOrangeBrush,
            Models.HealthStatus.Clipping => WarningOrangeBrush,
            _ => ErrorRedBrush
        };
    }
}
