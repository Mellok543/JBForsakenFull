using CounterStrikeSharp.API.Modules.Entities.Constants;

namespace JBF.Api;

public interface ISpecialDayContext
{
    void Finish(RoundEndReason reason);
}
