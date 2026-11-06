using System.Text.Json;
using System.Text.Json.Serialization;
using FleetFlow.Api.Contracts;
using FleetFlow.Application.Queries;
using FleetFlow.Domain.Shipments;
using FleetFlow.Domain.Vehicles;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FleetFlow.Api.OpenApi;

internal static class ApiExamples
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    private static readonly Guid Customer = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Driver = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Vehicle = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static AddressRequest Origin => new() { Street = "Demo Depotstrasse 1", City = "Graz", Postcode = "8010", CountryCode = "AT" };
    private static AddressRequest Destination => new() { Street = "Demo Lagerweg 2", City = "Vienna", Postcode = "1010", CountryCode = "AT" };

    public static Task Apply(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken token)
    {
        var route = context.Description.RelativePath?.Split('?')[0] ?? "";
        var method = context.Description.HttpMethod;
        if (!route.StartsWith("api/v1/", StringComparison.Ordinal)) return Task.CompletedTask;
        object? request = route switch
        {
            "api/v1/shipments" when method == "POST" => new CreateShipmentRequest
            {
                CustomerId = Customer,
                Origin = Origin,
                Destination = Destination,
                PickupWindowStartUtc = Now.AddHours(1),
                DeliveryDeadlineUtc = Now.AddHours(5),
                WeightKg = 1200.50m,
                PalletCount = 3,
                CargoDescription = "Fictional demo pallets"
            },
            "api/v1/shipments/{id}" when method == "PUT" => new UpdateShipmentRequest
            {
                Version = 1,
                CustomerId = Customer,
                Origin = Origin,
                Destination = Destination,
                PickupWindowStartUtc = Now.AddHours(1),
                DeliveryDeadlineUtc = Now.AddHours(5),
                WeightKg = 1200.50m,
                PalletCount = 3,
                CargoDescription = "Updated demo pallets"
            },
            "api/v1/shipments/{id}/assign" => new AssignShipmentRequest { Version = 1, DriverId = Driver, VehicleId = Vehicle },
            "api/v1/shipments/{id}/status" => new ChangeStatusRequest { Version = 2, Status = ShipmentStatus.Planned, Reason = "Dispatch confirmed" },
            "api/v1/customers" when method == "POST" => new CreateCustomerRequest { Name = "Alpenstern Demo GmbH", ContactName = "Demo Contact", Email = "demo@fleetflow.example" },
            _ when route.StartsWith("api/v1/drivers", StringComparison.Ordinal) && method != "GET" => new DriverRequest { EmployeeNumber = "DEMO-101", FirstName = "Demo", LastName = "Driver", Active = true, LicenseCategory = "C" },
            _ when route.StartsWith("api/v1/vehicles", StringComparison.Ordinal) && method != "GET" => new VehicleRequest { PlateNumber = "FF-DEMO101", Type = VehicleType.Truck, MaxPayloadKg = 12000m, Status = VehicleStatus.Available },
            _ => null
        };
        if (request is not null && operation.RequestBody?.Content is { } bodies)
            foreach (var body in bodies.Values) body.Example = JsonSerializer.SerializeToNode(request, request.GetType(), Json);

        var shipment = new ShipmentResponse(Guid.Parse("44444444-4444-4444-8444-444444444444"), "FF-2026-000001", Customer,
            new AddressResponse(Origin.Street, Origin.City, Origin.Postcode, Origin.CountryCode), new AddressResponse(Destination.Street, Destination.City, Destination.Postcode, Destination.CountryCode),
            Now.AddHours(1), Now.AddHours(5), 1200.50m, 3, "Fictional demo pallets", ShipmentStatus.Draft, null, null, null, null, Now, Now, 1, "Alpenstern Demo GmbH", null, null);
        var mutation = shipment with { CustomerName = null };
        object response = route switch
        {
            "api/v1/dashboard/summary" => new DashboardResponse(Now, 1, 1, 0, 0, 0, 0, 1, [shipment]),
            "api/v1/shipments" when method == "GET" => new PageResponse<ShipmentResponse>([shipment], 1, 20, 1, 1),
            "api/v1/shipments/{id}/events" => new PageResponse<ShipmentEventResponse>([new ShipmentEventResponse(Guid.Parse("55555555-5555-4555-8555-555555555555"), shipment.Id, ShipmentEventType.Created, null, ShipmentStatus.Draft, null, Now, "local-dispatcher")], 1, 20, 1, 1),
            _ when route.StartsWith("api/v1/customers", StringComparison.Ordinal) => method == "GET"
                ? new PageResponse<CustomerResponse>([new CustomerResponse(Customer, "Alpenstern Demo GmbH", "Demo Contact", "demo@fleetflow.example", null, Now)], 1, 20, 1, 1)
                : new CustomerResponse(Customer, "Alpenstern Demo GmbH", "Demo Contact", "demo@fleetflow.example", null, Now),
            _ when route.StartsWith("api/v1/drivers", StringComparison.Ordinal) => method == "GET"
                ? new PageResponse<DriverResponse>([new DriverResponse(Driver, "DEMO-101", "Demo", "Driver", true, "C", Now)], 1, 20, 1, 1)
                : new DriverResponse(Driver, "DEMO-101", "Demo", "Driver", true, "C", Now),
            _ when route.StartsWith("api/v1/vehicles", StringComparison.Ordinal) => method == "GET"
                ? new PageResponse<VehicleResponse>([new VehicleResponse(Vehicle, "FF-DEMO101", VehicleType.Truck, 12000m, VehicleStatus.Available, Now)], 1, 20, 1, 1)
                : new VehicleResponse(Vehicle, "FF-DEMO101", VehicleType.Truck, 12000m, VehicleStatus.Available, Now),
            "api/v1/shipments/{id}/assign" => mutation with { Version = 2, AssignedDriverId = Driver, AssignedVehicleId = Vehicle },
            "api/v1/shipments/{id}/status" => mutation with { Version = 3, Status = ShipmentStatus.Planned, AssignedDriverId = Driver, AssignedVehicleId = Vehicle },
            "api/v1/shipments/{id}" when method == "PUT" => mutation with { Version = 2, CargoDescription = "Updated demo pallets" },
            "api/v1/shipments" when method == "POST" => mutation,
            _ => shipment
        };
        if (operation.Responses is { } responses)
            foreach (var (code, item) in responses)
            {
                if (code is "200" or "201" && item.Content is { } content)
                    foreach (var body in content.Values) body.Example = JsonSerializer.SerializeToNode(response, response.GetType(), Json);
                if (code is "400" or "404" or "409" or "422" or "500" && item.Content is { } errorContent)
                {
                    var problemMedia = errorContent.Values.FirstOrDefault() ?? new OpenApiMediaType();
                    errorContent.Clear();
                    errorContent["application/problem+json"] = problemMedia;
                    item.Description = code switch { "400" => "Malformed input; code invalid_request and field errors.", "404" => "Resource not found; code not_found.", "409" => "Status, assignment, stale_version, resource_in_use or duplicate_value conflict; reload when stale.", "422" => "Business validation failure, e.g. invalid_weight, invalid_country or vehicle_overloaded.", _ => "Unexpected failure; safe generic details." };
                    foreach (var body in errorContent.Values)
                    {
                        body.Example = JsonSerializer.SerializeToNode(new
                        {
                            type = "about:blank",
                            title = "The operation could not be completed.",
                            status = int.Parse(code),
                            detail = "A safe explanation of the rejected operation.",
                            code = code switch { "400" => "invalid_request", "404" => "not_found", "409" => "stale_version", "422" => "vehicle_overloaded", _ => "unexpected_error" },
                            traceId = "example-request-trace"
                        }, Json);
                        if (code == "400")
                            body.Example!["errors"] = JsonSerializer.SerializeToNode(new Dictionary<string, string[]> { ["version"] = ["A required value is missing, invalid, or outside its allowed range."] }, Json);
                    }
                }
            }
        if (operation.Parameters is { } parameters)
            foreach (var parameter in parameters)
                parameter.Description ??= parameter.Name?.ToLowerInvariant() switch
                {
                    "page" => "One-based page, 1..1000000; default 1. Out-of-range pages return an empty items array.",
                    "pagesize" => "Items per page, 1..100; default 20.",
                    "status" => "Optional shipment status: Draft, Planned, InTransit, Delivered or Cancelled.",
                    "search" => "Literal case-insensitive substring (max 100 characters) in shipment number or origin/destination city.",
                    "customerid" => "Optional customer UUID; combines with other filters using AND.",
                    "deadline" => "Optional Overdue or DueSoon filter, excluding Delivered and Cancelled.",
                    _ => "Resource UUID."
                };
        return Task.CompletedTask;
    }
}
