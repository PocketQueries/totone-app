# MVP実装設計書

## 概要

MVP（音声キャプチャ・レベルメーター・ヘルス監視・セッション管理・文字起こし・議事録・設定）は全て実装済み。
本設計書は各機能の実装仕様を定義する。

## セッション管理

### ISessionService / SessionService

録音開始時に `RecordingSession` を生成し、停止時に `session.json` として保存する。

```
セッションフォルダ/session.json
```

- 録音開始: `CreateSession()` → `RecordingSession` 生成、`session.json` 初期保存
- 録音停止: `CompleteSession()` → EndTime・Duration・Status 更新、`session.json` 上書き
- JSON 永続化: `System.Text.Json` で serialize/deserialize

## 文字起こし

### ITranscriptionService

```csharp
Task<List<TranscriptSegment>> TranscribeAsync(
    string wavFilePath, string language,
    IProgress<int>? progress, CancellationToken ct);
```

マイク・スピーカーそれぞれの WAV を個別に文字起こしし、Speaker フィールドで識別する。

### CloudWhisperService（OpenAI Whisper API）

- エンドポイント: `POST https://api.openai.com/v1/audio/transcriptions`
- モデル: `whisper-1`
- リクエスト: `multipart/form-data`（file, model, language, response_format=verbose_json）
- ファイルサイズ上限 25MB → AudioConverter で 16kHz mono 16bit に変換して削減
- レスポンスから segments を抽出し `TranscriptSegment` に変換

### LocalWhisperService（Whisper.net）

- `Whisper.net` + `Whisper.net.Runtime.Cpu` パッケージ使用
- GGML モデルファイルをローカルからロード
- 16kHz mono float32 PCM に変換してから処理
- progress コールバックで進捗通知

### AudioConverter（ユーティリティ）

- WAV を 16kHz mono 16-bit PCM に変換（Whisper 用）
- WAV を 16kHz mono float32 に変換（Whisper.net 用）
- NAudio の `WaveFormatConversionStream` / `MediaFoundationResampler` を使用

## 議事録生成

### TemplateMinutesGenerator（MVP）

文字起こし結果をマークダウンテンプレートに整形:

```markdown
# 議事録

- 日時: {startTime} 〜 {endTime}
- 使用デバイス: {input} / {output}

## 発言録

| 時刻 | 話者 | 発言内容 |
|------|------|----------|
| 00:00 | 自分 | ... |
| 00:15 | 相手 | ... |
```

### LlmMinutesGenerator（v1.1 実装済み）

LLamaSharp + Qwen3 GGUF によるAI議事録生成。文字起こし結果からLLMが要約・構造化を行う。
条件付きコンパイル（`#if ENABLE_LLM`）でLLM組み込みビルドと組み込みなしビルドを切り替える。

#### 技術仕様

- **ライブラリ**: LLamaSharp 0.26.0 + LLamaSharp.Backend.Cpu 0.26.0
- **モデル**: Qwen3 GGUF（4B Q4_K_M 推奨、1.7B Q8_0 も可）
- **推論**: StatelessExecutor（1ショット推論、ChatSessionは非使用）
- **プロンプト形式**: Qwen3 ChatML（`<|im_start|>`/`<|im_end|>` タグ）を手動構築
- **`/no_think`**: ユーザーメッセージ末尾に配置し、思考モード出力を抑制
- **`<think>` フィルタ**: ストリーミング出力中に `<think>...</think>` ブロックをリアルタイム除去
- **Temperature**: 0.3（事実ベースの安定出力）
- **AntiPrompts**: `<|im_end|>`, `<|im_start|>`（ターン終了で停止）

#### 出力フォーマット

```markdown
# 議事録

## 要約
会議全体の概要（2-3文）

## 決定事項
- 決まったことのリスト

## アクションアイテム
- 担当者・期限付きのTODOリスト

## 次回予定
- 次回会議の日時
```

#### AppSettings 追加項目（v1.1）

| 項目 | 型 | デフォルト値 | 説明 |
|------|------|------|------|
| MinutesEngine | enum | Template | Template / LLM 切替 |
| LlmModelPath | string | "" | GGUF モデルファイルパス |

#### ビルド方法

```bash
# 組み込みなし版（デフォルト）
dotnet build src/OnlineMeetingRecorder/OnlineMeetingRecorder.csproj

# LLM組み込み版
dotnet build src/OnlineMeetingRecorder/OnlineMeetingRecorder.csproj -p:EnableLlm=true
```

#### ファイル構成（v1.1 追加分）

```
Services/Minutes/
  LlmMinutesGenerator.cs           # LLM議事録生成（#if ENABLE_LLM）
  MinutesGeneratorDispatcher.cs    # 設定に基づくランタイム切替
```

## 設定管理

### ISettingsService / SettingsService

- 保存先: `%LOCALAPPDATA%/OnlineMeetingRecorder/settings.json`
- アプリ起動時に自動ロード
- 設定変更時に自動保存

### AppSettings モデル

| 項目 | 型 | デフォルト値 | 説明 |
|------|------|------|------|
| SttEngine | enum | Cloud | Local / Cloud 切替 |
| OpenAiApiKey | string | "" | OpenAI API キー |
| WhisperModelPath | string | "" | ローカルモデルファイルパス |
| Language | string | "ja" | 文字起こし言語 |
| MinutesEngine | enum | Template | Template / LLM 切替（v1.1） |
| LlmModelPath | string | "" | GGUF モデルファイルパス（v1.1） |

## UI設計

### MainWindow レイアウト

ヘッダー、デバイス選択、レベルメーター、録音コントロールに加え、
下部に **タブ付きパネル** を配置:

