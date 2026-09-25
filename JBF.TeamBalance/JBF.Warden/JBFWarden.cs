using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.Warden.Api;
using JBF.Warden.Services;

namespace JBF.Warden;

public sealed class JBFWarden : BasePlugin
{
    private const float CommanderSelectionSeconds = 15.0f;

    private readonly WardenService _wardenService = new();
    private readonly IWardenApi _api;
    private readonly HashSet<int> _commanderPromptSlots = [];
    private int _roundSelectionGeneration;

    public JBFWarden()
    {
        _api = new WardenApi(_wardenService);
    }

    public override string ModuleName => "JBF Warden";
    public override string ModuleVersion => "1.4.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        Capabilities.RegisterPluginCapability(WardenCapability.Api, () => _api);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
    }

    [ConsoleCommand("css_w", "Become the warden and open the commander menu")]
    public void OnWardenCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!CanBecomeWarden(player, command))
        {
            return;
        }

        if (!_wardenService.TryClaim(player!))
        {
            command.ReplyToCommand(JailbreakChat.Format("Командир уже выбран."));
            return;
        }

        command.ReplyToCommand(JailbreakChat.Format("Вы командир."));
        FinishCommanderSelection();

        var commanderMenu = CommanderMenuCapability.Api.GetOptional();
        if (commanderMenu is null)
        {
            command.ReplyToCommand(JailbreakChat.Format("Модуль меню командира недоступен."));
            return;
        }

        commanderMenu.Open(player!);
    }

    [ConsoleCommand("css_uw", "Leave the warden role")]
    public void OnResignCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !_wardenService.TryResign(player))
        {
            command.ReplyToCommand(JailbreakChat.Format("Вы не являетесь командиром."));
            return;
        }

        MenuCapability.Api.GetOptional()?.Close(player);
        command.ReplyToCommand(JailbreakChat.Format("Вы покинули пост командира."));
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _wardenService.Reset();

        var generation = ++_roundSelectionGeneration;
        _commanderPromptSlots.Clear();

        // Give CS2 a short moment to finish spawning players before opening the prompt.
        AddTimer(
            0.5f,
            () => ShowCommanderPrompt(generation),
            TimerFlags.STOP_ON_MAPCHANGE);

        AddTimer(
            CommanderSelectionSeconds,
            () => AutoSelectCommander(generation),
            TimerFlags.STOP_ON_MAPCHANGE);

        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _roundSelectionGeneration++;
        CloseCommanderPrompts();
        _wardenService.Reset();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is not null && _wardenService.IsWarden(player))
        {
            _wardenService.NotifyKilled(player, @event.Attacker, @event.Weapon);
            _wardenService.Reset();
            MenuCapability.Api.GetOptional()?.Close(player);
        }

        return HookResult.Continue;
    }

    private void ShowCommanderPrompt(int generation)
    {
        if (generation != _roundSelectionGeneration || _wardenService.Warden is not null)
            return;

        var jailbreakApi = JailbreakCapability.Api.GetOptional();
        if (jailbreakApi?.IsRoundActive != true)
            return;

        if (SpecialDaysCapability.Api.GetOptional()?.IsActive == true ||
            LrCapability.Api.GetOptional()?.IsActive == true)
            return;

        var menu = MenuCapability.Api.GetOptional();
        if (menu is null)
            return;

        foreach (var player in CounterStrikeSharp.API.Utilities.GetPlayers())
        {
            if (!IsEligibleCommander(player))
                continue;

            _commanderPromptSlots.Add(player.Slot);

            menu.Open(
                player,
                "ЖЕЛАЕТЕ СТАТЬ КМД?",
                new[]
                {
                    new JailbreakMenuOption("Да", selected => TryTakeCommanderFromPrompt(selected, generation)),
                    new JailbreakMenuOption("Нет", selected =>
                    {
                        _commanderPromptSlots.Remove(selected.Slot);
                        MenuCapability.Api.GetOptional()?.Close(selected);
                    })
                });
        }
    }

    private void TryTakeCommanderFromPrompt(CCSPlayerController player, int generation)
    {
        _commanderPromptSlots.Remove(player.Slot);

        if (generation != _roundSelectionGeneration || !IsEligibleCommander(player))
            return;

        if (_wardenService.Warden is not null)
        {
            MenuCapability.Api.GetOptional()?.Close(player);
            UiCapability.Api.GetOptional()?.Notify(
                player,
                "Командир уже выбран.",
                UiNotificationType.Warning,
                3.0f);
            return;
        }

        if (!_wardenService.TryClaim(player))
            return;

        FinishCommanderSelection();
        CommanderMenuCapability.Api.GetOptional()?.Open(player);
    }

    private void AutoSelectCommander(int generation)
    {
        if (generation != _roundSelectionGeneration)
            return;

        if (_wardenService.Warden is not null)
        {
            CloseCommanderPrompts();
            return;
        }

        var jailbreakApi = JailbreakCapability.Api.GetOptional();
        if (jailbreakApi?.IsRoundActive != true ||
            SpecialDaysCapability.Api.GetOptional()?.IsActive == true ||
            LrCapability.Api.GetOptional()?.IsActive == true)
        {
            CloseCommanderPrompts();
            return;
        }

        var candidates = CounterStrikeSharp.API.Utilities.GetPlayers()
            .Where(IsEligibleCommander)
            .ToArray();

        CloseCommanderPrompts();

        if (candidates.Length == 0)
            return;

        var selected = candidates[Random.Shared.Next(candidates.Length)];
        if (!_wardenService.TryClaim(selected))
            return;

        UiCapability.Api.GetOptional()?.Notify(
            selected,
            "Вы автоматически выбраны командиром.",
            UiNotificationType.Important,
            4.0f);

        CommanderMenuCapability.Api.GetOptional()?.Open(selected);
    }

    private void FinishCommanderSelection()
    {
        _roundSelectionGeneration++;
        CloseCommanderPrompts();
    }

    private void CloseCommanderPrompts()
    {
        var menu = MenuCapability.Api.GetOptional();
        if (menu is not null)
        {
            foreach (var slot in _commanderPromptSlots.ToArray())
            {
                var player = CounterStrikeSharp.API.Utilities.GetPlayerFromSlot(slot);
                if (player is { IsValid: true } && menu.IsOpen(player))
                    menu.Close(player);
            }
        }

        _commanderPromptSlots.Clear();
    }

    private static bool IsEligibleCommander(CCSPlayerController? player)
    {
        return player is
        {
            IsValid: true,
            IsBot: false,
            PawnIsAlive: true,
            Team: CsTeam.CounterTerrorist
        };
    }

    private bool CanBecomeWarden(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid || !player.PawnIsAlive || player.Team != CsTeam.CounterTerrorist)
        {
            command.ReplyToCommand(JailbreakChat.Format("Команда доступна только живым игрокам КТ."));
            return false;
        }

        var jailbreakApi = JailbreakCapability.Api.GetOptional();
        if (jailbreakApi is null)
        {
            command.ReplyToCommand(JailbreakChat.Format("Core API недоступно."));
            return false;
        }

        if (!jailbreakApi.IsRoundActive)
        {
            command.ReplyToCommand(JailbreakChat.Format("Сейчас нет активного раунда."));
            return false;
        }

        if (SpecialDaysCapability.Api.GetOptional()?.IsActive == true)
        {
            command.ReplyToCommand(JailbreakChat.Format("Во время игрового дня выбрать командира нельзя."));
            return false;
        }

        if (LrCapability.Api.GetOptional()?.IsActive == true)
        {
            command.ReplyToCommand(JailbreakChat.Format("Во время LR выбрать командира нельзя."));
            return false;
        }

        return true;
    }

    private void OnClientDisconnect(int playerSlot)
    {
        _commanderPromptSlots.Remove(playerSlot);

        if (_wardenService.Warden?.Slot == playerSlot)
            _wardenService.Reset();
    }
}
