using FleetFlow.Domain.Common;

namespace FleetFlow.Domain.Customers;

public sealed class Customer
{
    private Customer() { }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string ContactName { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string? Phone { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Customer Create(string name, string contactName, string email, string? phone, DateTimeOffset now, Guid? id = null) => new()
    {
        Id = Guard.Id(id ?? Guid.CreateVersion7(), "Customer ID"),
        Name = Guard.Text(name, "Company name", 200),
        ContactName = Guard.Text(contactName, "Contact name", 150),
        Email = Guard.Email(email),
        Phone = Guard.Optional(phone, "Phone", 40),
        CreatedAtUtc = Guard.Utc(now)
    };
}
