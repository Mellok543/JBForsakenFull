using System.Collections.Concurrent;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using JBF.Api;
using JBF.Shop.Extensions;

namespace JBF.Shop.Services;

internal sealed class ShopSocialService
{
    private static readonly int[] StakeOptions = [10, 25, 50, 100, 250, 500];
    private static readonly int[] TransferOptions = [10, 25, 50, 100, 250, 500, 1000];
    private static readonly int[] RaffleOptions = [25, 50, 100, 250, 500, 1000];

    private readonly BasePlugin _plugin;
    private readonly ShopService _shop;
    private readonly Dictionary<Guid, PendingChallenge> _challenges = [];
    private ActiveRaffle? _raffle;

    public ShopSocialService(BasePlugin plugin, ShopService shop)
    {
        _plugin = plugin;
        _shop = shop;
    }

    public void OpenTransfer(CCSPlayerController player)
    {
        var menu = MenuCapability.Api.GetOptional();
        if (menu is null) return;

        var targets = Utilities.GetPlayers()
            .Where(p => p.IsUsable() && !p.IsBot && p.Slot != player.Slot)
            .OrderBy(p => p.PlayerName)
            .ToArray();

        if (targets.Length == 0)
        {
            player.PrintToChat(JailbreakChat.Format("Нет игроков для передачи кредитов."));
            return;
        }

        menu.Open(
            player,
            $"Передача | {_shop.GetCredits(player)} кредитов",
            targets.Select(target =>
                new JailbreakMenuOption(
                    target.PlayerName,
                    sender => OpenTransferAmount(sender, target)))
                .ToArray());
    }

    private void OpenTransferAmount(CCSPlayerController sender, CCSPlayerController target)
    {
        if (!target.IsUsable())
        {
            sender.PrintToChat(JailbreakChat.Format("Игрок уже недоступен."));
            return;
        }

        var menu = MenuCapability.Api.GetOptional();
        if (menu is null) return;

        menu.Open(
            sender,
            $"Передать → {target.PlayerName}",
            TransferOptions.Select(amount =>
                new JailbreakMenuOption(
                    $"{amount} кредитов",
                    player =>
                    {
                        if (!_shop.TryTransferCredits(player, target, amount, out var balance, out var error))
                        {
                            player.PrintToChat(JailbreakChat.Format(error));
                            OpenTransferAmount(player, target);
                            return;
                        }

                        player.PrintToChat(JailbreakChat.Format(
                            $"Вы передали {target.PlayerName} {amount} кредитов. Баланс: {balance}."));
                        target.PrintToChat(JailbreakChat.Format(
                            $"{player.PlayerName} передал вам {amount} кредитов."));
                    }))
                .Append(new JailbreakMenuOption("Назад", OpenTransfer))
                .ToArray());
    }

    public void OpenGames(CCSPlayerController player)
    {
        var menu = MenuCapability.Api.GetOptional();
        if (menu is null) return;

        JailbreakMenuOption[] options =
        [
            new("Камень, ножницы, бумага", p => OpenGameTargets(p, CreditGameType.RockPaperScissors)),
            new("Монетка", p => OpenGameTargets(p, CreditGameType.CoinFlip)),
            new("Покер — 5 карт", p => OpenGameTargets(p, CreditGameType.Poker)),
            new("Назад", _shop.OpenMainMenu)
        ];

        menu.Open(player, $"Игры | {_shop.GetCredits(player)} кредитов", options);
    }

    private void OpenGameTargets(CCSPlayerController player, CreditGameType game)
    {
        var menu = MenuCapability.Api.GetOptional();
        if (menu is null) return;

        var targets = Utilities.GetPlayers()
            .Where(p => p.IsUsable() && !p.IsBot && p.Slot != player.Slot)
            .OrderBy(p => p.PlayerName)
            .ToArray();

        if (targets.Length == 0)
        {
            player.PrintToChat(JailbreakChat.Format("Нет игроков для игры."));
            return;
        }

        menu.Open(
            player,
            $"{GameName(game)} | соперник",
            targets.Select(target =>
                    new JailbreakMenuOption(
                        target.PlayerName,
                        challenger => OpenStakeMenu(challenger, target, game)))
                .Append(new JailbreakMenuOption("Назад", OpenGames))
                .ToArray());
    }

