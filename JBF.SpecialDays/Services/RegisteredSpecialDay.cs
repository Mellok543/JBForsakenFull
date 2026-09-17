using JBF.Api;

namespace JBF.SpecialDays.Services;

internal sealed record RegisteredSpecialDay(Guid Token, ISpecialDay Day);
