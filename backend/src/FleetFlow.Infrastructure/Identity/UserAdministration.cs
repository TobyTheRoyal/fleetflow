using FleetFlow.Application.Queries;
using FleetFlow.Domain.Common;
using FleetFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FleetFlow.Infrastructure.Identity;

public sealed record UserAccount(Guid Id, string Email, string DisplayName, string Role, bool Active);
public sealed record NewUser(string Email, string DisplayName, string Role, string Password);
public sealed class UserAdministration(IdentityPersistence db, UserManager<ApplicationUser> users, TimeProvider clock)
{
    public async Task<PageResult<UserAccount>> ListAsync(PageRequest page, CancellationToken token)
    {
        var total = await db.Users.CountAsync(token);
        var records = await (from user in db.Users.AsNoTracking()
                             join assignment in db.UserRoles on user.Id equals assignment.UserId
                             join role in db.Roles on assignment.RoleId equals role.Id
                             orderby user.CreatedAtUtc descending, user.Id
                             select new UserAccount(user.Id, user.Email!, user.DisplayName, role.Name!, user.IsActive))
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize).ToArrayAsync(token);
        return new PageResult<UserAccount>(records, page.Page, page.PageSize, total);
    }

    public async Task<UserAccount> CreateAsync(NewUser request, CancellationToken token)
    {
        if (!FleetRoles.All.Contains(request.Role)) throw new FleetFlowException("invalid_role", "Choose Admin, Dispatcher or Viewer.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = request.Email.Trim(), UserName = request.Email.Trim(), DisplayName = request.DisplayName.Trim(), CreatedAtUtc = clock.GetUtcNow() };
        Ensure(await users.CreateAsync(user, request.Password));
        Ensure(await users.AddToRoleAsync(user, request.Role));
        await transaction.CommitAsync(token);
        return await Account(user);
    }

    public async Task<UserAccount> ActivateAsync(Guid actorId, Guid id, bool active, CancellationToken token)
    {
        if (actorId == id && !active)
            throw FleetFlowException.Conflict("self_deactivation", "You cannot deactivate your own account.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // Serialize administrative deactivation checks, preserving at least one active admin.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(741062005)", token);
        var user = await users.FindByIdAsync(id.ToString()) ?? throw FleetFlowException.NotFound("user");
        if (!active && await users.IsInRoleAsync(user, FleetRoles.Admin)
            && await (from u in db.Users
                      join ur in db.UserRoles on u.Id equals ur.UserId
                      where u.IsActive && ur.RoleId == FleetRoles.Id(0)
                      select u.Id).CountAsync(token) <= 1)
            throw FleetFlowException.Conflict("last_admin", "At least one active administrator is required.");
        user.IsActive = active;
        Ensure(await users.UpdateSecurityStampAsync(user));
        await transaction.CommitAsync(token);
        return await Account(user);
    }

    private async Task<UserAccount> Account(ApplicationUser user) => new(user.Id, user.Email!, user.DisplayName, (await users.GetRolesAsync(user)).Single(), user.IsActive);

    private static void Ensure(IdentityResult result)
    {
        if (!result.Succeeded) throw new FleetFlowException("invalid_user", string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
