using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;

namespace JBF.TeamBalance;

public sealed class JBFTeamBalance : BasePlugin, IPluginConfig<TeamBalanceConfig>
{
    private readonly Dictionary<ulong, CtTestSession> _sessions = [];
    private readonly Dictionary<ulong, DateTime> _bans = [];
    private readonly Queue<ulong> _queue = new();
    private readonly Dictionary<ulong, DateTime> _guardJoinedAt = [];
    private readonly HashSet<ulong> _approvedCtSwitches = [];
    private readonly Dictionary<int, DateTime> _joiningPlayers = [];

    private TeamBalanceDatabase? _database;
    private DateTime _nextCtTestHudUpdate;
    private DateTime _roundStartedAt;
    private bool _roundActive;

    public override string ModuleName => "JBF Team Balance";
    public override string ModuleVersion => "1.3.2";
    public override string ModuleAuthor => "Mell";

    public TeamBalanceConfig Config { get; set; } = new();

    public void OnConfigParsed(TeamBalanceConfig config)
    {
        Config = config;
        Config.TerroristsPerGuard = Math.Max(1, Config.TerroristsPerGuard);
        Config.QuestionNumber = Math.Max(1, Config.QuestionNumber);
        Config.CtBan = Math.Max(1, Config.CtBan);
        Config.QuestionTimeSeconds = Math.Clamp(Config.QuestionTimeSeconds, 5, 120);

        _database = new TeamBalanceDatabase(Config.Connection);
        _ = InitializeDatabaseAsync();
    }

    public override void Load(bool hotReload)
    {
        AddCommandListener("jointeam", OnJoinTeam, HookMode.Pre);
        AddCommand("css_ct", "Пройти тест / встать в очередь за CT", OnCtCommand);
        RegisterListener<Listeners.OnClientPutInServer>(OnClientPutInServer);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.OnTick>(OnTick);
    }

    public override void Unload(bool hotReload)
    {
        RemoveCommandListener("jointeam", OnJoinTeam, HookMode.Pre);
        foreach (var player in Utilities.GetPlayers().Where(IsUsable))
            UiCapability.Api.GetOptional()?.ClearQuestion(player!);

        _sessions.Clear();
        _queue.Clear();
        _guardJoinedAt.Clear();
        _approvedCtSwitches.Clear();
        _joiningPlayers.Clear();
    }

    private async Task InitializeDatabaseAsync()
    {
        if (_database is null)
            return;

        try
        {
            await _database.InitializeAsync();
            var bans = await _database.LoadBansAsync();

            Server.NextFrame(() =>
            {
                _bans.Clear();
                foreach (var pair in bans)
                    _bans[pair.Key] = pair.Value;

                Server.PrintToConsole($"[JBF] TeamBalance DB ready. Bans: {_bans.Count}.");
            });
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[JBF] TeamBalance database error: {ex.Message}");
        }
    }

    private HookResult OnJoinTeam(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsUsable(player))
            return HookResult.Continue;

        if (!int.TryParse(command.ArgByIndex(1), out var team))
            return HookResult.Continue;

        if (team == 0)
        {
            player!.PrintToChat(JailbreakChat.Format("Случайный выбор команды отключён. Для CT используй !ct."));
            return HookResult.Handled;
        }

        if (team == 3)
        {
            player!.PrintToChat(JailbreakChat.Format("Вход за CT доступен только через !ct."));
            return HookResult.Handled;
        }

