# Online Meeting Recorder

Windows 向けのオンラインミーティング録音・文字起こしアプリケーション。
Teams / Zoom / Google Meet などのミーティング音声を録音し、タイムスタンプ付きで文字起こし・議事録生成ができます。

**MVP + v1.1〜v1.7 全機能実装済み。**

## 主な機能

- **デュアルトラック録音** - マイク入力とスピーカー出力を別ファイルに同時録音（WASAPI SharedMode）
- **リアルタイム可視化** - レベルメーター、dB 表示、ヘルスステータス（無音・クリッピング・データ停止の検知）
- **デバイス管理** - 入出力デバイスの一覧表示・切替・ホットプラグ検知
- **文字起こし** - Whisper.net（ローカル）/ OpenAI Whisper API（クラウド）によるタイムスタンプ付き文字起こし
- **議事録生成** - テンプレートベースの議事録 + AI議事録（ローカルLLM / OpenAI API）
- **ととのえ機能** - 参加者・会議目的等のコンテキスト情報を追加して議事録をブラッシュアップ（結果は別ファイル保存）
- **プロンプト編集** - 議事録・ととのえ生成時のAIプロンプトを確認・編集可能
- **音声再生** - マイク/スピーカートラック個別再生、波形表示、シークスライダー
- **文字起こし同期表示** - 再生位置に対応する発言バブルのハイライト、セグメントクリックでシーク
- **話者可視化** - 話者アイコン・背景色・波形オーバーレイで自分/相手を区別
- **WAVヘッダ修復** - クラッシュ時の不正WAVヘッダを自動修復
- **圧縮フォーマット変換** - WAV → MP3/FLAC への変換
- **セッション管理** - 録音セッションの一覧表示・名前編集・削除・メタデータ自動保存
- **録音後自動処理** - 録音終了後に文字起こし→議事録生成を自動実行
- **設定管理** - STTエンジン・議事録エンジン・音声フォーマット・APIキー・言語設定をGUIから設定可能
- **システムトレイ常駐** - タスクトレイに常駐し録音状態をアイコン表示、コンテキストメニューで録音操作、最小化時トレイ格納
- **会議アプリ自動検知** - Zoom / Teams / Google Meet / Webex のプロセスを監視し、会議開始をバルーン通知。クリックで録音を自動開始
- **処理キャンセル** - 文字起こし・議事録生成・ととのえの処理を途中でキャンセル可能
- **経過時間表示** - 文字起こし・議事録生成中にリアルタイムで経過時間を表示、完了時に最終経過時間を表示
- **トークン消費量表示** - AI利用時（OpenAI API / ローカルLLM）に消費トークン数を表示
- **GPU アクセラレーション** - CUDA 12 対応GPUでローカル文字起こし・LLM議事録生成を高速化
- **オフライン動作** - ネットワーク不要で録音・ローカル文字起こしが完結

## 技術スタック

| カテゴリ | 技術 |
|----------|------|
| フレームワーク | .NET 8.0 (WPF) |
| 言語 | C# |
| 音声キャプチャ・再生 | NAudio 2.2.1 (WASAPI) + NAudio.Lame 2.1.0 (MP3) |
| ローカルSTT | Whisper.net 1.9.0 + Whisper.net.Runtime 1.9.0 |
| クラウドSTT | OpenAI Whisper API |
| ローカルLLM | LLamaSharp 0.26.0 + Qwen3 GGUF（LLM組み込みビルド時） |
| MVVM | CommunityToolkit.Mvvm 8.4.0 |
| DI | Microsoft.Extensions.Hosting 8.0.1 |

## 必要環境

- Windows 10 以降
- .NET 8.0 SDK

## ビルド・実行

```bash
# 標準ビルド（LLMなし）
dotnet build src/OnlineMeetingRecorder/OnlineMeetingRecorder.csproj

# LLM組み込みビルド（AI議事録生成対応）
dotnet build src/OnlineMeetingRecorder/OnlineMeetingRecorder.csproj -p:EnableLlm=true

# 実行
dotnet run --project src/OnlineMeetingRecorder/OnlineMeetingRecorder.csproj

# 配布用exeビルド（フレームワーク依存型、LLM含む、win-x64）
dotnet publish src/OnlineMeetingRecorder/OnlineMeetingRecorder.csproj -c Release -r win-x64 --self-contained false -p:EnableLlm=true -o publish
```

## セットアップ

### クラウド文字起こし（OpenAI Whisper API）

1. [OpenAI](https://platform.openai.com/) で API キーを取得
2. アプリの設定画面（歯車アイコン）で「クラウド」を選択し、API キーを入力

### ローカル文字起こし（Whisper.net）

1. GGML 形式の Whisper モデルファイルをダウンロード（例: `ggml-base.bin`, `ggml-small.bin`）
   - [Hugging Face - ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp/tree/main) から取得可能
2. アプリの設定画面で「ローカル」を選択し、モデルファイルのパスを指定

## AI議事録生成（LLM組み込みビルド）

LLM組み込みビルド（`-p:EnableLlm=true`）では、AI による議事録生成が利用できます。

1. GGUF 形式の LLM モデルファイルをダウンロード
   - 推奨: [Qwen3-4B-GGUF](https://huggingface.co/Qwen/Qwen3-4B-GGUF)（Q4_K_M、~2.4GB）
2. アプリの設定画面で「AI（ローカルLLM）」を選択し、モデルファイルのパスを指定

### プロトタイプ

`src/LlmPrototype/` に動作検証用コンソールアプリがあります。

```bash
dotnet run --project src/LlmPrototype/LlmPrototype.csproj -- "D:\models\Qwen3-4B-Q4_K_M.gguf"
```

## ドキュメント

| 資料 | 内容 |
|------|------|
| [architecture.md](docs/architecture.md) | アーキテクチャ設計・技術選定・レイヤー構成 |
| [features.md](docs/features.md) | 機能一覧・MVP スコープ・実装フェーズ |
| [data-flow.md](docs/data-flow.md) | データフロー・シーケンス図・スレッドモデル |
| [mvp-implementation.md](docs/mvp-implementation.md) | 実装設計書（MVP + v1.1 LLM議事録仕様） |
| [design-guide.md](docs/design-guide.md) | UI/UXデザインガイド（Totonoe コンセプト・カラー・コンポーネント仕様） |
| [setup-guide.md](docs/setup-guide.md) | 配布版セットアップ手順書（ランタイム・モデルDL・初期設定） |

## 注意事項

- **録音の合法性**: 会議の録音は、地域や国の法律により参加者の同意が必要な場合があります。利用者の責任において、適用される法律を遵守してください。
- **文字起こし精度**: STT（音声認識）の出力精度は保証されません。重要な内容は必ず元の音声で確認してください。
- **データ管理**: 録音データ・文字起こし結果・議事録はすべてローカルに保存されます。機密情報を含む場合は、利用者の責任で適切に管理してください。
- **APIキーの管理**: OpenAI API キーはローカルの設定ファイルに保存されます。共有PCでの利用時は取り扱いにご注意ください。

## ライセンス

Copyright 2026 Pocket Queries Inc.

[Apache License 2.0](LICENSE) の下でライセンスされています。詳細は [LICENSE](LICENSE) ファイルを参照してください。