    private void OpenStakeMenu(CCSPlayerController challenger, CCSPlayerController target, CreditGameType game)
    {
        if (!target.IsUsable())
        {
            challenger.PrintToChat(JailbreakChat.Format("Соперник уже недоступен."));
            return;
        }

        var menu = MenuCapability.Api.GetOptional();
        if (menu is null) return;

        menu.Open(
            challenger,
            $"{GameName(game)} | ставка",
            StakeOptions.Select(stake =>
                    new JailbreakMenuOption(
                        $"{stake} кредитов",
                        p => CreateChallenge(p, target, game, stake)))
                .Append(new JailbreakMenuOption("Назад", p => OpenGameTargets(p, game)))
                .ToArray());
    }

    private void CreateChallenge(
        CCSPlayerController challenger,
        CCSPlayerController target,
        CreditGameType game,
        int stake)
    {
        if (!challenger.IsUsable() || !target.IsUsable())
            return;

        if (_shop.GetCredits(challenger) < stake)
        {
            challenger.PrintToChat(JailbreakChat.Format("Недостаточно кредитов для этой ставки."));
            return;
        }

        if (_shop.GetCredits(target) < stake)
        {
            challenger.PrintToChat(JailbreakChat.Format(
                $"У {target.PlayerName} недостаточно кредитов для ставки {stake}."));
            return;
        }

        var challenge = new PendingChallenge(
            Guid.NewGuid(),
            challenger.SteamID,
            challenger.PlayerName,
            target.SteamID,
            target.PlayerName,
            game,
            stake,
            DateTime.UtcNow.AddSeconds(20));

        _challenges[challenge.Id] = challenge;

        challenger.PrintToChat(JailbreakChat.Format(
            $"Вызов отправлен игроку {target.PlayerName}: {GameName(game)}, ставка {stake}."));

        OpenChallenge(target, challenge);

        _plugin.AddTimer(
            20.0f,
            () =>
            {
                if (_challenges.Remove(challenge.Id))
                {
                    var source = FindPlayer(challenge.ChallengerSteamId);
                    source?.PrintToChat(JailbreakChat.Format(
                        $"Игрок {challenge.TargetName} не принял вызов."));
                }
            },
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void OpenChallenge(CCSPlayerController target, PendingChallenge challenge)
    {
        var menu = MenuCapability.Api.GetOptional();
        if (menu is null) return;

        menu.Open(
            target,
            $"{GameName(challenge.Game)} | {challenge.Stake}",
            [
                new JailbreakMenuOption(
                    $"Принять вызов от {challenge.ChallengerName}",
                    p => AcceptChallenge(p, challenge.Id)),
                new JailbreakMenuOption(
                    "Отклонить",
                    p =>
                    {
                        _challenges.Remove(challenge.Id);
                        FindPlayer(challenge.ChallengerSteamId)?.PrintToChat(
                            JailbreakChat.Format($"{p.PlayerName} отклонил вызов."));
                    })
            ]);
    }

    private void AcceptChallenge(CCSPlayerController target, Guid challengeId)
    {
        if (!_challenges.Remove(challengeId, out var challenge))
        {
            target.PrintToChat(JailbreakChat.Format("Вызов уже недействителен."));
            return;
        }

        if (challenge.ExpiresAt <= DateTime.UtcNow)
        {
            target.PrintToChat(JailbreakChat.Format("Время принятия вызова истекло."));
            return;
        }

        var challenger = FindPlayer(challenge.ChallengerSteamId);
        if (!challenger.IsUsable() || !target.IsUsable() || target.SteamID != challenge.TargetSteamId)
        {
            target.PrintToChat(JailbreakChat.Format("Один из игроков уже недоступен."));
            return;
        }

        if (!_shop.TryTakeCredits(challenger, challenge.Stake, out _))
        {
            target.PrintToChat(JailbreakChat.Format("У соперника больше нет нужной суммы."));
            challenger.PrintToChat(JailbreakChat.Format("Недостаточно кредитов для игры."));
            return;
        }

        if (!_shop.TryTakeCredits(target, challenge.Stake, out _))
        {
            _shop.TryGiveCredits(challenger, challenge.Stake, out _);
            target.PrintToChat(JailbreakChat.Format("Недостаточно кредитов для игры."));
            return;
        }

        switch (challenge.Game)
        {
            case CreditGameType.CoinFlip:
                ResolveCoinFlip(challenger, target, challenge.Stake);
                break;
            case CreditGameType.RockPaperScissors:
                OpenRpsChoice(challenger, target, challenge.Stake);
                break;
            case CreditGameType.Poker:
                ResolvePoker(challenger, target, challenge.Stake);
                break;
        }
    }

    private void ResolveCoinFlip(CCSPlayerController first, CCSPlayerController second, int stake)
    {
        var winner = Random.Shared.Next(2) == 0 ? first : second;
        var loser = winner.Slot == first.Slot ? second : first;
        var side = Random.Shared.Next(2) == 0 ? "ОРЁЛ" : "РЕШКА";

        _shop.TryGiveCredits(winner, checked(stake * 2), out var winnerBalance);

        Server.PrintToChatAll(JailbreakChat.Format(
            $"Монетка: {side}. {winner.PlayerName} выиграл у {loser.PlayerName} {stake} кредитов."));
        winner.PrintToChat(JailbreakChat.Format($"Баланс после игры: {winnerBalance}."));
    }

    private readonly Dictionary<Guid, RpsMatch> _rpsMatches = [];

    private void OpenRpsChoice(CCSPlayerController challenger, CCSPlayerController target, int stake)
    {
        var match = new RpsMatch(Guid.NewGuid(), challenger.SteamID, target.SteamID, stake);
        _rpsMatches[match.Id] = match;

        OpenRpsChoiceFor(challenger, match.Id);
        OpenRpsChoiceFor(target, match.Id);

        _plugin.AddTimer(
            20.0f,
            () =>
            {
                if (!_rpsMatches.Remove(match.Id, out var expired))
                    return;

                var a = FindPlayer(expired.FirstSteamId);
                var b = FindPlayer(expired.SecondSteamId);
                if (a.IsUsable()) _shop.TryGiveCredits(a, expired.Stake, out _);
                if (b.IsUsable()) _shop.TryGiveCredits(b, expired.Stake, out _);

                a?.PrintToChat(JailbreakChat.Format("КМН отменена: время выбора истекло. Ставка возвращена."));
                b?.PrintToChat(JailbreakChat.Format("КМН отменена: время выбора истекло. Ставка возвращена."));
            },
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void OpenRpsChoiceFor(CCSPlayerController player, Guid matchId)
    {
        var menu = MenuCapability.Api.GetOptional();
        if (menu is null) return;

        menu.Open(
            player,
            "КМН | сделайте выбор",
            [
                new JailbreakMenuOption("Камень", p => SelectRps(p, matchId, RpsMove.Rock)),
                new JailbreakMenuOption("Ножницы", p => SelectRps(p, matchId, RpsMove.Scissors)),
                new JailbreakMenuOption("Бумага", p => SelectRps(p, matchId, RpsMove.Paper))
            ]);
    }

    private void SelectRps(CCSPlayerController player, Guid matchId, RpsMove move)
    {
        if (!_rpsMatches.TryGetValue(matchId, out var match))
            return;

        if (player.SteamID == match.FirstSteamId)
            match.FirstMove = move;
        else if (player.SteamID == match.SecondSteamId)
            match.SecondMove = move;
        else
            return;

        player.PrintToChat(JailbreakChat.Format($"Вы выбрали: {RpsName(move)}."));

        if (match.FirstMove is null || match.SecondMove is null)
            return;

        _rpsMatches.Remove(matchId);

        var first = FindPlayer(match.FirstSteamId);
        var second = FindPlayer(match.SecondSteamId);
        if (!first.IsUsable() || !second.IsUsable())
        {
            if (first.IsUsable()) _shop.TryGiveCredits(first, match.Stake, out _);
            if (second.IsUsable()) _shop.TryGiveCredits(second, match.Stake, out _);
            return;
        }

        if (match.FirstMove == match.SecondMove)
        {
            _shop.TryGiveCredits(first, match.Stake, out _);
            _shop.TryGiveCredits(second, match.Stake, out _);
            Server.PrintToChatAll(JailbreakChat.Format(
                $"КМН: ничья ({RpsName(match.FirstMove.Value)}). Ставки возвращены."));
            return;
        }

        var firstWins =
            (match.FirstMove == RpsMove.Rock && match.SecondMove == RpsMove.Scissors) ||
            (match.FirstMove == RpsMove.Scissors && match.SecondMove == RpsMove.Paper) ||
            (match.FirstMove == RpsMove.Paper && match.SecondMove == RpsMove.Rock);

        var winner = firstWins ? first : second;
        var loser = firstWins ? second : first;
        _shop.TryGiveCredits(winner, checked(match.Stake * 2), out _);

        Server.PrintToChatAll(JailbreakChat.Format(
            $"КМН: {winner.PlayerName} ({RpsName(firstWins ? match.FirstMove.Value : match.SecondMove.Value)}) " +
            $"победил {loser.PlayerName} и выиграл {match.Stake} кредитов."));
    }

    private void ResolvePoker(CCSPlayerController first, CCSPlayerController second, int stake)
    {
        var deck = Enumerable.Range(0, 52).OrderBy(_ => Random.Shared.Next()).ToArray();
        var firstHand = deck.Take(5).Select(Card.FromIndex).ToArray();
        var secondHand = deck.Skip(5).Take(5).Select(Card.FromIndex).ToArray();

        var firstScore = PokerScore.Evaluate(firstHand);
        var secondScore = PokerScore.Evaluate(secondHand);

        first.PrintToChat(JailbreakChat.Format($"Покер: {FormatHand(firstHand)} | {firstScore.Name}."));
        second.PrintToChat(JailbreakChat.Format($"Покер: {FormatHand(secondHand)} | {secondScore.Name}."));

        var comparison = firstScore.CompareTo(secondScore);
        if (comparison == 0)
        {
            _shop.TryGiveCredits(first, stake, out _);
            _shop.TryGiveCredits(second, stake, out _);
            Server.PrintToChatAll(JailbreakChat.Format(
                $"Покер: {first.PlayerName} и {second.PlayerName} сыграли вничью. Ставки возвращены."));
            return;
        }

        var winner = comparison > 0 ? first : second;
        var loser = comparison > 0 ? second : first;
        var winningScore = comparison > 0 ? firstScore : secondScore;

        _shop.TryGiveCredits(winner, checked(stake * 2), out _);
        Server.PrintToChatAll(JailbreakChat.Format(
            $"Покер: {winner.PlayerName} победил {loser.PlayerName} ({winningScore.Name}) и выиграл {stake} кредитов."));
    }

    public void OpenRaffle(CCSPlayerController player)
    {
        var menu = MenuCapability.Api.GetOptional();
        if (menu is null) return;

        if (_raffle is { } raffle)
        {
            var remaining = Math.Max(0, (int)Math.Ceiling((raffle.EndsAt - DateTime.UtcNow).TotalSeconds));
            menu.Open(
                player,
                $"Розыгрыш | {raffle.Amount} кредитов",
                [
                    new JailbreakMenuOption($"Создатель: {raffle.CreatorName}", _ => { }, true),
                    new JailbreakMenuOption($"Участников: {raffle.Participants.Count}", _ => { }, true),
                    new JailbreakMenuOption($"До итогов: {remaining} сек.", _ => { }, true),
                    new JailbreakMenuOption(
                        player.SteamID == raffle.CreatorSteamId
                            ? "Вы создатель розыгрыша"
                            : raffle.Participants.Contains(player.SteamID)
                                ? "Вы уже участвуете"
                                : "Участвовать",
                        p => JoinRaffle(p),
                        player.SteamID == raffle.CreatorSteamId || raffle.Participants.Contains(player.SteamID)),
                    new JailbreakMenuOption("Назад", _shop.OpenMainMenu)
                ]);
            return;
        }

        menu.Open(
            player,
            $"Создать розыгрыш | {_shop.GetCredits(player)}",
            RaffleOptions.Select(amount =>
                    new JailbreakMenuOption(
                        $"Разыграть {amount} кредитов",
                        p => CreateRaffle(p, amount)))
                .Append(new JailbreakMenuOption("Назад", _shop.OpenMainMenu))
                .ToArray());
    }

    private void CreateRaffle(CCSPlayerController creator, int amount)
    {
        if (_raffle is not null)
        {
            OpenRaffle(creator);
            return;
        }

        if (!_shop.TryTakeCredits(creator, amount, out var balance))
        {
            creator.PrintToChat(JailbreakChat.Format("Недостаточно кредитов."));
            return;
        }

        var raffle = new ActiveRaffle(
            creator.SteamID,
            creator.PlayerName,
            amount,
            DateTime.UtcNow.AddSeconds(30),
            new HashSet<ulong>());

        _raffle = raffle;

        Server.PrintToChatAll(JailbreakChat.Format(
            $"{creator.PlayerName} разыгрывает {amount} кредитов! Откройте !shop → Розыгрыш кредитов."));

        creator.PrintToChat(JailbreakChat.Format($"Баланс после создания: {balance}."));

        _plugin.AddTimer(30.0f, FinishRaffle, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void JoinRaffle(CCSPlayerController player)
    {
        if (_raffle is null)
            return;

        if (player.SteamID == _raffle.CreatorSteamId)
        {
            player.PrintToChat(JailbreakChat.Format("Создатель не может участвовать в собственном розыгрыше."));
            return;
        }

        if (!_raffle.Participants.Add(player.SteamID))
        {
            player.PrintToChat(JailbreakChat.Format("Вы уже участвуете."));
            return;
        }

        player.PrintToChat(JailbreakChat.Format(
            $"Вы участвуете в розыгрыше {_raffle.Amount} кредитов."));
    }

    private void FinishRaffle()
    {
        var raffle = _raffle;
        _raffle = null;
        if (raffle is null)
            return;

        var candidates = raffle.Participants
            .Select(FindPlayer)
            .Where(p => p.IsUsable())
            .ToArray();

        if (candidates.Length == 0)
        {
            var creator = FindPlayer(raffle.CreatorSteamId);
            if (creator.IsUsable())
            {
                _shop.TryGiveCredits(creator, raffle.Amount, out _);
                creator.PrintToChat(JailbreakChat.Format(
                    "В розыгрыше не было участников. Кредиты возвращены."));
            }
            return;
        }

        var winner = candidates[Random.Shared.Next(candidates.Length)];
        _shop.TryGiveCredits(winner, raffle.Amount, out _);

        Server.PrintToChatAll(JailbreakChat.Format(
            $"Розыгрыш завершён! {winner.PlayerName} получил {raffle.Amount} кредитов."));
    }

    public void OpenTop(CCSPlayerController player)
    {
        var menu = MenuCapability.Api.GetOptional();
        if (menu is null) return;

        var top = _shop.GetTopBalances(20);
        var options = top
            .Select((entry, index) =>
                new JailbreakMenuOption(
                    $"#{index + 1} {entry.Name} — {entry.Credits}",
                    _ => { }))
            .Append(new JailbreakMenuOption("Назад", _shop.OpenMainMenu))
            .ToArray();

        menu.Open(player, "Топ богачей", options);
    }

    public void ResetRound()
    {
        foreach (var challenge in _challenges.Values.ToArray())
        {
            _challenges.Remove(challenge.Id);
        }
    }

    public void Shutdown()
    {
        if (_raffle is { } raffle)
        {
            var creator = FindPlayer(raffle.CreatorSteamId);
            if (creator.IsUsable())
                _shop.TryGiveCredits(creator, raffle.Amount, out _);
        }

        _raffle = null;
        _challenges.Clear();

        foreach (var match in _rpsMatches.Values)
        {
            var first = FindPlayer(match.FirstSteamId);
            var second = FindPlayer(match.SecondSteamId);
            if (first.IsUsable()) _shop.TryGiveCredits(first, match.Stake, out _);
            if (second.IsUsable()) _shop.TryGiveCredits(second, match.Stake, out _);
        }

        _rpsMatches.Clear();
    }

    private static CCSPlayerController? FindPlayer(ulong steamId) =>
        Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && p.SteamID == steamId);

    private static string GameName(CreditGameType type) => type switch
    {
        CreditGameType.RockPaperScissors => "КМН",
        CreditGameType.CoinFlip => "Монетка",
        CreditGameType.Poker => "Покер",
        _ => "Игра"
    };

    private static string RpsName(RpsMove move) => move switch
    {
        RpsMove.Rock => "Камень",
        RpsMove.Scissors => "Ножницы",
        RpsMove.Paper => "Бумага",
        _ => "?"
    };

    private static string FormatHand(IEnumerable<Card> hand) =>
        string.Join(" ", hand.Select(card => card.ToString()));

    private enum CreditGameType { RockPaperScissors, CoinFlip, Poker }
    private enum RpsMove { Rock, Scissors, Paper }

    private sealed record PendingChallenge(
        Guid Id,
        ulong ChallengerSteamId,
        string ChallengerName,
        ulong TargetSteamId,
        string TargetName,
        CreditGameType Game,
        int Stake,
        DateTime ExpiresAt);

    private sealed class RpsMatch
    {
        public Guid Id { get; }
        public ulong FirstSteamId { get; }
        public ulong SecondSteamId { get; }
        public int Stake { get; }
        public RpsMove? FirstMove { get; set; }
        public RpsMove? SecondMove { get; set; }

        public RpsMatch(Guid id, ulong firstSteamId, ulong secondSteamId, int stake)
        {
            Id = id;
            FirstSteamId = firstSteamId;
            SecondSteamId = secondSteamId;
            Stake = stake;
        }
    }

    private sealed record ActiveRaffle(
        ulong CreatorSteamId,
        string CreatorName,
        int Amount,
        DateTime EndsAt,
        HashSet<ulong> Participants);

    private readonly record struct Card(int Rank, int Suit)
    {
        public static Card FromIndex(int index) => new(index % 13 + 2, index / 13);

        public override string ToString()
        {
            var rank = Rank switch
            {
                14 => "A",
                13 => "K",
                12 => "Q",
                11 => "J",
                _ => Rank.ToString()
            };
            var suit = Suit switch
            {
                0 => "♠",
                1 => "♥",
                2 => "♦",
                _ => "♣"
            };
            return rank + suit;
        }
    }

    private readonly record struct PokerScore(int Category, int[] Kickers, string Name) : IComparable<PokerScore>
    {
        public int CompareTo(PokerScore other)
        {
            var category = Category.CompareTo(other.Category);
            if (category != 0) return category;

            var length = Math.Max(Kickers.Length, other.Kickers.Length);
            for (var i = 0; i < length; i++)
            {
                var left = i < Kickers.Length ? Kickers[i] : 0;
                var right = i < other.Kickers.Length ? other.Kickers[i] : 0;
                var compare = left.CompareTo(right);
                if (compare != 0) return compare;
            }

            return 0;
        }

        public static PokerScore Evaluate(Card[] cards)
        {
            var ranks = cards.Select(c => c.Rank).OrderByDescending(x => x).ToArray();
            var groups = ranks.GroupBy(x => x)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Key)
                .ToArray();

            var flush = cards.Select(c => c.Suit).Distinct().Count() == 1;
            var distinct = ranks.Distinct().OrderByDescending(x => x).ToArray();
            var straightHigh = 0;

            if (distinct.Length == 5)
            {
                if (distinct[0] - distinct[4] == 4)
                    straightHigh = distinct[0];
                else if (distinct.SequenceEqual([14, 5, 4, 3, 2]))
                    straightHigh = 5;
            }

            if (flush && straightHigh > 0)
                return new PokerScore(8, [straightHigh], "стрит-флеш");

            if (groups[0].Count() == 4)
                return new PokerScore(7, [groups[0].Key, groups[1].Key], "каре");

            if (groups[0].Count() == 3 && groups[1].Count() == 2)
                return new PokerScore(6, [groups[0].Key, groups[1].Key], "фулл-хаус");

            if (flush)
                return new PokerScore(5, ranks, "флеш");

            if (straightHigh > 0)
                return new PokerScore(4, [straightHigh], "стрит");

            if (groups[0].Count() == 3)
            {
                var kickers = groups.Skip(1).Select(g => g.Key).OrderByDescending(x => x);
                return new PokerScore(3, [groups[0].Key, .. kickers], "сет");
            }

            if (groups[0].Count() == 2 && groups[1].Count() == 2)
            {
                var pairHigh = Math.Max(groups[0].Key, groups[1].Key);
                var pairLow = Math.Min(groups[0].Key, groups[1].Key);
                return new PokerScore(2, [pairHigh, pairLow, groups[2].Key], "две пары");
            }

            if (groups[0].Count() == 2)
            {
                var kickers = groups.Skip(1).Select(g => g.Key).OrderByDescending(x => x);
                return new PokerScore(1, [groups[0].Key, .. kickers], "пара");
            }

            return new PokerScore(0, ranks, "старшая карта");
        }
    }
}
