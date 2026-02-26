using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Services.Audio;

public class AudioDeviceService : IAudioDeviceService
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly DispatcherTimer _pollTimer;
    private readonly ILogger<AudioDeviceService> _logger;
    private readonly object _cacheLock = new();

    private List<AudioDeviceInfo> _cachedInputDevices = new();
    private List<AudioDeviceInfo> _cachedOutputDevices = new();

    public event EventHandler? DevicesChanged;

    public AudioDeviceService(ILogger<AudioDeviceService> logger)
    {
        _logger = logger;
        _enumerator = new MMDeviceEnumerator();
        RefreshDeviceCache();

        // デバイス変更をポーリングで検知（NAudioのIMMNotificationClient は不安定な場合があるため）
        // DispatcherTimer を使用し、UIスレッド(STA)でCOM操作を実行する
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _pollTimer.Tick += (_, _) => CheckForDeviceChanges();
        _pollTimer.Start();
    }

    public IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        lock (_cacheLock)
            return _cachedInputDevices.ToList().AsReadOnly();
    }

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        lock (_cacheLock)
            return _cachedOutputDevices.ToList().AsReadOnly();
    }

    public AudioDeviceInfo? GetDefaultInputDevice()
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return ToDeviceInfo(device);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "デフォルト入力デバイスの取得に失敗");
            return null;
        }
    }

    public AudioDeviceInfo? GetDefaultOutputDevice()
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return ToDeviceInfo(device);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "デフォルト出力デバイスの取得に失敗");
            return null;
        }
    }

    /// <summary>
    /// デバイスIDからMMDeviceを取得する（呼び出し側でDisposeすること）
    /// </summary>
    public MMDevice GetMMDevice(string deviceId)
    {
        return _enumerator.GetDevice(deviceId);
    }

    private void RefreshDeviceCache()
    {
        try
        {
            var inputs = new List<AudioDeviceInfo>();
            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                    inputs.Add(ToDeviceInfo(device));
            }

            var outputs = new List<AudioDeviceInfo>();
            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                    outputs.Add(ToDeviceInfo(device));
            }

            lock (_cacheLock)
            {
                _cachedInputDevices = inputs;
                _cachedOutputDevices = outputs;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "デバイス列挙に失敗（既存キャッシュを保持）");
        }
    }

    private void CheckForDeviceChanges()
    {
        try
        {
            var newInputs = new List<string>();
            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                using (device)
                    newInputs.Add(device.ID);
            }
            newInputs.Sort();

            var newOutputs = new List<string>();
            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                    newOutputs.Add(device.ID);
            }
            newOutputs.Sort();

            bool changed;
            lock (_cacheLock)
            {
                var currentInputIds = _cachedInputDevices.Select(d => d.Id).OrderBy(id => id).ToList();
                var currentOutputIds = _cachedOutputDevices.Select(d => d.Id).OrderBy(id => id).ToList();
                changed = !newInputs.SequenceEqual(currentInputIds) || !newOutputs.SequenceEqual(currentOutputIds);
            }

            if (changed)
            {
                RefreshDeviceCache();
                DevicesChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "デバイスポーリングに失敗");
        }
    }

    private static AudioDeviceInfo ToDeviceInfo(MMDevice device)
    {
        return new AudioDeviceInfo
        {
            Id = device.ID,
            FriendlyName = device.FriendlyName,
            DataFlow = device.DataFlow,
            State = device.State
        };
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _enumerator.Dispose();
    }
}
