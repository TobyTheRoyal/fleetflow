using FleetFlow.Domain.Shipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FleetFlow.Infrastructure.Persistence.Configurations;

internal sealed class ShipmentEventConfiguration : IEntityTypeConfiguration<ShipmentEvent>
{
    public void Configure(EntityTypeBuilder<ShipmentEvent> builder)
    {
        builder.ToTable("ShipmentEvents", table =>
        {
            table.HasCheckConstraint("CK_ShipmentEvents_Type", "\"EventType\" IN ('Created','AssignmentChanged','StatusChanged','DetailsChanged')");
            table.HasCheckConstraint("CK_ShipmentEvents_Actor", "length(btrim(\"Actor\")) > 0");
            table.HasCheckConstraint("CK_ShipmentEvents_Statuses", "(\"OldStatus\" IS NULL OR \"OldStatus\" IN ('Draft','Planned','InTransit','Delivered','Cancelled')) AND (\"NewStatus\" IS NULL OR \"NewStatus\" IN ('Draft','Planned','InTransit','Delivered','Cancelled'))");
            table.HasCheckConstraint("CK_ShipmentEvents_Transition", "(\"EventType\" <> 'StatusChanged' OR (\"OldStatus\" IS NOT NULL AND \"NewStatus\" IS NOT NULL AND ((\"OldStatus\" = 'Draft' AND \"NewStatus\" IN ('Planned','Cancelled')) OR (\"OldStatus\" = 'Planned' AND \"NewStatus\" IN ('InTransit','Cancelled')) OR (\"OldStatus\" = 'InTransit' AND \"NewStatus\" = 'Delivered'))))");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EventType).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.OldStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.NewStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Actor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Message).HasMaxLength(1000);
        builder.HasIndex(x => new { x.ShipmentId, x.OccurredAtUtc });
    }
}
