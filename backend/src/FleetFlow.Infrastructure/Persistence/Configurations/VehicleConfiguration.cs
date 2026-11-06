using FleetFlow.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FleetFlow.Infrastructure.Persistence.Configurations;

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("Vehicles", table =>
        {
            table.HasCheckConstraint("CK_Vehicles_Payload", "\"MaxPayloadKg\" > 0");
            table.HasCheckConstraint("CK_Vehicles_Plate", "length(btrim(\"PlateNumber\")) > 0 AND \"PlateNumber\" = upper(btrim(\"PlateNumber\"))");
            table.HasCheckConstraint("CK_Vehicles_Enums", "\"Type\" IN ('Van','Truck') AND \"Status\" IN ('Available','Maintenance','Inactive')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PlateNumber).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => x.PlateNumber).IsUnique();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.MaxPayloadKg).HasPrecision(12, 2);
    }
}
