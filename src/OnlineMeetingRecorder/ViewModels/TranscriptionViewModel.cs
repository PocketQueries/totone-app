using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OnlineMeetingRecorder.Models;
using OnlineMeetingRecorder.Services.Minutes;
using OnlineMeetingRecorder.Services.Session;
using OnlineMeetingRecorder.Services.Settings;
using OnlineMeetingRecorder.Services.Transcription;

namespace OnlineMeetingRecorder.ViewModels;

public partial class TranscriptionViewModel : ObservableObject
{
    private readonly ITranscriptionService _cloudService;
    private readonly ITranscriptionService _localService;
    private readonly ISettingsService _settings;
    private readonly IMinutesGenerator _minutesGenerator;
    private readonly ISessionService _sessionService;
    private readonly ILogger<TranscriptionViewModel> _logger;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _minutesCts;
    private CancellationTokenSource? _totononeCts;

    /// <summary>いずれかの非同期処理（文字起こし・議事録生成・ととのえ）が実行中か</summary>
    private bool _isAnyProcessing;

    // 経過時間計測
    private Stopwatch? _processingStopwatch;
    private DispatcherTimer? _processingTimer;
    private string _processingStatusBase = "";
    private ProcessingPhase _currentPhase = ProcessingPhase.None;

    private enum ProcessingPhase { None, Transcription, Minutes, Totonoe }

    [ObservableProperty]
    private RecordingSession? _currentSession;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private bool _isTranscribing;

    [ObservableProperty]
    private bool _canTranscribe;

    [ObservableProperty]
    private string _transcriptionStatusText = "";

    [ObservableProperty]
    private string _transcriptionResultText = "";

    [ObservableProperty]
    private bool _isGeneratingMinutes;

    [ObservableProperty]
    private bool _canGenerateMinutes;

    [ObservableProperty]
    private string _minutesText = "";

    /// <summary>文字起こしエンジンが利用可能かどうか</summary>
    [ObservableProperty]
    private bool _isTranscriptionConfigured;

    // --- STTエンジン選択 ---

    /// <summary>利用可能な文字起こしエンジン一覧</summary>
    public ObservableCollection<EngineOption<SttEngine>> AvailableSttEngines { get; } = new();

    /// <summary>選択中の文字起こしエンジン</summary>
    [ObservableProperty]
    private EngineOption<SttEngine>? _selectedSttEngine;

    /// <summary>エンジン選択UIを表示するか（複数利用可能な場合のみ）</summary>
    [ObservableProperty]
    private bool _showSttEngineSelector;

    // --- 議事録エンジン選択 ---

    /// <summary>利用可能な議事録エンジン一覧</summary>
    public ObservableCollection<EngineOption<MinutesEngine>> AvailableMinutesEngines { get; } = new();

    /// <summary>選択中の議事録エンジン</summary>
    [ObservableProperty]
    private EngineOption<MinutesEngine>? _selectedMinutesEngine;

    /// <summary>エンジン選択UIを表示するか（複数利用可能な場合のみ）</summary>
    [ObservableProperty]
    private bool _showMinutesEngineSelector;

    // --- クリップボードコピー用 ---

    /// <summary>文字起こし結果テキストが存在するか</summary>
    [ObservableProperty]
    private bool _hasTranscriptionResult;

    /// <summary>議事録テキストが存在するか</summary>
    [ObservableProperty]
    private bool _hasMinutesResult;

    // --- ととのえ機能 ---

    /// <summary>お客様の会社名</summary>
    [ObservableProperty]
    private string _totonoeCustomerCompany = string.Empty;

    /// <summary>お客様の参加者名</summary>
    [ObservableProperty]
    private string _totonoeCustomerParticipants = string.Empty;

    /// <summary>自社の参加者名</summary>
    [ObservableProperty]
    private string _totonoeOurParticipants = string.Empty;

    /// <summary>会議の目的・内容</summary>
    [ObservableProperty]
    private string _totononeMeetingPurpose = string.Empty;

    /// <summary>専門用語・ドメイン知識</summary>
    [ObservableProperty]
    private string _totononeDomainKnowledge = string.Empty;

    /// <summary>ととのえ再生成中か</summary>
    [ObservableProperty]
    private bool _isTotonoeGenerating;

    /// <summary>ととのえステータステキスト</summary>
    [ObservableProperty]
    private string _totonoeStatusText = string.Empty;

    /// <summary>ととのえ済み議事録テキスト</summary>
    [ObservableProperty]
    private string _totonoeResultText = string.Empty;

    /// <summary>ととのえ結果が存在するか</summary>
    [ObservableProperty]
    private bool _hasTotonoeResult;

