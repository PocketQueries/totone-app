using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Audio;

namespace OnlineMeetingRecorder.ViewModels;

public partial class DeviceSelectionViewModel : ObservableObject
{
    private readonly IAudioDeviceService _deviceService;

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _inputDevices = new();

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _outputDevices = new();

    [ObservableProperty]
    private AudioDeviceInfo? _selectedInputDevice;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedOutputDevice;

    public DeviceSelectionViewModel(IAudioDeviceService deviceService)
    {
        _deviceService = deviceService;
        _deviceService.DevicesChanged += (_, _) => RefreshDevices();
        RefreshDevices();
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            var currentInputId = SelectedInputDevice?.Id;
            var currentOutputId = SelectedOutputDevice?.Id;

            var inputs = _deviceService.GetInputDevices();
            InputDevices = new ObservableCollection<AudioDeviceInfo>(inputs);

            var outputs = _deviceService.GetOutputDevices();
            OutputDevices = new ObservableCollection<AudioDeviceInfo>(outputs);

            // 以前の選択を復元、なければシステムデフォルト、それもなければ先頭
            var defaultInput = _deviceService.GetDefaultInputDevice();
            var defaultOutput = _deviceService.GetDefaultOutputDevice();

            SelectedInputDevice = InputDevices.FirstOrDefault(d => d.Id == currentInputId)
                ?? (defaultInput != null ? InputDevices.FirstOrDefault(d => d.Id == defaultInput.Id) : null)
                ?? InputDevices.FirstOrDefault();
            SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == currentOutputId)
                ?? (defaultOutput != null ? OutputDevices.FirstOrDefault(d => d.Id == defaultOutput.Id) : null)
                ?? OutputDevices.FirstOrDefault();
        });
    }

    /// <summary>
    /// システムのデフォルトデバイスを選択する。
    /// デフォルトが取得できない場合はリストの先頭デバイスを使用する。
    /// </summary>
    public void SelectDefaultDevices()
    {
        RefreshDevices();

        var defaultInput = _deviceService.GetDefaultInputDevice();
        var defaultOutput = _deviceService.GetDefaultOutputDevice();

        if (defaultInput != null)
        {
            SelectedInputDevice = InputDevices.FirstOrDefault(d => d.Id == defaultInput.Id)
                ?? InputDevices.FirstOrDefault();
        }

        if (defaultOutput != null)
        {
            SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == defaultOutput.Id)
                ?? OutputDevices.FirstOrDefault();
        }
    }
}
