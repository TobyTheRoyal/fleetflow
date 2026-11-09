using Microsoft.AspNetCore.Identity;

namespace FleetFlow.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public bool IsDemo { get; set; }
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public static class FleetRoles
{
    public const string Admin = "Admin", Dispatcher = "Dispatcher", Viewer = "Viewer";
    public static readonly string[] All = [Admin, Dispatcher, Viewer];
    public static Guid Id(int index) => new($"af000000-0000-0000-0000-{index + 1:D12}");
}
