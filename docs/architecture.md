# アーキテクチャ設計書

## 1. 概要

Online Meeting Recorder は、Windows環境でオンラインミーティングの音声を録音し、文字起こし・議事録生成を行うデスクトップアプリケーションである。

### 1.1 設計目標

| 目標 | 説明 |
|------|------|
| 軽量動作 | ミーティングアプリと同時起動してもCPU・メモリ・帯域に影響を与えない |
| デバイス共有 | WASAPI SharedMode のみ使用し、マイク・スピーカーの制御を奪わない |
| 録音信頼性 | 録音状態をリアルタイムで可視化し、「録音できていなかった」事態を防止 |
| オフライン動作 | ネットワーク不要で録音・文字起こしが完結する |
| 日本語最適化 | UI・文字起こし・議事録すべて日本語対応 |

### 1.2 品質ベンチマーク

Google Pixel Recorder の録音・文字起こし品質、ChatGPT の議事録整理品質と同等以上を目指す。

## 2. 技術スタック

| 領域 | 技術 | バージョン | ライセンス |
|------|------|-----------|-----------|
| UI フレームワーク | C# + WPF | .NET 8 LTS | MIT |
| MVVM ツールキット | CommunityToolkit.Mvvm | 8.4.0 | MIT |
| DI コンテナ | Microsoft.Extensions.Hosting | 8.0.1 | MIT |
| 音声キャプチャ | NAudio | 2.2.1 | MIT |
| ローカルSTT | Whisper.net + CUDA12 | 1.9.0 | MIT |
| クラウドSTT | OpenAI Whisper API | - | 商用API |
| ローカルLLM推論 | LLamaSharp + CUDA12 (llama.cpp) | 0.26.0 | MIT |
| 議事録AI | Qwen3 GGUF (4B Q4_K_M 推奨) | - | Apache 2.0 |

すべてのライブラリは MIT ライセンスであり、商用利用可能。コピーライト表記の義務があるが、アプリ内の「ライセンス情報」画面で対応する。

## 3. システム構成

システム構成の詳細は [system-architecture.drawio](system-architecture.drawio) を参照。

### 3.1 レイヤー構成

```
┌─────────────────────────────────────────────┐
│  Presentation Layer (WPF / XAML)            │
│  ├── Views (MainWindow, SettingsWindow)     │
│  ├── Controls (LevelMeterControl)           │
│  └── Converters (EnumToBoolConverter 等)    │
├─────────────────────────────────────────────┤
│  ViewModel Layer (CommunityToolkit.Mvvm)    │
│  ├── MainViewModel                          │
│  ├── RecordingViewModel                     │
│  ├── AudioLevelViewModel                    │
│  ├── DeviceSelectionViewModel               │
│  ├── TranscriptionViewModel                 │
│  └── SettingsViewModel                      │
├─────────────────────────────────────────────┤
│  Service Layer                              │
│  ├── Audio (キャプチャ・録音・レベル計測)    │
│  ├── Session (SessionService)               │
│  ├── Transcription (Cloud/LocalWhisper)     │
│  ├── Minutes (Template / LLM Minutes)       │
│  ├── Settings (SettingsService)             │
│  ├── MeetingDetection (会議アプリ検知)      │
│  └── SystemTray (SystemTrayService)         │
├─────────────────────────────────────────────┤
│  Infrastructure                             │
│  ├── NAudio (WASAPI)                        │
│  ├── Whisper.net                            │
│  ├── LLamaSharp (llama.cpp)                 │
│  ├── File I/O                               │
│  └── HTTP Client (API呼び出し)              │
└─────────────────────────────────────────────┘
```

### 3.2 MVVM パターン

- **Model**: データオブジェクト（AudioDeviceInfo, RecordingSession, TranscriptSegment等）
- **ViewModel**: CommunityToolkit.Mvvm のソースジェネレータで実装。`[ObservableProperty]`, `[RelayCommand]` を活用
- **View**: XAML によるデータバインディング。コードビハインドは最小限

### 3.3 DI (Dependency Injection)

`Microsoft.Extensions.Hosting` による Generic Host パターンで構成:

