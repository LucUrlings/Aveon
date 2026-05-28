using System.ComponentModel.DataAnnotations;

namespace backend.Features.Auth.Models;

public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }
}
