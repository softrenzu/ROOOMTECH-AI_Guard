using System.Security.Cryptography;
using Rooomtech.AIGuard.Core;
using Xunit;

namespace Rooomtech.AIGuard.Core.Tests;

public sealed class ProductLicensingTests
{
    [Fact]
    public void SignedBusinessLicense_Validates_WithMatchingPublicKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privatePem = key.ExportPkcs8PrivateKeyPem();
        var publicPem = key.ExportSubjectPublicKeyInfoPem();
        var now = DateTimeOffset.UtcNow;

        var license = new ProductLicense
        {
            LicenseId = Guid.NewGuid().ToString("D"),
            LicenseeName = "Test Admin",
            Organization = "Test Corporation",
            Seats = 25,
            IssuedAtUtc = now,
            ValidFromUtc = now.AddMinutes(-1),
            ValidUntilUtc = now.AddDays(30)
        };

        var signed = ProductLicensing.SignBusinessLicense(license, privatePem);
        var result = ProductLicensing.ValidateBusinessLicense(signed, publicPem, now);

        Assert.True(result.IsValid);
        Assert.True(result.CommercialUseAllowed);
    }

    [Fact]
    public void TamperedBusinessLicense_IsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privatePem = key.ExportPkcs8PrivateKeyPem();
        var publicPem = key.ExportSubjectPublicKeyInfoPem();
        var now = DateTimeOffset.UtcNow;

        var signed = ProductLicensing.SignBusinessLicense(new ProductLicense
        {
            LicenseId = Guid.NewGuid().ToString("D"),
            LicenseeName = "Test Admin",
            Organization = "Test Corporation",
            Seats = 5,
            IssuedAtUtc = now,
            ValidFromUtc = now.AddMinutes(-1),
            ValidUntilUtc = now.AddDays(30)
        }, privatePem);

        var tampered = signed with { Seats = 999 };
        var result = ProductLicensing.ValidateBusinessLicense(tampered, publicPem, now);

        Assert.False(result.IsValid);
        Assert.False(result.CommercialUseAllowed);
    }

    [Fact]
    public void ExpiredBusinessLicense_IsRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privatePem = key.ExportPkcs8PrivateKeyPem();
        var publicPem = key.ExportSubjectPublicKeyInfoPem();
        var now = DateTimeOffset.UtcNow;

        var signed = ProductLicensing.SignBusinessLicense(new ProductLicense
        {
            LicenseId = Guid.NewGuid().ToString("D"),
            LicenseeName = "Test Admin",
            Organization = "Test Corporation",
            Seats = 5,
            IssuedAtUtc = now.AddDays(-40),
            ValidFromUtc = now.AddDays(-40),
            ValidUntilUtc = now.AddDays(-1)
        }, privatePem);

        var result = ProductLicensing.ValidateBusinessLicense(signed, publicPem, now);

        Assert.False(result.IsValid);
        Assert.Contains("有効期限", result.Status);
    }
}
