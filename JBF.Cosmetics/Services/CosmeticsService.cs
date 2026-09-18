using CounterStrikeSharp.API.Core;
using JBF.Api;
using JBF.Cosmetics.Models;

namespace JBF.Cosmetics.Services;

internal sealed class CosmeticsService : ICosmeticsApi
{
    private const int Slots = 6;
    private readonly CosmeticsConfig _config;
    private readonly CosmeticsStorage _storage;
    private readonly CosmeticsRenderer _renderer;
    private readonly CosmeticsVisualService _visuals;
    private readonly Action<string>? _log;
    private readonly Dictionary<ulong, HashSet<string>> _owned;
    private readonly Dictionary<ulong, Dictionary<CosmeticCategory, string>> _equipped;
    private readonly Dictionary<int, CosmeticCategory?> _categories = [];
    private readonly Dictionary<int, int> _pages = [];
    private readonly Dictionary<int, string> _selected = [];

    public CosmeticsService(CosmeticsConfig config, CosmeticsRenderer renderer, Action<string>? log = null)
    {
        _config = config;
        _renderer = renderer;
        _visuals = new CosmeticsVisualService(log);
        _log = log;
        _storage = new CosmeticsStorage(config.Database);
        (_owned, _equipped) = _storage.LoadAll(log);
    }

    public void Open(CCSPlayerController player)
    {
        if (!Usable(player)) return;
        _categories.TryAdd(player.Slot, null);
        _pages.TryAdd(player.Slot, 0);
        EnsureSelection(player);
        Render(player);
        _renderer.Show(player);
    }

    public void Close(CCSPlayerController player) => _renderer.Hide(player);

    public void SetCategory(CCSPlayerController player, CosmeticCategory? category)
    {
        if (!Usable(player)) return;
        _categories[player.Slot] = category;
        _pages[player.Slot] = 0;
        _selected.Remove(player.Slot);
        EnsureSelection(player);
        Render(player);
    }

    public void ChangePage(CCSPlayerController player, int delta)
    {
        if (!Usable(player)) return;
        _pages[player.Slot] = Math.Clamp(_pages.GetValueOrDefault(player.Slot) + Math.Clamp(delta, -1, 1), 0, MaxPage(player));
        _selected.Remove(player.Slot);
        EnsureSelection(player);
        Render(player);
    }

    public void SelectSlot(CCSPlayerController player, int slot)
    {
        if (!Usable(player) || slot < 0 || slot >= Slots) return;
        var items = Filtered(player);
        var index = _pages.GetValueOrDefault(player.Slot) * Slots + slot;
        if (index >= items.Length) return;
        _selected[player.Slot] = items[index].Id;
        Render(player);
    }

    public void ToggleSelected(CCSPlayerController player)
    {
        if (!Usable(player) || !_selected.TryGetValue(player.Slot, out var id)) return;
        var item = Find(id);
        if (item is null) return;

        if (!Owns(player, id))
        {
            UiCapability.Api.Get()?.Notify(player, $"Предмет не получен • Источник: {SourceName(item.Source)}", UiNotificationType.Warning, 4f);
            return;
        }

        if (GetEquipped(player, item.Category)?.Equals(id, StringComparison.OrdinalIgnoreCase) == true)
            Unequip(player, item.Category);
        else
            Equip(player, id);

        Render(player);
    }

    public bool Owns(CCSPlayerController player, string cosmeticId)
        => Usable(player) && _owned.TryGetValue(player.SteamID, out var set) && set.Contains(cosmeticId);

    public bool Grant(CCSPlayerController player, string cosmeticId, string source = "Admin")
    {
        if (!Usable(player) || Find(cosmeticId) is null) return false;
        if (!_owned.TryGetValue(player.SteamID, out var set)) _owned[player.SteamID] = set = new(StringComparer.OrdinalIgnoreCase);
        if (set.Contains(cosmeticId)) return true;
        if (!_storage.SaveGrant(player.SteamID, cosmeticId, source, _log)) return false;
        set.Add(cosmeticId);
        UiCapability.Api.Get()?.Notify(player, $"Косметика получена: {Find(cosmeticId)!.Name}", UiNotificationType.Success, 4f);
        return true;
    }

    public bool Equip(CCSPlayerController player, string cosmeticId)
    {
        var item = Find(cosmeticId);
        if (!Usable(player) || item is null || !Owns(player, cosmeticId)) return false;
        if (!_storage.SaveEquip(player.SteamID, item.Category, cosmeticId, _log)) return false;
        if (!_equipped.TryGetValue(player.SteamID, out var map)) _equipped[player.SteamID] = map = [];
        map[item.Category] = cosmeticId;
        _visuals.Apply(player, item);
        UiCapability.Api.Get()?.Notify(player, $"Экипировано: {item.Name}", UiNotificationType.Success, 3f);
        return true;
    }

