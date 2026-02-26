namespace OnlineMeetingRecorder.Models;

/// <summary>
/// オーディオレベル計測データ
/// </summary>
public readonly struct AudioLevelData
{
    /// <summary>ピークレベル (0.0 ~ 1.0+)</summary>
    public float Peak { get; init; }

    /// <summary>RMSレベル (0.0 ~ ~0.7)</summary>
    public float Rms { get; init; }

    /// <summary>ピークレベル (dB)</summary>
    public float PeakDb { get; init; }

    /// <summary>RMSレベル (dB)</summary>
    public float RmsDb { get; init; }

    /// <summary>クリッピング検知</summary>
    public bool IsClipping { get; init; }

    /// <summary>無音検知</summary>
    public bool IsSilent { get; init; }

    public static AudioLevelData Empty => new()
    {
        Peak = 0f,
        Rms = 0f,
        PeakDb = -100f,
        RmsDb = -100f,
        IsClipping = false,
        IsSilent = true
    };
}
