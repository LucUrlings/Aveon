using System.ComponentModel.DataAnnotations;

namespace backend.Features.Auth.Models;

public sealed class UpdatePreferencesRequest
{
    [Required]
    [RegularExpression("^(best|cheapest|fastest)$", ErrorMessage = "Default return ranking must be best, cheapest, or fastest.")]
    public string DefaultReturnRanking { get; init; } = string.Empty;
}
