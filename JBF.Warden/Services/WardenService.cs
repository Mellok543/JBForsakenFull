using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using JBF.Api;

namespace JBF.Warden.Services;

internal sealed class WardenService
{
    private static readonly Color WardenColor = Color.FromArgb(255, 255, 190, 45);
    private static readonly Color NormalColor = Color.White;

    public CCSPlayerController? Warden { get; private set; }

    public event Action<CCSPlayerController>? WardenClaimed;
    public event Action<WardenKilledEvent>? WardenKilled;

    public bool IsWarden(CCSPlayerController player)
    {
        return Warden is not null && Warden.IsValid && Warden.Slot == player.Slot;
    }

    public bool TryClaim(CCSPlayerController player)
    {
        if (Warden is not null && Warden.IsValid)
            return IsWarden(player);

        Warden = player;
        SetRenderColor(player, WardenColor);
        WardenClaimed?.Invoke(player);

        Server.PrintToChatAll(JailbreakChat.Format($"Командиром стал {player.PlayerName}."));
        foreach (var target in Utilities.GetPlayers().Where(p => p is { IsValid: true, IsBot: false }))
            UiCapability.Api.Get()?.Notify(target, $"Командир: {player.PlayerName}", UiNotificationType.Important, 4.0f);

        return true;
    }

    public bool TryResign(CCSPlayerController player)
    {
        if (!IsWarden(player))
            return false;

        SetRenderColor(player, NormalColor);
        Warden = null;
        Server.PrintToChatAll(JailbreakChat.Format($"{player.PlayerName} покинул пост командира."));
        return true;
    }

    public void NotifyKilled(CCSPlayerController warden, CCSPlayerController? attacker, string weapon)
    {
        WardenKilled?.Invoke(new WardenKilledEvent(warden, attacker, weapon));
        Server.PrintToChatAll(JailbreakChat.Format($"Командир {warden.PlayerName} погиб."));
    }

    public void Reset()
    {
        if (Warden is { IsValid: true })
            SetRenderColor(Warden, NormalColor);

        Warden = null;
    }

    private static void SetRenderColor(CCSPlayerController player, Color color)
    {
        if (!player.IsValid || !player.PawnIsAlive || player.PlayerPawn.Value is not { IsValid: true } pawn)
            return;

        pawn.Render = color;
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
    }
}
