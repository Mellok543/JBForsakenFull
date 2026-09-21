using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using JBF.Api;
using Microsoft.Extensions.Logging;

namespace JBF.RTV;

public sealed class JbfRtv : BasePlugin
{
    private const int VoteDurationSeconds = 15;
    private const int VoteMapCount = 5;
    private const double RtvRequiredFraction = 0.60;
    private const string StartupMapName = "jb_spy_vs_spy";
    private const ulong StartupMapWorkshopId = 3659814949;

    private readonly HashSet<ulong> _rtvVotes = [];
    private readonly Dictionary<string, int> _mapVotes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ulong> _mapVoters = [];
    private readonly Dictionary<ulong, string> _nominations = [];

    private List<MapInfo> _currentVoteMaps = [];
    private bool _voteActive;
    private bool _endVoteStarted;
    private bool _startupMapHandled;
    private DateTime _voteEndsAt;
    private DateTime _nextHudUpdate;

    public override string ModuleName => "JBF RTV";
    public override string ModuleVersion => "2.1.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        // A hot reload during a live map must never force a map change.
        _startupMapHandled = hotReload;

        AddCommand("css_rtv", "Проголосовать за досрочную смену карты", OnRtvCommand);
        AddCommand("css_nominate", "Номинировать карту", OnNominateCommand);
        AddCommand("css_timeleft", "Показать оставшееся время карты", OnTimeleftCommand);

        AddTimer(
            10.0f,
            CheckMapTime,
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnTick>(Tick);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
    }

    public override void Unload(bool hotReload)
    {
        ClearVoteHud();
        _rtvVotes.Clear();
        _mapVotes.Clear();
        _mapVoters.Clear();
        _nominations.Clear();
        _currentVoteMaps.Clear();
    }


