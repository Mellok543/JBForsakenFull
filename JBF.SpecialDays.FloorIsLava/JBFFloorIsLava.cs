using System.Diagnostics.CodeAnalysis;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace JBF.SpecialDays.FloorIsLava;

public sealed class JBFFloorIsLava : BasePlugin, ISpecialDay
{
    private const string AdminPermission = "@jbf/admin";
    private const float DefaultRadius = 220.0f;
    private const float PreparationSeconds = 30.0f;
    private const float SafeZoneVerticalTolerance = 110.0f;
    private const float LavaDamageIntervalSeconds = 0.75f;
    private const int BaseLavaDamage = 10;
    private const int LavaDamageIncreasePerCycle = 5;
    private const int MaxLavaDamage = 45;
    private const float DefaultRoundTimeSeconds = 300.0f;

    private static readonly string[] DoorEntityNames =
    [
        "func_door",
        "func_movelinear",
        "func_door_rotating",
        "prop_door_rotating"
    ];

    private readonly LavaZoneRenderer _renderer = new();
    private readonly List<LavaPoint> _activeZones = [];
    private readonly Dictionary<int, DateTime> _nextDamageAt = [];

    private LavaPointStore? _store;
    private LavaMapConfig? _mapConfig;
    private IDisposable? _registration;
    private ISpecialDayContext? _context;
    private Timer? _phaseTimer;
    private Timer? _roundTimer;
    private LavaPhase _phase = LavaPhase.Stopped;
    private int _cycle;

    public override string ModuleName => "JBF Special Day: Floor Is Lava";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    public string Id => "floor-is-lava";
    public string Name => "Пол — лава";
    public int Order => 30;

    public override void Load(bool hotReload)
    {
        _store = new LavaPointStore(ModuleDirectory);
        RegisterListener<Listeners.OnTick>(Tick);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        var specialDays = SpecialDaysCapability.Api.GetOptional();
        if (specialDays is null)
        {
            Logger.LogWarning(
                "Special days capability is unavailable; Floor Is Lava was not registered.");
            return;
        }

        _registration = specialDays.RegisterDay(this);
    }

    public override void Unload(bool hotReload)
    {
        _registration?.Dispose();
        _registration = null;
        Stop();
    }