    public bool Unequip(CCSPlayerController player, CosmeticCategory category)
    {
        if (!Usable(player)) return false;
        if (!_storage.SaveEquip(player.SteamID, category, null, _log)) return false;
        if (_equipped.TryGetValue(player.SteamID, out var map)) map.Remove(category);
        _visuals.Remove(player, category);
        UiCapability.Api.Get()?.Notify(player, "Косметика снята.", UiNotificationType.Info, 3f);
        return true;
    }

    public string? GetEquipped(CCSPlayerController player, CosmeticCategory category)
        => Usable(player) && _equipped.TryGetValue(player.SteamID, out var map) ? map.GetValueOrDefault(category) : null;

    public void RefreshVisuals(CCSPlayerController player)
    {
        if (!Usable(player)) return;
        _visuals.RemoveAll(player);
        if (!player.PawnIsAlive) return;
        if (!_equipped.TryGetValue(player.SteamID, out var map)) return;

        foreach (var cosmeticId in map.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var item = Find(cosmeticId);
            if (item is not null)
                _visuals.Apply(player, item);
        }
    }


    public void OnDeath(CCSPlayerController? player)
    {
        if (player is not null)
            _visuals.RemoveAll(player);
    }

    public void OnMapStart() => _visuals.Reset();

    public void Shutdown() => _visuals.Reset();

    public void Disconnect(int slot)
    {
        _visuals.RemoveAll(slot);
        _categories.Remove(slot);
        _pages.Remove(slot);
        _selected.Remove(slot);
        _renderer.Forget(slot);
    }

    private void Render(CCSPlayerController player)
    {
        _pages[player.Slot] = Math.Clamp(_pages.GetValueOrDefault(player.Slot), 0, MaxPage(player));
        EnsureSelection(player);
        var items = Filtered(player);
        var page = _pages.GetValueOrDefault(player.Slot);

        _renderer.Text(player, "jbf_cos_counter", $"{OwnedCount(player)} / {_config.Items.Count(x => x.Enabled)} ПОЛУЧЕНО");
        _renderer.Text(player, "jbf_cos_page", $"СТРАНИЦА {page + 1} / {Math.Max(1, MaxPage(player) + 1)}");

        SetCategoryClasses(player);
        for (var slot = 0; slot < Slots; slot++)
        {
            var index = page * Slots + slot;
            var panel = $"jbf_cos_select_{slot}";
            var visible = index < items.Length;
            _renderer.SetClass(player, panel, "visible", visible);
            if (!visible) continue;

            var item = items[index];
            var owned = Owns(player, item.Id);
            var equipped = GetEquipped(player, item.Category)?.Equals(item.Id, StringComparison.OrdinalIgnoreCase) == true;
            var selected = _selected.GetValueOrDefault(player.Slot)?.Equals(item.Id, StringComparison.OrdinalIgnoreCase) == true;
            _renderer.Text(player, $"jbf_cos_item_{slot}_name", item.Name);
            _renderer.Text(player, $"jbf_cos_item_{slot}_type", CategoryName(item.Category));
            _renderer.Text(player, $"jbf_cos_item_{slot}_state", equipped ? "ЭКИПИРОВАНО" : owned ? "ДОСТУПНО" : SourceName(item.Source));
            _renderer.SetClass(player, panel, "owned", owned);
            _renderer.SetClass(player, panel, "equipped", equipped);
            _renderer.SetClass(player, panel, "selected", selected);
            SetRarity(player, panel, item.Rarity);
        }

        RenderDetails(player);
    }

