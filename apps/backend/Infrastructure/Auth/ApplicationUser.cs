using Microsoft.AspNetCore.Identity;

namespace backend.Infrastructure.Auth;

public sealed class ApplicationUser : IdentityUser
{
    public string DefaultReturnRanking { get; set; } = "best";
}
