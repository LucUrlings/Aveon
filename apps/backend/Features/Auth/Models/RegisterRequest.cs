using System.ComponentModel.DataAnnotations;

namespace backend.Features.Auth.Models;

public sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    public required string Email { get; init; }

    [Required]
    [MinLength(8)]
    public required string Password { get; init; }
}
