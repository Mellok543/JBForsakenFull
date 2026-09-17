using CounterStrikeSharp.API.Core;

namespace JBF.Api;

public sealed record JailbreakMenuOption
{
    public string Text { get; init; }
    public Action<CCSPlayerController> OnSelect { get; init; }
    public bool IsDisabled { get; init; }
    public string? DisabledReason { get; init; }

    // Preserve the original 3-argument constructor for already compiled modules
    // such as JBF.Shop, JBF.LR and JBF.SpecialDays.
    public JailbreakMenuOption(
        string text,
        Action<CCSPlayerController> onSelect,
        bool isDisabled = false)
        : this(text, onSelect, isDisabled, null)
    {
    }

    public JailbreakMenuOption(
        string text,
        Action<CCSPlayerController> onSelect,
        bool isDisabled,
        string? disabledReason)
    {
        Text = text;
        OnSelect = onSelect;
        IsDisabled = isDisabled;
        DisabledReason = disabledReason;
    }
}
