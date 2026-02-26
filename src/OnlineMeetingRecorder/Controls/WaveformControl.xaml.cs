using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NAudio.Wave;
using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Controls;

/// <summary>
/// WAVファイルの波形を描画し、再生位置のインジケーターとシーク操作を提供するコントロール。
/// Toki-Pink (#F58F98) で波形を中央線から上下対称に描画する。
/// 静的レイヤー（波形バー・話者オーバーレイ）と動的レイヤー（位置インジケーター）を分離し、
/// 位置更新時は動的レイヤーのみ再描画することでパフォーマンスを確保する。
/// </summary>
public partial class WaveformControl : UserControl
{
    private float[]? _waveformData;
    private bool _isDragging;

    // 話者セグメント情報（P-05: 話者位置の可視化用）
    private List<TranscriptSegment>? _segments;

    public WaveformControl()
    {
        InitializeComponent();
        SizeChanged += (_, _) => RedrawAll();
    }

    #region DependencyProperties

    public static readonly DependencyProperty WavFilePathProperty =
        DependencyProperty.Register(nameof(WavFilePath), typeof(string), typeof(WaveformControl),
            new PropertyMetadata(null, OnWavFilePathChanged));

    public string? WavFilePath
    {
        get => (string?)GetValue(WavFilePathProperty);
        set => SetValue(WavFilePathProperty, value);
    }

    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(nameof(Position), typeof(double), typeof(WaveformControl),
            new PropertyMetadata(0.0, OnPositionChanged));

