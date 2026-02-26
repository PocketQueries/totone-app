using System.IO;
using System.Text;

namespace OnlineMeetingRecorder.Services.Audio;

/// <summary>
/// クラッシュ等で破損したWAVファイルのヘッダを修復するサービス。
/// NAudioのWaveFileWriterが正常にDisposeされなかった場合、
/// RIFFチャンクサイズとdataチャンクサイズが0のままになる。
/// ファイルの実サイズから正しいサイズを算出してヘッダを修復する。
/// </summary>
public static class WavHeaderRepairService
{
    // WAV の RIFF/data サイズは 32-bit（最大 ~4GB）
    private const long MaxWavFileSize = uint.MaxValue;

    /// <summary>
    /// WAVファイルのヘッダを検証し、破損している場合は修復する。
    /// </summary>
    /// <returns>修復が行われた場合はtrue</returns>
    public static bool TryRepair(string wavFilePath)
    {
        if (!File.Exists(wavFilePath))
            return false;

        var fileInfo = new FileInfo(wavFilePath);
        if (fileInfo.Length < 44) // 最小WAVヘッダサイズ
            return false;

        // WAV の RIFF サイズは 32-bit のため、4GB 超のファイルは修復不可
        if (fileInfo.Length > MaxWavFileSize)
            return false;

        try
        {
            // 修復内容を事前に計算してからファイルに書き込む（部分破損防止）
            long riffSizeOffset;
            int expectedRiffSize;
            long dataSizeOffset = -1;
            int expectedDataSize = 0;

            using (var readStream = new FileStream(wavFilePath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(readStream, Encoding.ASCII, leaveOpen: true))
            {
                // RIFFヘッダ検証
                var riffId = new string(reader.ReadChars(4));
                if (riffId != "RIFF")
                    return false;

                var riffSize = reader.ReadInt32();
                var waveId = new string(reader.ReadChars(4));
                if (waveId != "WAVE")
                    return false;

                riffSizeOffset = 4;
                expectedRiffSize = (int)(readStream.Length - 8);
                var needsRepair = riffSize != expectedRiffSize;

                // fmtチャンクを探す
                var fmtId = new string(reader.ReadChars(4));
                if (fmtId != "fmt ")
                    return false;

                var fmtSize = reader.ReadInt32();
                readStream.Seek(fmtSize, SeekOrigin.Current);

                // dataチャンクを探す（他のチャンクをスキップ）
                while (readStream.Position + 8 <= readStream.Length)
                {
                    var chunkId = new string(reader.ReadChars(4));
                    var chunkSizePosition = readStream.Position;
                    var chunkSize = reader.ReadInt32();

                    if (chunkId == "data")
                    {
                        dataSizeOffset = chunkSizePosition;
                        expectedDataSize = (int)(readStream.Length - readStream.Position);
                        if (chunkSize != expectedDataSize)
                            needsRepair = true;
                        break;
                    }

                    // 他のチャンクはスキップ
                    if (chunkSize > 0 && readStream.Position + chunkSize <= readStream.Length)
                        readStream.Seek(chunkSize, SeekOrigin.Current);
                    else
                        break;
                }

                if (!needsRepair || dataSizeOffset < 0)
                    return false;
            }

            // 読み取り専用ストリームを閉じた後に書き込み
            using (var writeStream = new FileStream(wavFilePath, FileMode.Open, FileAccess.Write))
            using (var writer = new BinaryWriter(writeStream, Encoding.ASCII, leaveOpen: true))
            {
                // RIFFチャンクサイズを修復
                writeStream.Position = riffSizeOffset;
                writer.Write(expectedRiffSize);

                // dataチャンクサイズを修復
                writeStream.Position = dataSizeOffset;
                writer.Write(expectedDataSize);

                writer.Flush();
            }

            return true;
        }
        catch
        {
            // 修復失敗は無視（読み取りと書き込みを分離しているため部分破損リスクは最小限）
            return false;
        }
    }

    /// <summary>
    /// セッションフォルダ内の全WAVファイルを検証・修復する。
    /// </summary>
    public static (int checked_, int repaired) RepairSessionWavFiles(string sessionFolder)
    {
        var audioFolder = Path.Combine(sessionFolder, "audio");
        if (!Directory.Exists(audioFolder))
            return (0, 0);

        var wavFiles = Directory.GetFiles(audioFolder, "*.wav");
        var repaired = 0;

        foreach (var wavFile in wavFiles)
        {
            if (TryRepair(wavFile))
                repaired++;
        }

        return (wavFiles.Length, repaired);
    }
}
