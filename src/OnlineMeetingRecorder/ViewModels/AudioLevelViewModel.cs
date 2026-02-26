using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Audio;

namespace OnlineMeetingRecorder.ViewModels;

public partial class AudioLevelViewModel : ObservableObject, IDisposable
{
    private readonly IAudioRecorder _recorder;

    // マイクレベル (0.0 ~ 1.0)
    [ObservableProperty]
    private double _micPeakLevel;

    [ObservableProperty]
    private double _micRmsLevel;

    [ObservableProperty]
    private bool _micClipping;

    [ObservableProperty]
    private string _micDbText = "-∞ dB";

    // スピーカーレベル (0.0 ~ 1.0)
    [ObservableProperty]
    private double _speakerPeakLevel;

    [ObservableProperty]
    private double _speakerRmsLevel;

    [ObservableProperty]
    private bool _speakerClipping;

    [ObservableProperty]
    private string _speakerDbText = "-∞ dB";

    // ヘルスステータス
    [ObservableProperty]
    private HealthStatus _micHealth = HealthStatus.Healthy;

    [ObservableProperty]
    private HealthStatus _speakerHealth = HealthStatus.Healthy;

    [ObservableProperty]
    private string _micHealthText = "待機中";

    [ObservableProperty]
    private string _speakerHealthText = "待機中";

    public AudioLevelViewModel(IAudioRecorder recorder)
    {
        _recorder = recorder;
        _recorder.LevelsUpdated += OnLevelsUpdated;
        _recorder.HealthChanged += OnHealthChanged;
    }

    private void OnLevelsUpdated(object? sender, DualLevelEventArgs e)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            MicPeakLevel = Math.Min(e.MicLevel.Peak, 1.0);
            MicRmsLevel = Math.Min(e.MicLevel.Rms, 1.0);
            MicClipping = e.MicLevel.IsClipping;
            MicDbText = FormatDb(e.MicLevel.RmsDb);

            SpeakerPeakLevel = Math.Min(e.SpeakerLevel.Peak, 1.0);
            SpeakerRmsLevel = Math.Min(e.SpeakerLevel.Rms, 1.0);
            SpeakerClipping = e.SpeakerLevel.IsClipping;
            SpeakerDbText = FormatDb(e.SpeakerLevel.RmsDb);
        });
    }

    private void OnHealthChanged(object? sender, HealthChangedEventArgs e)
    {
        Application.Current?.Dispatcher?.BeginInvoke(() =>
        {
            MicHealth = e.MicHealth;
            SpeakerHealth = e.SpeakerHealth;
            MicHealthText = ToHealthText(e.MicHealth);
            SpeakerHealthText = ToHealthText(e.SpeakerHealth);
        });
    }

    private static string FormatDb(float db)
    {
        if (db <= -60f) return "-∞ dB";
        return $"{db:F0} dB";
    }

    private static string ToHealthText(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "正常",
        HealthStatus.Silent => "無音検知",
        HealthStatus.Clipping => "クリッピング",
        HealthStatus.DataStalled => "データ停止",
        HealthStatus.DeviceDisconnected => "デバイス切断",
        HealthStatus.WriteError => "書込エラー",
        _ => "不明"
    };

    public void Dispose()
    {
        _recorder.LevelsUpdated -= OnLevelsUpdated;
        _recorder.HealthChanged -= OnHealthChanged;
    }
}
