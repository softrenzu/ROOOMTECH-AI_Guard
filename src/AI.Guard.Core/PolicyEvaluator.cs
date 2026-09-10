namespace Rooomtech.AIGuard.Core;

public sealed class PolicyEvaluator
{
    private readonly GuardPolicy _policy;

    public PolicyEvaluator(GuardPolicy policy)
    {
        _policy = policy;
    }

    public AccessDecision Evaluate(AccessRequest request)
    {
        var filePath = NormalizePath(request.FilePath);
        var protectedRoot = _policy.ProtectedPaths
            .Select(NormalizePath)
            .FirstOrDefault(root => IsPathInside(filePath, root));

        if (protectedRoot is null)
        {
            return new AccessDecision
            {
                IsProtected = false,
                Allowed = true,
                Reason = "File is outside protected paths."
            };
        }

        var processPath = NormalizePath(request.ProcessPath);
        var candidate = _policy.AllowedApplications.FirstOrDefault(app =>
            string.Equals(NormalizePath(app.ExecutablePath), processPath, StringComparison.OrdinalIgnoreCase));

        if (candidate is null)
        {
            return new AccessDecision
            {
                IsProtected = true,
                Allowed = !_policy.DenyByDefault,
                Reason = _policy.DenyByDefault
                    ? "Protected path: process is not on the allow list."
                    : "Protected path: process is not listed, but deny-by-default is disabled."
            };
        }

        if (!string.IsNullOrWhiteSpace(candidate.Sha256))
        {
            if (string.IsNullOrWhiteSpace(request.ProcessSha256) ||
                !string.Equals(candidate.Sha256, request.ProcessSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new AccessDecision
                {
                    IsProtected = true,
                    Allowed = false,
                    Reason = "Executable path matched, but SHA-256 did not match."
                };
            }
        }

        return new AccessDecision
        {
            IsProtected = true,
            Allowed = true,
            Reason = "Protected path: process matched the allow list."
        };
    }

    private static string NormalizePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));

        try
        {
            return Path.GetFullPath(expanded)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return expanded
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .TrimEnd(Path.DirectorySeparatorChar);
        }
    }

    private static bool IsPathInside(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            return false;

        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
