using FleetFlow.Domain.Common;
using FleetFlow.Domain.Customers;
using FleetFlow.Domain.Drivers;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FleetFlow.Infrastructure.Persistence.Configurations;

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("Shipments", table =>
        {
            table.HasCheckConstraint("CK_Shipments_Weight", "\"WeightKg\" > 0");
            table.HasCheckConstraint("CK_Shipments_Pallets", "\"PalletCount\" >= 0");
            table.HasCheckConstraint("CK_Shipments_Schedule", "\"DeliveryDeadlineUtc\" > \"PickupWindowStartUtc\"");
            table.HasCheckConstraint("CK_Shipments_Version", "\"Version\" > 0");
            table.HasCheckConstraint("CK_Shipments_Status", "\"Status\" IN ('Draft','Planned','InTransit','Delivered','Cancelled')");
            table.HasCheckConstraint("CK_Shipments_Assignment", "(\"AssignedDriverId\" IS NULL) = (\"AssignedVehicleId\" IS NULL) AND (\"Status\" NOT IN ('Planned','InTransit','Delivered') OR \"AssignedDriverId\" IS NOT NULL)");
            table.HasCheckConstraint("CK_Shipments_ActualTimes", "(\"Status\" NOT IN ('InTransit','Delivered') OR \"ActualPickupAtUtc\" IS NOT NULL) AND (\"Status\" <> 'Delivered' OR \"ActualDeliveredAtUtc\" IS NOT NULL) AND (\"ActualDeliveredAtUtc\" IS NULL OR (\"ActualPickupAtUtc\" IS NOT NULL AND \"ActualDeliveredAtUtc\" >= \"ActualPickupAtUtc\"))");
            table.HasCheckConstraint("CK_Shipments_Updated", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
            table.HasCheckConstraint("CK_Shipments_Number", "length(btrim(\"ShipmentNumber\")) > 0");
            var codes = string.Join(',', Guard.CountryCodes.Select(code => $"'{code}'"));
            foreach (var prefix in new[] { "Origin", "Destination" })
            {
                table.HasCheckConstraint($"CK_Shipments_{prefix}", $"length(btrim(\"{prefix}Street\")) > 0 AND length(btrim(\"{prefix}City\")) > 0 AND length(btrim(\"{prefix}Postcode\")) > 0 AND \"{prefix}CountryCode\" IN ({codes})");
            }
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ShipmentNumber).HasMaxLength(40).IsRequired();
        builder.HasIndex(x => x.ShipmentNumber).IsUnique();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.WeightKg).HasPrecision(12, 2);
        builder.Property(x => x.CargoDescription).HasMaxLength(1000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        ConfigureAddress(builder.ComplexProperty(x => x.Origin), "Origin");
        ConfigureAddress(builder.ComplexProperty(x => x.Destination), "Destination");
        builder.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Driver>().WithMany().HasForeignKey(x => x.AssignedDriverId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.AssignedVehicleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.Status, x.DeliveryDeadlineUtc });
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasMany(x => x.Events).WithOne().HasForeignKey(x => x.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(x => x.Events).HasField("events").UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static void ConfigureAddress(ComplexPropertyBuilder<Address> builder, string prefix)
    {
        builder.IsRequired();
        builder.Property(x => x.Street).HasColumnName(prefix + "Street").HasMaxLength(200).IsRequired();
        builder.Property(x => x.City).HasColumnName(prefix + "City").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Postcode).HasColumnName(prefix + "Postcode").HasMaxLength(20).IsRequired();
        builder.Property(x => x.CountryCode).HasColumnName(prefix + "CountryCode").HasMaxLength(2).IsRequired();
    }
}
