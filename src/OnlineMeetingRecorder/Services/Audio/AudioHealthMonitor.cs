using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Services.Audio;

/// <summary>
/// 録音ストリームの健全性を監視する。
/// 無音継続、データ停止、クリッピングを検知する。
/// </summary>
public class AudioHealthMonitor
{
    private readonly TimeSpan _silenceWarningThreshold = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _dataStallThreshold = TimeSpan.FromMilliseconds(500);

    private DateTime _lastDataReceived = DateTime.UtcNow;
    private DateTime _silenceStarted = DateTime.UtcNow;
    private bool _wasSilent;

    public HealthStatus CurrentStatus { get; private set; } = HealthStatus.Healthy;

    /// <summary>
    /// 音声データ受信時に呼び出す
    /// </summary>
    public void OnDataReceived(AudioLevelData levels)
    {
        var now = DateTime.UtcNow;
        _lastDataReceived = now;

        if (levels.IsClipping)
        {
            CurrentStatus = HealthStatus.Clipping;
            _wasSilent = false;
            return;
        }

        if (levels.IsSilent)
        {
            if (!_wasSilent)
            {
                _silenceStarted = now;
                _wasSilent = true;
            }

            if (now - _silenceStarted > _silenceWarningThreshold)
            {
                CurrentStatus = HealthStatus.Silent;
                return;
            }
        }
        else
        {
            _wasSilent = false;
        }

        CurrentStatus = HealthStatus.Healthy;
    }

    /// <summary>
    /// 定期的に呼び出してデータ停止を検知する
    /// </summary>
    public void CheckForStall()
    {
        var now = DateTime.UtcNow;
        if (now - _lastDataReceived > _dataStallThreshold)
        {
            CurrentStatus = HealthStatus.DataStalled;
        }
    }

    /// <summary>
    /// デバイス切断を通知する
    /// </summary>
    public void NotifyDeviceDisconnected()
    {
        CurrentStatus = HealthStatus.DeviceDisconnected;
    }

    /// <summary>
    /// モニターをリセットする
    /// </summary>
    public void Reset()
    {
        _lastDataReceived = DateTime.UtcNow;
        _silenceStarted = DateTime.UtcNow;
        _wasSilent = false;
        CurrentStatus = HealthStatus.Healthy;
    }
}