    private void RenderDetails(CCSPlayerController player)
    {
        var item = _selected.TryGetValue(player.Slot, out var id) ? Find(id) : null;
        var has = item is not null;
        _renderer.SetClass(player, "jbf_cos_details", "has-item", has);
        if (item is null)
        {
            _renderer.Text(player, "jbf_cos_detail_name", "В ЭТОЙ КАТЕГОРИИ ПОКА НЕТ ПРЕДМЕТОВ");
            _renderer.Text(player, "jbf_cos_detail_desc", "Новые косметические предметы будут добавляться постепенно.");
            _renderer.Text(player, "jbf_cos_detail_meta", "");
            _renderer.Text(player, "jbf_cos_action_text", "");
            return;
        }

        var owned = Owns(player, item.Id);
        var equipped = GetEquipped(player, item.Category)?.Equals(item.Id, StringComparison.OrdinalIgnoreCase) == true;
        _renderer.Text(player, "jbf_cos_preview_symbol", PreviewSymbol(item.PreviewKey, item.Category));
        _renderer.Text(player, "jbf_cos_detail_name", item.Name);
        _renderer.Text(player, "jbf_cos_detail_desc", item.Description);
        _renderer.Text(player, "jbf_cos_detail_meta", $"{CategoryName(item.Category)}  •  {item.Rarity.ToUpperInvariant()}  •  {SourceName(item.Source)}");
        _renderer.Text(player, "jbf_cos_action_text", equipped ? "СНЯТЬ" : owned ? "ЭКИПИРОВАТЬ" : "НЕ ПОЛУЧЕНО");
        _renderer.SetClass(player, "jbf_cos_action", "disabled", !owned);
        SetRarity(player, "jbf_cos_details", item.Rarity);
    }

    private void EnsureSelection(CCSPlayerController player)
    {
        if (_selected.ContainsKey(player.Slot) && Find(_selected[player.Slot]) is not null) return;
        var items = Filtered(player);
        var index = _pages.GetValueOrDefault(player.Slot) * Slots;
        if (index < items.Length) _selected[player.Slot] = items[index].Id;
    }

    private CosmeticDefinition[] Filtered(CCSPlayerController player)
    {
        var category = _categories.GetValueOrDefault(player.Slot);
        return _config.Items.Where(x => x.Enabled && (category is null || x.Category == category))
            .OrderBy(x => x.Category).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private int MaxPage(CCSPlayerController player)
    {
        var count = Filtered(player).Length;
        return count == 0 ? 0 : (count - 1) / Slots;
    }

    private int OwnedCount(CCSPlayerController player) => _owned.TryGetValue(player.SteamID, out var set) ? set.Count : 0;
    private CosmeticDefinition? Find(string id) => _config.Items.FirstOrDefault(x => x.Enabled && x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private void SetCategoryClasses(CCSPlayerController player)
    {
        var current = _categories.GetValueOrDefault(player.Slot);
        foreach (var (id, category) in CategoryButtons())
            _renderer.SetClass(player, id, "active", current == category);
    }

    private static IEnumerable<(string Id, CosmeticCategory? Category)> CategoryButtons()
    {
        yield return ("jbf_cos_cat_all", null);
        yield return ("jbf_cos_cat_head", CosmeticCategory.Head);
        yield return ("jbf_cos_cat_back", CosmeticCategory.Back);
        yield return ("jbf_cos_cat_trail", CosmeticCategory.Trail);
        yield return ("jbf_cos_cat_aura", CosmeticCategory.Aura);
        yield return ("jbf_cos_cat_death", CosmeticCategory.DeathEffect);
        yield return ("jbf_cos_cat_player", CosmeticCategory.PlayerModel);
    }

    private void SetRarity(CCSPlayerController player, string panel, string rarity)
    {
        foreach (var value in new[] { "common", "rare", "epic", "legendary" })
            _renderer.SetClass(player, panel, $"rarity-{value}", value.Equals(rarity, StringComparison.OrdinalIgnoreCase));
    }

    private static string CategoryName(CosmeticCategory category) => category switch
    {
        CosmeticCategory.Head => "ГОЛОВА",
        CosmeticCategory.Back => "СПИНА",
        CosmeticCategory.Trail => "ТРЕЙЛ",
        CosmeticCategory.Aura => "АУРА",
        CosmeticCategory.DeathEffect => "ЭФФЕКТ СМЕРТИ",
        CosmeticCategory.PlayerModel => "МОДЕЛЬ ИГРОКА",
        _ => category.ToString().ToUpperInvariant()
    };

    private static string SourceName(string source) => source.ToLowerInvariant() switch
    {
        "battlepass" => "BATTLE PASS",
        "paid" => "ПОКУПКА",
        "admin" => "ADMIN",
        _ => source.ToUpperInvariant()
    };

    private static string PreviewSymbol(string key, CosmeticCategory category) => category switch
    {
        CosmeticCategory.Head => "♛",
        CosmeticCategory.Back => "◆",
        CosmeticCategory.Trail => "≋",
        CosmeticCategory.Aura => "✦",
        CosmeticCategory.DeathEffect => "☠",
        CosmeticCategory.PlayerModel => "◉",
        _ => "✦"
    };

    private static bool Usable(CCSPlayerController? player) => player is { IsValid: true, IsBot: false } && player.SteamID != 0;
}
