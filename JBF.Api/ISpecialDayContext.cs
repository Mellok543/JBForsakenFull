using CounterStrikeSharp.API.Modules.Entities.Constants;

namespace JBF.Api;

public interface ISpecialDayContext
{
    void Finish(RoundEndReason reason);

    // Stops only the special-day mode and keeps the current round running.
    // Useful when control must be handed back to another system such as LR.
    void End();
}
