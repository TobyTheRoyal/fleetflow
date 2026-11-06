using FleetFlow.Domain.Common;

namespace FleetFlow.Domain.Drivers;

public sealed class Driver
{
    private Driver() { }
    public Guid Id { get; private set; }
    public string EmployeeNumber { get; private set; } = null!;
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public bool Active { get; private set; }
    public string? LicenseCategory { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Driver Create(string employeeNumber, string firstName, string lastName, bool active, string? licenseCategory, DateTimeOffset now, Guid? id = null) => new()
    {
        Id = Guard.Id(id ?? Guid.CreateVersion7(), "Driver ID"),
        EmployeeNumber = Guard.Text(employeeNumber, "Employee number", 40).ToUpperInvariant(),
        FirstName = Guard.Text(firstName, "First name", 100),
        LastName = Guard.Text(lastName, "Last name", 100),
        Active = active,
        LicenseCategory = Guard.Optional(licenseCategory, "License category", 30),
        CreatedAtUtc = Guard.Utc(now)
    };
    public void Update(string employeeNumber, string firstName, string lastName, bool active, string? licenseCategory)
    {
        var validated = Create(employeeNumber, firstName, lastName, active, licenseCategory, CreatedAtUtc, Id);
        EmployeeNumber = validated.EmployeeNumber; FirstName = validated.FirstName; LastName = validated.LastName;
        Active = validated.Active; LicenseCategory = validated.LicenseCategory;
    }
}
