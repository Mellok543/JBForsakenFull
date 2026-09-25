using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using JBF.Api;
using JBF.Shop.Extensions;
using JBF.Shop.Models;

namespace JBF.Shop.Services;

internal sealed class ShopService : IShopApi
{
    private const float SpeedMultiplier = 1.25f;
    private const float GravityMultiplier = 0.65f;
    private const float SmallModelScale = 0.65f;
    private const float DoubleJumpVelocity = 300.0f;
    private const float BhopVelocity = 300.0f;
    private const float FloatTolerance = 0.001f;
    private const string GuardModel = "characters/models/ctm_sas/ctm_sas.vmdl";

    private readonly BasePlugin _plugin;
    private readonly string _configPath;
    private readonly ShopStorage _storage;
    private readonly Dictionary<ulong, ShopPlayerState> _players;
    private readonly Dictionary<ulong, Dictionary<string, int>> _roundPurchases = [];
    private readonly Dictionary<ulong, TimedEffectState> _speedEffects = [];
    private readonly Dictionary<ulong, TimedEffectState> _gravityEffects = [];
    private readonly Dictionary<ulong, DateTime> _doubleJumpEffects = [];
    private readonly Dictionary<ulong, DateTime> _bhopEffects = [];
    private readonly Dictionary<ulong, bool> _doubleJumpUsed = [];
    private readonly Dictionary<ulong, bool> _jumpWasPressed = [];
    private readonly Dictionary<ulong, ModelScaleEffectState> _smallModelEffects = [];
    private readonly Dictionary<ulong, ModelEffectState> _guardDisguiseEffects = [];
    private readonly HashSet<ulong> _rebelsThisRound = [];

    private ShopConfig _config;
    private IReadOnlyList<ShopItem> _items;
    private readonly ShopSocialService _social;
    private long _effectVersion;

    public ShopService(BasePlugin plugin)
    {
        _plugin = plugin;
        _configPath = ShopConfigPath.Get(plugin.ModuleDirectory);
        _config = LoadConfig();
        _storage = new ShopStorage(_config.Database);
        _players = _storage.Load();
        _items = BuildItems(_config.Items);
        _social = new ShopSocialService(plugin, this);
    }

    public void ReloadConfig()
    {
        if (TryReloadConfig(out var error))
            return;

        Server.PrintToConsole($"[JBF] Shop reload failed: {error}");
    }

    public bool TryReloadConfig(out string error)
    {
        if (!ShopConfig.TryLoad(_configPath, out var loaded, out error))
            return false;

        _config = loaded;
        _items = BuildItems(_config.Items);
        return true;
    }

    public void PrintCreditStatus(CCSPlayerController player)
    {
        var state = GetState(player);
        if (state is null)
        {
            player.PrintToChat(JailbreakChat.Format("Не удалось определить данные кредитов."));
            return;
        }

        player.PrintToChat(JailbreakChat.Format($"Баланс: {state.Credits} кредитов."));

        if (player.Team == CsTeam.Terrorist)
        {
            var isRebelThisRound = _rebelsThisRound.Contains(player.SteamID);
            var lawfulNext = StreakReward(
                IncrementStreak(state.LawfulStreak),
                _config.Rewards.LawfulStreakBase,
                _config.Rewards.LawfulStreakStep,
                _config.Rewards.LawfulStreakMaxReward);
            var rebelNext = StreakReward(
                IncrementStreak(state.RebelStreak),
                _config.Rewards.RebelStreakBase,
                _config.Rewards.RebelStreakStep,
                _config.Rewards.RebelStreakMaxReward);

            player.PrintToChat(JailbreakChat.Format(
                $"Мирная серия: {state.LawfulStreak} | следующий мирный раунд: +{lawfulNext}."));
            player.PrintToChat(JailbreakChat.Format(
                $"Серия бунта: {state.RebelStreak} | следующий бунтующий раунд: +{rebelNext}."));
            player.PrintToChat(JailbreakChat.Format(
                isRebelThisRound ? "Текущий раунд: вы уже стали бунтарём." : "Текущий раунд: пока без бунта."));
        }
        else if (player.Team == CsTeam.CounterTerrorist)
        {
            var guardNext = StreakReward(
                IncrementStreak(state.GuardDutyStreak),
                _config.Rewards.GuardDutyStreakBase,
                _config.Rewards.GuardDutyStreakStep,
                _config.Rewards.GuardDutyStreakMaxReward);

            player.PrintToChat(JailbreakChat.Format(
                $"Серия CT: {state.GuardDutyStreak} | следующий раунд за CT: +{guardNext}."));
            player.PrintToChat(JailbreakChat.Format(
                $"Убийство бунтаря: +{_config.Rewards.GuardKillRebel} | победа CT: +{_config.Rewards.GuardTeamWin}."));
        }
        else
        {
            player.PrintToChat(JailbreakChat.Format(
                $"Серии: мирная {state.LawfulStreak}, бунт {state.RebelStreak}, CT {state.GuardDutyStreak}."));
        }
    }

