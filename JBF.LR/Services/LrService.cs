using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.LR.Extensions;

namespace JBF.LR.Services;

internal sealed class LrService : ILrApi
{
    private readonly Dictionary<string, RegisteredLrGame> _games = new(StringComparer.OrdinalIgnoreCase);
    private ActiveLrMatch? _activeMatch;

    public event Action<LrMatchEndedEvent>? MatchEnded;

    public bool IsActive => _activeMatch is not null;

    public string? ActiveGameName => _activeMatch?.Game.Name;

    public CCSPlayerController? Inmate => _activeMatch?.Inmate;

    public CCSPlayerController? Guardian => _activeMatch?.Guardian;

    public IDisposable RegisterGame(ILrGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        if (string.IsNullOrWhiteSpace(game.Id))
        {
            throw new ArgumentException("LR game id cannot be empty.", nameof(game));
        }

        if (string.IsNullOrWhiteSpace(game.Name))
        {
            throw new ArgumentException("LR game name cannot be empty.", nameof(game));
        }

        var token = Guid.NewGuid();
        _games[game.Id] = new RegisteredLrGame(token, game);
        return new ActionDisposable(() => UnregisterGame(game.Id, token));
    }

    public void OpenMenu(CCSPlayerController player)
    {
        if (!CanOpenLr(player))
        {
            return;
        }

        var menuApi = MenuCapability.Api.Get();
        if (menuApi is null)
        {
            player.PrintToChat(JailbreakChat.Format("Menu API недоступно."));
            return;
        }

        if (_games.Count == 0)
        {
            player.PrintToChat(JailbreakChat.Format("Нет доступных LR-игр."));
            return;
        }

        var options = _games.Values
            .Select(registration => registration.Game)
            .OrderBy(game => game.Order)
            .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
            .Select(game => new JailbreakMenuOption(game.Name, inmate => OpenOpponentMenu(inmate, game)))
            .ToArray();

        menuApi.Open(player, "Last Request", options);
    }

    public bool TryEndActive()
    {
        if (_activeMatch is null)
        {
            return false;
        }

        EndActive(winner: null, announce: true);
        return true;
    }

    public HookResult HandleTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        if (_activeMatch is null || entity.DesignerName != "player")
        {
            return HookResult.Continue;
        }

        var victim = entity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        var attackerEntity = damageInfo.Attacker.Value;
        if (!victim.IsUsable() || attackerEntity?.DesignerName != "player")
        {
            return HookResult.Continue;
        }

        var attacker = attackerEntity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        if (!attacker.IsUsable())
        {
            return HookResult.Continue;
        }

        if (!IsParticipant(victim) && !IsParticipant(attacker))
        {
            return HookResult.Continue;
        }

        if (_activeMatch.Game.DisableAllDamage)
        {
            return HookResult.Handled;
        }

        var isDuelDamage =
            IsSamePlayer(attacker, _activeMatch.Inmate) && IsSamePlayer(victim, _activeMatch.Guardian) ||
            IsSamePlayer(attacker, _activeMatch.Guardian) && IsSamePlayer(victim, _activeMatch.Inmate);

        if (!isDuelDamage)
        {
            return HookResult.Handled;
        }