    /// <summary>再生位置（秒）</summary>
    public double Position
    {
        get => (double)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public static readonly DependencyProperty DurationProperty =
        DependencyProperty.Register(nameof(Duration), typeof(double), typeof(WaveformControl),
            new PropertyMetadata(1.0));

    /// <summary>総再生時間（秒）</summary>
    public double Duration
    {
        get => (double)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    #endregion

    private static void OnWavFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl control)
            control.LoadWaveform((string?)e.NewValue);
    }

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WaveformControl control)
            control.DrawIndicator();
    }

    /// <summary>話者セグメント情報を設定する（P-05 用）</summary>
    public void SetSegments(List<TranscriptSegment>? segments)
    {
        _segments = segments;
        RedrawAll();
    }

    /// <summary>WAVファイルから波形データをロードする</summary>
    private async void LoadWaveform(string? wavPath)
    {
        _waveformData = null;

        if (string.IsNullOrEmpty(wavPath) || !File.Exists(wavPath))
        {
            RedrawAll();
            return;
        }

        try
        {
            var data = await Task.Run(() =>
            {
                using var reader = new AudioFileReader(wavPath);
                var sampleCount = (int)(reader.Length / (reader.WaveFormat.BitsPerSample / 8));
                var targetPoints = 2000; // 波形表示用のポイント数
                var samplesPerPoint = Math.Max(1, sampleCount / targetPoints);
                var points = new List<float>();
                var buffer = new float[samplesPerPoint];

                while (reader.Read(buffer, 0, buffer.Length) > 0)
                {
                    // 各区間のピーク値を取得
                    float peak = 0;
                    foreach (var sample in buffer)
                    {
                        var abs = Math.Abs(sample);
                        if (abs > peak) peak = abs;
                    }
                    points.Add(peak);
                }

                return points.ToArray();
            });

            _waveformData = data;
            RedrawAll();
        }
        catch
        {
            // ロード失敗時は空の波形を表示
        }
    }

    /// <summary>静的レイヤー + 動的レイヤーの両方を再描画</summary>
    private void RedrawAll()
    {
        DrawStaticLayer();
        DrawIndicator();
    }

    /// <summary>静的レイヤー（波形バー + 話者オーバーレイ）を描画</summary>
    private void DrawStaticLayer()
    {
        StaticCanvas.Children.Clear();

        var width = StaticCanvas.ActualWidth;
        var height = StaticCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var centerY = height / 2;

        // DrawingVisual で1つのビジュアルとして描画（UIエレメント大量生成を回避）
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (_segments != null && Duration > 0)
                DrawSpeakerOverlay(dc, width, height);

            if (_waveformData != null && _waveformData.Length > 0)
                DrawWaveformBars(dc, width, height, centerY);
            else
                DrawCenterLine(dc, width, centerY);
        }

        StaticCanvas.Children.Add(new VisualHost(visual));
    }

    /// <summary>動的レイヤー（位置インジケーター）のみ再描画</summary>
    private void DrawIndicator()
    {
        IndicatorCanvas.Children.Clear();

        var width = IndicatorCanvas.ActualWidth;
        var height = IndicatorCanvas.ActualHeight;
        if (width <= 0 || height <= 0 || Duration <= 0) return;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawPositionIndicator(dc, width, height);
        }

        IndicatorCanvas.Children.Add(new VisualHost(visual));
    }

    private void DrawWaveformBars(DrawingContext dc, double width, double height, double centerY)
    {
        var barWidth = Math.Max(1, width / _waveformData!.Length);
        var maxAmplitude = centerY * 0.85;

        var tokiPink = new SolidColorBrush(Color.FromRgb(0xF5, 0x8F, 0x98));
        tokiPink.Freeze();
        var actualBarWidth = Math.Max(1, barWidth - 0.5);

        for (int i = 0; i < _waveformData.Length; i++)
        {
            var x = i * barWidth;
            var barHeight = _waveformData[i] * maxAmplitude;
            if (barHeight < 0.5) barHeight = 0.5;

            dc.DrawRoundedRectangle(
                tokiPink, null,
                new Rect(x, centerY - barHeight, actualBarWidth, barHeight * 2),
                0.5, 0.5);
        }
    }

    private void DrawSpeakerOverlay(DrawingContext dc, double width, double height)
    {
        if (_segments == null || Duration <= 0) return;

        var micBrush = new SolidColorBrush(Color.FromArgb(40, 0xF5, 0x8F, 0x98));
        var speakerBrush = new SolidColorBrush(Color.FromArgb(40, 0x7A, 0xAF, 0xCF));
        micBrush.Freeze();
        speakerBrush.Freeze();

        var halfHeight = height / 2;

        foreach (var seg in _segments)
        {
            var startX = (seg.Start.TotalSeconds / Duration) * width;
            var endX = (seg.End.TotalSeconds / Duration) * width;
            var segWidth = Math.Max(1, endX - startX);

            var isMic = seg.Speaker == "mic";
            dc.DrawRectangle(
                isMic ? micBrush : speakerBrush, null,
                new Rect(startX, isMic ? halfHeight : 0, segWidth, halfHeight));
        }
    }

    private static void DrawCenterLine(DrawingContext dc, double width, double centerY)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A)), 1);
        pen.Freeze();
        dc.DrawLine(pen, new Point(0, centerY), new Point(width, centerY));
    }

    private void DrawPositionIndicator(DrawingContext dc, double width, double height)
    {
        var positionRatio = Duration > 0 ? Position / Duration : 0;
        var x = positionRatio * width;

        var pen = new Pen(Brushes.White, 2);
        pen.Freeze();
        dc.DrawLine(pen, new Point(x, 0), new Point(x, height));

        // インジケーター上部の丸いノブ
        dc.DrawEllipse(Brushes.White, null, new Point(x, 2), 4, 4);
    }

    #region Mouse Seek

    private void WaveformCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        IndicatorCanvas.CaptureMouse();
        SeekToMousePosition(e);
    }

    private void WaveformCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
            SeekToMousePosition(e);
    }

    private void WaveformCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        IndicatorCanvas.ReleaseMouseCapture();
    }

    private void SeekToMousePosition(MouseEventArgs e)
    {
        var width = IndicatorCanvas.ActualWidth;
        if (width <= 0 || Duration <= 0) return;

        var x = e.GetPosition(IndicatorCanvas).X;
        var ratio = Math.Clamp(x / width, 0, 1);
        var seconds = ratio * Duration;

        Position = seconds;
    }

    #endregion

    /// <summary>
    /// DrawingVisual を Canvas の子要素として配置するための軽量ホスト。
    /// UIElement のレイアウトオーバーヘッドなしで描画できる。
    /// </summary>
    private sealed class VisualHost : FrameworkElement
    {
        private readonly DrawingVisual _visual;

        public VisualHost(DrawingVisual visual)
        {
            _visual = visual;
            AddVisualChild(visual);
            IsHitTestVisible = false;
        }

        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _visual;
    }
}
