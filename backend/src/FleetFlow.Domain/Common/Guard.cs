using System.Net.Mail;

namespace FleetFlow.Domain.Common;

public static class Guard
{
    private static readonly HashSet<string> Countries = new(
        ("AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ " +
         "CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR " +
         "GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO JP " +
         "KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT MU MV MW MX MY MZ " +
         "NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ " +
         "TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ UA UG UM US UY UZ VA VC VE VG VI VN VU WF WS YE YT ZA ZM ZW").Split(' '), StringComparer.Ordinal);

    public static IEnumerable<string> CountryCodes => Countries.Order(StringComparer.Ordinal);

    public static string Text(string? value, string field, int maxLength)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength)
            throw new FleetFlowException("invalid_input", $"{field} is required and must contain at most {maxLength} characters.");
        return text;
    }

    public static string? Optional(string? value, string field, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Text(value, field, maxLength);

    public static Guid Id(Guid id, string field)
    {
        if (id == Guid.Empty) throw new FleetFlowException("invalid_input", $"{field} must be a valid identifier.");
        return id;
    }

    public static DateTimeOffset Utc(DateTimeOffset value)
    {
        if (value == DateTimeOffset.MinValue || value == DateTimeOffset.MaxValue)
            throw new FleetFlowException("invalid_timestamp", "A valid timestamp is required.");
        return value.ToUniversalTime();
    }

    public static decimal Weight(decimal value, string field)
    {
        if (value <= 0 || value > 9999999999.99m || decimal.Round(value, 2) != value)
            throw new FleetFlowException("invalid_weight", $"{field} must be positive, fit decimal(12,2), and have at most two decimal places.");
        return value;
    }

    public static string Country(string value)
    {
        var country = Text(value, "Country code", 2).ToUpperInvariant();
        if (!Countries.Contains(country)) throw new FleetFlowException("invalid_country", "Use a valid ISO 3166-1 alpha-2 country code.");
        return country;
    }

    public static string Email(string value)
    {
        var email = Text(value, "Email", 254);
        if (!MailAddress.TryCreate(email, out var parsed) || !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
            throw new FleetFlowException("invalid_email", "A valid contact email address is required.");
        return email;
    }
}
