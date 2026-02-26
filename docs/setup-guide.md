# Online Meeting Recorder セットアップ手順書

本書では、配布された Online Meeting Recorder の初回セットアップ手順を説明します。

## 動作要件

| 項目 | 要件 |
|------|------|
| OS | Windows 10 以降（64bit） |
| ランタイム | .NET 8.0 Desktop Runtime |
| メモリ | 4GB 以上（ローカルLLM使用時は 8GB 以上推奨） |
| GPU（推奨） | NVIDIA CUDA 対応GPU + CUDA Toolkit 13.x（ローカルSTT/LLMの高速化） |
| ストレージ | アプリ本体: 約30MB + モデルファイル（後述） |

## 1. .NET 8.0 Desktop Runtime のインストール

本アプリの実行には **.NET 8.0 Desktop Runtime** が必要です。

1. 以下の公式サイトにアクセスします
   - https://dotnet.microsoft.com/ja-jp/download/dotnet/8.0
2. 「.NET Desktop Runtime 8.0.x」の **Windows x64** インストーラをダウンロードします
   - 「ASP.NET Core Runtime」や「.NET Runtime」ではなく **Desktop Runtime** を選んでください
3. ダウンロードしたインストーラ（`windowsdesktop-runtime-8.0.x-win-x64.exe`）を実行します
4. インストール完了後、コマンドプロンプトで以下を実行して確認します

```
dotnet --list-runtimes
```

`Microsoft.WindowsDesktop.App 8.0.x` が表示されていれば OK です。

## 2. アプリの配置

