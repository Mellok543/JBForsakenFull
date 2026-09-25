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
    private const int GameCommissionPercent = 5;
    private const int MinimumCustomStake = 10;
    private const int MinimumRaffleAmount = 2500;
    private const int MinimumPlayersForRaffle = 5;
    private const float RaffleDurationSeconds = 600.0f;

    private static readonly int[] StakeOptions = [10, 25, 50, 100, 250, 500];
    private static readonly int[] TransferOptions = [10, 25, 50, 100, 250, 500, 1000];
    private static readonly int[] RaffleOptions = [2500, 5000, 10000, 25000];

    private readonly BasePlugin _plugin;
    private readonly ShopService _shop;
    private readonly Dictionary<Guid, PendingChallenge> _challenges = [];
    private readonly Dictionary<Guid, PokerMatch> _pokerMatches = [];
    private readonly Dictionary<ulong, PendingCustomStake> _pendingCustomStakes = [];
    private readonly Dictionary<ulong, DateTime> _pendingCustomRaffles = [];
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
                    $"{amount} кредитов + 5% комиссия",
                    player =>
                    {
                        if (!_shop.TryTransferCredits(player, target, amount, out var balance, out var error))
                        {
                            player.PrintToChat(JailbreakChat.Format(error));
                            OpenTransferAmount(player, target);
                            return;
                        }

                        var fee = _shop.GetTransferFee(amount);
                        player.PrintToChat(JailbreakChat.Format(
                            $"Вы передали {target.PlayerName} {amount} кредитов. Комиссия: {fee}. Баланс: {balance}."));
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
                .Append(new JailbreakMenuOption(
                    "Своя сумма",
                    p => BeginCustomStakeInput(p, target, game)))
                .Append(new JailbreakMenuOption("Назад", p => OpenGameTargets(p, game)))
                .ToArray());
    }

    private void BeginCustomStakeInput(
        CCSPlayerController challenger,
        CCSPlayerController target,
        CreditGameType game)
    {
        if (!challenger.IsUsable() || !target.IsUsable())
            return;

        _pendingCustomStakes[challenger.SteamID] = new PendingCustomStake(
            target.SteamID,
            target.PlayerName,
            game,
            DateTime.UtcNow.AddSeconds(30));

        MenuCapability.Api.GetOptional()?.Close(challenger);
        challenger.PrintToChat(JailbreakChat.Format(
            $"Напишите в чат !<сумма>, например !500. Минимум {MinimumCustomStake}. Комиссия игры: {GameCommissionPercent}%."));
    }

    public void HandleCustomStakeInput(CCSPlayerController player, int amount)
    {
        if (!_pendingCustomStakes.Remove(player.SteamID, out var pending))
        {
            player.PrintToChat(JailbreakChat.Format(
                "Сначала выберите «Своя сумма» в меню игры."));
            return;
        }

        if (pending.ExpiresAt <= DateTime.UtcNow)
        {
            player.PrintToChat(JailbreakChat.Format("Время ввода ставки истекло."));
            return;
        }

        if (amount < MinimumCustomStake)
        {
            player.PrintToChat(JailbreakChat.Format(
                $"Минимальная ставка: {MinimumCustomStake} кредитов."));
            return;
        }

        var target = FindPlayer(pending.TargetSteamId);
        if (!target.IsUsable())
        {
            player.PrintToChat(JailbreakChat.Format("Соперник уже недоступен."));
            return;
        }

        CreateChallenge(player, target, pending.Game, amount);
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
                StartPoker(challenger, target, challenge.Stake);
                break;
        }
    }

    private void ResolveCoinFlip(CCSPlayerController first, CCSPlayerController second, int stake)
    {
        var winner = Random.Shared.Next(2) == 0 ? first : second;
        var loser = winner.Slot == first.Slot ? second : first;
        var side = Random.Shared.Next(2) == 0 ? "ОРЁЛ" : "РЕШКА";

        var payout = CalculateGamePayout(stake);
        var commission = checked(stake * 2) - payout;
        _shop.TryGiveCredits(winner, payout, out var winnerBalance);

        Server.PrintToChatAll(JailbreakChat.Format(
            $"Монетка: {side}. {winner.PlayerName} победил {loser.PlayerName}. Выплата: {payout}, комиссия: {commission}."));
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
        var payout = CalculateGamePayout(match.Stake);
        var commission = checked(match.Stake * 2) - payout;
        _shop.TryGiveCredits(winner, payout, out _);

        Server.PrintToChatAll(JailbreakChat.Format(
            $"КМН: {winner.PlayerName} ({RpsName(firstWins ? match.FirstMove.Value : match.SecondMove.Value)}) " +
            $"победил {loser.PlayerName}. Выплата: {payout}, комиссия: {commission}."));
    }

    private void StartPoker(CCSPlayerController first, CCSPlayerController second, int ante)
    {
        var deck = Enumerable.Range(0, 52)
            .OrderBy(_ => Random.Shared.Next())
            .Select(Card.FromIndex)
            .ToArray();

        var match = new PokerMatch(
            Guid.NewGuid(),
            first.SteamID,
            first.PlayerName,
            second.SteamID,
            second.PlayerName,
            ante,
            deck.Take(2).ToArray(),
            deck.Skip(2).Take(2).ToArray(),
            deck.Skip(4).Take(5).ToArray())
        {
            Pot = checked(ante * 2),
            FirstInvested = ante,
            SecondInvested = ante,
            Street = PokerStreet.PreFlop,
            CurrentActorSteamId = first.SteamID
        };

        _pokerMatches[match.Id] = match;

        first.PrintToChat(JailbreakChat.Format(
            $"Покер начался против {second.PlayerName}. Анте: {ante}. Ваши карты: {FormatHand(match.FirstHole)}."));
        second.PrintToChat(JailbreakChat.Format(
            $"Покер начался против {first.PlayerName}. Анте: {ante}. Ваши карты: {FormatHand(match.SecondHole)}."));

        OpenPokerTurn(match);
    }

    private void OpenPokerTurn(PokerMatch match)
    {
        if (!_pokerMatches.ContainsKey(match.Id))
            return;

        var actor = FindPlayer(match.CurrentActorSteamId);
        var opponent = FindPlayer(match.Other(match.CurrentActorSteamId));

        if (!actor.IsUsable())
        {
            if (opponent.IsUsable())
                AwardPokerByFold(match, opponent, actor?.PlayerName ?? "соперник");
            else
                CancelPoker(match);
            return;
        }

        if (!opponent.IsUsable())
        {
            AwardPokerByFold(match, actor, opponent?.PlayerName ?? "соперник");
            return;
        }

        var menu = MenuCapability.Api.GetOptional();
        if (menu is null)
        {
            CancelPoker(match);
            return;
        }

        var hole = match.HoleFor(actor.SteamID);
        var toCall = match.CurrentBet - match.RoundContribution(actor.SteamID);
        var community = VisibleCommunity(match);
        var options = new List<JailbreakMenuOption>
        {
            new($"Ваши карты: {FormatHand(hole)}", _ => { }, true),
            new($"Стол: {(community.Length == 0 ? "—" : FormatHand(community))}", _ => { }, true),
            new($"Банк: {match.Pot} | текущая ставка: {match.CurrentBet}", _ => { }, true)
        };

        if (toCall == 0)
        {
            options.Add(new JailbreakMenuOption("Чек", p => PokerCheck(p, match.Id)));
        }
        else
        {
            var canCall = _shop.GetCredits(actor) >= toCall;
            options.Add(new JailbreakMenuOption(
                $"Колл {toCall}",
                p => PokerCall(p, match.Id),
                !canCall,
                !canCall ? "Недостаточно кредитов" : null));
        }

        options.Add(new JailbreakMenuOption("Повысить ставку", p => OpenPokerRaiseMenu(p, match.Id)));
        options.Add(new JailbreakMenuOption("Пас", p => PokerFold(p, match.Id)));

        menu.Open(actor, $"Покер | {StreetName(match.Street)}", options);

        match.TurnToken++;
        var token = match.TurnToken;
        _plugin.AddTimer(
            30.0f,
            () =>
            {
                if (!_pokerMatches.TryGetValue(match.Id, out var current) ||
                    current.TurnToken != token ||
                    current.CurrentActorSteamId != actor.SteamID)
                {
                    return;
                }

                actor.PrintToChat(JailbreakChat.Format("Покер: время хода истекло — пас."));
                PokerFold(actor, match.Id);
            },
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void OpenPokerRaiseMenu(CCSPlayerController player, Guid matchId)
    {
        if (!_pokerMatches.TryGetValue(matchId, out var match) ||
            match.CurrentActorSteamId != player.SteamID)
            return;

        var menu = MenuCapability.Api.GetOptional();
        if (menu is null) return;

        var toCall = match.CurrentBet - match.RoundContribution(player.SteamID);
        var raises = StakeOptions
            .Select(amount =>
            {
                var totalCost = checked(toCall + amount);
                var disabled = _shop.GetCredits(player) < totalCost;
                return new JailbreakMenuOption(
                    $"+{amount} (внести {totalCost})",
                    p => PokerRaise(p, matchId, amount),
                    disabled,
                    disabled ? "Недостаточно кредитов" : null);
            })
            .Append(new JailbreakMenuOption("Назад", _ => OpenPokerTurn(match)))
            .ToArray();

        menu.Open(player, "Покер | повышение", raises);
    }

    private void PokerCheck(CCSPlayerController player, Guid matchId)
    {
        if (!_pokerMatches.TryGetValue(matchId, out var match) ||
            match.CurrentActorSteamId != player.SteamID)
            return;

        if (match.CurrentBet != match.RoundContribution(player.SteamID))
            return;

        if (match.LastActionWasCheck)
        {
            AdvancePokerStreet(match);
            return;
        }

        match.LastActionWasCheck = true;
        match.CurrentActorSteamId = match.Other(player.SteamID);
        OpenPokerTurn(match);
    }

    private void PokerCall(CCSPlayerController player, Guid matchId)
    {
        if (!_pokerMatches.TryGetValue(matchId, out var match) ||
            match.CurrentActorSteamId != player.SteamID)
            return;

        var toCall = match.CurrentBet - match.RoundContribution(player.SteamID);
        if (toCall <= 0)
        {
            PokerCheck(player, matchId);
            return;
        }

        if (!_shop.TryTakeCredits(player, toCall, out _))
        {
            player.PrintToChat(JailbreakChat.Format("Недостаточно кредитов для колла."));
            OpenPokerTurn(match);
            return;
        }

        match.AddContribution(player.SteamID, toCall);
        match.Pot = checked(match.Pot + toCall);
        match.AddInvested(player.SteamID, toCall);

        AdvancePokerStreet(match);
    }

    private void PokerRaise(CCSPlayerController player, Guid matchId, int raiseBy)
    {
        if (!_pokerMatches.TryGetValue(matchId, out var match) ||
            match.CurrentActorSteamId != player.SteamID ||
            raiseBy <= 0)
            return;

        var toCall = match.CurrentBet - match.RoundContribution(player.SteamID);
        var total = checked(toCall + raiseBy);

        if (!_shop.TryTakeCredits(player, total, out _))
        {
            player.PrintToChat(JailbreakChat.Format("Недостаточно кредитов для повышения."));
            OpenPokerTurn(match);
            return;
        }

        match.AddContribution(player.SteamID, total);
        match.AddInvested(player.SteamID, total);
        match.Pot = checked(match.Pot + total);
        match.CurrentBet = match.RoundContribution(player.SteamID);
        match.LastActionWasCheck = false;
        match.CurrentActorSteamId = match.Other(player.SteamID);

        var opponent = FindPlayer(match.CurrentActorSteamId);
        opponent?.PrintToChat(JailbreakChat.Format(
            $"{player.PlayerName} повысил ставку на {raiseBy}. Нужно уравнять {match.CurrentBet - match.RoundContribution(match.CurrentActorSteamId)}."));

        OpenPokerTurn(match);
    }

    private void PokerFold(CCSPlayerController player, Guid matchId)
    {
        if (!_pokerMatches.TryGetValue(matchId, out var match) ||
            match.CurrentActorSteamId != player.SteamID)
            return;

        var winner = FindPlayer(match.Other(player.SteamID));
        if (!winner.IsUsable())
        {
            CancelPoker(match);
            return;
        }

        AwardPokerByFold(match, winner, player.PlayerName);
    }

    private void AdvancePokerStreet(PokerMatch match)
    {
        match.FirstRoundContribution = 0;
        match.SecondRoundContribution = 0;
        match.CurrentBet = 0;
        match.LastActionWasCheck = false;

        if (match.Street == PokerStreet.River)
        {
            ResolvePokerShowdown(match);
            return;
        }

        match.Street++;
        match.CurrentActorSteamId =
            match.Street == PokerStreet.PreFlop ? match.FirstSteamId : match.SecondSteamId;

        var first = FindPlayer(match.FirstSteamId);
        var second = FindPlayer(match.SecondSteamId);
        var board = VisibleCommunity(match);

        first?.PrintToChat(JailbreakChat.Format(
            $"Покер: {StreetName(match.Street)}. Стол: {FormatHand(board)}. Банк: {match.Pot}."));
        second?.PrintToChat(JailbreakChat.Format(
            $"Покер: {StreetName(match.Street)}. Стол: {FormatHand(board)}. Банк: {match.Pot}."));

        OpenPokerTurn(match);
    }

    private void ResolvePokerShowdown(PokerMatch match)
    {
        if (!_pokerMatches.Remove(match.Id))
            return;

        var first = FindPlayer(match.FirstSteamId);
        var second = FindPlayer(match.SecondSteamId);

        if (!first.IsUsable() || !second.IsUsable())
        {
            if (first.IsUsable()) AwardDetachedPokerWinner(match, first, second?.PlayerName ?? match.SecondName);
            else if (second.IsUsable()) AwardDetachedPokerWinner(match, second, first?.PlayerName ?? match.FirstName);
            return;
        }

        var firstScore = BestPokerScore(match.FirstHole.Concat(match.Community).ToArray());
        var secondScore = BestPokerScore(match.SecondHole.Concat(match.Community).ToArray());

        first.PrintToChat(JailbreakChat.Format(
            $"Шоудаун: ваши {FormatHand(match.FirstHole)} | {firstScore.Name}. Соперник: {FormatHand(match.SecondHole)}."));
        second.PrintToChat(JailbreakChat.Format(
            $"Шоудаун: ваши {FormatHand(match.SecondHole)} | {secondScore.Name}. Соперник: {FormatHand(match.FirstHole)}."));

        var comparison = firstScore.CompareTo(secondScore);
        var payout = CalculatePotPayout(match.Pot);
        var commission = match.Pot - payout;

        if (comparison == 0)
        {
            var firstShare = payout / 2;
            var secondShare = payout - firstShare;
            _shop.TryGiveCredits(first, firstShare, out _);
            _shop.TryGiveCredits(second, secondShare, out _);

            Server.PrintToChatAll(JailbreakChat.Format(
                $"Покер: ничья между {first.PlayerName} и {second.PlayerName}. Банк {match.Pot}, комиссия {commission}."));
            return;
        }

        var winner = comparison > 0 ? first : second;
        var loser = comparison > 0 ? second : first;
        var winningScore = comparison > 0 ? firstScore : secondScore;

        _shop.TryGiveCredits(winner, payout, out _);

        Server.PrintToChatAll(JailbreakChat.Format(
            $"Покер: {winner.PlayerName} победил {loser.PlayerName} ({winningScore.Name}). " +
            $"Банк: {match.Pot}, выплата: {payout}, комиссия: {commission}."));
    }

    private void AwardPokerByFold(PokerMatch match, CCSPlayerController winner, string foldedName)
    {
        if (!_pokerMatches.Remove(match.Id))
            return;

        var payout = CalculatePotPayout(match.Pot);
        var commission = match.Pot - payout;
        _shop.TryGiveCredits(winner, payout, out _);

        Server.PrintToChatAll(JailbreakChat.Format(
            $"Покер: {foldedName} сделал пас. {winner.PlayerName} получает {payout} из банка {match.Pot}. Комиссия: {commission}."));
    }

    private void AwardDetachedPokerWinner(PokerMatch match, CCSPlayerController winner, string loserName)
    {
        var payout = CalculatePotPayout(match.Pot);
        var commission = match.Pot - payout;
        _shop.TryGiveCredits(winner, payout, out _);

        Server.PrintToChatAll(JailbreakChat.Format(
            $"Покер: {loserName} покинул игру. {winner.PlayerName} получает {payout}. Комиссия: {commission}."));
    }

    private void CancelPoker(PokerMatch match)
    {
        if (!_pokerMatches.Remove(match.Id))
            return;

        var first = FindPlayer(match.FirstSteamId);
        var second = FindPlayer(match.SecondSteamId);

        if (first.IsUsable())
            _shop.TryGiveCredits(first, match.FirstInvested, out _);
        if (second.IsUsable())
            _shop.TryGiveCredits(second, match.SecondInvested, out _);
    }

    private static Card[] VisibleCommunity(PokerMatch match)
    {
        var count = match.Street switch
        {
            PokerStreet.PreFlop => 0,
            PokerStreet.Flop => 3,
            PokerStreet.Turn => 4,
            PokerStreet.River => 5,
            _ => 5
        };

        return match.Community.Take(count).ToArray();
    }

    private static PokerScore BestPokerScore(Card[] cards)
    {
        PokerScore? best = null;

        for (var a = 0; a < cards.Length - 4; a++)
        for (var b = a + 1; b < cards.Length - 3; b++)
        for (var d = b + 1; d < cards.Length - 2; d++)
        for (var e = d + 1; e < cards.Length - 1; e++)
        for (var f = e + 1; f < cards.Length; f++)
        {
            var score = PokerScore.Evaluate([cards[a], cards[b], cards[d], cards[e], cards[f]]);
            if (best is null || score.CompareTo(best.Value) > 0)
                best = score;
        }

        return best ?? throw new InvalidOperationException("Недостаточно карт для оценки покера.");
    }

    private static int CalculatePotPayout(int pot)
        => Math.Max(0, (int)Math.Floor(pot * 0.95m));

    private static string StreetName(PokerStreet street) => street switch
    {
        PokerStreet.PreFlop => "Префлоп",
        PokerStreet.Flop => "Флоп",
        PokerStreet.Turn => "Тёрн",
        PokerStreet.River => "Ривер",
        _ => "Покер"
    };

    public void OpenRaffle(CCSPlayerController player)
    {
        var menu = MenuCapability.Api.GetOptional();
        if (menu is null) return;

        if (_raffle is { } raffle)
        {
            var remaining = Math.Max(0, (int)Math.Ceiling((raffle.EndsAt - DateTime.UtcNow).TotalSeconds));
            var remainingMinutes = remaining / 60;
            var remainingSeconds = remaining % 60;
            menu.Open(
                player,
                $"Розыгрыш | {raffle.Amount} кредитов",
                [
                    new JailbreakMenuOption($"Создатель: {raffle.CreatorName}", _ => { }, true),
                    new JailbreakMenuOption($"Участников: {raffle.Participants.Count}", _ => { }, true),
                    new JailbreakMenuOption($"До итогов: {remainingMinutes}:{remainingSeconds:00}", _ => { }, true),
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
                .Append(new JailbreakMenuOption(
                    "Своя сумма",
                    BeginCustomRaffleInput))
                .Append(new JailbreakMenuOption("Назад", _shop.OpenMainMenu))
                .ToArray());
    }

    private void BeginCustomRaffleInput(CCSPlayerController creator)
    {
        _pendingCustomRaffles[creator.SteamID] = DateTime.UtcNow.AddSeconds(30);
        MenuCapability.Api.GetOptional()?.Close(creator);
        creator.PrintToChat(JailbreakChat.Format(
            $"Напишите в чат !<сумма>, например !5000. Минимальная сумма: {MinimumRaffleAmount} кредитов."));
    }

    public bool TryHandleBangAmount(CCSPlayerController player, int amount)
    {
        if (_pendingCustomStakes.ContainsKey(player.SteamID))
        {
            HandleCustomStakeInput(player, amount);
            return true;
        }

        if (_pendingCustomRaffles.Remove(player.SteamID, out var expiresAt))
        {
            if (expiresAt <= DateTime.UtcNow)
            {
                player.PrintToChat(JailbreakChat.Format("Время ввода суммы розыгрыша истекло."));
                return true;
            }

            CreateRaffle(player, amount);
            return true;
        }

        return false;
    }

    private void CreateRaffle(CCSPlayerController creator, int amount)
    {
        if (_raffle is not null)
        {
            OpenRaffle(creator);
            return;
        }

        if (amount < MinimumRaffleAmount)
        {
            creator.PrintToChat(JailbreakChat.Format(
                $"Минимальная сумма розыгрыша: {MinimumRaffleAmount} кредитов."));
            return;
        }

        var onlinePlayers = Utilities.GetPlayers()
            .Count(p => p.IsValid && !p.IsBot);

        if (onlinePlayers < MinimumPlayersForRaffle)
        {
            creator.PrintToChat(JailbreakChat.Format(
                $"Для розыгрыша нужно минимум {MinimumPlayersForRaffle} игроков на сервере. Сейчас: {onlinePlayers}."));
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
            DateTime.UtcNow.AddSeconds(RaffleDurationSeconds),
            new HashSet<ulong>());

        _raffle = raffle;

        Server.PrintToChatAll(JailbreakChat.Format(
            $"{creator.PlayerName} разыгрывает {amount} кредитов! Откройте !shop → Розыгрыш кредитов."));

        creator.PrintToChat(JailbreakChat.Format($"Баланс после создания: {balance}."));

        _plugin.AddTimer(RaffleDurationSeconds, FinishRaffle);
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
        _pendingCustomStakes.Clear();
        _pendingCustomRaffles.Clear();

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
        _pendingCustomStakes.Clear();
        _pendingCustomRaffles.Clear();

        foreach (var match in _rpsMatches.Values)
        {
            var first = FindPlayer(match.FirstSteamId);
            var second = FindPlayer(match.SecondSteamId);
            if (first.IsUsable()) _shop.TryGiveCredits(first, match.Stake, out _);
            if (second.IsUsable()) _shop.TryGiveCredits(second, match.Stake, out _);
        }

        _rpsMatches.Clear();

        foreach (var match in _pokerMatches.Values.ToArray())
            CancelPoker(match);

        _pokerMatches.Clear();
    }

    private static int CalculateGamePayout(int stake)
    {
        var pot = checked(stake * 2);
        return (int)Math.Floor(pot * 0.95m);
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

    private sealed record PendingCustomStake(
        ulong TargetSteamId,
        string TargetName,
        CreditGameType Game,
        DateTime ExpiresAt);

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

    private enum PokerStreet
    {
        PreFlop,
        Flop,
        Turn,
        River
    }

    private sealed class PokerMatch
    {
        public Guid Id { get; }
        public ulong FirstSteamId { get; }
        public string FirstName { get; }
        public ulong SecondSteamId { get; }
        public string SecondName { get; }
        public int Ante { get; }
        public Card[] FirstHole { get; }
        public Card[] SecondHole { get; }
        public Card[] Community { get; }

        public int Pot { get; set; }
        public int FirstInvested { get; set; }
        public int SecondInvested { get; set; }
        public int FirstRoundContribution { get; set; }
        public int SecondRoundContribution { get; set; }
        public int CurrentBet { get; set; }
        public bool LastActionWasCheck { get; set; }
        public PokerStreet Street { get; set; }
        public ulong CurrentActorSteamId { get; set; }
        public int TurnToken { get; set; }

        public PokerMatch(
            Guid id,
            ulong firstSteamId,
            string firstName,
            ulong secondSteamId,
            string secondName,
            int ante,
            Card[] firstHole,
            Card[] secondHole,
            Card[] community)
        {
            Id = id;
            FirstSteamId = firstSteamId;
            FirstName = firstName;
            SecondSteamId = secondSteamId;
            SecondName = secondName;
            Ante = ante;
            FirstHole = firstHole;
            SecondHole = secondHole;
            Community = community;
        }

        public ulong Other(ulong steamId) => steamId == FirstSteamId ? SecondSteamId : FirstSteamId;

        public Card[] HoleFor(ulong steamId) => steamId == FirstSteamId ? FirstHole : SecondHole;

        public int RoundContribution(ulong steamId)
            => steamId == FirstSteamId ? FirstRoundContribution : SecondRoundContribution;

        public void AddContribution(ulong steamId, int amount)
        {
            if (steamId == FirstSteamId) FirstRoundContribution += amount;
            else SecondRoundContribution += amount;
        }

        public void AddInvested(ulong steamId, int amount)
        {
            if (steamId == FirstSteamId) FirstInvested += amount;
            else SecondInvested += amount;
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