```csharp
Host.CreateDefaultBuilder()
    .ConfigureServices(services => {
        services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
        services.AddSingleton<IAudioRecorder, AudioRecorder>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ITranscriptionService, CloudWhisperService>();
        // Minutes - ディスパッチャが設定に基づきランタイムで切替
        services.AddSingleton<TemplateMinutesGenerator>();
#if ENABLE_LLM
        services.AddSingleton<LlmMinutesGenerator>();
#endif
        services.AddSingleton<IMinutesGenerator, MinutesGeneratorDispatcher>();
        services.AddSingleton<IMeetingDetectionService, ProcessMeetingDetectionService>();
        services.AddSingleton<SystemTrayService>();
        services.AddSingleton<MainViewModel>();
        // ...
    })
    .Build();
```

## 4. 音声キャプチャエンジン

### 4.1 デュアルストリーム方式

2つの独立した WASAPI キャプチャストリームで同時録音する:

| ストリーム | NAudio クラス | モード | 用途 |
|-----------|--------------|--------|------|
| マイク入力 | `WasapiCapture` | SharedMode | 自分の音声 |
| スピーカー出力 | `WasapiLoopbackCapture` | SharedMode | 参加者の音声 |

#### SharedMode の重要性

**ExclusiveMode は使用しない。** ExclusiveMode はデバイスを排他的に占有するため、ミーティングアプリの音声入出力に影響を与える。SharedMode はデバイスを共有し、他のアプリケーションへの影響がない。

### 4.2 データフロー

データフローの詳細は [data-flow.md](data-flow.md) のシーケンス図を参照。

```
キャプチャスレッド (NAudio内部)
  → DataAvailable コールバック
    → WaveFileWriter.Write (WAVファイル書き込み)
    → AudioLevelMeter.Calculate (Peak/RMS計算)
    → AudioHealthMonitor.OnDataReceived (状態監視)

タイマースレッド (33ms = ~30fps)
  → 最新レベルデータ読み取り (lock保護)
  → LevelsUpdated イベント発火
  → HealthChanged イベント発火

UIスレッド
  → Dispatcher.BeginInvoke でレベル値をバインディングプロパティに反映
  → LevelMeterControl がリアルタイム描画
```

### 4.3 スレッドモデル

| スレッド | 役割 | 注意事項 |
|---------|------|---------|
| UIスレッド (STA) | WPF描画、バインディング更新 | 重い処理を行わない |
| Micキャプチャスレッド | マイク DataAvailable 処理 | NAudio が内部生成 |
| Loopbackキャプチャスレッド | スピーカー DataAvailable 処理 | NAudio が内部生成 |
| レベルタイマースレッド | 30fps でレベルイベント発火 | System.Timers.Timer |
| フラッシュタイマースレッド | 5秒ごとにWAVフラッシュ | クラッシュ対策 |

### 4.4 技術的注意点

1. **WasapiLoopbackCapture の無音問題**: 完全無音時に `DataAvailable` が発火しない。`AudioHealthMonitor` がデータ停止を検知し UI に通知する
2. **WAV フォーマット**: デバイスのネイティブフォーマット（通常 32bit float, 48kHz, stereo）をそのまま使用。強制変換しない
3. **WAV ヘッダ安全策**: 5秒ごとにフラッシュ。クラッシュ時はヘッダ修復ユーティリティで復旧可能
4. **Bluetooth HFP**: ハンズフリープロファイルでは 16kHz mono に劣化する場合がある

## 5. 文字起こし・議事録パイプライン

### 5.1 文字起こしフロー

```
録音停止
  → AudioConverter で WAV を変換
    → Cloud: 16kHz mono 16-bit PCM (API サイズ制限対策)
    → Local: 16kHz mono float32 (Whisper.net 入力形式)
  → ITranscriptionService.TranscribeAsync()
    → マイク WAV → TranscriptSegment[] (Speaker = "自分")
    → スピーカー WAV → TranscriptSegment[] (Speaker = "相手")
  → 結果をタイムスタンプ順にマージ
  → transcript.json / transcript.txt に保存
```

### 5.2 議事録生成フロー

```
文字起こし完了
  → IMinutesGenerator.GenerateAsync() → MinutesResult (Text + TokenCount)
    → [テンプレート] TemplateMinutesGenerator: 発言録をマークダウンテーブルに整形
    → [AI] LlmMinutesGenerator: LLamaSharp + Qwen3 で要約・決定事項・ToDo を生成（トークンカウント付き）
    → [AI] CloudMinutesGenerator: OpenAI API で議事録生成（usage からトークン情報抽出）
  → minutes/minutes.md に保存
  → ステータスに経過時間・トークン消費量を表示
```

