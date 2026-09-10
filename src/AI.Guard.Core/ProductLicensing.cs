using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rooomtech.AIGuard.Core;

public sealed record ProductLicense
{
    public string Product { get; init; } = "ROOOMTECH AI Guard";
    public string Edition { get; init; } = "Business";
    public string LicenseId { get; init; } = string.Empty;
    public string LicenseeName { get; init; } = string.Empty;
    public string Organization { get; init; } = string.Empty;
    public int Seats { get; init; } = 1;
    public DateTimeOffset IssuedAtUtc { get; init; }
    public DateTimeOffset ValidFromUtc { get; init; }
    public DateTimeOffset? ValidUntilUtc { get; init; }
    public string SignatureBase64 { get; init; } = string.Empty;
}

public sealed record LicenseValidationResult(
    bool IsValid,
    bool CommercialUseAllowed,
    string Status,
    ProductLicense? License = null);

public static class ProductLicensing
{
    public const string ProductName = "ROOOMTECH AI Guard";
    public const string BusinessEdition = "Business";

    public const string PublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEuJcEW/HpqTrxJYBz9/GeLJzW0n4e
zI+YdghmSip0iWrfS6/vOWtgn02eVdYuS1rz1XI3wYnSA5UyDLK0juUmQA==
-----END PUBLIC KEY-----
""";

    public const string PublicKeySha256 = "43AB1207EB5B04A8027288F970FE1B6F315C7F688FF5D04B75F1357C373D6509";

    public static LicenseValidationResult ValidatePersonalUse()
        => new(true, false, "個人による私的利用：無償");

    public static LicenseValidationResult ValidateBusinessLicense(ProductLicense? license, DateTimeOffset? nowUtc = null)
        => ValidateBusinessLicense(license, PublicKeyPem, nowUtc);

    public static LicenseValidationResult ValidateBusinessLicense(
        ProductLicense? license,
        string publicKeyPem,
        DateTimeOffset? nowUtc = null)
    {
        if (license is null)
            return new(false, false, "法人・団体・業務利用には有効な法人ライセンスが必要です。");

        if (!string.Equals(license.Product, ProductName, StringComparison.Ordinal))
            return new(false, false, "ライセンスの製品名が一致しません。", license);

        if (!string.Equals(license.Edition, BusinessEdition, StringComparison.Ordinal))
            return new(false, false, "法人向けBusinessライセンスではありません。", license);

        if (string.IsNullOrWhiteSpace(license.LicenseId) ||
            string.IsNullOrWhiteSpace(license.LicenseeName) ||
            string.IsNullOrWhiteSpace(license.Organization))
            return new(false, false, "ライセンス情報が不足しています。", license);

        if (license.Seats < 1 || license.Seats > 100000)
            return new(false, false, "ライセンス端末数が不正です。", license);

        var now = (nowUtc ?? DateTimeOffset.UtcNow).ToUniversalTime();
        if (now < license.ValidFromUtc.ToUniversalTime())
            return new(false, false, "ライセンスの利用開始日前です。", license);

        if (license.ValidUntilUtc is { } validUntil && now > validUntil.ToUniversalTime())
            return new(false, false, "法人ライセンスの有効期限が切れています。", license);

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(license.SignatureBase64);
        }
        catch
        {
            return new(false, false, "ライセンス署名の形式が不正です。", license);
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            var valid = ecdsa.VerifyData(
                CanonicalPayload(license),
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);

            return valid
                ? new(true, true, BuildBusinessStatus(license), license)
                : new(false, false, "法人ライセンスの署名を検証できませんでした。", license);
        }
        catch (Exception ex)
        {
            return new(false, false, "法人ライセンスの検証に失敗しました: " + ex.Message, license);
        }
    }

    public static ProductLicense SignBusinessLicense(ProductLicense license, string privateKeyPem)
    {
        if (license is null)
            throw new ArgumentNullException(nameof(license));

        var unsigned = license with { SignatureBase64 = string.Empty };
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        var signature = ecdsa.SignData(
            CanonicalPayload(unsigned),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);

        return unsigned with { SignatureBase64 = Convert.ToBase64String(signature) };
    }

    public static ProductLicense? LoadLicense(string path)
    {
        if (!File.Exists(path))
            return null;

        return JsonSerializer.Deserialize<ProductLicense>(File.ReadAllText(path), JsonOptions);
    }

    public static void SaveLicense(string path, ProductLicense license)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(license, JsonOptions), new UTF8Encoding(false));
    }

    private static byte[] CanonicalPayload(ProductLicense license)
    {
        var payload = new
        {
            product = license.Product,
            edition = license.Edition,
            licenseId = license.LicenseId,
            licenseeName = license.LicenseeName,
            organization = license.Organization,
            seats = license.Seats,
            issuedAtUtc = license.IssuedAtUtc.ToUniversalTime().ToString("O"),
            validFromUtc = license.ValidFromUtc.ToUniversalTime().ToString("O"),
            validUntilUtc = license.ValidUntilUtc?.ToUniversalTime().ToString("O")
        };

        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    private static string BuildBusinessStatus(ProductLicense license)
    {
        var expiry = license.ValidUntilUtc is null
            ? "無期限"
            : license.ValidUntilUtc.Value.ToLocalTime().ToString("yyyy/MM/dd");
        return $"法人ライセンス有効：{license.Organization} / {license.Seats}端末 / 有効期限 {expiry}";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
