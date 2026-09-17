using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.LR.Extensions;

namespace JBF.LR.Services;

internal sealed class LrService : ILrApi
{
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMilliseconds(250);
    private readonly Dictionary<string, RegisteredLrGame> _games = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _preLrProtectedSlots = [];
    private readonly LrVipSuppressionService _vipSuppression = new();
    private ActiveLrMatch? _activeMatch;
    private CBeam? _opponentLink;
    private DateTime _preLrProtectionUntil;
    private DateTime _nextMaintenance;
    private bool _twoInmatesPhaseAnnounced;
    private bool _lastInmateMenuOpened;

    public event Action<LrMatchEndedEvent>? MatchEnded;
    public bool IsActive => _activeMatch is not null;
    public string? ActiveGameName => _activeMatch?.Game.Name;
    public CCSPlayerController? Inmate => _activeMatch?.Inmate;
    public CCSPlayerController? Guardian => _activeMatch?.Guardian;

    public IDisposable RegisterGame(ILrGame game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(game.Id)) throw new ArgumentException("LR game id cannot be empty.", nameof(game));
        if (string.IsNullOrWhiteSpace(game.Name)) throw new ArgumentException("LR game name cannot be empty.", nameof(game));

        var token = Guid.NewGuid();
        _games[game.Id] = new RegisteredLrGame(token, game);
        return new ActionDisposable(() => UnregisterGame(game.Id, token));
    }

    public void OpenMenu(CCSPlayerController player)
    {
        if (!CanOpenLr(player)) return;

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
            .Select(game => new JailbreakMenuOption(game.Name, inmate => OpenGame(inmate, game)))
            .ToArray();

        menuApi.Open(player, "Last Request", options);
    }

    public bool TryEndActive()
    {
        if (_activeMatch is null) return false;
        EndActive(winner: null, announce: true);
        return true;
    }

    public HookResult HandleTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        if (entity.DesignerName != "player") return HookResult.Continue;

        var victim = entity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        if (!victim.IsUsable()) return HookResult.Continue;

        if (_preLrProtectedSlots.Contains(victim.Slot))
        {
            if (DateTime.UtcNow < _preLrProtectionUntil)
                return HookResult.Handled;

            _preLrProtectedSlots.Clear();
        }

        if (_activeMatch is null) return HookResult.Continue;

        var attackerEntity = damageInfo.Attacker.Value;
        if (attackerEntity?.DesignerName != "player") return HookResult.Continue;

        var attacker = attackerEntity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        if (!attacker.IsUsable()) return HookResult.Continue;

        if (IsParticipant(victim) != IsParticipant(attacker))
            return HookResult.Handled;

        if (!IsParticipant(victim) && !IsParticipant(attacker))
            return HookResult.Continue;

        if (_activeMatch.Game.DisableAllDamage)
            return HookResult.Handled;

        var isDuelDamage =
            IsSamePlayer(attacker, _activeMatch.Inmate) && IsSamePlayer(victim, _activeMatch.Guardian) ||
            IsSamePlayer(attacker, _activeMatch.Guardian) && IsSamePlayer(victim, _activeMatch.Inmate);

        if (!isDuelDamage) return HookResult.Handled;
        return _activeMatch.Game.OnTakeDamage(entity, damageInfo);
    }

    public HookResult HandleBulletImpact(EventBulletImpact @event, GameEventInfo info) =>
        _activeMatch?.Game.OnBulletImpact(@event, info) ?? HookResult.Continue;

    public HookResult HandleWeaponZoom(EventWeaponZoom @event, GameEventInfo info) =>
        _activeMatch?.Game.OnWeaponZoom(@event, info) ?? HookResult.Continue;

    public void HandlePlayerDeath(CCSPlayerController? victim)
    {
        if (_activeMatch is not null && victim.IsUsable())
        {
            if (IsSamePlayer(victim, _activeMatch.Inmate)) EndActive(_activeMatch.Guardian, announce: true);
            else if (IsSamePlayer(victim, _activeMatch.Guardian)) EndActive(_activeMatch.Inmate, announce: true);
        }

        Server.NextFrame(EvaluateLrPhase);
    }

    public void HandleDisconnect(int playerSlot)
    {
        _preLrProtectedSlots.Remove(playerSlot);

        if (_activeMatch is not null)
        {
            if (_activeMatch.Inmate.Slot == playerSlot) EndActive(_activeMatch.Guardian, announce: true);
            else if (_activeMatch.Guardian.Slot == playerSlot) EndActive(_activeMatch.Inmate, announce: true);
        }

        _vipSuppression.Forget(playerSlot);
    }

    public void Tick()
    {
        var now = DateTime.UtcNow;
        if (now < _nextMaintenance) return;
        _nextMaintenance = now + MaintenanceInterval;

        if (_preLrProtectedSlots.Count > 0 && now >= _preLrProtectionUntil)
            _preLrProtectedSlots.Clear();

        EvaluateLrPhase();

        if (_activeMatch is null) return;
        UpdateOpponentLink();
        EnforceLrInventory(_activeMatch.Inmate);
        EnforceLrInventory(_activeMatch.Guardian);
        PlayerEffectsCapability.Api.Get()?.Clear(_activeMatch.Inmate);
        PlayerEffectsCapability.Api.Get()?.Clear(_activeMatch.Guardian);
    }

    public void ResetRound()
    {
        EndActive(winner: null, announce: false);
        _preLrProtectedSlots.Clear();
        _twoInmatesPhaseAnnounced = false;
        _lastInmateMenuOpened = false;
        _preLrProtectionUntil = default;
        RemoveOpponentLink();
    }

    private void EvaluateLrPhase()
    {
        if (JailbreakCapability.Api.Get()?.IsRoundActive != true || SpecialDaysCapability.Api.Get()?.IsActive == true)
            return;

        var aliveInmates = Utilities.GetPlayers()
            .Where(player => player.IsUsable() && player.PawnIsAlive && player.Team == CsTeam.Terrorist)
            .ToArray();

        if (aliveInmates.Length == 2 && !_twoInmatesPhaseAnnounced)
        {
            _twoInmatesPhaseAnnounced = true;
            _preLrProtectedSlots.Clear();
            foreach (var inmate in aliveInmates) _preLrProtectedSlots.Add(inmate.Slot);
            _preLrProtectionUntil = DateTime.UtcNow.AddSeconds(10);

            foreach (var player in Utilities.GetPlayers().Where(p => p.IsUsable() && !p.IsBot))
                UiCapability.Api.Get()?.Announce(player, "ВРЕМЯ LR", "Осталось 2 заключённых • 10 секунд защиты", UiNotificationType.Important, 5.0f);

            Server.PrintToChatAll(JailbreakChat.Format("Время LR: осталось 2 заключённых. Они получили бессмертие на 10 секунд."));
        }

        if (aliveInmates.Length != 1 || _activeMatch is not null || _lastInmateMenuOpened)
            return;

        var last = aliveInmates[0];
        var aliveGuards = Utilities.GetPlayers().Any(player =>
            player.IsUsable() && player.PawnIsAlive && player.Team == CsTeam.CounterTerrorist);
        if (!aliveGuards) return;

        _lastInmateMenuOpened = true;
        UiCapability.Api.Get()?.Announce(last, "LAST REQUEST", "Выберите игру и соперника", UiNotificationType.Success, 4.0f);
        OpenMenu(last);
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

        var aliveInmates = Utilities.GetPlayers().Count(candidate => candidate.IsUsable() && candidate.PawnIsAlive && candidate.Team == CsTeam.Terrorist);
        if (aliveInmates != 1)
        {
            player.PrintToChat(JailbreakChat.Format("LR доступен последнему живому заключённому."));
            return false;
        }

        var aliveGuards = Utilities.GetPlayers().Count(candidate => candidate.IsUsable() && candidate.PawnIsAlive && candidate.Team == CsTeam.CounterTerrorist);
        if (aliveGuards == 0)
        {
            player.PrintToChat(JailbreakChat.Format("Нет живых охранников для LR."));
            return false;
        }

        return true;
    }

    private void OpenGame(CCSPlayerController inmate, ILrGame game)
    {
        if (game is ILrGameVariants { Variants.Count: > 0 } variants)
        {
            OpenVariantMenu(inmate, game.Name, variants.Variants);
            return;
        }

        OpenOpponentMenu(inmate, game);
    }

    private void OpenVariantMenu(CCSPlayerController inmate, string title, IReadOnlyList<ILrGame> variants)
    {
        if (!CanOpenLr(inmate)) return;
        var menuApi = MenuCapability.Api.Get();
        if (menuApi is null) return;

        var options = variants
            .OrderBy(game => game.Order)
            .Select(game => new JailbreakMenuOption(game.Name, player => OpenOpponentMenu(player, game)))
            .ToArray();

        menuApi.Open(inmate, title, options);
    }

    private void OpenOpponentMenu(CCSPlayerController inmate, ILrGame game)
    {
        if (!CanOpenLr(inmate)) return;

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
        if (!CanStartMatch(inmate, guardian)) return;

        _activeMatch = new ActiveLrMatch(game, inmate, guardian);
        _preLrProtectedSlots.Clear();
        MenuCapability.Api.Get()?.Close(inmate);

        var wardenApi = WardenCapability.Api.Get();
        if (wardenApi?.Warden is not null) wardenApi.TryResign(wardenApi.Warden);

        PlayerEffectsCapability.Api.Get()?.Clear(inmate);
        PlayerEffectsCapability.Api.Get()?.Clear(guardian);
        _vipSuppression.Suppress(inmate);
        _vipSuppression.Suppress(guardian);
        inmate.ResetForLr();
        guardian.ResetForLr();
        CreateOpponentLink();

        foreach (var player in Utilities.GetPlayers().Where(p => p.IsUsable() && !p.IsBot))
            UiCapability.Api.Get()?.Announce(player, game.Name, $"{inmate.PlayerName}  VS  {guardian.PlayerName}", UiNotificationType.Important, 4.0f);

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

    private void EnforceLrInventory(CCSPlayerController player)
    {
        if (_activeMatch is null || !player.IsUsable() || !player.PawnIsAlive) return;
        if (_activeMatch.Game is not ILrInventoryRules rules) return;

        var pawn = player.PlayerPawn.Value;
        var weaponServices = pawn?.WeaponServices;
        var itemServices = pawn?.ItemServices;
        var weapon = weaponServices?.ActiveWeapon.Value;
        if (itemServices is null || weapon is null || !weapon.IsValid || rules.IsWeaponAllowed(weapon)) return;

        try
        {
            itemServices.DropActivePlayerWeapon(weapon);
            Server.NextFrame(() =>
            {
                try
                {
                    if (weapon.IsValid) weapon.Remove();
                }
                catch { }
            });
        }
        catch { }
    }

    private void CreateOpponentLink()
    {
        RemoveOpponentLink();
        var beam = Utilities.CreateEntityByName<CBeam>("beam");
        if (beam is null) return;
        beam.Width = 1.5f;
        beam.Render = Color.Gold;
        beam.DispatchSpawn();
        _opponentLink = beam;
        UpdateOpponentLink();
    }

    private void UpdateOpponentLink()
    {
        if (_activeMatch is null || _opponentLink is not { IsValid: true }) return;
        var start = _activeMatch.Inmate.PlayerPawn.Value?.AbsOrigin;
        var end = _activeMatch.Guardian.PlayerPawn.Value?.AbsOrigin;
        if (start is null || end is null) return;

        var startPoint = new Vector(start.X, start.Y, start.Z + 45.0f);
        _opponentLink.Teleport(startPoint, new QAngle(), new Vector());
        _opponentLink.EndPos.X = end.X;
        _opponentLink.EndPos.Y = end.Y;
        _opponentLink.EndPos.Z = end.Z + 45.0f;
        Utilities.SetStateChanged(_opponentLink, "CBeam", "m_vecEndPos");
    }

    private void RemoveOpponentLink()
    {
        if (_opponentLink is { IsValid: true }) _opponentLink.Remove();
        _opponentLink = null;
    }

    private void EndActive(CCSPlayerController? winner, bool announce)
    {
        if (_activeMatch is null) return;

        var match = _activeMatch;
        _activeMatch = null;
        RemoveOpponentLink();
        match.Game.Stop();
        _vipSuppression.Restore(match.Inmate);
        _vipSuppression.Restore(match.Guardian);

        if (!announce) return;
        MatchEnded?.Invoke(new LrMatchEndedEvent(match.Game.Name, match.Inmate, match.Guardian, winner));

        if (winner.IsUsable())
        {
            Server.PrintToChatAll(JailbreakChat.Format($"LR завершён: {winner.PlayerName} победил в {match.Game.Name}."));
            foreach (var player in Utilities.GetPlayers().Where(p => p.IsUsable() && !p.IsBot))
                UiCapability.Api.Get()?.Notify(player, $"LR: победил {winner.PlayerName}", UiNotificationType.Success, 4.0f);
        }
        else
        {
            Server.PrintToChatAll(JailbreakChat.Format($"LR завершён: {match.Game.Name} без победителя."));
        }
    }

    private void UnregisterGame(string id, Guid token)
    {
        if (!_games.TryGetValue(id, out var registration) || registration.Token != token) return;
        if (_activeMatch?.Game == registration.Game) EndActive(winner: null, announce: true);
        _games.Remove(id);
    }

    private bool IsParticipant(CCSPlayerController player) => _activeMatch is not null &&
        (IsSamePlayer(player, _activeMatch.Inmate) || IsSamePlayer(player, _activeMatch.Guardian));

    private static bool IsSamePlayer(CCSPlayerController? first, CCSPlayerController? second) =>
        first.IsUsable() && second.IsUsable() && first.Slot == second.Slot;
}
