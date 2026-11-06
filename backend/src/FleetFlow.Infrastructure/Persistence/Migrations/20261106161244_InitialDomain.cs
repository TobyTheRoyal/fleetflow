using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FleetFlow.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialDomain : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence(
            name: "ShipmentNumberSequence");

        migrationBuilder.CreateTable(
            name: "Customers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ContactName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Customers", x => x.Id);
                table.CheckConstraint("CK_Customers_Required", "length(btrim(\"Name\")) > 0 AND length(btrim(\"ContactName\")) > 0 AND length(btrim(\"Email\")) > 0");
            });

        migrationBuilder.CreateTable(
            name: "Drivers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Active = table.Column<bool>(type: "boolean", nullable: false),
                LicenseCategory = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Drivers", x => x.Id);
                table.CheckConstraint("CK_Drivers_Required", "length(btrim(\"EmployeeNumber\")) > 0 AND \"EmployeeNumber\" = upper(btrim(\"EmployeeNumber\")) AND length(btrim(\"FirstName\")) > 0 AND length(btrim(\"LastName\")) > 0");
            });

        migrationBuilder.CreateTable(
            name: "Vehicles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PlateNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                MaxPayloadKg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Vehicles", x => x.Id);
                table.CheckConstraint("CK_Vehicles_Enums", "\"Type\" IN ('Van','Truck') AND \"Status\" IN ('Available','Maintenance','Inactive')");
                table.CheckConstraint("CK_Vehicles_Payload", "\"MaxPayloadKg\" > 0");
                table.CheckConstraint("CK_Vehicles_Plate", "length(btrim(\"PlateNumber\")) > 0 AND \"PlateNumber\" = upper(btrim(\"PlateNumber\"))");
            });

        migrationBuilder.CreateTable(
            name: "Shipments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ShipmentNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                PickupWindowStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DeliveryDeadlineUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CargoDescription = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                WeightKg = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                PalletCount = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                AssignedDriverId = table.Column<Guid>(type: "uuid", nullable: true),
                AssignedVehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                ActualPickupAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ActualDeliveredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<long>(type: "bigint", nullable: false),
                DestinationCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                DestinationCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                DestinationPostcode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                DestinationStreet = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                OriginCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                OriginCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                OriginPostcode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                OriginStreet = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Shipments", x => x.Id);
                table.CheckConstraint("CK_Shipments_ActualTimes", "(\"Status\" NOT IN ('InTransit','Delivered') OR \"ActualPickupAtUtc\" IS NOT NULL) AND (\"Status\" <> 'Delivered' OR \"ActualDeliveredAtUtc\" IS NOT NULL) AND (\"ActualDeliveredAtUtc\" IS NULL OR (\"ActualPickupAtUtc\" IS NOT NULL AND \"ActualDeliveredAtUtc\" >= \"ActualPickupAtUtc\"))");
                table.CheckConstraint("CK_Shipments_Assignment", "(\"AssignedDriverId\" IS NULL) = (\"AssignedVehicleId\" IS NULL) AND (\"Status\" NOT IN ('Planned','InTransit','Delivered') OR \"AssignedDriverId\" IS NOT NULL)");
                table.CheckConstraint("CK_Shipments_Destination", "length(btrim(\"DestinationStreet\")) > 0 AND length(btrim(\"DestinationCity\")) > 0 AND length(btrim(\"DestinationPostcode\")) > 0 AND \"DestinationCountryCode\" IN ('AD','AE','AF','AG','AI','AL','AM','AO','AQ','AR','AS','AT','AU','AW','AX','AZ','BA','BB','BD','BE','BF','BG','BH','BI','BJ','BL','BM','BN','BO','BQ','BR','BS','BT','BV','BW','BY','BZ','CA','CC','CD','CF','CG','CH','CI','CK','CL','CM','CN','CO','CR','CU','CV','CW','CX','CY','CZ','DE','DJ','DK','DM','DO','DZ','EC','EE','EG','EH','ER','ES','ET','FI','FJ','FK','FM','FO','FR','GA','GB','GD','GE','GF','GG','GH','GI','GL','GM','GN','GP','GQ','GR','GS','GT','GU','GW','GY','HK','HM','HN','HR','HT','HU','ID','IE','IL','IM','IN','IO','IQ','IR','IS','IT','JE','JM','JO','JP','KE','KG','KH','KI','KM','KN','KP','KR','KW','KY','KZ','LA','LB','LC','LI','LK','LR','LS','LT','LU','LV','LY','MA','MC','MD','ME','MF','MG','MH','MK','ML','MM','MN','MO','MP','MQ','MR','MS','MT','MU','MV','MW','MX','MY','MZ','NA','NC','NE','NF','NG','NI','NL','NO','NP','NR','NU','NZ','OM','PA','PE','PF','PG','PH','PK','PL','PM','PN','PR','PS','PT','PW','PY','QA','RE','RO','RS','RU','RW','SA','SB','SC','SD','SE','SG','SH','SI','SJ','SK','SL','SM','SN','SO','SR','SS','ST','SV','SX','SY','SZ','TC','TD','TF','TG','TH','TJ','TK','TL','TM','TN','TO','TR','TT','TV','TW','TZ','UA','UG','UM','US','UY','UZ','VA','VC','VE','VG','VI','VN','VU','WF','WS','YE','YT','ZA','ZM','ZW')");
                table.CheckConstraint("CK_Shipments_Number", "length(btrim(\"ShipmentNumber\")) > 0");
                table.CheckConstraint("CK_Shipments_Origin", "length(btrim(\"OriginStreet\")) > 0 AND length(btrim(\"OriginCity\")) > 0 AND length(btrim(\"OriginPostcode\")) > 0 AND \"OriginCountryCode\" IN ('AD','AE','AF','AG','AI','AL','AM','AO','AQ','AR','AS','AT','AU','AW','AX','AZ','BA','BB','BD','BE','BF','BG','BH','BI','BJ','BL','BM','BN','BO','BQ','BR','BS','BT','BV','BW','BY','BZ','CA','CC','CD','CF','CG','CH','CI','CK','CL','CM','CN','CO','CR','CU','CV','CW','CX','CY','CZ','DE','DJ','DK','DM','DO','DZ','EC','EE','EG','EH','ER','ES','ET','FI','FJ','FK','FM','FO','FR','GA','GB','GD','GE','GF','GG','GH','GI','GL','GM','GN','GP','GQ','GR','GS','GT','GU','GW','GY','HK','HM','HN','HR','HT','HU','ID','IE','IL','IM','IN','IO','IQ','IR','IS','IT','JE','JM','JO','JP','KE','KG','KH','KI','KM','KN','KP','KR','KW','KY','KZ','LA','LB','LC','LI','LK','LR','LS','LT','LU','LV','LY','MA','MC','MD','ME','MF','MG','MH','MK','ML','MM','MN','MO','MP','MQ','MR','MS','MT','MU','MV','MW','MX','MY','MZ','NA','NC','NE','NF','NG','NI','NL','NO','NP','NR','NU','NZ','OM','PA','PE','PF','PG','PH','PK','PL','PM','PN','PR','PS','PT','PW','PY','QA','RE','RO','RS','RU','RW','SA','SB','SC','SD','SE','SG','SH','SI','SJ','SK','SL','SM','SN','SO','SR','SS','ST','SV','SX','SY','SZ','TC','TD','TF','TG','TH','TJ','TK','TL','TM','TN','TO','TR','TT','TV','TW','TZ','UA','UG','UM','US','UY','UZ','VA','VC','VE','VG','VI','VN','VU','WF','WS','YE','YT','ZA','ZM','ZW')");
                table.CheckConstraint("CK_Shipments_Pallets", "\"PalletCount\" >= 0");
                table.CheckConstraint("CK_Shipments_Schedule", "\"DeliveryDeadlineUtc\" > \"PickupWindowStartUtc\"");
                table.CheckConstraint("CK_Shipments_Status", "\"Status\" IN ('Draft','Planned','InTransit','Delivered','Cancelled')");
                table.CheckConstraint("CK_Shipments_Updated", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
                table.CheckConstraint("CK_Shipments_Version", "\"Version\" > 0");
                table.CheckConstraint("CK_Shipments_Weight", "\"WeightKg\" > 0");
                table.ForeignKey(
                    name: "FK_Shipments_Customers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "Customers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Shipments_Drivers_AssignedDriverId",
                    column: x => x.AssignedDriverId,
                    principalTable: "Drivers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Shipments_Vehicles_AssignedVehicleId",
                    column: x => x.AssignedVehicleId,
                    principalTable: "Vehicles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ShipmentEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                EventType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                OldStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                NewStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ShipmentEvents", x => x.Id);
                table.CheckConstraint("CK_ShipmentEvents_Actor", "length(btrim(\"Actor\")) > 0");
                table.CheckConstraint("CK_ShipmentEvents_Statuses", "(\"OldStatus\" IS NULL OR \"OldStatus\" IN ('Draft','Planned','InTransit','Delivered','Cancelled')) AND (\"NewStatus\" IS NULL OR \"NewStatus\" IN ('Draft','Planned','InTransit','Delivered','Cancelled'))");
                table.CheckConstraint("CK_ShipmentEvents_Transition", "(\"EventType\" <> 'StatusChanged' OR (\"OldStatus\" IS NOT NULL AND \"NewStatus\" IS NOT NULL AND ((\"OldStatus\" = 'Draft' AND \"NewStatus\" IN ('Planned','Cancelled')) OR (\"OldStatus\" = 'Planned' AND \"NewStatus\" IN ('InTransit','Cancelled')) OR (\"OldStatus\" = 'InTransit' AND \"NewStatus\" = 'Delivered'))))");
                table.CheckConstraint("CK_ShipmentEvents_Type", "\"EventType\" IN ('Created','AssignmentChanged','StatusChanged','DetailsChanged')");
                table.ForeignKey(
                    name: "FK_ShipmentEvents_Shipments_ShipmentId",
                    column: x => x.ShipmentId,
                    principalTable: "Shipments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Drivers_EmployeeNumber",
            table: "Drivers",
            column: "EmployeeNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ShipmentEvents_ShipmentId_OccurredAtUtc",
            table: "ShipmentEvents",
            columns: new[] { "ShipmentId", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_AssignedDriverId",
            table: "Shipments",
            column: "AssignedDriverId");

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_AssignedVehicleId",
            table: "Shipments",
            column: "AssignedVehicleId");

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_CreatedAtUtc",
            table: "Shipments",
            column: "CreatedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_CustomerId",
            table: "Shipments",
            column: "CustomerId");

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_ShipmentNumber",
            table: "Shipments",
            column: "ShipmentNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Shipments_Status_DeliveryDeadlineUtc",
            table: "Shipments",
            columns: new[] { "Status", "DeliveryDeadlineUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_Vehicles_PlateNumber",
            table: "Vehicles",
            column: "PlateNumber",
            unique: true);
        migrationBuilder.Sql("""
            CREATE FUNCTION prevent_shipment_event_mutation() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'Shipment history is immutable' USING ERRCODE = '23514';
            END;
            $$;
            CREATE TRIGGER shipment_events_immutable
            BEFORE UPDATE OR DELETE ON "ShipmentEvents"
            FOR EACH ROW EXECUTE FUNCTION prevent_shipment_event_mutation();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER shipment_events_immutable ON \"ShipmentEvents\"; DROP FUNCTION prevent_shipment_event_mutation();");
        migrationBuilder.DropTable(
            name: "ShipmentEvents");

        migrationBuilder.DropTable(
            name: "Shipments");

        migrationBuilder.DropTable(
            name: "Customers");

        migrationBuilder.DropTable(
            name: "Drivers");

        migrationBuilder.DropTable(
            name: "Vehicles");

        migrationBuilder.DropSequence(
            name: "ShipmentNumberSequence");
    }
}
