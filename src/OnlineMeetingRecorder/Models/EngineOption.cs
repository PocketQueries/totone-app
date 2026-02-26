namespace OnlineMeetingRecorder.Models;

/// <summary>
/// エンジン選択ComboBox用のモデル
/// </summary>
public class EngineOption<TEnum> where TEnum : Enum
{
    public required TEnum Value { get; init; }
    public required string DisplayName { get; init; }

    public override string ToString() => DisplayName;
}
