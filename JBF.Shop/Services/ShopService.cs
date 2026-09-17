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
    private readonly BasePlugin _plugin;
    private readonly string _configPath;
    private readonly ShopStorage _storage;
    private readonly Dictionary<ulong, ShopPlayerState> _players;
    private readonly Dictionary<int, Dictionary<string, int>> _roundPurchases = [];
    private ShopConfig _config;
    private IReadOnlyList<ShopItem> _items;

    public ShopService(BasePlugin plugin)
    {
        _plugin = plugin;
        _configPath = ShopConfigPath.Get(plugin.ModuleDirectory);
        _config = ShopConfig.LoadOrCreate(_configPath);
        _storage = new ShopStorage(_config.Database);
        _players = _storage.Load();
        _items = BuildItems(_config.Items);
    }

    public void ReloadConfig()
    {
        _config = ShopConfig.LoadOrCreate(_configPath);
        _items = BuildItems(_config.Items);
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
        Save();
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

        state.Credits = (int)Math.Clamp((long)state.Credits + amount, 0, int.MaxValue);
        newBalance = state.Credits;
        Save();
        return true;
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
            CreateItem("gravity", "Гравитация на 10 секунд", config.Gravity, CsTeam.Terrorist, ApplyGravity)
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

    public void OpenShop(CCSPlayerController player)
    {
        if (!CanUseShop(player))
        {
            return;
        }

        var menuApi = MenuCapability.Api.Get();
        if (menuApi is null)
        {
            player.PrintToChat(JailbreakChat.Format("Menu API недоступно."));
            return;
        }

        var credits = GetCredits(player);
        var options = _items
            .Where(item => item.Team is null || item.Team == player.Team)
            .Select(item =>
            {
                var used = GetRoundUses(player, item.Id);
                var disabled = credits < item.Cost || used >= item.RoundLimit;
                var suffix = item.RoundLimit > 0 ? $" [{used}/{item.RoundLimit}]" : string.Empty;
                return new JailbreakMenuOption(
                    $"{item.Text} - {item.Cost} кредитов{suffix}",
                    buyer => Buy(buyer, item),
                    disabled);
            })
            .ToArray();

        menuApi.Open(player, $"Shop | {credits} кредитов", options);
    }

    public void RewardRoundPlayers()
    {
        foreach (var player in Utilities.GetPlayers().Where(player =>
                     player.IsUsable() && !player.IsBot &&
                     player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            RewardPlayer(player, _config.Rewards.RoundParticipation, "за участие в раунде", announce: false, save: false);
            if (player.PawnIsAlive)
            {
                RewardPlayer(player, _config.Rewards.RoundSurvival, "за выживание", announce: false, save: false);
            }
        }

        Save();
    }

    public void RewardKill(CCSPlayerController? victim, CCSPlayerController? attacker)
    {
        if (!victim.IsUsable() || !attacker.IsUsable() || victim.Slot == attacker.Slot)
        {
            return;
        }

        if (LrCapability.Api.Get()?.IsActive == true || SpecialDaysCapability.Api.Get()?.IsActive == true)
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
        RewardPlayer(match.Inmate, _config.Rewards.LrParticipation, "за участие в LR", announce: false, save: false);
        RewardPlayer(match.Guardian, _config.Rewards.LrParticipation, "за участие в LR", announce: false, save: false);

        if (match.Winner.IsUsable())
        {
            RewardPlayer(match.Winner, _config.Rewards.LrWin, $"за победу в LR ({match.GameName})", announce: true, save: false);
        }

        Save();
    }

    public void ResetRound()
    {
        _roundPurchases.Clear();
    }

    public void Save()
    {
        _storage.Save(_players);
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

        if (GetRoundUses(player, item.Id) >= item.RoundLimit)
        {
            player.PrintToChat(JailbreakChat.Format("Лимит покупки на этот раунд исчерпан."));
            return;
        }

        var state = GetState(player);
        if (state is null)
        {
            player.PrintToChat(JailbreakChat.Format("Не удалось определить SteamID."));
            return;
        }

        if (state.Credits < item.Cost)
        {
            player.PrintToChat(JailbreakChat.Format("Недостаточно кредитов."));
            return;
        }

        state.Credits -= item.Cost;
        AddRoundUse(player, item.Id);
        item.Apply(player);
        Save();

        player.PrintToChat(JailbreakChat.Format($"Куплено: {item.Text}. Баланс: {state.Credits}."));
        OpenShop(player);
    }

    private bool CanUseShop(CCSPlayerController player)
    {
        if (!player.IsUsable() || !player.PawnIsAlive)
        {
            player.PrintToChat(JailbreakChat.Format("Магазин доступен только живым игрокам."));
            return false;
        }

        if (JailbreakCapability.Api.Get()?.IsRoundActive != true)
        {
            player.PrintToChat(JailbreakChat.Format("Магазин доступен только во время активного раунда."));
            return false;
        }

        if (LrCapability.Api.Get()?.IsActive == true)
        {
            player.PrintToChat(JailbreakChat.Format("Покупки недоступны во время LR."));
            return false;
        }

        if (SpecialDaysCapability.Api.Get()?.IsActive == true)
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
        bool announce = true,
        bool save = true)
    {
        var state = GetState(player);
        if (state is null || amount <= 0)
        {
            return;
        }

        state.Credits = Math.Max(0, state.Credits + amount);
        if (announce)
        {
            player.PrintToChat(JailbreakChat.Format($"+{amount} кредитов {reason}. Баланс: {state.Credits}."));
        }

        if (save)
        {
            Save();
        }
    }

    private int GetRoundUses(CCSPlayerController player, string itemId)
    {
        return _roundPurchases.TryGetValue(player.Slot, out var purchases) &&
               purchases.TryGetValue(itemId, out var uses)
            ? uses
            : 0;
    }

    private void AddRoundUse(CCSPlayerController player, string itemId)
    {
        if (!_roundPurchases.TryGetValue(player.Slot, out var purchases))
        {
            purchases = [];
            _roundPurchases[player.Slot] = purchases;
        }

        purchases[itemId] = purchases.GetValueOrDefault(itemId) + 1;
    }

    private void ApplySpeed(CCSPlayerController player)
    {
        if (!player.IsUsable() || !player.PawnIsAlive)
        {
            return;
        }

        var pawn = player.PlayerPawn.Value!;
        pawn.VelocityModifier = 1.25f;
        Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flVelocityModifier");

        _plugin.AddTimer(10.0f, () =>
        {
            if (!player.IsUsable() || !player.PawnIsAlive)
            {
                return;
            }

            player.PlayerPawn.Value!.VelocityModifier = 1.0f;
            Utilities.SetStateChanged(player.PlayerPawn.Value, "CCSPlayerPawnBase", "m_flVelocityModifier");
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void ApplyGravity(CCSPlayerController player)
    {
        if (!player.IsUsable() || !player.PawnIsAlive)
        {
            return;
        }

        var pawn = player.PlayerPawn.Value!;
        pawn.GravityScale = 0.65f;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_flGravityScale");

        _plugin.AddTimer(10.0f, () =>
        {
            if (!player.IsUsable() || !player.PawnIsAlive)
            {
                return;
            }

            player.PlayerPawn.Value!.GravityScale = 1.0f;
            Utilities.SetStateChanged(player.PlayerPawn.Value, "CBaseEntity", "m_flGravityScale");
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }
}
