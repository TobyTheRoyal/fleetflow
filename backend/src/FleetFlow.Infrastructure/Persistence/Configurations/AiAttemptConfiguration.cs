using FleetFlow.Infrastructure.Identity;
using FleetFlow.Infrastructure.Insights;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FleetFlow.Infrastructure.Persistence.Configurations;

public sealed class AiAttemptConfiguration : IEntityTypeConfiguration<AiAttempt>
{
    public void Configure(EntityTypeBuilder<AiAttempt> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Model).HasMaxLength(60).IsRequired();
        builder.Property(x => x.SelectionJson).HasMaxLength(6000);
        builder.Property(x => x.ErrorCode).HasMaxLength(60);
        builder.HasIndex(x => new { x.DayUtc, x.UserId });
        builder.HasIndex(x => new { x.UserId, x.Fingerprint, x.CacheExpiresAtUtc });
        builder.HasIndex(x => x.LeaseExpiresAtUtc).HasFilter("\"CompletedAtUtc\" IS NULL");
        builder.ToTable("AiAttempts", table =>
        {
            table.HasCheckConstraint("CK_AiAttempts_Lease", "\"LeaseExpiresAtUtc\" > \"StartedAtUtc\"");
            table.HasCheckConstraint("CK_AiAttempts_Fingerprint", "length(\"Fingerprint\") = 64");
        });
    }
}
