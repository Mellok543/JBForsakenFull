using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using JBF.Api;

namespace JBF.LR.KnifeDuel;

public sealed class JBFKnifeDuel : BasePlugin
{
    private readonly KnifeDuelGroup _game = new();
    private IDisposable? _registration;

    public override string ModuleName => "JBF LR: Knife Duel";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Mell";

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnPlayerButtonsChanged>(_game.OnButtonsChanged);
    }

    public override void OnAllPluginsLoaded(bool hotReload)
    {
        var lr = LrCapability.Api.Get();
        if (lr is not null) _registration = lr.RegisterGame(_game);
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
        new KnifeVariant("knife-speed", "Увеличенная скорость", 20, 100, 1.60f, 1.0f),
        new KnifeVariant("knife-gravity", "Увеличенная гравитация", 30, 100, 1.0f, 1.5f, 1.65f),
        new KnifeVariant("knife-health", "Увеличенное HP", 40, 300, 1.0f, 1.0f)
    ];

    public void Start(ILrMatchContext context) { }
    public void Stop() { }

    public void OnButtonsChanged(CCSPlayerController player, PlayerButtons pressed, PlayerButtons released)
    {
        foreach (var variant in Variants.OfType<KnifeVariant>())
            variant.OnButtonsChanged(player, pressed);
    }
}

internal sealed class KnifeVariant(string id, string name, int order, int health, float speed, float gravity, float jumpMultiplier = 1.0f)
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

    public void OnButtonsChanged(CCSPlayerController player, PlayerButtons pressed)
    {
        if (_context is null || jumpMultiplier <= 1.0f || !pressed.HasFlag(PlayerButtons.Jump)) return;
        if (player.Slot != _context.Inmate.Slot && player.Slot != _context.Guardian.Slot) return;

        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid || !pawn.GroundEntity.IsValid) return;

        Server.NextFrame(() =>
        {
            if (_context is null || !player.IsValid || !player.PawnIsAlive) return;
            var currentPawn = player.PlayerPawn.Value;
            if (currentPawn is null || !currentPawn.IsValid || currentPawn.AbsVelocity.Z <= 0.0f) return;
            currentPawn.AbsVelocity.Z *= jumpMultiplier;
        });
    }

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