- **キャンセル対応**: 全生成メソッドが `CancellationToken` を受け取り、途中中断可能
- **経過時間**: `Stopwatch` + `DispatcherTimer`（500ms間隔）で処理中リアルタイム表示
- **トークン表示**: AI利用時のみ完了ステータスに消費トークン数を表示（テンプレート生成時は非表示）

#### AI議事録生成（LlmMinutesGenerator）

- **ライブラリ**: LLamaSharp 0.26.0（llama.cpp の C# バインディング）
- **モデル**: Qwen3 4B Q4_K_M GGUF（推奨）。1.7B Q8_0 でも動作可
- **推論方式**: StatelessExecutor + ChatML形式プロンプト手動構築
- **出力**: 要約・決定事項・アクションアイテム・次回予定の4セクション
- **`<think>` フィルタ**: Qwen3のthinkingモード出力をストリーミング中にフィルタリング
- **GPU アクセラレーション**: CUDA 12 対応GPU + CUDA Toolkit インストール時、`LlmGpuLayerCount` 設定でGPUオフロード可能（既定: 999=全レイヤー）。CUDA Toolkit がない場合はCPUフォールバック
- **パフォーマンス参考値（CPU推論）**:

| モデル | 推論時間 | 速度 | メモリ |
|--------|---------|------|--------|
| Qwen3 1.7B Q8_0 | ~15秒 | ~18 tokens/sec | ~2.2GB |
| Qwen3 4B Q4_K_M | ~22秒 | ~12 tokens/sec | ~3.0GB |

GPU推論時は大幅に高速化される（目安: 100-300+ tokens/sec）。

### 5.3 設定の永続化

- 保存先: `%LOCALAPPDATA%/OnlineMeetingRecorder/settings.json`
- `SettingsService` がアプリ起動時に自動ロード、変更時に自動保存
- `AppSettings` モデル: STTエンジン選択、OpenAI API キー、Whisper モデルパス、言語設定

## 6. ネットワーク状態管理

- `NetworkInterface.GetIsNetworkAvailable()` による定期チェック（10秒間隔）
- `NetworkChange.NetworkAvailabilityChanged` イベントでリアルタイム検知
- UI右上にオンライン/オフラインアイコンを常時表示
- オフライン時はクラウドSTTとクラウドアップロードを自動的に無効化

## 7. データ保存

### 7.1 セッションフォルダ構造

```
%LOCALAPPDATA%/OnlineMeetingRecorder/Sessions/
  {yyyy-MM-dd_HHmm}_{id}/
    audio/
      mic.wav            # マイク録音 (32bit float, 48kHz)
      speaker.wav        # スピーカー録音 (32bit float, 48kHz)
      mixed.mp3          # 合成音声 (64kbps, 16kHz, mono) - 聞き返し用
    transcript/
      transcript.json    # タイムスタンプ付き文字起こし
      transcript.txt     # プレーンテキスト
    minutes/
      minutes.md         # 議事録
    session.json         # セッションメタデータ
```

### 7.2 session.json スキーマ

```json
{
  "id": "abc123def456",
  "title": "週次定例",
  "startTime": "2026-02-22T14:30:00+09:00",
  "endTime": "2026-02-22T15:30:00+09:00",
  "inputDeviceName": "マイク (Realtek Audio)",
  "outputDeviceName": "スピーカー (Realtek Audio)",
  "status": "Completed",
  "calendarEventTitle": null,
  "participants": null
}
```

## 8. 拡張ポイント

### 8.1 プラグインインタフェース

以下のインタフェースにより、実装を差し替え可能:

| インタフェース | 用途 | 現在の実装 |
|---------------|------|-----------|
| `ITranscriptionService` | 文字起こし | CloudWhisperService (OpenAI API) / LocalWhisperService (Whisper.net) |
| `IMinutesGenerator` | 議事録生成 | MinutesGeneratorDispatcher → TemplateMinutesGenerator / LlmMinutesGenerator |
| `ISessionService` | セッション管理 | SessionService (session.json 永続化) |
| `ISettingsService` | 設定管理 | SettingsService (settings.json 永続化) |
| `IAudioDeviceService` | デバイス管理 | AudioDeviceService (WASAPI) |
| `IMeetingDetectionService` | 会議アプリ検知 | ProcessMeetingDetectionService (プロセス監視) |