    private void OnMapStart(string mapName)
    {
        if (_startupMapHandled)
            return;

        _startupMapHandled = true;

        if (string.Equals(
                mapName,
                StartupMapName,
                StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInformation(
                "RTV: startup map is already {Map} ({WorkshopId}).",
                StartupMapName,
                StartupMapWorkshopId);
            return;
        }

        Logger.LogInformation(
            "RTV: initial map {CurrentMap} detected. Switching startup map to {StartupMap} ({WorkshopId}).",
            mapName,
            StartupMapName,
            StartupMapWorkshopId);

        AddTimer(
            2.0f,
            () =>
            {
                Server.PrintToConsole(
                    $"[JBF] Startup map -> {StartupMapName} ({StartupMapWorkshopId}).");
                Server.ExecuteCommand(
                    $"ds_workshop_changelevel {StartupMapName}");
            },
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void CheckMapTime()
    {
        if (_endVoteStarted || _voteActive)
            return;

        var timeLimit = ConVar.Find("mp_timelimit");
        if (timeLimit is null)
            return;

        var minutes = timeLimit.GetPrimitiveValue<float>();
        if (minutes <= 0)
            return;

        var secondsLeft = minutes * 60.0f - Server.CurrentTime;
        if (secondsLeft > 300.0f)
            return;

        _endVoteStarted = true;
        Server.PrintToChatAll(
            JailbreakChat.Format(
                "До конца карты осталось 5 минут. Начинается голосование за следующую карту."));

        StartMapVote();
    }

    private void StartMapVote()
    {
        if (_voteActive)
            return;

        var maps = LoadMaps();
        if (maps.Count == 0)
        {
            Logger.LogWarning("RTV: maplist.txt is empty or unavailable.");
            Server.PrintToChatAll(
                JailbreakChat.Format("Не удалось запустить голосование: список карт пуст."));
            return;
        }

        _rtvVotes.Clear();
        _mapVotes.Clear();
        _mapVoters.Clear();

        var nominatedNames = _nominations.Values
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var nominatedMaps = maps
            .Where(map => nominatedNames.Contains(
                map.Name,
                StringComparer.OrdinalIgnoreCase))
            .Take(VoteMapCount)
            .ToList();

        var randomMaps = maps
            .Where(map => nominatedMaps.All(candidate =>
                !candidate.Name.Equals(map.Name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(_ => Random.Shared.Next())
            .Take(Math.Max(0, VoteMapCount - nominatedMaps.Count));

        _currentVoteMaps = nominatedMaps
            .Concat(randomMaps)
            .Take(VoteMapCount)
            .ToList();

        _nominations.Clear();

        foreach (var map in _currentVoteMaps)
            _mapVotes[map.Name] = 0;

        _voteActive = true;
        _voteEndsAt = DateTime.UtcNow.AddSeconds(VoteDurationSeconds);
        _nextHudUpdate = default;

        foreach (var player in HumanPlayers())
        {
            OpenVoteMenu(player);
            ShowVoteHud(player);
        }

        Server.PrintToChatAll(
            JailbreakChat.Format(
                $"Голосование за следующую карту началось. Время: {VoteDurationSeconds} сек."));

        AddTimer(
            VoteDurationSeconds,
            EndMapVote,
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void OpenVoteMenu(CCSPlayerController player)
    {
        var menu = MenuCapability.Api.GetOptional();
        if (menu is null)
            return;

        var options = _currentVoteMaps
            .Select(map => new JailbreakMenuOption(
                map.Name,
                voter => RegisterMapVote(voter, map)))
            .ToArray();

        menu.Open(player, "ВЫБОР КАРТЫ", options);
    }

    private void RegisterMapVote(CCSPlayerController player, MapInfo map)
    {
        if (!_voteActive)
            return;

        var steamId = player.AuthorizedSteamID?.SteamId64;
        if (steamId is null)
            return;

        if (!_mapVoters.Add(steamId.Value))
        {
            player.PrintToChat(JailbreakChat.Format("Ты уже проголосовал."));
            return;
        }

        _mapVotes[map.Name] = _mapVotes.GetValueOrDefault(map.Name) + 1;

        player.PrintToChat(
            JailbreakChat.Format($"Ты проголосовал за {map.Name}."));

        RefreshVoteHud();
    }

    private void Tick()
    {
        if (!_voteActive)
            return;

        var now = DateTime.UtcNow;
        if (now < _nextHudUpdate)
            return;

        _nextHudUpdate = now.AddMilliseconds(250);
        RefreshVoteHud();
    }

    private void RefreshVoteHud()
    {
        if (!_voteActive)
            return;

        foreach (var player in HumanPlayers())
            ShowVoteHud(player);
    }

    private void ShowVoteHud(CCSPlayerController player)
    {
        var ui = UiCapability.Api.GetOptional();
        if (ui is null)
            return;

        var secondsLeft = Math.Max(
            0,
            (int)Math.Ceiling((_voteEndsAt - DateTime.UtcNow).TotalSeconds));

        var entries = _currentVoteMaps
            .Select(map => new MapVoteHudEntry(
                map.Name,
                _mapVotes.GetValueOrDefault(map.Name)))
            .ToArray();

        ui.SetMapVote(
            player,
            "ГОЛОСОВАНИЕ",
            entries,
            $"{secondsLeft} СЕК.");
    }

    private void EndMapVote()
    {
        if (!_voteActive)
            return;

        _voteActive = false;

        foreach (var player in HumanPlayers())
        {
            MenuCapability.Api.GetOptional()?.Close(player);
            UiCapability.Api.GetOptional()?.ClearMapVote(player);
        }

        var highestVotes = _mapVotes.Values.DefaultIfEmpty(0).Max();
        if (highestVotes <= 0)
        {
            Server.PrintToChatAll(
                JailbreakChat.Format("Никто не проголосовал за карту."));
            return;
        }

        var leaders = _currentVoteMaps
            .Where(map => _mapVotes.GetValueOrDefault(map.Name) == highestVotes)
            .ToArray();

        if (leaders.Length == 0)
            return;

        var winner = leaders[Random.Shared.Next(leaders.Length)];

        Server.PrintToChatAll(
            JailbreakChat.Format(
                $"Победила карта {winner.Name} — {highestVotes} голос(ов)."));

        AddTimer(
            5.0f,
            () => Server.ExecuteCommand($"ds_workshop_changelevel {winner.Name}"),
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void OpenNominateMenu(CCSPlayerController player)
    {
        var menu = MenuCapability.Api.GetOptional();
        if (menu is null)
        {
            player.PrintToChat(JailbreakChat.Format("Menu API недоступно."));
            return;
        }

        var maps = LoadMaps();
        if (maps.Count == 0)
        {
            player.PrintToChat(JailbreakChat.Format("Список карт пуст."));
            return;
        }

        var options = maps
            .Select(map => new JailbreakMenuOption(
                map.Name,
                voter =>
                {
                    var steamId = voter.AuthorizedSteamID?.SteamId64;
                    if (steamId is null)
                        return;

                    _nominations[steamId.Value] = map.Name;
                    voter.PrintToChat(
                        JailbreakChat.Format(
                            $"Ты номинировал карту {map.Name}."));
                }))
            .ToArray();

        menu.Open(player, "НОМИНАЦИЯ КАРТЫ", options);
    }

    private void OnRtvCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsHuman(player))
            return;

        if (_voteActive)
        {
            player!.PrintToChat(
                JailbreakChat.Format("Голосование за карту уже идёт."));
            return;
        }

        var steamId = player!.AuthorizedSteamID?.SteamId64;
        if (steamId is null)
            return;

        var players = HumanPlayers().ToArray();
        var requiredVotes = Math.Max(
            1,
            (int)Math.Ceiling(players.Length * RtvRequiredFraction));

        if (!_rtvVotes.Add(steamId.Value))
        {
            player.PrintToChat(JailbreakChat.Format("Ты уже проголосовал за RTV."));
            return;
        }

        Server.PrintToChatAll(
            JailbreakChat.Format(
                $"RTV: {_rtvVotes.Count}/{requiredVotes}."));

        if (_rtvVotes.Count >= requiredVotes)
            StartMapVote();
    }

    private void OnNominateCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsHuman(player))
            return;

        if (_voteActive)
        {
            player!.PrintToChat(
                JailbreakChat.Format("Во время голосования номинации недоступны."));
            return;
        }

        OpenNominateMenu(player!);
    }

    private void OnTimeleftCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsHuman(player))
            return;

        var timeLimit = ConVar.Find("mp_timelimit");
        if (timeLimit is null)
        {
            player!.PrintToChat(
                JailbreakChat.Format("Не удалось получить mp_timelimit."));
            return;
        }

        var minutes = timeLimit.GetPrimitiveValue<float>();
        if (minutes <= 0)
        {
            player!.PrintToChat(
                JailbreakChat.Format("Лимит времени карты отключён."));
            return;
        }

        var secondsLeft = Math.Max(0.0f, minutes * 60.0f - Server.CurrentTime);
        var minutesLeft = (int)Math.Ceiling(secondsLeft / 60.0f);

        player!.PrintToChat(
            JailbreakChat.Format(
                $"До смены карты: {minutesLeft} мин."));
    }

    private List<MapInfo> LoadMaps()
    {
        var path = Path.Combine(ModuleDirectory, "maplist.txt");

        if (!File.Exists(path))
        {
            Logger.LogWarning("RTV: maplist.txt not found at {Path}", path);
            return [];
        }

        var result = new List<MapInfo>();

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                continue;

            var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                string.IsNullOrWhiteSpace(parts[0]) ||
                !ulong.TryParse(parts[1], out var workshopId))
            {
                Logger.LogWarning("RTV: invalid maplist line: {Line}", rawLine);
                continue;
            }

            result.Add(new MapInfo
            {
                Name = parts[0],
                WorkshopId = workshopId
            });
        }

        return result;
    }

    private void OnClientDisconnect(int slot)
    {
        var player = Utilities.GetPlayerFromSlot(slot);
        if (player is null)
            return;

        var steamId = player.AuthorizedSteamID?.SteamId64;
        if (steamId is null)
            return;

        _rtvVotes.Remove(steamId.Value);
        _nominations.Remove(steamId.Value);
    }

    private void ClearVoteHud()
    {
        foreach (var player in HumanPlayers())
            UiCapability.Api.GetOptional()?.ClearMapVote(player);
    }

    private static IEnumerable<CCSPlayerController> HumanPlayers() =>
        Utilities.GetPlayers().Where(IsHuman)!;

    private static bool IsHuman(CCSPlayerController? player) =>
        player is { IsValid: true, IsBot: false };
}
