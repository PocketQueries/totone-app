using System.Diagnostics;
using LLama;
using LLama.Common;
using LLama.Sampling;

namespace LlmPrototype;

class Program
{
    static async Task Main(string[] args)
    {
        // モデルパスの取得: コマンドライン引数 or 対話入力
        var modelPath = args.Length > 0 ? args[0] : PromptForModelPath();

        if (!File.Exists(modelPath))
        {
            Console.Error.WriteLine($"エラー: モデルファイルが見つかりません: {modelPath}");
            return;
        }

        var totalSw = Stopwatch.StartNew();
        Console.WriteLine($"モデル読み込み中: {modelPath}");
        Console.WriteLine("（初回読み込みには数十秒かかる場合があります）");
        var loadSw = Stopwatch.StartNew();

        var gpuLayers = args.Length > 1 && int.TryParse(args[1], out var g) ? g : 999;

        var modelParams = new ModelParams(modelPath)
        {
            ContextSize = 4096,
            GpuLayerCount = gpuLayers,
        };

        using var model = LLamaWeights.LoadFromFile(modelParams);
        var executor = new StatelessExecutor(model, modelParams);

        loadSw.Stop();
        Console.WriteLine($"モデル読み込み完了。（{loadSw.Elapsed.TotalSeconds:F1}秒）");
        Console.WriteLine("---");

        var sampleTranscript = GetSampleTranscript();

        Console.WriteLine("=== 入力トランスクリプト ===");
        Console.WriteLine(sampleTranscript);
        Console.WriteLine();
        Console.WriteLine("=== 議事録生成中... ===");
        Console.WriteLine();

        var prompt = BuildMinutesPrompt(sampleTranscript);

        var inferenceParams = new InferenceParams
        {
            MaxTokens = 1024,
            SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.3f },
            AntiPrompts = ["<|im_end|>", "<|im_start|>"],
        };

        // ストリーミング出力（<think>ブロックをフィルタリング）
        var inferSw = Stopwatch.StartNew();
        var tokenCount = 0;
        var insideThink = false;
        var buffer = "";
        await foreach (var token in executor.InferAsync(prompt, inferenceParams))
        {
            tokenCount++;
            buffer += token;

            // <think> 開始検出
            if (!insideThink && buffer.Contains("<think>"))
            {
                // <think> より前のテキストがあれば出力
                var idx = buffer.IndexOf("<think>");
                if (idx > 0)
                    Console.Write(buffer[..idx]);
                insideThink = true;
                buffer = buffer[(idx + "<think>".Length)..];
                continue;
            }

            // </think> 終了検出
            if (insideThink && buffer.Contains("</think>"))
            {
                insideThink = false;
                buffer = buffer[(buffer.IndexOf("</think>") + "</think>".Length)..];
                // 残りがあれば出力
                if (buffer.Length > 0)
                {
                    Console.Write(buffer);
                    buffer = "";
                }
                continue;
            }

            // think内はバッファに溜めるだけ（捨てる）
            if (insideThink)
            {
                // バッファが大きくなりすぎないよう末尾だけ保持
                if (buffer.Length > 100)
                    buffer = buffer[^20..];
                continue;
            }

            // 通常出力（タグの途中かもしれないので少し待つ）
            if (!buffer.Contains('<'))
            {
                Console.Write(buffer);
                buffer = "";
            }
            else if (buffer.Length > 20)
            {
                // < があるが長いのでタグではない
                Console.Write(buffer);
                buffer = "";
            }
        }
        // 残りのバッファを出力
        if (!insideThink && buffer.Length > 0)
            Console.Write(buffer);

        inferSw.Stop();
        totalSw.Stop();

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine("=== 生成完了 ===");
        Console.WriteLine();
        Console.WriteLine("--- パフォーマンス ---");
        Console.WriteLine($"モデル読み込み: {loadSw.Elapsed.TotalSeconds:F1}秒");
        Console.WriteLine($"推論時間:       {inferSw.Elapsed.TotalSeconds:F1}秒");
        Console.WriteLine($"トークン数:     {tokenCount}");
        Console.WriteLine($"速度:           {tokenCount / inferSw.Elapsed.TotalSeconds:F1} tokens/sec");
        Console.WriteLine($"合計時間:       {totalSw.Elapsed.TotalSeconds:F1}秒");
    }

    static string PromptForModelPath()
    {
        Console.Write("GGUFモデルファイルのパスを入力してください: ");
        return Console.ReadLine()?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// サンプル会議文字起こしデータ。
    /// TranscriptSegment.ToString() フォーマット: [mm:ss - mm:ss] 話者: テキスト
    /// </summary>
    static string GetSampleTranscript()
    {
        return """
            [00:00 - 00:14] 自分: それでは定例ミーティングを始めます。今日の議題は来月のリリーススケジュールについてです。
            [00:15 - 00:34] 相手: はい、よろしくお願いします。まず現在の進捗ですが、バックエンドAPIの実装は予定通り今週中に完了する見込みです。
            [00:35 - 00:39] 自分: フロントエンドの方はどうですか。
            [00:40 - 00:59] 相手: フロントエンドは少し遅れています。認証周りの実装で想定外の課題が出ていまして、あと3日ほど追加が必要です。
            [01:00 - 01:09] 自分: 3日の遅れですね。それだとテスト期間に影響が出ますか。
            [01:10 - 01:29] 相手: テスト開始を3月10日から13日にずらす必要があります。ただし、テスト自体の期間は短縮せず1週間確保したいです。
            [01:30 - 01:44] 自分: 了解しました。では、リリース日は当初の3月20日から3月23日に変更ということでいいですか。
            [01:45 - 01:59] 相手: はい、その方向でお願いします。あと、テスト環境のサーバー増設について、インフラチームに依頼を出しておきたいのですが。
            [02:00 - 02:09] 自分: それは田中さんに依頼してください。予算は承認済みです。
            [02:10 - 02:19] 相手: 承知しました。今週中にインフラチームの田中さんに連絡します。
            [02:20 - 02:24] 自分: 他に何かありますか。
            [02:25 - 02:29] 相手: 特にありません。次回は来週の同じ時間でよろしいですか。
            [02:30 - 02:34] 自分: はい、来週もこの時間で。では本日は以上です。ありがとうございました。
            [02:35 - 02:37] 相手: ありがとうございました。
            """;
    }

    /// <summary>
    /// Qwen3 ChatML形式でプロンプトを構築。
    /// /no_think で思考モードを無効化し、直接的な議事録出力を得る。
    /// </summary>
    static string BuildMinutesPrompt(string transcript)
    {
        var systemPrompt = """
            あなたは会議の議事録を作成するアシスタントです。
            以下の会議の文字起こしを読み、議事録をMarkdown形式で作成してください。

            議事録には以下のセクションを含めてください：
            1. **要約** - 会議全体の概要（2-3文）
            2. **決定事項** - 会議で決まったことのリスト
            3. **アクションアイテム** - 担当者と期限を含むTODOリスト
            4. **次回予定** - 次回の会議予定

            簡潔かつ正確に記述してください。
            """;

        // /no_think はユーザーメッセージ末尾に配置するのがQwen3の正式な使い方
        return $"<|im_start|>system\n{systemPrompt.Trim()}<|im_end|>\n<|im_start|>user\n以下の会議文字起こしから議事録を作成してください。\n\n{transcript.Trim()} /no_think<|im_end|>\n<|im_start|>assistant\n";
    }
}
