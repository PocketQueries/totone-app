using OnlineMeetingRecorder.Models;

namespace OnlineMeetingRecorder.Services.Audio;

public interface IAudioDeviceService : IDisposable
{
    IReadOnlyList<AudioDeviceInfo> GetInputDevices();
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();
    AudioDeviceInfo? GetDefaultInputDevice();
    AudioDeviceInfo? GetDefaultOutputDevice();

    event EventHandler? DevicesChanged;
}