        return _activeMatch.Game.OnTakeDamage(entity, damageInfo);
    }

    public HookResult HandleBulletImpact(EventBulletImpact @event, GameEventInfo info)
    {
        return _activeMatch?.Game.OnBulletImpact(@event, info) ?? HookResult.Continue;
    }

    public HookResult HandleWeaponZoom(EventWeaponZoom @event, GameEventInfo info)
    {
        return _activeMatch?.Game.OnWeaponZoom(@event, info) ?? HookResult.Continue;
    }

    public void HandlePlayerDeath(CCSPlayerController? victim)
    {
        if (_activeMatch is null || !victim.IsUsable())
        {
            return;
        }

        if (IsSamePlayer(victim, _activeMatch.Inmate))
        {
            EndActive(_activeMatch.Guardian, announce: true);
        }
        else if (IsSamePlayer(victim, _activeMatch.Guardian))
        {
            EndActive(_activeMatch.Inmate, announce: true);
        }
    }

    public void HandleDisconnect(int playerSlot)
    {
        if (_activeMatch is null)
        {
            return;
        }

        if (_activeMatch.Inmate.Slot == playerSlot)
        {
            EndActive(_activeMatch.Guardian, announce: true);
        }
        else if (_activeMatch.Guardian.Slot == playerSlot)
        {
            EndActive(_activeMatch.Inmate, announce: true);
        }
    }

    public void ResetRound()
    {
        EndActive(winner: null, announce: false);
    }

    private bool CanOpenLr(CCSPlayerController player)
    {
        if (!player.IsUsable() || !player.PawnIsAlive || player.Team != CsTeam.Terrorist)
        {
            player.PrintToChat(JailbreakChat.Format("LR доступен только живому заключённому."));
            return false;
        }

        if (JailbreakCapability.Api.Get()?.IsRoundActive != true)
        {
            player.PrintToChat(JailbreakChat.Format("Сейчас нет активного раунда."));
            return false;
        }

        if (SpecialDaysCapability.Api.Get()?.IsActive == true)
        {
            player.PrintToChat(JailbreakChat.Format("LR нельзя запускать во время игрового дня."));
            return false;
        }

        if (_activeMatch is not null)
        {
            player.PrintToChat(JailbreakChat.Format("Сейчас уже идёт LR."));
            return false;
        }

        var aliveInmates = Utilities.GetPlayers().Count(candidate =>
            candidate.IsUsable() && candidate.PawnIsAlive && candidate.Team == CsTeam.Terrorist);
        if (aliveInmates != 1)
        {
            player.PrintToChat(JailbreakChat.Format("LR доступен только последнему живому заключённому."));
            return false;
        }

        var aliveGuards = Utilities.GetPlayers().Count(candidate =>
            candidate.IsUsable() && candidate.PawnIsAlive && candidate.Team == CsTeam.CounterTerrorist);
        if (aliveGuards == 0)
        {
            player.PrintToChat(JailbreakChat.Format("Нет живых охранников для LR."));
            return false;
        }

        return true;
    }

    private void OpenOpponentMenu(CCSPlayerController inmate, ILrGame game)
    {
        if (!CanOpenLr(inmate))
        {
            return;
        }

        var menuApi = MenuCapability.Api.Get();
        if (menuApi is null)
        {
            inmate.PrintToChat(JailbreakChat.Format("Menu API недоступно."));
            return;
        }

        var options = Utilities.GetPlayers()
            .Where(player => player.IsUsable() && player.PawnIsAlive && player.Team == CsTeam.CounterTerrorist)
            .Select(guardian => new JailbreakMenuOption(guardian.PlayerName, _ => StartMatch(game, inmate, guardian)))
            .ToArray();

        if (options.Length == 0)
        {
            inmate.PrintToChat(JailbreakChat.Format("Нет живых охранников для LR."));
            return;
        }

        menuApi.Open(inmate, "Выберите CT", options);
    }

    private void StartMatch(ILrGame game, CCSPlayerController inmate, CCSPlayerController guardian)
    {
        if (!CanStartMatch(inmate, guardian))
        {
            return;
        }

        _activeMatch = new ActiveLrMatch(game, inmate, guardian);
        MenuCapability.Api.Get()?.Close(inmate);

        var wardenApi = WardenCapability.Api.Get();
        if (wardenApi?.Warden is not null)
        {
            wardenApi.TryResign(wardenApi.Warden);
        }

        inmate.ResetForLr();
        guardian.ResetForLr();

        Server.PrintToChatAll(JailbreakChat.Format($"LR начался: {game.Name} | T: {inmate.PlayerName} vs CT: {guardian.PlayerName}."));
        game.Start(new LrMatchContext(inmate, guardian, winner => EndActive(winner, announce: true)));
    }

    private bool CanStartMatch(CCSPlayerController inmate, CCSPlayerController guardian)
    {
        if (!inmate.IsUsable() || !inmate.PawnIsAlive || inmate.Team != CsTeam.Terrorist)
        {
            inmate.PrintToChat(JailbreakChat.Format("Заключённый больше не доступен для LR."));
            return false;
        }

        if (!guardian.IsUsable() || !guardian.PawnIsAlive || guardian.Team != CsTeam.CounterTerrorist)
        {
            inmate.PrintToChat(JailbreakChat.Format("Выбранный охранник больше не доступен."));
            return false;
        }

        if (_activeMatch is not null)
        {
            inmate.PrintToChat(JailbreakChat.Format("Сейчас уже идёт LR."));
            return false;
        }

        return true;
    }

    private void EndActive(CCSPlayerController? winner, bool announce)
    {
        if (_activeMatch is null)
        {
            return;
        }

        var match = _activeMatch;
        _activeMatch = null;
        match.Game.Stop();

        if (!announce)
        {
            return;
        }

        MatchEnded?.Invoke(new LrMatchEndedEvent(match.Game.Name, match.Inmate, match.Guardian, winner));

        if (winner.IsUsable())
        {
            Server.PrintToChatAll(JailbreakChat.Format($"LR завершён: {winner.PlayerName} победил в {match.Game.Name}."));
        }
        else
        {
            Server.PrintToChatAll(JailbreakChat.Format($"LR завершён: {match.Game.Name} без победителя."));
        }
    }

    private void UnregisterGame(string id, Guid token)
    {
        if (!_games.TryGetValue(id, out var registration) || registration.Token != token)
        {
            return;
        }

        if (_activeMatch?.Game == registration.Game)
        {
            EndActive(winner: null, announce: true);
        }

        _games.Remove(id);
    }

    private bool IsParticipant(CCSPlayerController player)
    {
        return _activeMatch is not null &&
               (IsSamePlayer(player, _activeMatch.Inmate) || IsSamePlayer(player, _activeMatch.Guardian));
    }

    private static bool IsSamePlayer(CCSPlayerController? first, CCSPlayerController? second)
    {
        return first.IsUsable() && second.IsUsable() && first.Slot == second.Slot;
    }
}