    /// <summary>議事録を完成させるための追加情報の提案</summary>
    [ObservableProperty]
    private string _totonoeAdditionalInfoSuggestion = string.Empty;

    /// <summary>追加情報の提案が存在するか</summary>
    [ObservableProperty]
    private bool _hasAdditionalInfoSuggestion;

    // --- プロンプト編集 ---

    /// <summary>議事録用システムプロンプト</summary>
    [ObservableProperty]
    private string _minutesSystemPrompt = string.Empty;

    /// <summary>議事録用ユーザープロンプト</summary>
    [ObservableProperty]
    private string _minutesUserPrompt = string.Empty;

    /// <summary>議事録プロンプト表示中か</summary>
    [ObservableProperty]
    private bool _isMinutesPromptVisible;

    /// <summary>ととのえ用システムプロンプト</summary>
    [ObservableProperty]
    private string _totonoeSystemPrompt = string.Empty;

    /// <summary>ととのえ用ユーザープロンプト</summary>
    [ObservableProperty]
    private string _totonoeUserPrompt = string.Empty;

    /// <summary>ととのえプロンプト表示中か</summary>
    [ObservableProperty]
    private bool _isTotonoePromptVisible;

    // --- プロンプトプリセット選択 ---

    /// <summary>利用可能なプロンプトプリセット一覧</summary>
    public ObservableCollection<PromptPreset> AvailablePresets { get; } = new();

    /// <summary>選択中のプロンプトプリセット</summary>
    [ObservableProperty]
    private PromptPreset? _selectedPreset;

    public ObservableCollection<TranscriptSegment> Segments { get; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TranscriptionViewModel(
        CloudWhisperService cloudService,
        LocalWhisperService localService,
        ISettingsService settings,
        IMinutesGenerator minutesGenerator,
        ISessionService sessionService,
        ILogger<TranscriptionViewModel> logger)
    {
        _cloudService = cloudService;
        _localService = localService;
        _settings = settings;
        _minutesGenerator = minutesGenerator;
        _sessionService = sessionService;
        _logger = logger;

        RefreshAvailableEngines();
        RefreshAvailablePresets();
    }

    // CommunityToolkit.Mvvm の partial method hooks
    partial void OnTranscriptionResultTextChanged(string value)
    {
        HasTranscriptionResult = !string.IsNullOrWhiteSpace(value);
    }

    partial void OnMinutesTextChanged(string value)
    {
        HasMinutesResult = !string.IsNullOrWhiteSpace(value);
    }

    partial void OnTotonoeResultTextChanged(string value)
    {
        HasTotonoeResult = !string.IsNullOrWhiteSpace(value);
    }

    partial void OnTotonoeAdditionalInfoSuggestionChanged(string value)
    {
        HasAdditionalInfoSuggestion = !string.IsNullOrWhiteSpace(value);
    }

    // --- 経過時間計測ヘルパー ---

    private void StartProcessingTimer(ProcessingPhase phase)
    {
        _currentPhase = phase;
        _processingStopwatch = Stopwatch.StartNew();
        _processingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _processingTimer.Tick += OnProcessingTimerTick;
        _processingTimer.Start();
    }

    private void StopProcessingTimer()
    {
        _processingTimer?.Stop();
        _processingTimer = null;
        _processingStopwatch?.Stop();
        _currentPhase = ProcessingPhase.None;
    }

    private string FormatElapsed()
    {
        if (_processingStopwatch == null) return "";
        var elapsed = _processingStopwatch.Elapsed;
        return elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}分{elapsed.Seconds:D2}秒"
            : $"{elapsed.Seconds}秒";
    }

    private void OnProcessingTimerTick(object? sender, EventArgs e)
    {
        var elapsed = FormatElapsed();
        if (_currentPhase == ProcessingPhase.Totonoe)
            TotonoeStatusText = $"{_processingStatusBase}（経過: {elapsed}）";
        else if (_currentPhase != ProcessingPhase.None)
            TranscriptionStatusText = $"{_processingStatusBase}（経過: {elapsed}）";
    }

    private void SetProcessingStatus(string baseText)
    {
        _processingStatusBase = baseText;
        if (_currentPhase == ProcessingPhase.Totonoe)
            TotonoeStatusText = baseText;
        else
            TranscriptionStatusText = baseText;
    }

    // --- 排他制御ヘルパー ---

    /// <summary>処理開始を試みる。既に処理中なら false を返しステータスにメッセージ表示</summary>
    private bool TryBeginProcessing(string operationName)
    {
        if (_isAnyProcessing)
        {
            TranscriptionStatusText = $"処理中のため「{operationName}」を実行できません。処理完了後に再実行してください。";
            return false;
        }
        _isAnyProcessing = true;
        return true;
    }

    private void EndProcessing()
    {
        _isAnyProcessing = false;
    }

