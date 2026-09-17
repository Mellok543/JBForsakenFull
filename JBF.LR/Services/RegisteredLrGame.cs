using JBF.Api;

namespace JBF.LR.Services;

internal sealed record RegisteredLrGame(Guid Token, ILrGame Game);
