using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.CommanderTools.Extensions;
using JBF.CommanderTools.Services;
using JBF.CommanderTools.Tools;

namespace JBF.CommanderTools;

public sealed class JBFCommanderTools : BasePlugin
{
    private readonly CommanderMenuService _commanderMenu = new();
    private readonly CommanderToolsState _state = new();
    private readonly List<IDisposable> _registrations = [];
    private readonly IReadOnlyList<ICommanderTool> _tools;
    private readonly TTeamMuteService _muteService;

    public JBFCommanderTools()
    {
        _muteService = new TTeamMuteService(this);
        var playerSelection = new PlayerSelectionService();
        _tools =
        [
            new DoorsTool(),
            new FriendlyFireTool(_state),
            new NoBlockTool(_state),
            new BhopTool(_state),
            new HealTool(playerSelection),
            new KillTool(playerSelection),
            new RespawnTool(playerSelection),
            new ColorDivisionTool(_state),
            new FreeDayTool(_state),
            new InmateCountTool(this),
            new TTeamMuteTool(_muteService)
        ];
    }

    public override string ModuleName => "JBF Commander Tools";
    public override string ModuleVersion => "1.3.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        Capabilities.RegisterPluginCapability(CommanderMenuCapability.Api, () => _commanderMenu);
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(OnTakeDamage);

        foreach (var tool in _tools)
        {
            _registrations.Add(_commanderMenu.RegisterItem(
                new CommanderMenuItem(
                    tool.Id,
                    tool.Text,
                    commander =>
                    {
                        Server.PrintToChatAll(JailbreakChat.Format($"Командир {commander.PlayerName}: {tool.Text}."));
                        tool.Execute(commander);
                    },
                    tool.Order)));
        }

    }

    public override void Unload(bool hotReload)
    {
        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
        _muteService.ClearMute();
    }

    [ConsoleCommand("css_cm", "Open the commander menu")]
    public void OnCommanderMenuCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand(JailbreakChat.Format("Команда доступна только игрокам."));
            return;
        }

        _commanderMenu.Open(player);
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _state.ResetRound();
        _muteService.ResetRound();
        _muteService.ApplyRoundStartMute();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _muteService.ClearMute();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnBulletImpact(EventBulletImpact @event, GameEventInfo info)
    {
        var shooter = @event.Userid;
        if (!shooter.IsUsable() || shooter.Team != CsTeam.CounterTerrorist)
            return HookResult.Continue;

        var impact = new Vector(@event.X, @event.Y, @event.Z);
        const float maxDistance = 56.0f;
        CBasePlayerWeapon? nearest = null;
        var nearestDistanceSquared = maxDistance * maxDistance;

        foreach (var weapon in Utilities.FindAllEntitiesByDesignerName<CBasePlayerWeapon>("weapon_"))
        {
            if (weapon is null || !weapon.IsValid)
                continue;

            var owner = weapon.OwnerEntity.Value;
            if (owner is { IsValid: true })
                continue;

            var origin = weapon.AbsOrigin;
            if (origin is null)
                continue;

            var dx = origin.X - impact.X;
            var dy = origin.Y - impact.Y;
            var dz = origin.Z - impact.Z;
            var distanceSquared = dx * dx + dy * dy + dz * dz;

            if (distanceSquared > nearestDistanceSquared)
                continue;

            nearest = weapon;
            nearestDistanceSquared = distanceSquared;
        }

        if (nearest is { IsValid: true })
            nearest.Remove();

        return HookResult.Continue;
    }

    private void OnTick()
    {
        if (!_state.BhopEnabled)
        {
            return;
        }

        foreach (var player in Utilities.GetPlayers().Where(player =>
                     player.IsUsable() && !player.IsBot && player.PawnIsAlive))
        {
            var pawn = player.PlayerPawn.Value!;
            var flags = (PlayerFlags)pawn.Flags;

            if (player.Buttons.HasFlag(PlayerButtons.Jump) &&
                flags.HasFlag(PlayerFlags.FL_ONGROUND) &&
                !pawn.MoveType.HasFlag(MoveType_t.MOVETYPE_LADDER))
            {
                pawn.AbsVelocity.Z = 300;
            }
        }
    }

    private HookResult OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        if (!_state.FreeDayEnabled || entity.DesignerName != "player")
        {
            return HookResult.Continue;
        }

        var victim = entity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();
        var attackerEntity = damageInfo.Attacker.Value;
        if (attackerEntity?.DesignerName != "player")
        {
            return HookResult.Continue;
        }

        var attacker = attackerEntity.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>();

        if (!victim.IsUsable() || !attacker.IsUsable())
        {
            return HookResult.Continue;
        }

        return victim.Team == CsTeam.CounterTerrorist && attacker.Team == CsTeam.Terrorist
            ? HookResult.Handled
            : HookResult.Continue;
    }
}
