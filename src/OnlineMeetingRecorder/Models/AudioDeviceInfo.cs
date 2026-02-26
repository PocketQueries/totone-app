using NAudio.CoreAudioApi;

namespace OnlineMeetingRecorder.Models;

/// <summary>
/// オーディオデバイス情報
/// </summary>
public class AudioDeviceInfo
{
    public string Id { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = string.Empty;
    public DataFlow DataFlow { get; init; }
    public DeviceState State { get; init; }

    public override string ToString() => FriendlyName;
}