    public void Start(ISpecialDayContext context)
    {
        Stop();

        if (_store is null)
            return;

        _mapConfig = _store.Load(Server.MapName);
        if (_mapConfig.Points.Count == 0)
        {
            Server.PrintToChatAll(
                JailbreakChat.Format("Пол — лава: для этой карты не настроены безопасные точки."));
            context.Finish(RoundEndReason.RoundDraw);
            return;
        }

        _context = context;
        _phase = LavaPhase.Preparation;
        _cycle = 0;
        _nextDamageAt.Clear();
        _activeZones.Clear();

        OpenCells();
        PrintRules();

        AnnounceAll(
            "ПОЛ — ЛАВА",
            "Подготовка • 30 секунд",
            UiNotificationType.Important,
            6.0f);

        _phaseTimer = AddTimer(
            PreparationSeconds,
            BeginSafeZoneWarning,
            TimerFlags.STOP_ON_MAPCHANGE);

        _roundTimer = AddTimer(
            GetRoundTimeSeconds(),
            HandleRoundTimeExpired,
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    public void Stop()
    {
        _phaseTimer?.Kill();
        _roundTimer?.Kill();
        _phaseTimer = null;
        _roundTimer = null;

        _renderer.Clear();
        _activeZones.Clear();
        _nextDamageAt.Clear();

        _mapConfig = null;
        _context = null;
        _phase = LavaPhase.Stopped;
        _cycle = 0;
    }

    public void OnPlayerSpawn(CCSPlayerController player)
    {
    }

    public void OnPlayerDeath(CCSPlayerController? victim, CCSPlayerController? attacker)
    {
        if (victim is not null)
            _nextDamageAt.Remove(victim.Slot);

        if (_context is not null)
            Server.NextFrame(EvaluateRoundState);
    }

    public HookResult OnTakeDamage(CBaseEntity entity, CTakeDamageInfo damageInfo)
    {
        if (_context is null || entity.DesignerName != "player")
            return HookResult.Continue;

        var victim = entity
            .As<CCSPlayerPawn>()
            .Controller.Value?
            .As<CCSPlayerController>();

        var attackerEntity = damageInfo.Attacker.Value;
        if (attackerEntity?.DesignerName != "player")
            return HookResult.Continue;

        var attacker = attackerEntity
            .As<CCSPlayerPawn>()
            .Controller.Value?
            .As<CCSPlayerController>();

        if (IsUsable(victim) &&
            IsUsable(attacker) &&
            victim.Slot == attacker.Slot)
        {
            return HookResult.Continue;
        }

        return HookResult.Handled;
    }

    private void Tick()
    {
        if (_context is null || _phase != LavaPhase.Lava)
            return;

        var now = DateTime.UtcNow;

        foreach (var player in ActiveTerrorists().ToArray())
        {
            if (IsInsideAnySafeZone(player))
                continue;

            if (_nextDamageAt.TryGetValue(player.Slot, out var next) && now < next)
                continue;

            ApplyLavaDamage(player);
            _nextDamageAt[player.Slot] =
                now.AddSeconds(LavaDamageIntervalSeconds);
        }
    }

    private void BeginSafeZoneWarning()
    {
        if (_context is null || _mapConfig is null)
            return;

        _phaseTimer = null;
        _phase = LavaPhase.Warning;
        _cycle++;

        var aliveT = ActiveTerrorists().Count();
        var selected = LavaSafeZoneSelector.SelectRandom(
            _mapConfig.Points,
            aliveT);

        _activeZones.Clear();
        _activeZones.AddRange(selected);
        _renderer.Show(_activeZones);

        var warningSeconds = RandomSeconds(8.0f, 12.0f);

        AnnounceAll(
            "ЛАВА СКОРО!",
            $"Ищите зелёные островки • {warningSeconds:F0} сек.",
            UiNotificationType.Warning,
            5.0f);

        Server.PrintToChatAll(
            JailbreakChat.Format(
                $"Безопасных островков: {_activeZones.Count}. Бегите в зелёную зону."));

        _phaseTimer = AddTimer(
            warningSeconds,
            StartLava,
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void StartLava()
    {
        if (_context is null)
            return;

        _phaseTimer = null;
        _phase = LavaPhase.Lava;
        _nextDamageAt.Clear();

        AnnounceAll(
            "ПОЛ — ЛАВА",
            "ВНЕ ЗЕЛЁНОЙ ЗОНЫ ВЫ ПОЛУЧАЕТЕ УРОН",
            UiNotificationType.Error,
            4.0f);

        _phaseTimer = AddTimer(
            RandomSeconds(6.0f, 9.0f),
            EndLava,
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void EndLava()
    {
        if (_context is null)
            return;

        _phaseTimer = null;
        _phase = LavaPhase.Rest;
        _nextDamageAt.Clear();
        _renderer.Clear();
        _activeZones.Clear();

        AnnounceAll(
            "ЛАВА ОСТЫЛА",
            "Можно покинуть островки",
            UiNotificationType.Success,
            3.0f);

        _phaseTimer = AddTimer(
            RandomSeconds(6.0f, 10.0f),
            BeginSafeZoneWarning,
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private bool IsInsideAnySafeZone(CCSPlayerController player)
    {
        var origin = player.PlayerPawn.Value?.AbsOrigin;
        if (origin is null)
            return false;

        foreach (var zone in _activeZones)
        {
            if (MathF.Abs(origin.Z - zone.Z) > SafeZoneVerticalTolerance)
                continue;

            var dx = origin.X - zone.X;
            var dy = origin.Y - zone.Y;

            if (dx * dx + dy * dy <= zone.Radius * zone.Radius)
                return true;
        }

        return false;
    }

    private void ApplyLavaDamage(CCSPlayerController player)
    {
        if (!IsActiveTerrorist(player))
            return;

        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid)
            return;

        var damage = Math.Min(
            MaxLavaDamage,
            BaseLavaDamage +
            Math.Max(0, _cycle - 1) * LavaDamageIncreasePerCycle);

        var health = pawn.Health - damage;
        if (health <= 0)
        {
            pawn.CommitSuicide(true, true);
            return;
        }

        pawn.Health = health;
        Utilities.SetStateChanged(
            pawn,
            "CBaseEntity",
            "m_iHealth");
    }

    private void EvaluateRoundState()
    {
        if (_context is null)
            return;

        var aliveT = ActiveTerrorists().Count();
        var aliveCt = ActiveCounterTerrorists().Count();

        if (aliveT == 0)
        {
            _context.Finish(RoundEndReason.CTsWin);
            return;
        }

        if (aliveCt == 0)
        {
            _context.Finish(RoundEndReason.TerroristsWin);
            return;
        }

        if (aliveT <= 2)
            _context.End();
    }

    private void HandleRoundTimeExpired()
    {
        _roundTimer = null;

        if (_context is null)
            return;

        _context.Finish(RoundEndReason.TerroristsWin);
    }

    [ConsoleCommand("css_lavapoint", "Manage Floor Is Lava safe-zone points")]
    [RequiresPermissions(AdminPermission)]
    public void OnLavaPoint(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null || !player.IsValid || player.IsBot)
        {
            command.ReplyToCommand(
                JailbreakChat.Format("Эта команда используется только игроком на карте."));
            return;
        }

        if (_store is null)
        {
            command.ReplyToCommand(
                JailbreakChat.Format("Хранилище точек недоступно."));
            return;
        }

        var action = command.ArgCount >= 2
            ? command.GetArg(1).Trim().ToLowerInvariant()
            : "add";

        var config = _store.Load(Server.MapName);

        switch (action)
        {
            case "add":
                AddPoint(player, command, config);
                break;
            case "undo":
                UndoPoint(command, config);
                break;
            case "list":
                ListPoints(command, config);
                break;
            case "sample":
                SamplePoints(command, config);
                break;
            case "clear":
                ClearPoints(command, config);
                break;
            default:
                command.ReplyToCommand(
                    JailbreakChat.Format(
                        "Использование: !lavapoint [add [радиус] | undo | list | sample | clear]"));
                break;
        }
    }

    private void AddPoint(
        CCSPlayerController player,
        CommandInfo command,
        LavaMapConfig config)
    {
        var pawn = player.PlayerPawn.Value;
        var origin = pawn?.AbsOrigin;

        if (pawn is null || !pawn.IsValid || origin is null)
        {
            command.ReplyToCommand(
                JailbreakChat.Format("Не удалось получить вашу позицию."));
            return;
        }

        var radius = DefaultRadius;

        if (command.ArgCount >= 3)
        {
            if (!float.TryParse(
                    command.GetArg(2),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out radius) ||
                radius < 50.0f ||
                radius > 1000.0f)
            {
                command.ReplyToCommand(
                    JailbreakChat.Format(
                        "Радиус должен быть числом от 50 до 1000. Например: !lavapoint add 220"));
                return;
            }
        }

        var nextId = config.Points.Count == 0
            ? 1
            : config.Points.Max(x => x.Id) + 1;

        var point = new LavaPoint
        {
            Id = nextId,
            X = origin.X,
            Y = origin.Y,
            Z = origin.Z,
            Radius = radius
        };

        config.Points.Add(point);
        _store!.Save(config);

        command.ReplyToCommand(
            JailbreakChat.Format(
                $"Точка #{point.Id} сохранена для {config.Map}. " +
                $"X={point.X:F1} Y={point.Y:F1} Z={point.Z:F1} R={point.Radius:F0}. " +
                $"Всего точек: {config.Points.Count}."));
    }

    private void UndoPoint(CommandInfo command, LavaMapConfig config)
    {
        if (config.Points.Count == 0)
        {
            command.ReplyToCommand(
                JailbreakChat.Format("На этой карте точек ещё нет."));
            return;
        }

        var point = config.Points[^1];
        config.Points.RemoveAt(config.Points.Count - 1);
        _store!.Save(config);

        command.ReplyToCommand(
            JailbreakChat.Format(
                $"Последняя точка #{point.Id} удалена. Осталось: {config.Points.Count}."));
    }

    private static void ListPoints(CommandInfo command, LavaMapConfig config)
    {
        if (config.Points.Count == 0)
        {
            command.ReplyToCommand(
                JailbreakChat.Format($"Для карты {config.Map} точек пока нет."));
            return;
        }

        command.ReplyToCommand(
            JailbreakChat.Format(
                $"Карта {config.Map}: сохранено {config.Points.Count} безопасных точек."));

        foreach (var point in config.Points)
        {
            command.ReplyToCommand(
                JailbreakChat.Format(
                    $"#{point.Id}: X={point.X:F1} Y={point.Y:F1} Z={point.Z:F1} R={point.Radius:F0}"));
        }
    }

    private void SamplePoints(CommandInfo command, LavaMapConfig config)
    {
        var alivePrisoners = ActiveTerrorists().Count();
        var selected = LavaSafeZoneSelector.SelectRandom(
            config.Points,
            alivePrisoners);

        _renderer.Show(selected);

        command.ReplyToCommand(
            JailbreakChat.Format(
                $"Живых T: {alivePrisoners}. Показано островков: {selected.Count}/{config.Points.Count}."));

        if (selected.Count > 0)
        {
            command.ReplyToCommand(
                JailbreakChat.Format(
                    $"Точки: {string.Join(", ", selected.Select(x => $"#{x.Id}"))}. Стены исчезнут через 15 сек."));
        }

        AddTimer(
            15.0f,
            _renderer.Clear,
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void ClearPoints(CommandInfo command, LavaMapConfig config)
    {
        var count = config.Points.Count;
        config.Points.Clear();
        _store!.Save(config);

        command.ReplyToCommand(
            JailbreakChat.Format(
                $"Для карты {config.Map} удалено точек: {count}."));
    }

    private static void PrintRules()
    {
        Server.PrintToChatAll(
            JailbreakChat.Format("Игровой день: Пол — лава."));
        Server.PrintToChatAll(
            JailbreakChat.Format("Джайлы открыты. Игра начнётся через 30 секунд."));
        Server.PrintToChatAll(
            JailbreakChat.Format("Перед лавой появятся зелёные безопасные островки."));
        Server.PrintToChatAll(
            JailbreakChat.Format("Когда начинается лава, игроки вне зелёных стен получают постепенный урон."));
        Server.PrintToChatAll(
            JailbreakChat.Format("Каждый цикл островки выбираются случайно, а урон лавы постепенно растёт."));
    }

    private static void OpenCells()
    {
        foreach (var designerName in DoorEntityNames)
        {
            foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(designerName))
            {
                if (entity.IsValid)
                    entity.AcceptInput("Open");
            }
        }

        foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_breakable"))
        {
            if (entity.IsValid)
                entity.AcceptInput("Break");
        }
    }

    private static void AnnounceAll(
        string title,
        string subtitle,
        UiNotificationType type,
        float durationSeconds)
    {
        var ui = UiCapability.Api.GetOptional();
        if (ui is null)
            return;

        foreach (var player in Utilities.GetPlayers().Where(
                     player => player is { IsValid: true, IsBot: false }))
        {
            ui.Announce(player, title, subtitle, type, durationSeconds);
        }
    }

    private static float RandomSeconds(float min, float max) =>
        min + (float)Random.Shared.NextDouble() * (max - min);

    private static float GetRoundTimeSeconds()
    {
        try
        {
            var minutes =
                ConVar.Find("mp_roundtime")?.GetPrimitiveValue<float>() ?? 0.0f;

            if (minutes > 0.0f)
                return Math.Max(1.0f, minutes * 60.0f);
        }
        catch
        {
        }

        return DefaultRoundTimeSeconds;
    }

    private static IEnumerable<CCSPlayerController> ActiveTerrorists() =>
        Utilities.GetPlayers().Where(IsActiveTerrorist);

    private static IEnumerable<CCSPlayerController> ActiveCounterTerrorists() =>
        Utilities.GetPlayers().Where(player =>
            IsUsable(player) &&
            player.PawnIsAlive &&
            player.Team == CsTeam.CounterTerrorist);

    private static bool IsActiveTerrorist(
        [NotNullWhen(true)] CCSPlayerController? player) =>
        IsUsable(player) &&
        player.PawnIsAlive &&
        player.Team == CsTeam.Terrorist;

    private static bool IsUsable(
        [NotNullWhen(true)] CCSPlayerController? player) =>
        player is { IsValid: true, PlayerPawn.IsValid: true } &&
        player.PlayerPawn.Value?.IsValid == true;

    private enum LavaPhase
    {
        Stopped,
        Preparation,
        Warning,
        Lava,
        Rest
    }
}
