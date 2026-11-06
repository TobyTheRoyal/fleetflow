using FleetFlow.Domain.Drivers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FleetFlow.Infrastructure.Persistence.Configurations;

internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("Drivers", table => table.HasCheckConstraint("CK_Drivers_Required", "length(btrim(\"EmployeeNumber\")) > 0 AND \"EmployeeNumber\" = upper(btrim(\"EmployeeNumber\")) AND length(btrim(\"FirstName\")) > 0 AND length(btrim(\"LastName\")) > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EmployeeNumber).HasMaxLength(40).IsRequired();
        builder.HasIndex(x => x.EmployeeNumber).IsUnique();
        builder.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.LicenseCategory).HasMaxLength(30);
    }
}
