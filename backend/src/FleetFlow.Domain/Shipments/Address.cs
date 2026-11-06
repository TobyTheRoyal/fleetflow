using FleetFlow.Domain.Common;

namespace FleetFlow.Domain.Shipments;

public sealed record Address
{
    private Address() { }
    public string Street { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string Postcode { get; private set; } = null!;
    public string CountryCode { get; private set; } = null!;

    public static Address Create(string street, string city, string postcode, string countryCode) => new()
    {
        Street = Guard.Text(street, "Street", 200),
        City = Guard.Text(city, "City", 100),
        Postcode = Guard.Text(postcode, "Postcode", 20),
        CountryCode = Guard.Country(countryCode)
    };
}
