namespace Rooomtech.AIGuard.Core;

public static class PolicySafety
{
    public const int MaxProtectedPaths = 8;
    public const int MaxAllowedApplications = 32;

    public static void ValidateForEnforcement(GuardPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (!policy.DenyByDefault)
            throw new InvalidOperationException("製品版Driverはdeny-by-defaultポリシーのみを受け付けます。");

        if (policy.ProtectedPaths.Count > MaxProtectedPaths)
            throw new InvalidOperationException($"保護フォルダは最大{MaxProtectedPaths}件です。");

        if (policy.AllowedApplications.Count > MaxAllowedApplications)
            throw new InvalidOperationException($"許可アプリは最大{MaxAllowedApplications}件です。");

        foreach (var path in policy.ProtectedPaths)
        {
            if (!TryValidateProtectedPath(path, out var reason))
                throw new InvalidOperationException(reason);
        }
    }

    public static bool TryValidateProtectedPath(string path, out string reason)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "空のパスは保護対象にできません。";
            return false;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (!Path.IsPathFullyQualified(expanded))
        {
            reason = $"絶対パスを指定してください: {path}";
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(expanded)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            reason = $"無効な保護フォルダです: {path}";
            return false;
        }

        var root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(root) && string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"ドライブまたは共有のルート全体は保護対象にできません: {fullPath}";
            return false;
        }

        foreach (var unsafeRoot in GetUnsafeSystemRoots())
        {
            if (IsSameOrInside(fullPath, unsafeRoot))
            {
                reason = $"Windowsやアプリの動作に影響するため、このシステム領域は保護対象にできません: {fullPath}";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static IEnumerable<string> GetUnsafeSystemRoots()
    {
        var candidates = new List<string?>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSameOrInside(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
