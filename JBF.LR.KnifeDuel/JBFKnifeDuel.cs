using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using JBF.Api;

namespace JBF.LR.KnifeDuel;

public sealed class JBFKnifeDuel : BasePlugin
{
    private IDisposable? _registration;

    public override string ModuleName => "JBF LR: Knife Duel";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Mell";

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        var lr = LrCapability.Api.Get();
        if (lr is not null) _registration = lr.RegisterGame(new KnifeDuelGroup());
    }

    public override void Unload(bool hotReload) => _registration?.Dispose();
}

internal sealed class KnifeDuelGroup : ILrGame, ILrGameVariants
{
    public string Id => "knife-duel";
    public string Name => "Дуэль на ножах";
    public int Order => 10;
    public bool DisableAllDamage => false;
    public IReadOnlyList<ILrGame> Variants { get; } =
    [
        new KnifeVariant("knife-standard", "Стандарт", 10, 100, 1.0f, 1.0f),
        new KnifeVariant("knife-speed", "Увеличенная скорость", 20, 100, 1.35f, 1.0f),
        new KnifeVariant("knife-gravity", "Увеличенная гравитация", 30, 100, 1.0f, 1.5f),
        new KnifeVariant("knife-health", "Увеличенное HP", 40, 300, 1.0f, 1.0f)
    ];

    public void Start(ILrMatchContext context) { }
    public void Stop() { }
}

internal sealed class KnifeVariant(string id, string name, int order, int health, float speed, float gravity)
    : ILrGame, ILrInventoryRules
{
    private ILrMatchContext? _context;

    public string Id => id;
    public string Name => name;
    public int Order => order;
    public bool DisableAllDamage => false;

    public void Start(ILrMatchContext context)
    {
        _context = context;
        Prepare(context.Inmate);
        Prepare(context.Guardian);
    }

    public void Stop()
    {
        RestoreMovement(_context?.Inmate);
        RestoreMovement(_context?.Guardian);
        _context = null;
    }

    public bool IsWeaponAllowed(CBasePlayerWeapon weapon) =>
        weapon.DesignerName?.Contains("knife", StringComparison.OrdinalIgnoreCase) == true;

    private void Prepare(CCSPlayerController player)
    {
        LrPlayerRules.Normalize(player, health, 100);
        player.RemoveWeapons();
        player.GiveNamedItem(CsItem.Knife);
        var pawn = player.PlayerPawn.Value!;
        pawn.VelocityModifier = speed;
        pawn.GravityScale = gravity;
    }

    private static void RestoreMovement(CCSPlayerController? player)
    {
        if (!LrPlayerRules.IsUsable(player)) return;
        player!.PlayerPawn.Value!.VelocityModifier = 1.0f;
        player.PlayerPawn.Value.GravityScale = 1.0f;
    }
}
