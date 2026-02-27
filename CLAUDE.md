# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# ビルド
dotnet build src/OnlineMeetingRecorder/OnlineMeetingRecorder.csproj

# 実行
dotnet run --project src/OnlineMeetingRecorder/OnlineMeetingRecorder.csproj
```

テストプロジェクト・リンター・CI/CD は未導入。

## Architecture

Windows 向けオンラインミーティング録音・文字起こしアプリ（.NET 8 WPF）。

### レイヤー構成

```
MainWindow.xaml / SettingsWindow.xaml (Views)
  → MainViewModel (子VMを合成)
    → RecordingViewModel / AudioLevelViewModel / DeviceSelectionViewModel
    → TranscriptionViewModel / SettingsViewModel
      → IAudioRecorder / IAudioDeviceService (Audio Services)
      → ISessionService (Session管理)
      → ITranscriptionService (CloudWhisperService / LocalWhisperService)
      → IMinutesGenerator (MinutesGeneratorDispatcher → TemplateMinutesGenerator / LlmMinutesGenerator)
      → ISettingsService (設定永続化)
      → IMeetingDetectionService (会議アプリ検知)
        → ICaptureSource (MicCaptureSource / LoopbackCaptureSource)
```

- **MVVM**: CommunityToolkit.Mvvm の `[ObservableProperty]` / `[RelayCommand]` でバインディング
- **DI**: `App.xaml.cs` で `Host.CreateDefaultBuilder()` を使い、サービスと ViewModel を Singleton 登録
- **イベント駆動**: サービスは C# イベントで状態変更を通知し、ViewModel が購読して UI に反映

### 音声パイプライン

- **WASAPI SharedMode** でマイク（Capture）とスピーカー（Loopback）をデュアルトラック録音
- 32-bit float / 48kHz で `mic.wav` と `speaker.wav` に分離保存
- `AudioLevelMeter`（静的ユーティリティ）が Peak/RMS/dB をリアルタイム算出
- `AudioHealthMonitor` が無音（5秒）・クリッピング（Peak≥0.99）・データ停止（500ms）を検知

### スレッドモデル

- UI スレッド（WPF Dispatcher）、音声キャプチャスレッド（NAudio コールバック）、タイマースレッド（レベル更新 ~30fps、WAV フラッシュ 5秒）
- 共有状態は `lock` で保護（`_levelLock`, `_writeLock`）

### セッションデータ保存先

```
%LOCALAPPDATA%/OnlineMeetingRecorder/Sessions/{datetime}_{id}/
├── audio/          # mic.wav, speaker.wav
├── transcript/     # 文字起こし結果
├── minutes/        # 議事録
└── session.json    # セッションメタデータ
```

## Conventions

- **名前空間**: `OnlineMeetingRecorder.{Models|Services|Services.Audio|Services.Session|Services.Settings|Services.Transcription|Services.Minutes|Services.MeetingDetection|ViewModels|Views|Converters|Controls}`
- **ファイルスコープ名前空間**: `namespace X;` 形式を使用
- **Nullable 有効**: `<Nullable>enable</Nullable>`
- **サービスはインターフェース経由**: `IAudioRecorder`, `IAudioDeviceService`, `ICaptureSource`, `ISessionService`, `ISettingsService`, `ITranscriptionService`, `IMinutesGenerator`, `IMeetingDetectionService` 等
- **コメント**: UI・ドメイン関連は日本語、技術的コメントは英語
- **git commit メッセージ**: 日本語で記述する

## Issue 駆動開発フロー

- Issue 番号・URL を受け取ったら、まず `gh issue view <番号>` で内容を確認する
- **最新の main から分岐**してブランチを作成する（`git checkout main && git pull origin main`）
- ブランチ命名: `feature/#123-説明` / `fix/#123-説明` / `refactor/#123-説明` / `docs/#123-説明`
- **作業開始時**: `gh issue comment <番号>` で着手コメントを投稿する
- コミットメッセージに Issue 番号を含める（例: `feat: 録音キャンセル機能を追加 #123`）
- **作業完了時**: `gh issue comment <番号>` で完了コメントを投稿する
- PR 作成時、本文に `Closes #123` を含めて Issue を自動クローズする
- 詳細は `docs/development-flow.md` を参照

## ドキュメント管理ルール

- 資料は `docs/` フォルダ配下で管理する
- `README.md` はプロジェクト概要・資料インデックス・原則的な内容のみを記載し、シンプルに保つ
- `README.md` に `docs/` 配下の資料へのインデックス（リンク一覧）を維持する
- 作業開始時に関連する `docs/` 配下の資料を確認する
- 作業中・作業後に資料の内容に変更が生じた場合は、資料を更新して更新漏れを防ぐ
- 資料は定期的に整理し、冗長・不要な記載を削除してコンテキストの肥大化を防ぐ
