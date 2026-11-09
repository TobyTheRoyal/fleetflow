using FleetFlow.Infrastructure.Insights;
using FleetFlow.Infrastructure.Portfolio;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FleetFlow.Infrastructure.Identity;

// Identity needs a stable public-schema context while logistics uses a validated request scope.
public sealed class IdentityPersistence(DbContextOptions<IdentityPersistence> options) : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;
    public DbSet<PortfolioSlot> PortfolioSlots => Set<PortfolioSlot>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureIdentity(modelBuilder);
    }
    internal static void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUser>().Property(x => x.DisplayName).HasMaxLength(150);
        modelBuilder.Entity<ApplicationUser>().Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone");
        modelBuilder.Entity<ApplicationUser>().HasIndex(x => x.NormalizedEmail).IsUnique();
        for (var i = 0; i < FleetRoles.All.Length; i++)
            modelBuilder.Entity<IdentityRole<Guid>>().HasData(new IdentityRole<Guid>(FleetRoles.All[i])
            { Id = FleetRoles.Id(i), NormalizedName = FleetRoles.All[i].ToUpperInvariant(), ConcurrencyStamp = FleetRoles.Id(i).ToString() });
        modelBuilder.Entity<PortfolioSlot>(slot =>
        {
            slot.ToTable("PortfolioSlots", "public");
            slot.HasKey(x => x.Id);
            slot.Property(x => x.Id).ValueGeneratedNever();
            slot.HasIndex(x => x.UserId).IsUnique();
            slot.Property(x => x.Role).HasMaxLength(20);
            slot.Property(x => x.SchemaName).HasMaxLength(40);
            slot.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            slot.ToTable(t => t.HasCheckConstraint("CK_PortfolioSlots_Bounds", "\"Id\" BETWEEN 1 AND 13 AND \"Role\" IN ('Viewer','Dispatcher') AND \"Writes\" >= 0"));
        });
    }
}
