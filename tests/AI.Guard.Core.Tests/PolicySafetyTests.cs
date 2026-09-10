using Rooomtech.AIGuard.Core;
using Xunit;

namespace Rooomtech.AIGuard.Core.Tests;

public sealed class PolicySafetyTests
{
    [Fact]
    public void UserDocumentsFolder_IsAllowed()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrWhiteSpace(userProfile));

        var path = Path.Combine(userProfile, "Documents", "AI Guard Protected");
        var allowed = PolicySafety.TryValidateProtectedPath(path, out var reason);

        Assert.True(allowed, reason);
    }

    [Fact]
    public void DriveRoot_IsRejected()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory)!;
        var allowed = PolicySafety.TryValidateProtectedPath(root, out var reason);

        Assert.False(allowed);
        Assert.Contains("ルート", reason);
    }

    [Fact]
    public void WindowsFolder_IsRejected()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var path = Path.Combine(windows, "Temp", "AI Guard Protected");
        var allowed = PolicySafety.TryValidateProtectedPath(path, out var reason);

        Assert.False(allowed);
        Assert.Contains("システム領域", reason);
    }

    [Fact]
    public void AppDataFolder_IsRejected()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(appData, "AI Guard Protected");
        var allowed = PolicySafety.TryValidateProtectedPath(path, out var reason);

        Assert.False(allowed);
        Assert.Contains("システム領域", reason);
    }

    [Fact]
    public void RelativePath_IsRejected()
    {
        var allowed = PolicySafety.TryValidateProtectedPath(@"Documents\AI Guard Protected", out var reason);

        Assert.False(allowed);
        Assert.Contains("絶対パス", reason);
    }

    [Fact]
    public void Enforcement_RequiresDenyByDefault()
    {
        var policy = new GuardPolicy
        {
            DenyByDefault = false
        };

        Assert.Throws<InvalidOperationException>(() => PolicySafety.ValidateForEnforcement(policy));
    }
}