1. **セッション情報タブ**: セッション状態・保存先表示
2. **文字起こしタブ**: 文字起こし実行ボタン・進捗・結果表示
3. **議事録タブ**: 議事録生成ボタン・結果表示

ウィンドウサイズ: 560x720

### 設定ダイアログ

ヘッダーに歯車アイコンボタンを追加し、設定ウィンドウを表示:
- STTエンジン選択（ラジオボタン）
- OpenAI API キー入力
- Whisper モデルパス選択
- 言語設定
- 議事録エンジン選択（LLM組み込みビルド時のみ表示）
- LLMモデルパス選択（AI選択時のみ表示）

## NuGet パッケージ

| パッケージ | バージョン | 用途 |
|-----------|-----------|------|
| Whisper.net | 1.9.0 | ローカル文字起こし |
| Whisper.net.Runtime | 1.9.0 | Whisper CPU ランタイム |
| LLamaSharp | 0.26.0 | ローカルLLM推論（v1.1） |
| LLamaSharp.Backend.Cpu | 0.26.0 | llama.cpp CPU バックエンド（v1.1） |

## v1.1 実装仕様

### WAVヘッダ修復（R-08）

`WavHeaderRepairService` がクラッシュ等で破損したWAVファイルのヘッダを自動修復する。

- RIFFチャンクサイズとdataチャンクサイズをファイル実サイズから算出して修復
- `AudioRecorder.StopRecording()` 完了後に自動実行
- 最小WAVヘッダサイズ（44バイト）未満のファイルはスキップ

### 圧縮フォーマット変換（R-09）

`AudioConverter` にMP3/FLAC変換メソッドを追加。

- **MP3**: NAudio.Lame 2.1.0 でエンコード（16kHz mono 16-bit → LAME STANDARD プリセット）
- **FLAC**: Windows MediaFoundation FLAC エンコーダー（Windows 10+）
- `AppSettings.AudioExportFormat` (WAV/MP3/FLAC) で設定、設定画面から選択可能

### 音声再生（P-01）

`IAudioPlaybackService` / `AudioPlaybackService` で NAudio WaveOutEvent による再生を提供。

- Load/Play/Pause/Stop/Seek 操作
- 30fps タイマーで再生位置を通知
- マイク/スピーカートラック切替対応
- `PlaybackViewModel` が再生状態・位置・トラック選択を管理

### 波形表示（P-02）

`WaveformControl` カスタムコントロールで波形を描画。

- WAVファイルからPCMデータをロードし2000ポイントに間引き
- Toki-Pink で中央線から上下対称に波形バーを描画
- 白い垂直線 + ノブで再生位置インジケーターを表示
- マウスドラッグでシーク操作
- 話者セグメント情報のオーバーレイ（マイク: Toki-Pink、スピーカー: 青系）

### シークスライダー（P-03）

`DarkSlider` スタイルで Toki-Pink のシークバーを提供。

- `PlaybackViewModel.CurrentPositionSeconds` と双方向バインド
- 波形コントロールのマウスシークとも同期

### 文字起こし同期表示（P-04）

再生タブ下部に `TranscriptSegment` を発言バブルとして一覧表示。

- 各セグメントにタイムスタンプ・話者アイコン・テキストを表示
- セグメントクリックで該当位置にシーク + 再生開始
- `PlaybackViewModel.HighlightedSegmentIndex` で再生位置に対応するセグメントを追跡

### 話者位置の可視化（P-05）

- 波形上: マイク発言区間を下半分（Toki-Pink 薄）、スピーカー発言区間を上半分（青系 薄）でオーバーレイ
- 発言バブル: 話者アイコン（🎙/🔊）と背景色で区別
  - マイク（自分）: `#2A2A2A`
  - スピーカー（相手）: `#2D2028`（Toki-Pink薄暗）

### AppSettings 追加項目（v1.1）

| 項目 | 型 | デフォルト値 | 説明 |
|------|------|------|------|
| AudioExportFormat | enum | WAV | WAV / MP3 / FLAC 切替 |

### NuGet パッケージ追加（v1.1）

| パッケージ | バージョン | 用途 |
|-----------|-----------|------|
| NAudio.Lame | 2.1.0 | MP3 エンコード |

## ファイル構成

```
Models/
  AppSettings.cs              # 設定モデル（AudioExportFormat 追加）

Services/Audio/
  IAudioPlaybackService.cs    # 再生インターフェース（v1.1）
  AudioPlaybackService.cs     # NAudio WaveOutEvent 再生実装（v1.1）
  WavHeaderRepairService.cs   # WAVヘッダ修復（v1.1）

Services/Session/
  ISessionService.cs          # セッション管理インターフェース
  SessionService.cs           # セッション管理実装

Services/Settings/
  ISettingsService.cs         # 設定管理インターフェース
  SettingsService.cs          # 設定管理実装

Services/Transcription/
  ITranscriptionService.cs    # 文字起こしインターフェース
  CloudWhisperService.cs      # OpenAI Whisper API
  LocalWhisperService.cs      # Whisper.net ローカル
  AudioConverter.cs           # 音声フォーマット変換（MP3/FLAC 追加）

Services/Minutes/
  IMinutesGenerator.cs              # 議事録生成インターフェース
  TemplateMinutesGenerator.cs       # テンプレート議事録生成
  LlmMinutesGenerator.cs            # LLM議事録生成（#if ENABLE_LLM）
  MinutesGeneratorDispatcher.cs     # ランタイム切替ディスパッチャ

ViewModels/
  PlaybackViewModel.cs        # 再生VM（v1.1）
  TranscriptionViewModel.cs   # 文字起こしVM
  SettingsViewModel.cs        # 設定VM

Controls/
  WaveformControl.xaml/.cs    # 波形表示コントロール（v1.1）

Views/
  SettingsWindow.xaml          # 設定ウィンドウ
```
