namespace OnlineMeetingRecorder.Models;

/// <summary>
/// 録音ヘルスステータス
/// </summary>
public enum HealthStatus
{
    /// <summary>正常に音声データ受信中</summary>
    Healthy,

    /// <summary>RMSが閾値以下の状態が継続</summary>
    Silent,

    /// <summary>ピークが0.99以上</summary>
    Clipping,

    /// <summary>DataAvailableが500ms以上発火なし</summary>
    DataStalled,

    /// <summary>デバイス切断</summary>
    DeviceDisconnected,

    /// <summary>WAVファイル書き込みエラー</summary>
    WriteError
}
