using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace JBF.Shop.Models;

internal sealed record ShopItem(
    string Id,
    string Text,
    int Cost,
    int RoundLimit,
    CsTeam? Team,
    Action<CCSPlayerController> Apply);
