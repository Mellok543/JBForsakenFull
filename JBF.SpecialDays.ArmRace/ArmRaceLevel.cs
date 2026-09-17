using CounterStrikeSharp.API.Modules.Entities.Constants;

namespace JBF.SpecialDays.ArmRace;

internal sealed record ArmRaceLevel(int RequiredKills, IReadOnlyList<CsItem> Weapons, bool GiveZeus = true);
