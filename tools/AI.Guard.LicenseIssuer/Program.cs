using Rooomtech.AIGuard.Core;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintHelp();
    return;
}

if (!string.Equals(args[0], "issue", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Unknown command.");
    PrintHelp();
    Environment.ExitCode = 2;
    return;
}

var options = ParseOptions(args.Skip(1).ToArray());
var privateKeyPath = Require(options, "private-key");
var licensee = Require(options, "licensee");
var organization = Require(options, "organization");
var outputPath = Require(options, "out");

if (!int.TryParse(options.GetValueOrDefault("seats", "1"), out var seats) || seats < 1)
    throw new ArgumentException("--seats must be an integer >= 1.");

DateTimeOffset? validUntil = null;
if (options.TryGetValue("valid-until", out var expiryText) && !string.IsNullOrWhiteSpace(expiryText))
{
    if (!DateTimeOffset.TryParse(expiryText, out var parsed))
        throw new ArgumentException("--valid-until must be a valid date/time, for example 2027-09-30.");
    validUntil = parsed.ToUniversalTime();
}

var now = DateTimeOffset.UtcNow;
var license = new ProductLicense
{
    LicenseId = Guid.NewGuid().ToString("D"),
    LicenseeName = licensee,
    Organization = organization,
    Seats = seats,
    IssuedAtUtc = now,
    ValidFromUtc = now,
    ValidUntilUtc = validUntil
};

var privateKeyPem = File.ReadAllText(privateKeyPath);
var signed = ProductLicensing.SignBusinessLicense(license, privateKeyPem);
ProductLicensing.SaveLicense(outputPath, signed);

var validation = ProductLicensing.ValidateBusinessLicense(signed);
if (!validation.IsValid)
    throw new InvalidOperationException("Issued license did not validate against the embedded product public key: " + validation.Status);

Console.WriteLine($"License issued: {signed.LicenseId}");
Console.WriteLine($"Organization : {signed.Organization}");
Console.WriteLine($"Seats        : {signed.Seats}");
Console.WriteLine($"Valid until  : {(signed.ValidUntilUtc is null ? "No expiry" : signed.ValidUntilUtc.Value.ToString("O"))}");
Console.WriteLine($"Output       : {Path.GetFullPath(outputPath)}");

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        var token = args[i];
        if (!token.StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Unexpected argument: {token}");
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Missing value for {token}");
        result[token[2..]] = args[++i];
    }
    return result;
}

static string Require(Dictionary<string, string> options, string key)
{
    if (!options.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        throw new ArgumentException($"Missing required option --{key}.");
    return value;
}

static void PrintHelp()
{
    Console.WriteLine("""
ROOOMTECH AI Guard License Issuer

Issue a signed Business license offline:

  AIGuard.LicenseIssuer issue \
    --private-key ROOOMTECH_AI_Guard_license_private.pem \
    --licensee "Customer Administrator" \
    --organization "Example Corporation" \
    --seats 25 \
    --valid-until 2027-09-30 \
    --out Example-Corporation.aiguard-license.json

Omit --valid-until for a perpetual license.
Never distribute or commit the private key.
""");
}