    /// <summary>
    /// 録音完了時に RecordingViewModel から呼ばれる
    /// </summary>
    public void OnRecordingCompleted(RecordingSession session)
    {
        CurrentSession = session;
        CanTranscribe = true;
        CanGenerateMinutes = false;
        Segments.Clear();
        TranscriptionResultText = "";
        MinutesText = "";
        TranscriptionStatusText = "録音が完了しました。自動で文字起こしを開始します...";
        ClearTotonoeFields();
    }

    /// <summary>
    /// 録音完了後に文字起こし→議事録生成→合成音声生成を自動実行する
    /// </summary>
    public async Task AutoProcessAfterRecordingAsync()
    {
        if (!TryBeginProcessing("自動処理（文字起こし・議事録生成）")) return;
        try
        {
            // 文字起こし実行（内部メソッドを直接呼び出し、デッドロック回避）
            await TranscribeInternalAsync();

            // 文字起こし成功後に議事録生成
            if (Segments.Count > 0 && CanGenerateMinutes)
            {
                await GenerateMinutesInternalAsync();
            }

            // 聞き返し用の合成音声を生成
            await GenerateMixedAudioAsync();
        }
        finally
        {
            EndProcessing();
        }
    }

    /// <summary>
    /// マイクとスピーカーの音声を合成して軽量MP3を生成する（聞き返し用）。
    /// 失敗しても他の処理に影響しない非クリティカル処理。
    /// </summary>
    private async Task GenerateMixedAudioAsync()
    {
        if (CurrentSession == null) return;

        var micWav = Path.Combine(CurrentSession.FolderPath, "audio", "mic.wav");
        var speakerWav = Path.Combine(CurrentSession.FolderPath, "audio", "speaker.wav");

        if (!File.Exists(micWav) || !File.Exists(speakerWav)) return;

        try
        {
            TranscriptionStatusText = "合成音声を生成中...";
            await AudioConverter.MixToMp3Async(micWav, speakerWav, ct: _cts?.Token ?? CancellationToken.None);
            TranscriptionStatusText = "合成音声の生成が完了しました";
        }
        catch (OperationCanceledException)
        {
            // キャンセル時は無視
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "合成音声の生成に失敗");
            TranscriptionStatusText = $"合成音声の生成をスキップしました: {ex.Message}";
        }
    }

    /// <summary>
    /// セッション一覧から選択された過去セッションをロードする。
    /// 既存の文字起こし・議事録ファイルがあれば表示する。
    /// </summary>
    public async Task LoadSessionAsync(RecordingSession? session)
    {
        if (_isAnyProcessing)
        {
            TranscriptionStatusText = "処理中のためセッションを切り替えできません。処理完了後に再度選択してください。";
            return;
        }

        CurrentSession = session;
        Segments.Clear();
        TranscriptionResultText = "";
        MinutesText = "";
        CanGenerateMinutes = false;
        CanTranscribe = false;
        TranscriptionStatusText = "";
        ClearTotonoeFields();

        if (session == null) return;

        // 既存の文字起こし結果をロード
        var transcriptPath = Path.Combine(session.FolderPath, "transcript", "transcript.json");
        if (File.Exists(transcriptPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(transcriptPath);
                var segments = JsonSerializer.Deserialize<List<TranscriptSegment>>(json, JsonOptions);
                if (segments != null)
                {
                    foreach (var seg in segments)
                        Segments.Add(seg);
                    TranscriptionResultText = string.Join("\n", segments.Select(s => s.ToString()));
                    CanGenerateMinutes = segments.Count > 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "transcript.json のパースに失敗");
            }
        }

        // 既存の議事録をロード
        var minutesPath = Path.Combine(session.FolderPath, "minutes", "minutes.md");
        if (File.Exists(minutesPath))
            MinutesText = await File.ReadAllTextAsync(minutesPath);

        // 既存のととのえ済み議事録をロード
        var totononePath = Path.Combine(session.FolderPath, "minutes", "minutes_totonoe.md");
        if (File.Exists(totononePath))
            TotonoeResultText = await File.ReadAllTextAsync(totononePath);

        // WAVファイルの存在を確認して文字起こし可否を判定
        var micWav = Path.Combine(session.FolderPath, "audio", "mic.wav");
        var speakerWav = Path.Combine(session.FolderPath, "audio", "speaker.wav");
        CanTranscribe = File.Exists(micWav) || File.Exists(speakerWav);

        TranscriptionStatusText = Segments.Count > 0
            ? $"文字起こし済み（{Segments.Count}セグメント）— 再実行も可能です"
            : CanTranscribe
                ? "音声ファイルがあります。文字起こしを開始できます。"
                : "音声ファイルが見つかりません。";

        // ととのえコンテキストをロード（ClearTotonoeFieldsは既に冒頭で実行済み）
        await LoadTotonoeContextAsync();

        // 追加情報の提案をロード
        var suggestionPath = Path.Combine(session.FolderPath, "minutes", "additional_info_suggestion.txt");
        if (File.Exists(suggestionPath))
            TotonoeAdditionalInfoSuggestion = await File.ReadAllTextAsync(suggestionPath);
    }

    [RelayCommand]
    private async Task TranscribeAsync()
    {
        if (!TryBeginProcessing("文字起こし")) return;
        try
        {
            await TranscribeInternalAsync();
        }
        finally
        {
            EndProcessing();
        }
    }

    private async Task TranscribeInternalAsync()
    {
        if (CurrentSession == null) return;

        var service = GetActiveService();
        if (!service.IsAvailable)
        {
            TranscriptionStatusText = $"{service.Name} が利用できません。設定を確認してください。";
            return;
        }

        IsTranscribing = true;
        CanTranscribe = false;
        ProgressValue = 0;
        Segments.Clear();
        TranscriptionResultText = "";
        MinutesText = "";
        _cts = new CancellationTokenSource();
        StartProcessingTimer(ProcessingPhase.Transcription);

        try
        {
            SetProcessingStatus($"{service.Name} で文字起こし中...");

            // セッション状態を更新
            CurrentSession.Status = SessionStatus.Transcribing;
            await _sessionService.UpdateSessionAsync(CurrentSession);

            var allSegments = new List<TranscriptSegment>();
            var language = _settings.Settings.Language;

            // ローカルWhisperの場合、ランタイム情報を取得するヘルパー
            string GetRuntimeSuffix() => service is LocalWhisperService local && local.LoadedRuntimeName != null
                ? $" [{local.LoadedRuntimeName}]" : "";

            // マイク音声の文字起こし
            var micWav = Path.Combine(CurrentSession.FolderPath, "audio", "mic.wav");
            if (File.Exists(micWav))
            {
                SetProcessingStatus($"{service.Name} - マイク音声を処理中...");
                var micProgress = new Progress<int>(p => ProgressValue = p / 2);
                var micSegments = await service.TranscribeAsync(micWav, "mic", language, micProgress, _cts.Token);
                allSegments.AddRange(micSegments);
            }

            // スピーカー音声の文字起こし（ランタイム情報はモデル初回ロード後に確定）
            var speakerWav = Path.Combine(CurrentSession.FolderPath, "audio", "speaker.wav");
            if (File.Exists(speakerWav))
            {
                SetProcessingStatus($"{service.Name}{GetRuntimeSuffix()} - スピーカー音声を処理中...");
                var speakerProgress = new Progress<int>(p => ProgressValue = 50 + p / 2);
                var speakerSegments = await service.TranscribeAsync(speakerWav, "speaker", language, speakerProgress, _cts.Token);
                allSegments.AddRange(speakerSegments);
            }

            // 時系列でソート
            allSegments = allSegments.OrderBy(s => s.Start).ToList();

            // UIに反映
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Segments.Clear();
                foreach (var seg in allSegments)
                    Segments.Add(seg);
            });

            // テキスト表示
            TranscriptionResultText = string.Join("\n", allSegments.Select(s => s.ToString()));

            // ファイルに保存
            await SaveTranscriptionAsync(allSegments);

            // セッション状態を更新
            CurrentSession.Status = SessionStatus.Transcribed;
            await _sessionService.UpdateSessionAsync(CurrentSession);

            // 文字起こし結果が変わったのでプロンプトをクリアし、次回生成時に再構築させる
            MinutesSystemPrompt = string.Empty;
            MinutesUserPrompt = string.Empty;
            TotonoeSystemPrompt = string.Empty;
            TotonoeUserPrompt = string.Empty;

            StopProcessingTimer();
            TranscriptionStatusText = $"文字起こし完了（{allSegments.Count}セグメント / {FormatElapsed()}）";
            CanGenerateMinutes = allSegments.Count > 0;
            ProgressValue = 100;
        }
        catch (OperationCanceledException)
        {
            StopProcessingTimer();
            TranscriptionStatusText = $"文字起こしがキャンセルされました。（{FormatElapsed()}）";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文字起こし中にエラーが発生");
            StopProcessingTimer();
            TranscriptionStatusText = $"エラー: {ex.Message}";
            if (CurrentSession != null)
            {
                CurrentSession.Status = SessionStatus.Error;
                await _sessionService.UpdateSessionAsync(CurrentSession);
            }
        }
        finally
        {
            IsTranscribing = false;
            CanTranscribe = true;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private async Task GenerateMinutesAsync()
    {
        if (!TryBeginProcessing("議事録生成")) return;
        try
        {
            await GenerateMinutesInternalAsync();
        }
        finally
        {
            EndProcessing();
        }
    }

    private async Task GenerateMinutesInternalAsync()
    {
        if (CurrentSession == null || Segments.Count == 0) return;

        IsGeneratingMinutes = true;
        CanGenerateMinutes = false;
        _minutesCts = new CancellationTokenSource();
        StartProcessingTimer(ProcessingPhase.Minutes);

        try
        {
            var engine = SelectedMinutesEngine?.Value ?? _settings.Settings.MinutesEngine;

            var statusBase = engine switch
            {
                MinutesEngine.CloudApi => "AI議事録を生成中...（OpenAI API）",
                MinutesEngine.Llm => "AI議事録を生成中...（ローカルLLM）",
                _ => "議事録を生成中..."
            };
            SetProcessingStatus(statusBase);

            // プロンプトが空の場合のみ自動設定する（ユーザーの手動編集を保持）
            if (string.IsNullOrWhiteSpace(MinutesSystemPrompt) || string.IsNullOrWhiteSpace(MinutesUserPrompt))
                RefreshMinutesPrompts();

            // 議事録生成
            MinutesResult result;
            if (engine == MinutesEngine.CloudApi && _minutesGenerator is MinutesGeneratorDispatcher dispatcher)
                result = await dispatcher.GenerateWithPromptsAsync(MinutesSystemPrompt, MinutesUserPrompt, _minutesCts.Token);
            else if (_minutesGenerator is MinutesGeneratorDispatcher disp2)
                result = await disp2.GenerateWithEngineAsync(CurrentSession, Segments.ToList(), engine, _minutesCts.Token);
            else
                result = await _minutesGenerator.GenerateAsync(CurrentSession, Segments.ToList(), _minutesCts.Token);

            MinutesText = result.Text;

            // 追加情報の提案を反映・保存
            TotonoeAdditionalInfoSuggestion = result.SuggestedAdditionalInfo ?? string.Empty;
            await SaveAdditionalInfoSuggestionAsync(TotonoeAdditionalInfoSuggestion);

            // 議事録をファイルに保存
            await SaveMinutesAsync(MinutesText);

            StopProcessingTimer();
            var engineName = engine switch
            {
                MinutesEngine.CloudApi => "OpenAI API",
                MinutesEngine.Llm => "ローカルLLM",
                _ => "テンプレート"
            };
            var statusParts = new List<string> { engineName, FormatElapsed() };
            if (result.TokenCount.HasValue)
                statusParts.Add($"{result.TokenCount:N0}トークン");
            TranscriptionStatusText = $"議事録の生成が完了しました。（{string.Join(" / ", statusParts)}）";
        }
        catch (OperationCanceledException)
        {
            StopProcessingTimer();
            TranscriptionStatusText = $"議事録生成がキャンセルされました。（{FormatElapsed()}）";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "議事録生成中にエラーが発生");
            StopProcessingTimer();
            TranscriptionStatusText = $"議事録生成エラー: {ex.Message}";
        }
        finally
        {
            IsGeneratingMinutes = false;
            CanGenerateMinutes = true;
            _minutesCts?.Dispose();
            _minutesCts = null;
        }
    }

    [RelayCommand]
    private void CancelTranscription()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void CancelMinutesGeneration()
    {
        _minutesCts?.Cancel();
    }

    [RelayCommand]
    private void CancelTotonoeRegeneration()
    {
        _totononeCts?.Cancel();
    }

    [RelayCommand]
    private void CopyStatusToClipboard()
    {
        if (!string.IsNullOrWhiteSpace(TranscriptionStatusText))
        {
            Clipboard.SetText(TranscriptionStatusText);
        }
    }

    [RelayCommand]
    private void CopyTotonoeStatusToClipboard()
    {
        if (!string.IsNullOrWhiteSpace(TotonoeStatusText))
        {
            Clipboard.SetText(TotonoeStatusText);
        }
    }

    [RelayCommand]
    private void CopyTranscriptionToClipboard()
    {
        if (!string.IsNullOrWhiteSpace(TranscriptionResultText))
        {
            Clipboard.SetText(TranscriptionResultText);
            TranscriptionStatusText = "文字起こしテキストをクリップボードにコピーしました。";
        }
    }

    [RelayCommand]
    private void CopyMinutesToClipboard()
    {
        if (!string.IsNullOrWhiteSpace(MinutesText))
        {
            Clipboard.SetText(MinutesText);
            TranscriptionStatusText = "議事録をクリップボードにコピーしました。";
        }
    }

    /// <summary>
    /// ととのえ: コンテキスト付きで議事録を再生成する
    /// </summary>
    [RelayCommand]
    private async Task TotonoeRegenerateAsync()
    {
        if (!TryBeginProcessing("ととのえ再生成")) return;
        try
        {
            await TotonoeRegenerateInternalAsync();
        }
        finally
        {
            EndProcessing();
        }
    }

    private async Task TotonoeRegenerateInternalAsync()
    {
        if (CurrentSession == null || string.IsNullOrWhiteSpace(MinutesText)) return;

        IsTotonoeGenerating = true;
        _totononeCts = new CancellationTokenSource();
        StartProcessingTimer(ProcessingPhase.Totonoe);

        try
        {
            var context = BuildTotonoeContext();

            // コンテキストをセッションフォルダに保存
            await SaveTotonoeContextAsync(context);

            var engine = SelectedMinutesEngine?.Value ?? _settings.Settings.MinutesEngine;

            SetProcessingStatus("ととのえ中...追加情報を反映して議事録を再生成しています");

            // プロンプトが空の場合のみ自動設定する（ユーザーの手動編集を保持）
            if (string.IsNullOrWhiteSpace(TotonoeSystemPrompt) || string.IsNullOrWhiteSpace(TotonoeUserPrompt))
                RefreshTotonoePrompts();

            MinutesResult result;
            // カスタムプロンプト（生成済み議事録ベース）で再生成
            if (engine == MinutesEngine.CloudApi && _minutesGenerator is MinutesGeneratorDispatcher dispatcher)
                result = await dispatcher.GenerateWithPromptsAsync(TotonoeSystemPrompt, TotonoeUserPrompt, _totononeCts.Token);
            else if (_minutesGenerator is MinutesGeneratorDispatcher disp2)
                result = await disp2.GenerateWithContextAndEngineAsync(CurrentSession, Segments.ToList(), context, engine, _totononeCts.Token);
            else
                result = await _minutesGenerator.GenerateWithContextAsync(CurrentSession, Segments.ToList(), context, _totononeCts.Token);

            TotonoeResultText = result.Text;

            // ととのえ議事録を別ファイルに保存（元の議事録はそのまま）
            await SaveTotonoeMinutesAsync(result.Text);

            StopProcessingTimer();
            var tokenInfo = result.TokenCount.HasValue ? $" / {result.TokenCount:N0}トークン" : "";
            TotonoeStatusText = $"ととのえ完了（{FormatElapsed()}{tokenInfo}）";
        }
        catch (OperationCanceledException)
        {
            StopProcessingTimer();
            TotonoeStatusText = $"ととのえがキャンセルされました。（{FormatElapsed()}）";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ととのえ処理中にエラーが発生");
            StopProcessingTimer();
            TotonoeStatusText = $"ととのえエラー: {ex.Message}";
        }
        finally
        {
            IsTotonoeGenerating = false;
            _totononeCts?.Dispose();
            _totononeCts = null;
        }
    }

    private TotonoeContext BuildTotonoeContext() => new()
    {
        CustomerCompany = TotonoeCustomerCompany,
        CustomerParticipants = TotonoeCustomerParticipants,
        OurParticipants = TotonoeOurParticipants,
        MeetingPurpose = TotononeMeetingPurpose,
        DomainKnowledge = TotononeDomainKnowledge
    };

    private async Task SaveTotonoeContextAsync(TotonoeContext context)
    {
        if (CurrentSession == null) return;

        var minutesDir = Path.Combine(CurrentSession.FolderPath, "minutes");
        Directory.CreateDirectory(minutesDir);

        var json = JsonSerializer.Serialize(context, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(minutesDir, "totonoe_context.json"), json);
    }

    private async Task LoadTotonoeContextAsync()
    {
        if (CurrentSession == null) return;

        var contextPath = Path.Combine(CurrentSession.FolderPath, "minutes", "totonoe_context.json");
        if (!File.Exists(contextPath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(contextPath);
            var context = JsonSerializer.Deserialize<TotonoeContext>(json, JsonOptions);
            if (context != null)
            {
                TotonoeCustomerCompany = context.CustomerCompany;
                TotonoeCustomerParticipants = context.CustomerParticipants;
                TotonoeOurParticipants = context.OurParticipants;
                TotononeMeetingPurpose = context.MeetingPurpose;
                TotononeDomainKnowledge = context.DomainKnowledge;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "totonoe-context.json のパースに失敗");
        }
    }

    [RelayCommand]
    private void CopyTotonoeResultToClipboard()
    {
        if (!string.IsNullOrWhiteSpace(TotonoeResultText))
        {
            Clipboard.SetText(TotonoeResultText);
            TotonoeStatusText = "ととのえ済み議事録をクリップボードにコピーしました。";
        }
    }

    // --- プロンプトプリセット ---

    /// <summary>プリセット一覧を設定から更新する</summary>
    public void RefreshAvailablePresets()
    {
        AvailablePresets.Clear();
        foreach (var preset in _settings.Settings.PromptPresets)
            AvailablePresets.Add(preset);

        SelectedPreset = AvailablePresets
            .FirstOrDefault(p => p.Id == _settings.Settings.SelectedPresetId)
            ?? AvailablePresets.FirstOrDefault();
    }

    partial void OnSelectedPresetChanged(PromptPreset? value)
    {
        if (value != null)
        {
            _settings.Settings.SelectedPresetId = value.Id;

            // プロンプト表示中なら自動更新
            if (IsMinutesPromptVisible)
                UpdateMinutesSystemPromptFromPreset();
            if (IsTotonoePromptVisible)
                UpdateTotonoeSystemPromptFromPreset();
        }
    }

    /// <summary>プリセット変更時にシステムプロンプトを更新する（セッションがなくてもプレビュー表示）</summary>
    private void UpdateMinutesSystemPromptFromPreset()
    {
        if (CurrentSession != null && Segments.Count > 0)
        {
            RefreshMinutesPrompts();
        }
        else
        {
            var basePrompt = SelectedPreset != null && !string.IsNullOrWhiteSpace(SelectedPreset.SystemPrompt)
                ? SelectedPreset.SystemPrompt
                : null;
            MinutesSystemPrompt = CloudMinutesGenerator.BuildSystemPrompt(null, basePrompt);
        }
    }

    /// <summary>プリセット変更時にととのえシステムプロンプトを更新する</summary>
    private void UpdateTotonoeSystemPromptFromPreset()
    {
        if (!string.IsNullOrWhiteSpace(MinutesText))
        {
            RefreshTotonoePrompts();
        }
        else
        {
            var basePrompt = SelectedPreset != null && !string.IsNullOrWhiteSpace(SelectedPreset.SystemPrompt)
                ? SelectedPreset.SystemPrompt
                : null;
            TotonoeSystemPrompt = CloudMinutesGenerator.BuildSystemPrompt(null, basePrompt);
        }
    }

    // --- プロンプト編集機能 ---

    /// <summary>議事録用プロンプトを現在のセッション情報で更新する</summary>
    private void RefreshMinutesPrompts()
    {
        if (CurrentSession == null || Segments.Count == 0) return;

        // プリセットが選択されている場合はそのシステムプロンプトをベースに使用
        var basePrompt = SelectedPreset != null && !string.IsNullOrWhiteSpace(SelectedPreset.SystemPrompt)
            ? SelectedPreset.SystemPrompt
            : null;
        MinutesSystemPrompt = CloudMinutesGenerator.BuildSystemPrompt(null, basePrompt);
        MinutesUserPrompt = CloudMinutesGenerator.BuildUserMessage(CurrentSession, Segments.ToList());
    }

    /// <summary>ととのえ用プロンプトを現在のセッション・コンテキスト情報で更新する（生成済み議事録ベース）</summary>
    private void RefreshTotonoePrompts()
    {
        if (string.IsNullOrWhiteSpace(MinutesText)) return;
        var context = BuildTotonoeContext();

        // プリセットが選択されている場合はそのシステムプロンプトをベースに使用
        var basePrompt = SelectedPreset != null && !string.IsNullOrWhiteSpace(SelectedPreset.SystemPrompt)
            ? SelectedPreset.SystemPrompt
            : null;
        TotonoeSystemPrompt = CloudMinutesGenerator.BuildSystemPrompt(context, basePrompt);
        TotonoeUserPrompt = CloudMinutesGenerator.BuildTotonoeUserMessage(MinutesText);
    }

    [RelayCommand]
    private void ToggleMinutesPromptVisibility()
    {
        if (!IsMinutesPromptVisible)
            UpdateMinutesSystemPromptFromPreset();
        IsMinutesPromptVisible = !IsMinutesPromptVisible;
    }

    [RelayCommand]
    private void ToggleTotonoePromptVisibility()
    {
        if (!IsTotonoePromptVisible)
            UpdateTotonoeSystemPromptFromPreset();
        IsTotonoePromptVisible = !IsTotonoePromptVisible;
    }

    [RelayCommand]
    private void ResetMinutesPrompts()
    {
        RefreshMinutesPrompts();
    }

    [RelayCommand]
    private void ResetTotonoePrompts()
    {
        RefreshTotonoePrompts();
    }

    private async Task SaveTotonoeMinutesAsync(string minutes)
    {
        if (CurrentSession == null) return;

        var minutesDir = Path.Combine(CurrentSession.FolderPath, "minutes");
        Directory.CreateDirectory(minutesDir);

        await File.WriteAllTextAsync(Path.Combine(minutesDir, "minutes_totonoe.md"), minutes);
    }

    private async Task SaveAdditionalInfoSuggestionAsync(string suggestion)
    {
        if (CurrentSession == null) return;

        var minutesDir = Path.Combine(CurrentSession.FolderPath, "minutes");
        Directory.CreateDirectory(minutesDir);

        var path = Path.Combine(minutesDir, "additional_info_suggestion.txt");
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            if (File.Exists(path)) File.Delete(path);
        }
        else
        {
            await File.WriteAllTextAsync(path, suggestion);
        }
    }

    private void ClearTotonoeFields()
    {
        TotonoeCustomerCompany = string.Empty;
        TotonoeCustomerParticipants = string.Empty;
        TotonoeOurParticipants = string.Empty;
        TotononeMeetingPurpose = string.Empty;
        TotononeDomainKnowledge = string.Empty;
        TotonoeStatusText = string.Empty;
        TotonoeResultText = string.Empty;
        TotonoeAdditionalInfoSuggestion = string.Empty;
    }

    /// <summary>
    /// 文字起こしエンジンの設定状態を再チェックする
    /// </summary>
    public void RefreshConfigurationStatus()
    {
        IsTranscriptionConfigured = GetActiveService().IsAvailable;
        RefreshAvailableEngines();
        RefreshAvailablePresets();
    }

    /// <summary>
    /// 利用可能なエンジンの一覧を更新する
    /// </summary>
    public void RefreshAvailableEngines()
    {
        // --- STTエンジン ---
        AvailableSttEngines.Clear();

        if (_cloudService.IsAvailable)
            AvailableSttEngines.Add(new EngineOption<SttEngine>
            {
                Value = SttEngine.Cloud,
                DisplayName = _cloudService.Name
            });

        if (_localService.IsAvailable)
            AvailableSttEngines.Add(new EngineOption<SttEngine>
            {
                Value = SttEngine.Local,
                DisplayName = _localService.Name
            });

        ShowSttEngineSelector = AvailableSttEngines.Count > 1;

        SelectedSttEngine = AvailableSttEngines
            .FirstOrDefault(e => e.Value == _settings.Settings.SttEngine)
            ?? AvailableSttEngines.FirstOrDefault();

        // --- 議事録エンジン ---
        AvailableMinutesEngines.Clear();

        AvailableMinutesEngines.Add(new EngineOption<MinutesEngine>
        {
            Value = MinutesEngine.Template,
            DisplayName = "テンプレート（発言録）"
        });

        if (!string.IsNullOrWhiteSpace(_settings.Settings.OpenAiApiKey))
        {
            AvailableMinutesEngines.Add(new EngineOption<MinutesEngine>
            {
                Value = MinutesEngine.CloudApi,
                DisplayName = "AI（OpenAI API）"
            });
        }

#if ENABLE_LLM
        if (!string.IsNullOrWhiteSpace(_settings.Settings.LlmModelPath))
        {
            AvailableMinutesEngines.Add(new EngineOption<MinutesEngine>
            {
                Value = MinutesEngine.Llm,
                DisplayName = "AI（ローカルLLM）"
            });
        }
#endif

        ShowMinutesEngineSelector = AvailableMinutesEngines.Count > 1;

        SelectedMinutesEngine = AvailableMinutesEngines
            .FirstOrDefault(e => e.Value == _settings.Settings.MinutesEngine)
            ?? AvailableMinutesEngines.FirstOrDefault();
    }

    private ITranscriptionService GetActiveService()
    {
        // インライン選択がある場合はそちらを優先
        if (SelectedSttEngine != null)
            return SelectedSttEngine.Value == SttEngine.Local ? _localService : _cloudService;

        return _settings.Settings.SttEngine == SttEngine.Local ? _localService : _cloudService;
    }

    private async Task SaveTranscriptionAsync(List<TranscriptSegment> segments)
    {
        if (CurrentSession == null) return;

        var transcriptDir = Path.Combine(CurrentSession.FolderPath, "transcript");
        Directory.CreateDirectory(transcriptDir);

        // JSON 形式で保存
        var json = JsonSerializer.Serialize(segments, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(transcriptDir, "transcript.json"), json);

        // テキスト形式でも保存
        var text = string.Join("\n", segments.Select(s => s.ToString()));
        await File.WriteAllTextAsync(Path.Combine(transcriptDir, "transcript.txt"), text);
    }

    private async Task SaveMinutesAsync(string minutes)
    {
        if (CurrentSession == null) return;

        var minutesDir = Path.Combine(CurrentSession.FolderPath, "minutes");
        Directory.CreateDirectory(minutesDir);

        await File.WriteAllTextAsync(Path.Combine(minutesDir, "minutes.md"), minutes);
    }
}