1. 配布された `OnlineMeetingRecorder` フォルダを任意の場所に配置します
   - 例: `C:\Tools\OnlineMeetingRecorder\`
2. フォルダ内の `OnlineMeetingRecorder.exe` をダブルクリックして起動します
3. 必要に応じてデスクトップにショートカットを作成してください

## 3. モデルファイルのダウンロードと設定

アプリは3種類のAIエンジンに対応しており、利用したい機能に応じてモデルのセットアップが異なります。

### 利用パターン早見表

| やりたいこと | 必要なセットアップ |
|-------------|-------------------|
| クラウドで文字起こし | [3-A] OpenAI APIキーの設定 |
| オフラインで文字起こし | [3-B] Whisperモデルのダウンロード |
| AIで議事録を自動生成（クラウド） | [3-A] OpenAI APIキーの設定 |
| AIで議事録を自動生成（オフライン） | [3-C] LLMモデルのダウンロード |
| ローカル処理をGPUで高速化 | [3-D] CUDA Toolkit のインストール |
| 録音のみ（文字起こし・議事録不要） | セットアップ不要 |

---

### 3-A. OpenAI APIキーの設定（クラウド機能を使う場合）

クラウド文字起こし（Whisper API）またはクラウドAI議事録生成を利用するには、OpenAI APIキーが必要です。

1. [OpenAI Platform](https://platform.openai.com/) にアクセスし、アカウントを作成またはログインします
2. [APIキーの管理画面](https://platform.openai.com/api-keys) で新しいAPIキーを作成します
3. アプリを起動し、左上の **歯車アイコン（設定）** をクリックします
4. 「OpenAI APIキー」欄にコピーしたAPIキーを貼り付けます
5. STTエンジンで「クラウド」を選択します
6. （議事録生成もクラウドで行う場合）議事録エンジンで「クラウドAPI」を選択します

> **注意**: APIの利用には OpenAI の従量課金が発生します。料金は [OpenAI Pricing](https://openai.com/pricing) を確認してください。

---

### 3-B. Whisperモデルのダウンロード（ローカル文字起こしを使う場合）

オフラインで文字起こしを行うには、Whisper の GGML 形式モデルファイルが必要です。

#### モデルの選択

| モデル名 | ファイルサイズ | 精度 | 速度 | 推奨用途 |
|---------|-------------|------|------|---------|
| `ggml-base.bin` | 約 140MB | 標準 | 高速 | 手軽に使いたい場合 |
| `ggml-small.bin` | 約 460MB | 高い | 普通 | 精度と速度のバランス重視 |
| `ggml-medium.bin` | 約 1.5GB | 高精度 | 遅い | 精度を重視する場合 |

#### ダウンロード手順

1. 以下のページにアクセスします
   - https://huggingface.co/ggerganov/whisper.cpp/tree/main
2. 使用したいモデルファイル（例: `ggml-base.bin`）をクリックし、「download」でダウンロードします
3. ダウンロードしたファイルを任意のフォルダに配置します
   - 例: `C:\Models\whisper\ggml-base.bin`

#### アプリでの設定

1. アプリを起動し、**歯車アイコン（設定）** をクリックします
2. STTエンジンで **「ローカル」** を選択します
3. 「Whisperモデルパス」欄にダウンロードしたモデルファイルのフルパスを入力します
   - 例: `C:\Models\whisper\ggml-base.bin`

---

### 3-C. LLMモデルのダウンロード（ローカルAI議事録を使う場合）

オフラインでAI議事録生成を行うには、GGUF 形式の LLM モデルファイルが必要です。

#### 推奨モデル

| モデル名 | ファイルサイズ | メモリ使用量 | 推奨用途 |
|---------|-------------|------------|---------|
| Qwen3-4B-Q4_K_M.gguf | 約 2.4GB | 約 4GB | 推奨（バランス重視） |
| Qwen3-1.7B-Q8_0.gguf | 約 1.7GB | 約 3GB | 軽量・高速処理重視 |

#### ダウンロード手順

1. 以下のページにアクセスします
   - https://huggingface.co/Qwen/Qwen3-4B-GGUF
2. 「Files and versions」タブを開きます
3. `Qwen3-4B-Q4_K_M.gguf` をクリックし、「download」でダウンロードします
4. ダウンロードしたファイルを任意のフォルダに配置します
   - 例: `C:\Models\llm\Qwen3-4B-Q4_K_M.gguf`

#### アプリでの設定

1. アプリを起動し、**歯車アイコン（設定）** をクリックします
2. 議事録エンジンで **「AI（ローカルLLM）」** を選択します
3. 「LLMモデルパス」欄にダウンロードしたモデルファイルのフルパスを入力します
   - 例: `C:\Models\llm\Qwen3-4B-Q4_K_M.gguf`

> **注意**: 初回のモデル読み込みには数十秒かかる場合があります。2回目以降はキャッシュにより高速化されます。

---

### 3-D. CUDA Toolkit のインストール（GPU高速化を使う場合）

ローカル文字起こし・ローカルLLM議事録生成をGPUで高速化するには、**NVIDIA CUDA Toolkit** のインストールが必要です。

> **注意**: NVIDIAドライバのみでは不十分です。`nvidia-smi` でCUDAバージョンが表示されても、CUDA Toolkit がなければGPU推論は利用できません（CPUフォールバックで動作します）。

#### 必要なバージョン

| ライブラリ | 必要な CUDA Toolkit | 必要な NVIDIA ドライバー |
|-----------|-------------------|----------------------|
| Whisper.net.Runtime.Cuda 1.9.0（ローカル文字起こし） | **CUDA Toolkit 13.0.1 以上** | **580 以上** |
| LLamaSharp.Backend.Cuda12 0.26.0（ローカルLLM議事録） | CUDA Toolkit 12.4.1 以上 | 525 以上 |

> **重要**: ローカル文字起こし（Whisper）のGPU高速化には **CUDA Toolkit 13 以上** および **NVIDIA ドライバー 580 以上** の両方が必要です。CUDA Toolkit 13 は `nvcudart_hybrid64.dll` をドライバー経由で提供するため、ドライバーが古い場合は DLL ロードに失敗し CPU フォールバックで動作します。ドライバーバージョンは `nvidia-smi` の出力で確認できます。

#### インストール手順

1. 以下のページにアクセスします
   - https://developer.nvidia.com/cuda-downloads
2. OS: Windows、Architecture: x86_64、Version: お使いのWindows版、Installer Type: exe (local) を選択します
3. ダウンロードしたインストーラを実行します（「Express」インストールでOK）
4. インストール完了後、PCを再起動します
5. コマンドプロンプトで以下を実行して確認します

```
nvcc --version
```

`Cuda compilation tools, release 13.x` のような表示が出れば OK です。

#### GPU高速化の確認

- アプリ起動後、ローカルLLM議事録生成やローカル文字起こしを実行します
- タスクマネージャーの「パフォーマンス」タブでGPU使用率が上がることを確認してください
- CUDA Toolkit がない場合やバージョンが不足している場合はCPUで動作します（エラーにはなりません）
- Debug出力（Visual Studio の Output 窓）に `[Whisper] Loaded runtime: Cuda` と表示されればGPUが使われています

---

## 4. 基本的な使い方

1. **録音開始**: メイン画面の録音ボタンをクリックします
2. **デバイス選択**: マイクとスピーカーのデバイスをドロップダウンから選択できます
3. **録音停止**: 録音ボタンを再度クリックして停止します
4. **文字起こし**: 録音停止後、自動的に文字起こしが実行されます
5. **議事録生成**: 文字起こし完了後、議事録生成ボタンで議事録を作成できます
6. **セッション管理**: 過去の録音セッションは左側のセッション一覧から再表示できます

### 処理中の操作

- **キャンセル**: 文字起こし・議事録生成・ととのえの処理中に「キャンセル」ボタンで中断できます
- **経過時間**: 処理中はリアルタイムで経過時間が表示されます（例: `AI議事録を生成中...（ローカルLLM）（経過: 15秒）`）
- **トークン消費量**: AI（OpenAI API / ローカルLLM）利用時は、完了ステータスに消費トークン数が表示されます（例: `議事録の生成が完了しました。（OpenAI API / 15秒 / 1,234トークン）`）

## 5. データの保存場所

| データ | 保存先 |
|--------|--------|
| 設定ファイル | `%LOCALAPPDATA%\OnlineMeetingRecorder\settings.json` |
| 録音データ・文字起こし・議事録 | `%LOCALAPPDATA%\OnlineMeetingRecorder\Sessions\` |

> `%LOCALAPPDATA%` は通常 `C:\Users\{ユーザー名}\AppData\Local` です。
> エクスプローラのアドレスバーに `%LOCALAPPDATA%\OnlineMeetingRecorder` と入力するとフォルダを開けます。

## トラブルシューティング

### アプリが起動しない

- .NET 8.0 Desktop Runtime がインストールされているか確認してください
- コマンドプロンプトで `dotnet --list-runtimes` を実行し、`Microsoft.WindowsDesktop.App 8.0.x` が表示されるか確認してください

### 音声デバイスが表示されない

- Windows の「サウンド設定」で対象のマイク・スピーカーが有効になっているか確認してください
- アプリを再起動してみてください

### ローカル文字起こしでエラーが発生する

- モデルファイルのパスが正しいか確認してください
- モデルファイルが破損していないか確認してください（再ダウンロードを試してください）

### ローカルLLM議事録生成が遅い

- **GPU アクセラレーション**: 設定画面の「GPU LAYER COUNT」が `999`（全レイヤー）になっているか確認してください。`0` はCPUのみで動作します
- NVIDIA CUDA 12.x 対応ドライバがインストールされているか確認してください（`nvidia-smi` コマンドで確認可能）
- より軽量なモデル（Qwen3-1.7B）への変更を検討してください
- メモリ不足の場合、他のアプリケーションを閉じてメモリを確保してください

### GPU が使われていないようだ

- **NVIDIA ドライバーのバージョンを確認してください**（`nvidia-smi` コマンドで確認可能）
  - ローカル文字起こし（Whisper）: **ドライバー 580 以上** が必要（CUDA 13 対応ドライバー）
  - ドライバーが古い場合は https://www.nvidia.com/Download/index.aspx/ から最新版をダウンロードしてください
- **CUDA Toolkit のバージョンを確認してください**（`nvcc --version` で確認可能）
  - ローカル文字起こし（Whisper）: **CUDA Toolkit 13.0.1 以上** が必要
  - ローカルLLM（LLamaSharp）: CUDA Toolkit 12.4.1 以上が必要
- NVIDIAドライバだけでは不十分です → [3-D] を参照
- 設定画面の「GPU LAYER COUNT」が `0` になっていないか確認してください（LLM議事録生成用）
- タスクマネージャーの「パフォーマンス」タブでGPU使用率を確認できます
