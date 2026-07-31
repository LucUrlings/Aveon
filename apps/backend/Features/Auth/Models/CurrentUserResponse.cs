namespace backend.Features.Auth.Models;

public sealed record CurrentUserResponse(
    bool IsAuthenticated,
    string? Id,
    string? Email,
    IReadOnlyList<string> Roles,
    string? DefaultReturnRanking);
