using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OnlineMeetingRecorder.Services;
using OnlineMeetingRecorder.Services.Audio;
using OnlineMeetingRecorder.Services.MeetingDetection;
using OnlineMeetingRecorder.Services.Minutes;
using OnlineMeetingRecorder.Services.Session;
using OnlineMeetingRecorder.Services.Settings;
using OnlineMeetingRecorder.Services.Transcription;
using OnlineMeetingRecorder.ViewModels;
using OnlineMeetingRecorder.Views;
using Serilog;
using Serilog.Events;

namespace OnlineMeetingRecorder;

public partial class App : Application
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    private IHost? _host;
    private SystemTrayService? _trayService;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── Serilog 初期化 ──
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OnlineMeetingRecorder", "Logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(logDir, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== Totonoe 起動 ===");

        // ── グローバル例外ハンドラ ──
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

#if ENABLE_LLM
        // LLamaSharp: CUDA 12 バックエンドの準備
        //
        // 問題: LLamaSharp は CUDA_PATH 環境変数から CUDA メジャーバージョンを検出し、
        // runtimes/win-x64/native/cuda{N}/ を探す。CUDA_PATH が v13.x を指していると
        // cuda13/ を探すが、LLamaSharp.Backend.Cuda12 は cuda12/ しか提供しないため
        // ロード失敗→CPU フォールバックが発生する。
        // 対策: CUDA 12.x のパスが存在する場合、CUDA_PATH を一時的に上書きする。

        // CUDA_PATH が v12 以外を指している場合、CUDA 12.x パスに切り替え
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH") ?? "";
        if (!cudaPath.Contains("v12.", StringComparison.OrdinalIgnoreCase))
        {
            // CUDA_PATH_V12_* 環境変数から v12.x のパスを探す
            var cuda12Path = Environment.GetEnvironmentVariables()
                .Cast<System.Collections.DictionaryEntry>()
                .Where(e => e.Key is string key && key.StartsWith("CUDA_PATH_V12_", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Value as string)
                .FirstOrDefault(p => !string.IsNullOrEmpty(p) && Directory.Exists(p));

            if (cuda12Path != null)
            {
                Environment.SetEnvironmentVariable("CUDA_PATH", cuda12Path);
            }
        }

        // cuda12/ の DLL 依存解決の準備
        var nativeBase = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native");
        var cuda12Dir = Path.Combine(nativeBase, "cuda12");

        if (Directory.Exists(cuda12Dir))
        {
            // ggml-cpu.dll を CPU バックエンドから cuda12 にコピー（未存在時のみ）
            // CUDA バックエンドでも CPU テンソル操作に必要
            var targetCpuDll = Path.Combine(cuda12Dir, "ggml-cpu.dll");
            if (!File.Exists(targetCpuDll))
            {
                string[] cpuDirs = ["avx512", "avx2", "avx", "noavx"];
                foreach (var dir in cpuDirs)
                {
                    var sourceCpuDll = Path.Combine(nativeBase, dir, "ggml-cpu.dll");
                    if (File.Exists(sourceCpuDll))
                    {
                        try { File.Copy(sourceCpuDll, targetCpuDll); }
                        catch { /* ビルド直後の初回起動で失敗しても次回成功する */ }
                        break;
                    }
                }
            }

            // cuda12/ を Windows DLL 検索パスに追加
            // → llama.dll の暗黙的依存 (ggml.dll, ggml-cuda.dll 等) が解決可能になる
            SetDllDirectory(cuda12Dir);
        }

        // CUDA 優先 + ロード失敗時は CPU にフォールバック
        LLama.Native.NativeLibraryConfig.All
            .WithCuda(true)
            .WithAutoFallback(true);
#endif

        // スプラッシュ画面を表示
        var splash = new SplashWindow();
        splash.Show();

        // DI コンテナの構築
        splash.UpdateStatus("サービスを初期化しています...");
        _host = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((context, services) =>
            {
                // Services - Audio
                services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
                services.AddSingleton<IAudioRecorder, AudioRecorder>();
                services.AddSingleton<IAudioPlaybackService, AudioPlaybackService>();

                // Services - Session
                services.AddSingleton<ISessionService, SessionService>();

                // Services - Settings
                services.AddSingleton<ISettingsService, SettingsService>();

                // Services - Transcription
                services.AddSingleton<CloudWhisperService>();
                services.AddSingleton<LocalWhisperService>();

                // Services - Minutes
                services.AddSingleton<TemplateMinutesGenerator>();
                services.AddSingleton<CloudMinutesGenerator>();
#if ENABLE_LLM
                services.AddSingleton<LlmMinutesGenerator>();
#endif
                services.AddSingleton<IMinutesGenerator, MinutesGeneratorDispatcher>();

                // Services - Meeting Detection
                services.AddSingleton<IMeetingDetectionService, ProcessMeetingDetectionService>();

                // Services - System Tray
                services.AddSingleton<SystemTrayService>();

                // ViewModels
                services.AddSingleton<DeviceSelectionViewModel>();
                services.AddSingleton<AudioLevelViewModel>();
                services.AddSingleton<RecordingViewModel>();
                services.AddSingleton<TranscriptionViewModel>();
                services.AddSingleton<SessionListViewModel>();
                services.AddSingleton<PlaybackViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddSingleton<MainViewModel>();

                // Views
                services.AddSingleton<MainWindow>();
                services.AddSingleton<GadgetWindow>();
            })
            .Build();

        // 設定を起動時にロード（最低 800ms 表示を保証）
        splash.UpdateStatus("設定を読み込んでいます...");
        var settingsService = _host.Services.GetRequiredService<ISettingsService>();
        await Task.WhenAll(settingsService.LoadAsync(), Task.Delay(800));

        // ウィンドウの準備
        splash.UpdateStatus("準備完了");
        var viewModel = _host.Services.GetRequiredService<MainViewModel>();

        // メインウィンドウ（ととのえ画面）は非表示で準備
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = viewModel;
        MainWindow = mainWindow;

        // ガジェットウィンドウ（録音画面）を起動時に表示
        var gadgetWindow = _host.Services.GetRequiredService<GadgetWindow>();
        gadgetWindow.DataContext = viewModel;
        gadgetWindow.Show();

        // システムトレイを初期化（ガジェットモードで開始）
        _trayService = _host.Services.GetRequiredService<SystemTrayService>();
        _trayService.Initialize(mainWindow);
        _trayService.InitializeGadgetWindow(gadgetWindow);
        _trayService.SetGadgetMode(true);
        _trayService.ExitRequested += OnTrayExitRequested;

        // スプラッシュをフェードアウトで閉じる
        splash.CloseWithFade();
    }

    private void OnTrayExitRequested(object? sender, EventArgs e)
    {
        Shutdown();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            // システムトレイのクリーンアップ
            _trayService?.Dispose();

            // ViewModelとサービスのクリーンアップ
            _host.Services.GetService<MainViewModel>()?.Dispose();
            _host.Services.GetService<IAudioRecorder>()?.Dispose();
            _host.Services.GetService<IAudioPlaybackService>()?.Dispose();
            _host.Services.GetService<CloudWhisperService>()?.Dispose();
            _host.Services.GetService<LocalWhisperService>()?.Dispose();
            (_host.Services.GetService<IMinutesGenerator>() as IDisposable)?.Dispose();
            _host.Services.GetService<IMeetingDetectionService>()?.Dispose();

            await _host.StopAsync();
            _host.Dispose();
        }

        Log.Information("=== Totonoe 終了 ===");
        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }

    #region Global Exception Handlers

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "UI スレッドで未処理例外が発生");
        e.Handled = true; // クラッシュを防止
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Fatal(ex, "AppDomain で未処理例外が発生 (IsTerminating={IsTerminating})", e.IsTerminating);
        else
            Log.Fatal("AppDomain で未処理例外が発生: {ExceptionObject}", e.ExceptionObject);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Task で未観測の例外が発生");
        e.SetObserved(); // プロセス終了を防止
    }

    #endregion
}