### 8.2 システムトレイ常駐（SystemTrayService）

`SystemTrayService` がタスクトレイへの常駐機能を提供する。

- **技術基盤**: `System.Windows.Forms.NotifyIcon`（FrameworkReference で WindowsForms を参照）
- **名前空間**: `OnlineMeetingRecorder.Services`
- **アイコンオーバーレイ**: 録音状態に応じて表示を切替
  - 待機中: アプリアイコン
  - 録音中: 赤ドットオーバーレイ
  - 一時停止: オレンジドットオーバーレイ
- **コンテキストメニュー**: ウィンドウ表示 / 録音開始・停止 / 一時停止・再開 / 終了
- **トレイ格納**: ウィンドウの最小化・閉じる操作でトレイに格納、ダブルクリックで復元
- **バルーン通知**: 初回トレイ格納時に通知を表示
- **会議検知通知**: 会議アプリ検知時・終了時にバルーン通知を表示し、クリックで録音開始/停止

### 8.3 会議アプリ自動検知（ProcessMeetingDetectionService）

`IMeetingDetectionService` / `ProcessMeetingDetectionService` が会議アプリのプロセス監視を提供する。

- **名前空間**: `OnlineMeetingRecorder.Services.MeetingDetection`
- **監視方式**: `DispatcherTimer` による5秒間隔ポーリング（`AudioDeviceService` と同パターン）
- **検知対象**:
  - Zoom: プロセス名 `Zoom` + ウィンドウタイトル検証（`Zoom Meeting` / `Zoom ミーティング` 等を含む場合のみ検知）
  - Teams: プロセス名 `ms-teams`（タイトルだけでは会議中の区別が困難なためプロセス存在のみ）
  - Google Meet: Chrome/Edge/Firefox のウィンドウタイトルに `Meet -` / `Google Meet` を含む
  - Webex: プロセス名 `CiscoCollabHost` / `webexmta`（会議専用プロセスのためタイトル検証不要）
- **重複通知防止**: `HashSet<MeetingApp>` で検知済みアプリを管理、プロセス消失まで再通知しない
- **録音中スキップ**: `SetRecordingActive(bool)` で録音状態を通知、録音中は新規検知イベントを抑止
- **イベント**: `MeetingDetected` / `MeetingEnded` を発火し、`SystemTrayService` がバルーン通知で応答
- **設定**: `AppSettings.MeetingDetectionEnabled` でON/OFF切替

### 8.4 ロギング機構

- **Serilog** を Microsoft.Extensions.Logging と統合し、DI 経由で `ILogger<T>` をサービス・ViewModelに自動注入
- **ファイルシンク**: `%LOCALAPPDATA%/OnlineMeetingRecorder/Logs/app-{yyyy-MM-dd}.log`（日次ローリング、31日保持）
- **ログレベル**: Information（通常操作）、Warning（リカバリ可能エラー）、Error（クリティカルエラー）、Fatal（未処理例外）
- **グローバル例外ハンドラ（3種）**:
  - `DispatcherUnhandledException` — UI スレッドの未処理例外をキャッチしてログ出力
  - `AppDomain.CurrentDomain.UnhandledException` — 非UIスレッドの未処理例外
  - `TaskScheduler.UnobservedTaskException` — Fire-and-forget タスクの例外
- **対象コンポーネント**: AudioRecorder, AudioDeviceService, SettingsService, RecordingViewModel, TranscriptionViewModel, SessionListViewModel, LocalWhisperService, LlmMinutesGenerator

### 8.5 将来の拡張候補

- カレンダー連携（Google Calendar / Outlook）: 会議タイトル・参加者の自動取得
- リアルタイム文字起こし: ストリーミングSTTで会議中にテキスト表示
- 録音データ再生: 波形表示 + 文字起こし同期シーク
- クラウドアップロード: Google Drive / OneDrive 連携
- 話者分離 (Speaker Diarization): 参加者ごとの発言分離
- ~~音声合成~~ → v1.8 で実装済み（mixed.mp3 自動生成）
