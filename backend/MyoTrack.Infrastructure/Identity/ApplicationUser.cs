using Microsoft.AspNetCore.Identity;

namespace MyoTrack.Infrastructure.Identity;

public class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class AppRoles
{
    public const string Student = "Student";
    public const string Trainer = "Trainer";
    public const string Nutritionist = "Nutritionist";
    public const string Admin = "Admin";

    public static readonly string[] All = [Student, Trainer, Nutritionist, Admin];
}