    public int GetCredits(CCSPlayerController player)
    {
        return GetState(player)?.Credits ?? 0;
    }

    public bool TryGetCredits(CCSPlayerController player, out int credits)
    {
        var state = GetState(player);
        credits = state?.Credits ?? 0;
        return state is not null;
    }

    public bool TrySetCredits(CCSPlayerController player, int credits, out int newBalance)
    {
        var state = GetState(player);
        if (state is null || credits < 0)
        {
            newBalance = 0;
            return false;
        }

        state.Credits = credits;
        newBalance = state.Credits;
        _storage.QueueSave(state);
        return true;
    }

    public bool TryAddCredits(CCSPlayerController player, int amount, out int newBalance)
    {
        var state = GetState(player);
        if (state is null)
        {
            newBalance = 0;
            return false;
        }

        state.Credits = ClampCredits((long)state.Credits + amount);
        newBalance = state.Credits;
        _storage.QueueSave(state);
        return true;
    }

    public void OpenShop(CCSPlayerController player) => OpenMainMenu(player);

    public void OpenMainMenu(CCSPlayerController player)
    {
        if (!player.IsValid || player.IsBot)
            return;

        var menuApi = MenuCapability.Api.GetOptional();
        if (menuApi is null)
        {
            player.PrintToChat(JailbreakChat.Format("Menu API недоступно."));
            return;
        }

        JailbreakMenuOption[] options =
        [
            new("Магазин", OpenItemsMenu),
            new("Игры", _social.OpenGames),
            new("Розыгрыш кредитов", _social.OpenRaffle),
            new("Топ богачей", _social.OpenTop)
        ];

        menuApi.Open(player, $"Кредиты | {GetCredits(player)}", options);
    }

    private void OpenItemsMenu(CCSPlayerController player)
    {
        if (!CanUseShop(player))
            return;

        var menuApi = MenuCapability.Api.GetOptional();
        if (menuApi is null)
        {
            player.PrintToChat(JailbreakChat.Format("Menu API недоступно."));
            return;
        }

        var credits = GetCredits(player);
        var itemOptions = _items
            .Where(item => item.Team is null || item.Team == player.Team)
            .Select(item =>
            {
                var used = GetRoundUses(player, item.Id);
                var limitReached = used >= item.RoundLimit;
                var suffix = $" [{used}/{item.RoundLimit}]";
                var affordability = credits < item.Cost ? " • не хватает кредитов" : string.Empty;

                return new JailbreakMenuOption(
                    $"{item.Text} - {item.Cost} кредитов{suffix}{affordability}",
                    buyer => Buy(buyer, item),
                    limitReached,
                    limitReached ? "Лимит покупки на этот раунд исчерпан" : null);
            });

        var options = new[]
            {
                new JailbreakMenuOption("Передать кредиты", _social.OpenTransfer)
            }
            .Concat(itemOptions)
            .Append(new JailbreakMenuOption("Назад", OpenMainMenu))
            .ToArray();

        menuApi.Open(player, $"Магазин | {credits} кредитов", options);
    }

    internal bool TryTransferCredits(
        CCSPlayerController sender,
        CCSPlayerController target,
        int amount,
        out int senderBalance,
        out string error)
    {
        senderBalance = 0;
        error = string.Empty;

        if (amount <= 0 || !sender.IsUsable() || !target.IsUsable() || sender.Slot == target.Slot)
        {
            error = "Передача недоступна.";
            return false;
        }

        var senderState = GetState(sender);
        var targetState = GetState(target);
        if (senderState is null || targetState is null)
        {
            error = "Не удалось определить данные игрока.";
            return false;
        }

        var fee = CalculateFivePercentFee(amount);
        var totalCharge = (long)amount + fee;

        if (senderState.Credits < totalCharge)
        {
            senderBalance = senderState.Credits;
            error = $"Недостаточно кредитов. С учётом комиссии 5% нужно {totalCharge}.";
            return false;
        }

        senderState.Credits -= (int)totalCharge;
        targetState.Credits = ClampCredits((long)targetState.Credits + amount);
        senderBalance = senderState.Credits;

        _storage.QueueSave(senderState);
        _storage.QueueSave(targetState);
        return true;
    }