        return HookResult.Continue;
    }

    private void OnCtCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsUsable(player))
            return;

        if (player!.Team != CsTeam.Terrorist)
        {
            player.PrintToChat(JailbreakChat.Format("Чтобы встать в очередь CT, сначала перейди за T."));
            return;
        }

        var id = player.SteamID;

        if (_bans.TryGetValue(id, out var banUntil))
        {
            if (DateTime.UtcNow < banUntil)
            {
                var minutes = (int)Math.Ceiling((banUntil - DateTime.UtcNow).TotalMinutes);
                player.PrintToChat(JailbreakChat.Format($"Вход за CT заблокирован ещё на {minutes} мин."));
                return;
            }

            _bans.Remove(id);
            if (_database is not null)
                _ = SafeDb(() => _database.RemoveBanAsync(id));
        }

        if (_sessions.ContainsKey(id))
        {
            player.PrintToChat(JailbreakChat.Format("Ты уже проходишь тест CT."));
            return;
        }

        if (_queue.Contains(id))
        {
            player.PrintToChat(JailbreakChat.Format($"Ты уже в очереди CT. Позиция: {QueuePosition(id)}."));
            return;
        }

        StartTest(player);
    }

    private void StartTest(CCSPlayerController player)
    {
        if (Config.Questions.Count == 0)
        {
            player.PrintToChat(JailbreakChat.Format("Тест CT не настроен: в конфиге нет вопросов."));
            return;
        }

        var count = Math.Min(Config.QuestionNumber, Config.Questions.Count);
        var questions = Config.Questions
            .OrderBy(_ => Random.Shared.Next())
            .Take(count)
            .ToList();

        _sessions[player.SteamID] = new CtTestSession(questions);

        player.PrintToChat(JailbreakChat.Format($"Тест CT: {count} вопросов. Ошибаться нельзя."));
        ShowQuestion(player);
    }

    private void ShowQuestion(CCSPlayerController player)
    {
        if (!_sessions.TryGetValue(player.SteamID, out var session) ||
            session.CurrentQuestion >= session.Questions.Count)
            return;

        var menu = MenuCapability.Api.GetOptional();
        var ui = UiCapability.Api.GetOptional();

        if (menu is null || ui is null)
        {
            player.PrintToChat(JailbreakChat.Format("Menu/UI API недоступно."));
            _sessions.Remove(player.SteamID);
            return;
        }

        var question = session.Questions[session.CurrentQuestion];
        session.QuestionDeadline = DateTime.UtcNow.AddSeconds(Config.QuestionTimeSeconds);
        session.LastDisplayedSeconds = -1;

        var options = question.Answers
            .OrderBy(_ => Random.Shared.Next())
            .Select(answer => new JailbreakMenuOption(answer, p => HandleAnswer(p, answer)))
            .ToArray();

        ui.SetQuestion(
            player,
            $"ВОПРОС {session.CurrentQuestion + 1}/{session.Questions.Count}",
            question.Question,
            $"{Config.QuestionTimeSeconds} СЕК.");

        menu.Open(
            player,
            "ВАРИАНТЫ ОТВЕТА",
            options);
    }

    private void HandleAnswer(CCSPlayerController player, string answer)
    {
        if (!_sessions.TryGetValue(player.SteamID, out var session) ||
            session.CurrentQuestion >= session.Questions.Count)
            return;

        var question = session.Questions[session.CurrentQuestion];

        if (DateTime.UtcNow >= session.QuestionDeadline)
        {
            FailCtTest(player, "Время на ответ истекло.");
            return;
        }

        if (!string.Equals(answer, question.CorrectAnswer, StringComparison.Ordinal))
        {
            FailCtTest(player, "Неверный ответ.");
            return;
        }

        session.CurrentQuestion++;

        if (session.CurrentQuestion < session.Questions.Count)
        {
            ShowQuestion(player);
            return;
        }

        _sessions.Remove(player.SteamID);

        MenuCapability.Api.GetOptional()?.Close(player);
        UiCapability.Api.GetOptional()?.ClearQuestion(player);
        player.PrintToChat(JailbreakChat.Format("Тест CT пройден. Ты добавлен в очередь."));
        AddToQueue(player);
    }

    private void OnTick()
    {
        UpdateCtTestHud();
        EnforceJoiningPlayersTeam();
    }

    private void UpdateCtTestHud()
    {
        var now = DateTime.UtcNow;
        if (now < _nextCtTestHudUpdate)
            return;

        _nextCtTestHudUpdate = now.AddMilliseconds(200);

        foreach (var pair in _sessions.ToArray())
        {
            var player = Utilities.GetPlayers()
                .FirstOrDefault(candidate =>
                    IsUsable(candidate) &&
                    candidate!.SteamID == pair.Key);

            if (player is null)
                continue;

            var session = pair.Value;
            var secondsLeft = Math.Max(
                0,
                (int)Math.Ceiling((session.QuestionDeadline - now).TotalSeconds));

            if (secondsLeft <= 0)
            {
                FailCtTest(player, "Время на ответ истекло.");
                continue;
            }

            if (session.LastDisplayedSeconds == secondsLeft)
                continue;

            session.LastDisplayedSeconds = secondsLeft;

            var question = session.Questions[session.CurrentQuestion];
            UiCapability.Api.GetOptional()?.SetQuestion(
                player,
                $"ВОПРОС {session.CurrentQuestion + 1}/{session.Questions.Count}",
                question.Question,
                $"{secondsLeft} СЕК.");
        }
    }

    private void FailCtTest(CCSPlayerController player, string reason)
    {
        _sessions.Remove(player.SteamID);
        MenuCapability.Api.GetOptional()?.Close(player);
        UiCapability.Api.GetOptional()?.ClearQuestion(player);

        var until = DateTime.UtcNow.AddMinutes(Config.CtBan);
        _bans[player.SteamID] = until;

        if (_database is not null)
            _ = SafeDb(() => _database.SetBanAsync(player.SteamID, until));

        player.PrintToChat(
            JailbreakChat.Format(
                $"{reason} CT заблокированы на {Config.CtBan} мин."));
    }

    private void AddToQueue(CCSPlayerController player)
    {
        if (_queue.Contains(player.SteamID))
        {
            player.PrintToChat(JailbreakChat.Format($"Ты уже в очереди CT. Позиция: {QueuePosition(player.SteamID)}."));
            return;
        }

        _queue.Enqueue(player.SteamID);
        player.PrintToChat(JailbreakChat.Format($"Ты добавлен в очередь CT. Позиция: {_queue.Count}. Перевод выполняется в конце раунда."));
    }

    [GameEventHandler]
    public HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _roundStartedAt = DateTime.UtcNow;
        _roundActive = true;
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _roundActive = false;
        Server.NextFrame(BalanceAtRoundEnd);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPlayerTeam(EventPlayerTeam @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!IsUsable(player))
            return HookResult.Continue;

        if (player!.Team == CsTeam.CounterTerrorist)
        {
            if (!_approvedCtSwitches.Remove(player.SteamID))
            {
                var unauthorized = player;
                Server.NextFrame(() =>
                {
                    if (!IsUsable(unauthorized) || unauthorized.Team != CsTeam.CounterTerrorist)
                        return;

                    unauthorized.SwitchTeam(CsTeam.Terrorist);
                    unauthorized.PrintToChat(JailbreakChat.Format("Автоматический/случайный перевод за CT отменён. Для входа используй !ct и пройди тест."));
                });

                return HookResult.Continue;
            }

            _guardJoinedAt[player.SteamID] = DateTime.UtcNow;
            RemoveFromQueue(player.SteamID);
            _sessions.Remove(player.SteamID);
            UiCapability.Api.GetOptional()?.ClearQuestion(player);
        }
        else
        {
            _guardJoinedAt.Remove(player.SteamID);
            _approvedCtSwitches.Remove(player.SteamID);
        }

        return HookResult.Continue;
    }

    private void BalanceAtRoundEnd()
    {
        var active = Utilities.GetPlayers()
            .Where(p => IsUsable(p) && p!.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
            .Select(p => p!)
            .ToArray();

        if (active.Length == 0)
            return;

        var maxCt = GetMaxCt(active.Length);
        var guards = active
            .Where(p => p.Team == CsTeam.CounterTerrorist)
            .ToList();

        if (guards.Count > maxCt)
        {
            var excess = guards.Count - maxCt;

            foreach (var guard in guards
                         .OrderByDescending(p => _guardJoinedAt.GetValueOrDefault(p.SteamID, DateTime.MinValue))
                         .Take(excess))
            {
                guard.SwitchTeam(CsTeam.Terrorist);
                _guardJoinedAt.Remove(guard.SteamID);
                guard.PrintToChat(JailbreakChat.Format($"Баланс команд: ты переведён за T. Лимит CT: {maxCt}."));
            }
        }

        while (_queue.Count > 0)
        {
            active = Utilities.GetPlayers()
                .Where(p => IsUsable(p) && p!.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
                .Select(p => p!)
                .ToArray();

            maxCt = GetMaxCt(active.Length);
            var ctCount = active.Count(p => p.Team == CsTeam.CounterTerrorist);

            if (ctCount >= maxCt)
                break;

            var id = _queue.Dequeue();
            var player = active.FirstOrDefault(p => p.SteamID == id);

            if (player is null || player.Team != CsTeam.Terrorist)
                continue;

            _approvedCtSwitches.Add(id);
            player.SwitchTeam(CsTeam.CounterTerrorist);
            _guardJoinedAt[id] = DateTime.UtcNow;

            player.PrintToChat(JailbreakChat.Format($"Ты переведён за CT. CT: {ctCount + 1}/{maxCt}."));
        }
    }

    private int GetMaxCt(int totalPlayers)
    {
        var ratio = Math.Max(1, Config.TerroristsPerGuard);
        return totalPlayers <= 0
            ? 0
            : Math.Max(1, totalPlayers / (ratio + 1));
    }

    private int QueuePosition(ulong steamId)
    {
        var index = 1;

        foreach (var id in _queue)
        {
            if (id == steamId)
                return index;

            index++;
        }

        return 0;
    }

    private void RemoveFromQueue(ulong steamId)
    {
        if (!_queue.Contains(steamId))
            return;

        var remaining = _queue
            .Where(id => id != steamId)
            .ToArray();

        _queue.Clear();

        foreach (var id in remaining)
            _queue.Enqueue(id);
    }

    private void OnClientPutInServer(int slot)
    {
        // Casual can auto-assign a player after the initial connection callbacks.
        // Keep a short protection window and force only unauthorized CT placement
        // back to T during that window.
        _joiningPlayers[slot] = DateTime.UtcNow.AddSeconds(10);

        Server.NextFrame(() => EnsureJoiningPlayerNotCt(slot, tryRespawn: false));

        AddTimer(
            1.5f,
            () => EnsureJoiningPlayerNotCt(slot, tryRespawn: true),
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void EnforceJoiningPlayersTeam()
    {
        var now = DateTime.UtcNow;

        foreach (var pair in _joiningPlayers.ToArray())
        {
            if (now >= pair.Value)
            {
                _joiningPlayers.Remove(pair.Key);
                continue;
            }

            EnsureJoiningPlayerNotCt(pair.Key, tryRespawn: false);
        }
    }

    private void EnsureJoiningPlayerNotCt(int slot, bool tryRespawn)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (!IsUsable(player))
            return;

        if (player!.Team == CsTeam.CounterTerrorist)
        {
            // This is not an approved queue transfer, so never allow the
            // Casual auto-assignment to leave a newly connected player in CT.
            _approvedCtSwitches.Remove(player.SteamID);
            player.SwitchTeam(CsTeam.Terrorist);

            // Switching an already spawned player changes the team but keeps
            // the old CT position. Respawn on the next frame so the player is
            // placed on a T spawn (the jail cells on Jailbreak maps).
            Server.NextFrame(() =>
            {
                if (!IsUsable(player) || player.Team != CsTeam.Terrorist)
                    return;

                player.Respawn();
                player.PrintToChat(JailbreakChat.Format(
                    "Ты автоматически переведён за T и перемещён на респаун заключённых."));
            });

            return;
        }

        if (tryRespawn)
            TryRespawnLateJoin(player);
    }

    private void TryRespawnLateJoin(CCSPlayerController player)
    {
        if (!_roundActive ||
            DateTime.UtcNow - _roundStartedAt > TimeSpan.FromSeconds(30))
        {
            return;
        }

        // Give the team switch one more frame to settle before respawning.
        Server.NextFrame(() =>
        {
            if (!IsUsable(player) ||
                player.Team != CsTeam.Terrorist ||
                player.PawnIsAlive)
            {
                return;
            }

            player.Respawn();
            player.PrintToChat(
                JailbreakChat.Format(
                    "Ты подключился в первые 30 секунд раунда и был автоматически возрождён."));
        });
    }

    private void OnClientDisconnect(int slot)
    {
        var player = Utilities.GetPlayerFromSlot(slot);

        if (player is null || player.SteamID == 0)
            return;

        _joiningPlayers.Remove(slot);
        UiCapability.Api.GetOptional()?.ClearQuestion(player);
        _sessions.Remove(player.SteamID);
        _guardJoinedAt.Remove(player.SteamID);
        _approvedCtSwitches.Remove(player.SteamID);
        RemoveFromQueue(player.SteamID);
    }

    private async Task SafeDb(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Server.PrintToConsole($"[JBF] TeamBalance DB write failed: {ex.Message}");
        }
    }

    private static bool IsUsable(CCSPlayerController? player) =>
        player is { IsValid: true, IsBot: false } && player.SteamID != 0;

    private sealed class CtTestSession
    {
        public CtTestSession(List<CtQuestion> questions) => Questions = questions;

        public List<CtQuestion> Questions { get; }

        public int CurrentQuestion { get; set; }

        public DateTime QuestionDeadline { get; set; }

        public int LastDisplayedSeconds { get; set; } = -1;
    }
}
