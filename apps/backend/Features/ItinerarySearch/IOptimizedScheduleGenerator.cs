using backend.Features.ItinerarySearch.Models;

namespace backend.Features.ItinerarySearch;

public interface IOptimizedScheduleGenerator
{
    OptimizedSchedulePlan Generate(OptimizedTripRequest request);
}

public record OptimizedSchedulePlan(
    OptimizerFeasibility Feasibility,
    List<AbstractItinerarySchedule> Schedules,
    int CandidateStatesEvaluated,
    int CandidateStatesPruned);