    internal int GetTransferFee(int amount) => CalculateFivePercentFee(amount);

    internal void HandleCustomStakeInput(CCSPlayerController player, int amount)
        => _social.HandleCustomStakeInput(player, amount);

    internal void HandleCustomRaffleInput(CCSPlayerController player, int amount)
        => _social.HandleCustomRaffleInput(player, amount);

    internal bool TryTakeCredits(CCSPlayerController player, int amount, out int newBalance)
    {
        var state = GetState(player);
        if (state is null || amount <= 0 || state.Credits < amount)
        {
            newBalance = state?.Credits ?? 0;
            return false;
        }

        state.Credits -= amount;
        newBalance = state.Credits;
        _storage.QueueSave(state);
        return true;
    }

    internal bool TryGiveCredits(CCSPlayerController player, int amount, out int newBalance)
    {
        if (amount <= 0)
        {
            newBalance = GetCredits(player);
            return false;
        }

        return TryAddCredits(player, amount, out newBalance);
    }

    internal IReadOnlyList<(string Name, int Credits)> GetTopBalances(int limit)
    {
        return _players.Values
            .Where(state => !string.IsNullOrWhiteSpace(state.PlayerName))
            .OrderByDescending(state => state.Credits)
            .ThenBy(state => state.PlayerName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, limit))
            .Select(state => (state.PlayerName, state.Credits))
            .ToArray();
    }

    public void RewardRoundPlayers(CsTeam winner)
    {
        foreach (var player in Utilities.GetPlayers().Where(player =>
                     player.IsUsable() && !player.IsBot &&
                     player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            RewardPlayer(player, _config.Rewards.RoundParticipation, "за участие в раунде", announce: false);

            if (player.PawnIsAlive)
                RewardPlayer(player, _config.Rewards.RoundSurvival, "за выживание", announce: false);

            var state = GetState(player);
            if (state is null)
                continue;

            if (player.Team == CsTeam.Terrorist)
            {
                state.GuardDutyStreak = 0;

                if (_rebelsThisRound.Contains(player.SteamID))
                {
                    state.RebelStreak = IncrementStreak(state.RebelStreak);
                    state.LawfulStreak = 0;

                    var reward = StreakReward(
                        state.RebelStreak,
                        _config.Rewards.RebelStreakBase,
                        _config.Rewards.RebelStreakStep,
                        _config.Rewards.RebelStreakMaxReward);

                    RewardPlayer(player, reward, $"за серию бунта x{state.RebelStreak}");
                }
                else
                {
                    state.LawfulStreak = IncrementStreak(state.LawfulStreak);
                    state.RebelStreak = 0;

                    var reward = StreakReward(
                        state.LawfulStreak,
                        _config.Rewards.LawfulStreakBase,
                        _config.Rewards.LawfulStreakStep,
                        _config.Rewards.LawfulStreakMaxReward);

                    RewardPlayer(player, reward, $"за мирную серию x{state.LawfulStreak}");
                }

                if (winner == CsTeam.Terrorist)
                    RewardPlayer(player, _config.Rewards.InmateTeamWin, "за победу заключённых", announce: false);
            }
            else
            {
                state.LawfulStreak = 0;
                state.RebelStreak = 0;
                state.GuardDutyStreak = IncrementStreak(state.GuardDutyStreak);

                var reward = StreakReward(
                    state.GuardDutyStreak,
                    _config.Rewards.GuardDutyStreakBase,
                    _config.Rewards.GuardDutyStreakStep,
                    _config.Rewards.GuardDutyStreakMaxReward);

                RewardPlayer(player, reward, $"за службу в охране x{state.GuardDutyStreak}");

                if (winner == CsTeam.CounterTerrorist)
                    RewardPlayer(player, _config.Rewards.GuardTeamWin, "за победу охраны", announce: false);
            }

            _storage.QueueSave(state);
        }
    }

    public void MarkRebel(CCSPlayerController player)
    {
        if (!player.IsUsable() || player.Team != CsTeam.Terrorist)
            return;

        _rebelsThisRound.Add(player.SteamID);
    }

    public void RewardRebelKilled(RebelKilledEvent data)
    {
        var killer = data.Killer;
        if (!killer.IsUsable() || killer!.Team != CsTeam.CounterTerrorist)
            return;

        RewardPlayer(killer, _config.Rewards.GuardKillRebel, "за убийство бунтаря");
    }

    public void RewardKill(CCSPlayerController? victim, CCSPlayerController? attacker)
    {
        if (!victim.IsUsable() || !attacker.IsUsable() || victim.Slot == attacker.Slot)
        {
            return;
        }

        if (LrCapability.Api.GetOptional()?.IsActive == true || SpecialDaysCapability.Api.GetOptional()?.IsActive == true)
        {
            return;
        }

        if (attacker.Team == CsTeam.Terrorist && victim.Team == CsTeam.CounterTerrorist)
        {
            RewardPlayer(attacker, _config.Rewards.InmateKillGuard, "за убийство охранника");
        }
    }

    public void RewardLrMatch(LrMatchEndedEvent match)
    {
        RewardPlayer(match.Inmate, _config.Rewards.LrParticipation, "за участие в LR", announce: false);
        RewardPlayer(match.Guardian, _config.Rewards.LrParticipation, "за участие в LR", announce: false);

        if (match.Winner.IsUsable())
        {
            RewardPlayer(match.Winner, _config.Rewards.LrWin, $"за победу в LR ({match.GameName})", announce: true);
        }
    }

    public void ResetRound()
    {
        _social.ResetRound();
        _roundPurchases.Clear();
        _rebelsThisRound.Clear();

        // Old timers are invalidated because every new effect receives a globally
        // unique version. Clearing here prevents effects from leaking into a new round.
        _speedEffects.Clear();
        _gravityEffects.Clear();
        _doubleJumpEffects.Clear();
        _bhopEffects.Clear();
        _doubleJumpUsed.Clear();
        _jumpWasPressed.Clear();
        RestoreAllSmallModels();
        RestoreAllGuardDisguises();
        _smallModelEffects.Clear();
        _guardDisguiseEffects.Clear();
    }

    public void Shutdown()
    {
        _social.Shutdown();
        RestoreAllSmallModels();
        RestoreAllGuardDisguises();
        _speedEffects.Clear();
        _gravityEffects.Clear();
        _doubleJumpEffects.Clear();
        _bhopEffects.Clear();
        _doubleJumpUsed.Clear();
        _jumpWasPressed.Clear();
        _smallModelEffects.Clear();
        _guardDisguiseEffects.Clear();
        _storage.Dispose();
    }

    public void Tick()
    {
        var now = DateTime.UtcNow;

        foreach (var player in Utilities.GetPlayers().Where(player =>
                     player.IsUsable() && !player.IsBot && player.PawnIsAlive))
        {
            var steamId = player.GetSteamId64();
            if (steamId is null)
                continue;

            var pawn = player.PlayerPawn.Value!;
            var flags = (PlayerFlags)pawn.Flags;
            var onGround = flags.HasFlag(PlayerFlags.FL_ONGROUND);
            var jumpPressed = player.Buttons.HasFlag(PlayerButtons.Jump);

            if (_bhopEffects.TryGetValue(steamId.Value, out var bhopUntil))
            {
                if (now >= bhopUntil)
                {
                    _bhopEffects.Remove(steamId.Value);
                }
                else if (jumpPressed &&
                         onGround &&
                         !pawn.MoveType.HasFlag(MoveType_t.MOVETYPE_LADDER))
                {
                    pawn.AbsVelocity.Z = BhopVelocity;
                }
            }

            if (_doubleJumpEffects.TryGetValue(steamId.Value, out var doubleJumpUntil))
            {
                if (now >= doubleJumpUntil)
                {
                    _doubleJumpEffects.Remove(steamId.Value);
                    _doubleJumpUsed.Remove(steamId.Value);
                    _jumpWasPressed.Remove(steamId.Value);
                }
                else
                {
                    if (onGround)
                        _doubleJumpUsed[steamId.Value] = false;

                    var wasPressed = _jumpWasPressed.GetValueOrDefault(steamId.Value);
                    var canDoubleJump =
                        !onGround &&
                        jumpPressed &&
                        !wasPressed &&
                        !_doubleJumpUsed.GetValueOrDefault(steamId.Value) &&
                        !pawn.MoveType.HasFlag(MoveType_t.MOVETYPE_LADDER);

                    if (canDoubleJump)
                    {
                        pawn.AbsVelocity.Z = DoubleJumpVelocity;
                        _doubleJumpUsed[steamId.Value] = true;
                    }

                    _jumpWasPressed[steamId.Value] = jumpPressed;
                }
            }
        }
    }

    private IReadOnlyList<ShopItem> BuildItems(ShopItemsConfig config)
    {
        ShopItem?[] items =
        [
            CreateItem("hp-50", "+50 HP", config.Hp50, null, player =>
            {
                var pawn = player.PlayerPawn.Value!;
                player.SetHealthValue(Math.Min(200, pawn.Health + 50));
            }),
            CreateItem("hp-100", "+100 HP", config.Hp100, null, player =>
            {
                var pawn = player.PlayerPawn.Value!;
                player.SetHealthValue(Math.Min(200, pawn.Health + 100));
            }),
            CreateItem("armor-50", "50 брони", config.Armor50, null, player =>
            {
                var pawn = player.PlayerPawn.Value!;
                player.SetArmorValue(Math.Min(200, pawn.ArmorValue + 50));
            }),
            CreateItem("armor-100", "100 брони", config.Armor100, null, player =>
            {
                var pawn = player.PlayerPawn.Value!;
                player.SetArmorValue(Math.Min(200, pawn.ArmorValue + 100));
            }),
            CreateItem("smoke", "Дымовая граната", config.Smoke, CsTeam.Terrorist, player => player.GiveNamedItem(CsItem.SmokeGrenade)),
            CreateItem("flash", "Флешка", config.Flash, CsTeam.Terrorist, player => player.GiveNamedItem(CsItem.Flashbang)),
            CreateItem("speed", "Скорость на 10 секунд", config.Speed, CsTeam.Terrorist, ApplySpeed),
            CreateItem("gravity", "Гравитация на 10 секунд", config.Gravity, CsTeam.Terrorist, ApplyGravity),
            CreateItem("double-jump", "Двойной прыжок на 7 секунд", config.DoubleJump, CsTeam.Terrorist, ApplyDoubleJump),
            CreateItem("bhop-7s", "Bhop на 7 секунд", config.Bhop, CsTeam.Terrorist, ApplyBhop),
            CreateItem("small-model", "Уменьшенная модель на 5 секунд", config.SmallModel, CsTeam.Terrorist, ApplySmallModel),
            CreateItem("guard-disguise", "Костюм охранника на 15 секунд", config.GuardDisguise, CsTeam.Terrorist, ApplyGuardDisguise)
        ];

        return items.OfType<ShopItem>().ToArray();
    }

    private static ShopItem? CreateItem(
        string id,
        string text,
        ShopItemConfig config,
        CsTeam? team,
        Action<CCSPlayerController> apply)
    {
        if (!config.Enabled || config.Cost < 0 || config.RoundLimit <= 0)
        {
            return null;
        }

        return new ShopItem(
            id,
            text,
            config.Cost,
            config.RoundLimit,
            team,
            apply);
    }

    private void Buy(CCSPlayerController player, ShopItem item)
    {
        if (!CanUseShop(player))
        {
            return;
        }

        if (item.Team is not null && item.Team != player.Team)
        {
            player.PrintToChat(JailbreakChat.Format("Этот предмет недоступен вашей команде."));
            return;
        }

        var state = GetState(player);
        if (state is null)
        {
            player.PrintToChat(JailbreakChat.Format("Не удалось определить SteamID."));
            return;
        }

        if (GetRoundUses(state.SteamId, item.Id) >= item.RoundLimit)
        {
            player.PrintToChat(JailbreakChat.Format("Лимит покупки на этот раунд исчерпан."));
            return;
        }

        if (state.Credits < item.Cost)
        {
            player.PrintToChat(JailbreakChat.Format("Недостаточно кредитов."));
            return;
        }

        state.Credits -= item.Cost;
        AddRoundUse(state.SteamId, item.Id);

        try
        {
            item.Apply(player);
        }
        catch (Exception exception)
        {
            // Roll back payment and round limit if the effect could not be applied.
            state.Credits = ClampCredits((long)state.Credits + item.Cost);
            RemoveRoundUse(state.SteamId, item.Id);
            Server.PrintToConsole($"[JBF] Shop item '{item.Id}' failed for {player.PlayerName}: {exception.Message}");
            player.PrintToChat(JailbreakChat.Format("Не удалось применить покупку. Кредиты возвращены."));
            return;
        }

        _storage.QueueSave(state);

        player.PrintToChat(JailbreakChat.Format($"Куплено: {item.Text}. Баланс: {state.Credits}."));
        OpenItemsMenu(player);
    }

    private bool CanUseShop(CCSPlayerController player)
    {
        if (!player.IsUsable() || !player.PawnIsAlive)
        {
            player.PrintToChat(JailbreakChat.Format("Магазин доступен только живым игрокам."));
            return false;
        }

        if (JailbreakCapability.Api.GetOptional()?.IsRoundActive != true)
        {
            player.PrintToChat(JailbreakChat.Format("Магазин доступен только во время активного раунда."));
            return false;
        }

        if (LrCapability.Api.GetOptional()?.IsActive == true)
        {
            player.PrintToChat(JailbreakChat.Format("Покупки недоступны во время LR."));
            return false;
        }

        if (SpecialDaysCapability.Api.GetOptional()?.IsActive == true)
        {
            player.PrintToChat(JailbreakChat.Format("Покупки недоступны во время игрового дня."));
            return false;
        }

        if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            player.PrintToChat(JailbreakChat.Format("Магазин доступен только игрокам T и CT."));
            return false;
        }

        return true;
    }

    private ShopPlayerState? GetState(CCSPlayerController player)
    {
        var steamId = player.GetSteamId64();
        if (steamId is null)
        {
            return null;
        }

        if (!_players.TryGetValue(steamId.Value, out var state))
        {
            state = new ShopPlayerState
            {
                SteamId = steamId.Value
            };
            _players[steamId.Value] = state;
        }

        state.PlayerName = player.PlayerName;
        return state;
    }

    private void RewardPlayer(
        CCSPlayerController player,
        int amount,
        string reason,
        bool announce = true)
    {
        var state = GetState(player);
        if (state is null || amount <= 0)
        {
            return;
        }

        state.Credits = ClampCredits((long)state.Credits + amount);
        _storage.QueueSave(state);

        if (announce)
        {
            player.PrintToChat(JailbreakChat.Format($"+{amount} кредитов {reason}. Баланс: {state.Credits}."));
        }
    }

    private int GetRoundUses(CCSPlayerController player, string itemId)
    {
        var steamId = player.GetSteamId64();
        return steamId is null ? 0 : GetRoundUses(steamId.Value, itemId);
    }

    private int GetRoundUses(ulong steamId, string itemId)
    {
        return _roundPurchases.TryGetValue(steamId, out var purchases) &&
               purchases.TryGetValue(itemId, out var uses)
            ? uses
            : 0;
    }

    private void AddRoundUse(ulong steamId, string itemId)
    {
        if (!_roundPurchases.TryGetValue(steamId, out var purchases))
        {
            purchases = [];
            _roundPurchases[steamId] = purchases;
        }

        purchases[itemId] = purchases.GetValueOrDefault(itemId) + 1;
    }

    private void RemoveRoundUse(ulong steamId, string itemId)
    {
        if (!_roundPurchases.TryGetValue(steamId, out var purchases) ||
            !purchases.TryGetValue(itemId, out var uses))
        {
            return;
        }

        if (uses <= 1)
        {
            purchases.Remove(itemId);
            if (purchases.Count == 0)
            {
                _roundPurchases.Remove(steamId);
            }

            return;
        }

        purchases[itemId] = uses - 1;
    }

    private void ApplySpeed(CCSPlayerController player)
    {
        if (!player.IsUsable() || !player.PawnIsAlive)
        {
            throw new InvalidOperationException("Player is not alive.");
        }

        var steamId = player.GetSteamId64() ?? throw new InvalidOperationException("SteamID is unavailable.");
        var pawn = player.PlayerPawn.Value!;
        var version = Interlocked.Increment(ref _effectVersion);
        var original = _speedEffects.TryGetValue(steamId, out var previous)
            ? previous.OriginalValue
            : pawn.VelocityModifier;

        _speedEffects[steamId] = new TimedEffectState(version, original, SpeedMultiplier);
        pawn.VelocityModifier = SpeedMultiplier;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flVelocityModifier");

        _plugin.AddTimer(10.0f, () => FinishSpeedEffect(player, steamId, version), TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void FinishSpeedEffect(CCSPlayerController player, ulong steamId, long version)
    {
        if (!_speedEffects.TryGetValue(steamId, out var effect) || effect.Version != version)
        {
            return;
        }

        _speedEffects.Remove(steamId);

        if (!player.IsUsable() || !player.PawnIsAlive || player.GetSteamId64() != steamId)
        {
            return;
        }

        var pawn = player.PlayerPawn.Value!;
        if (Math.Abs(pawn.VelocityModifier - effect.AppliedValue) > FloatTolerance)
        {
            // Another module changed the value while our effect was active.
            // Do not overwrite the newer external state.
            return;
        }

        pawn.VelocityModifier = effect.OriginalValue;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flVelocityModifier");
    }

    private void ApplyGravity(CCSPlayerController player)
    {
        if (!player.IsUsable() || !player.PawnIsAlive)
        {
            throw new InvalidOperationException("Player is not alive.");
        }

        var steamId = player.GetSteamId64() ?? throw new InvalidOperationException("SteamID is unavailable.");
        var pawn = player.PlayerPawn.Value!;
        var version = Interlocked.Increment(ref _effectVersion);
        var original = _gravityEffects.TryGetValue(steamId, out var previous)
            ? previous.OriginalValue
            : pawn.GravityScale;

        _gravityEffects[steamId] = new TimedEffectState(version, original, GravityMultiplier);
        pawn.GravityScale = GravityMultiplier;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_flGravityScale");

        _plugin.AddTimer(10.0f, () => FinishGravityEffect(player, steamId, version), TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void FinishGravityEffect(CCSPlayerController player, ulong steamId, long version)
    {
        if (!_gravityEffects.TryGetValue(steamId, out var effect) || effect.Version != version)
        {
            return;
        }

        _gravityEffects.Remove(steamId);

        if (!player.IsUsable() || !player.PawnIsAlive || player.GetSteamId64() != steamId)
        {
            return;
        }

        var pawn = player.PlayerPawn.Value!;
        if (Math.Abs(pawn.GravityScale - effect.AppliedValue) > FloatTolerance)
        {
            return;
        }

        pawn.GravityScale = effect.OriginalValue;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_flGravityScale");
    }

    private void ApplyDoubleJump(CCSPlayerController player)
    {
        EnsureAlive(player);
        var steamId = player.GetSteamId64() ?? throw new InvalidOperationException("SteamID is unavailable.");
        _doubleJumpEffects[steamId] = DateTime.UtcNow.AddSeconds(7);
        _doubleJumpUsed[steamId] = false;
        _jumpWasPressed[steamId] = player.Buttons.HasFlag(PlayerButtons.Jump);
    }

    private void ApplyBhop(CCSPlayerController player)
    {
        EnsureAlive(player);
        var steamId = player.GetSteamId64() ?? throw new InvalidOperationException("SteamID is unavailable.");
        _bhopEffects[steamId] = DateTime.UtcNow.AddSeconds(7);
    }

    private void ApplySmallModel(CCSPlayerController player)
    {
        EnsureAlive(player);
        var steamId = player.GetSteamId64() ?? throw new InvalidOperationException("SteamID is unavailable.");
        var pawn = player.PlayerPawn.Value!;
        var node = pawn.CBodyComponent?.SceneNode
                   ?? throw new InvalidOperationException("Player scene node is unavailable.");

        var version = Interlocked.Increment(ref _effectVersion);
        var originalScale = _smallModelEffects.TryGetValue(steamId, out var previous)
            ? previous.OriginalScale
            : node.Scale;

        _smallModelEffects[steamId] = new ModelScaleEffectState(version, originalScale, SmallModelScale);
        node.Scale = SmallModelScale;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_CBodyComponent");

        _plugin.AddTimer(
            5.0f,
            () => FinishSmallModel(player, steamId, version),
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void FinishSmallModel(CCSPlayerController player, ulong steamId, long version)
    {
        if (!_smallModelEffects.TryGetValue(steamId, out var effect) || effect.Version != version)
            return;

        _smallModelEffects.Remove(steamId);

        if (!player.IsUsable() ||
            !player.PawnIsAlive ||
            player.GetSteamId64() != steamId ||
            player.PlayerPawn.Value is not { IsValid: true } pawn ||
            pawn.CBodyComponent?.SceneNode is not { } node)
        {
            return;
        }

        if (Math.Abs(node.Scale - effect.AppliedScale) > FloatTolerance)
            return;

        node.Scale = effect.OriginalScale;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_CBodyComponent");
    }

    private void ApplyGuardDisguise(CCSPlayerController player)
    {
        EnsureAlive(player);
        var steamId = player.GetSteamId64() ?? throw new InvalidOperationException("SteamID is unavailable.");
        var pawn = player.PlayerPawn.Value!;

        var originalModel =
            _guardDisguiseEffects.TryGetValue(steamId, out var previous)
                ? previous.OriginalModel
                : pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState?.ModelName;

        if (string.IsNullOrWhiteSpace(originalModel))
            throw new InvalidOperationException("Current player model is unavailable.");

        var version = Interlocked.Increment(ref _effectVersion);
        _guardDisguiseEffects[steamId] = new ModelEffectState(version, originalModel, GuardModel);

        pawn.SetModel(GuardModel);

        _plugin.AddTimer(
            15.0f,
            () => FinishGuardDisguise(player, steamId, version),
            TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void FinishGuardDisguise(CCSPlayerController player, ulong steamId, long version)
    {
        if (!_guardDisguiseEffects.TryGetValue(steamId, out var effect) || effect.Version != version)
            return;

        _guardDisguiseEffects.Remove(steamId);

        if (!player.IsUsable() ||
            !player.PawnIsAlive ||
            player.GetSteamId64() != steamId ||
            player.PlayerPawn.Value is not { IsValid: true } pawn)
        {
            return;
        }

        var currentModel =
            pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState?.ModelName;

        if (!string.Equals(currentModel, effect.AppliedModel, StringComparison.OrdinalIgnoreCase))
            return;

        pawn.SetModel(effect.OriginalModel);
    }

    private void RestoreAllSmallModels()
    {
        foreach (var player in Utilities.GetPlayers().Where(player => player.IsUsable() && !player.IsBot))
        {
            var steamId = player.GetSteamId64();
            if (steamId is null ||
                !_smallModelEffects.TryGetValue(steamId.Value, out var effect) ||
                player.PlayerPawn.Value is not { IsValid: true } pawn ||
                pawn.CBodyComponent?.SceneNode is not { } node)
            {
                continue;
            }

            if (Math.Abs(node.Scale - effect.AppliedScale) <= FloatTolerance)
            {
                node.Scale = effect.OriginalScale;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_CBodyComponent");
            }
        }
    }

    private void RestoreAllGuardDisguises()
    {
        foreach (var player in Utilities.GetPlayers().Where(player => player.IsUsable() && !player.IsBot))
        {
            var steamId = player.GetSteamId64();
            if (steamId is null ||
                !_guardDisguiseEffects.TryGetValue(steamId.Value, out var effect) ||
                player.PlayerPawn.Value is not { IsValid: true } pawn)
            {
                continue;
            }

            var currentModel =
                pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState?.ModelName;

            if (string.Equals(currentModel, effect.AppliedModel, StringComparison.OrdinalIgnoreCase))
                pawn.SetModel(effect.OriginalModel);
        }
    }

    private static void EnsureAlive(CCSPlayerController player)
    {
        if (!player.IsUsable() || !player.PawnIsAlive)
            throw new InvalidOperationException("Player is not alive.");
    }

    private ShopConfig LoadConfig()
    {
        return ShopConfig.LoadOrCreate(
            _configPath,
            message => Server.PrintToConsole($"[JBF] Shop config error: {message}"));
    }

    private static int IncrementStreak(int current)
        => current == int.MaxValue ? int.MaxValue : current + 1;

    private static int StreakReward(int streak, int baseReward, int step, int maxReward)
    {
        if (streak <= 0 || baseReward <= 0 || maxReward <= 0)
            return 0;

        var safeStep = Math.Max(0, step);
        var reward = (long)baseReward + (long)(streak - 1) * safeStep;
        return (int)Math.Clamp(reward, 0L, Math.Max(0, maxReward));
    }

    private static int CalculateFivePercentFee(int amount)
    {
        if (amount <= 0)
            return 0;

        return Math.Max(1, (int)Math.Ceiling(amount * 0.05m));
    }

    private static int ClampCredits(long value)
    {
        return (int)Math.Clamp(value, 0L, int.MaxValue);
    }

    private sealed record TimedEffectState(long Version, float OriginalValue, float AppliedValue);
    private sealed record ModelScaleEffectState(long Version, float OriginalScale, float AppliedScale);
    private sealed record ModelEffectState(long Version, string OriginalModel, string AppliedModel);
}
