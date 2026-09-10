using System.IO.Pipes;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using Rooomtech.AIGuard.Agent;
using Rooomtech.AIGuard.Core;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
};

var policyPath = ResolvePolicyPath();
EnsureInitialPolicy(policyPath);
var auditPath = ResolveAuditPath();

if (args.Length == 0 || string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase))
{
    PrintHelp();
    return;
}

switch (args[0].ToLowerInvariant())
{
    case "check":
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: AIGuard check <file-path> <process-path> [operation]");
            Environment.ExitCode = 2;
            return;
        }

        var policy = JsonPolicyStore.Load(policyPath);
        var evaluator = new PolicyEvaluator(policy);
        var checkRequest = BuildRequest(policy, args[1], args[2], args.Length >= 4 ? args[3] : "read");
        var decision = evaluator.Evaluate(checkRequest);
        AppendAudit(auditPath, new AuditRecord { Request = checkRequest, Decision = decision }, jsonOptions);
        Console.WriteLine(JsonSerializer.Serialize(decision, jsonOptions));
        Environment.ExitCode = decision.Allowed ? 0 : 10;
        return;
    }

    case "sync-driver":
    {
        var policy = JsonPolicyStore.Load(policyPath);
        var ok = DriverPolicyBridge.TryPushPolicy(policy, out var message);
        Console.WriteLine(message);
        Environment.ExitCode = ok ? 0 : 20;
        return;
    }

    case "service":
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Windows Service mode is only supported on Windows.");
            Environment.ExitCode = 2;
            return;
        }

        ServiceBase.Run(new AiGuardWindowsService());
        return;
    }

    case "serve":
    {
        Console.WriteLine("ROOOMTECH AI Guard agent");
        Console.WriteLine($"Policy: {policyPath}");
        Console.WriteLine($"Audit : {auditPath}");
        Console.WriteLine("Pipe  : ROOOMTECH_AIGuard");

        var initialPolicy = JsonPolicyStore.Load(policyPath);
        if (DriverPolicyBridge.TryPushPolicy(initialPolicy, out var driverMessage))
            Console.WriteLine(driverMessage);
        else
            Console.WriteLine($"Driver sync pending: {driverMessage}");

        await RunServerAsync(policyPath, auditPath, jsonOptions);
        return;
    }

    default:
        Console.Error.WriteLine($"Unknown command: {args[0]}");
        PrintHelp();
        Environment.ExitCode = 2;
        return;
}

static AccessRequest BuildRequest(GuardPolicy policy, string filePath, string processPath, string operation)
{
    string? sha256 = null;
    var fullProcessPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(processPath.Trim().Trim('"')));

    var requiresHash = policy.AllowedApplications.Any(app =>
        string.Equals(
            Path.GetFullPath(Environment.ExpandEnvironmentVariables(app.ExecutablePath.Trim().Trim('"'))),
            fullProcessPath,
            StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(app.Sha256));

    if (requiresHash && File.Exists(fullProcessPath))
    {
        using var stream = File.OpenRead(fullProcessPath);
        sha256 = Convert.ToHexString(SHA256.HashData(stream));
    }

    return new AccessRequest
    {
        FilePath = filePath,
        ProcessPath = fullProcessPath,
        Operation = operation,
        ProcessSha256 = sha256
    };
}

static async Task RunServerAsync(
    string policyPath,
    string auditPath,
    JsonSerializerOptions jsonOptions)
{
    while (true)
    {
        await using var pipe = new NamedPipeServerStream(
            "ROOOMTECH_AIGuard",
            PipeDirection.InOut,
            8,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        await pipe.WaitForConnectionAsync();

        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        var line = await reader.ReadLineAsync();
        if (string.IsNullOrWhiteSpace(line))
            continue;

        try
        {
            var policy = JsonPolicyStore.Load(policyPath);
            var evaluator = new PolicyEvaluator(policy);
            var request = JsonSerializer.Deserialize<AccessRequest>(line, jsonOptions)
                          ?? throw new InvalidDataException("Request was empty.");

            var normalized = BuildRequest(policy, request.FilePath, request.ProcessPath, request.Operation);
            normalized = normalized with { ProcessId = request.ProcessId };

            var decision = evaluator.Evaluate(normalized);
            AppendAudit(auditPath, new AuditRecord { Request = normalized, Decision = decision }, jsonOptions);
            await writer.WriteLineAsync(JsonSerializer.Serialize(decision, jsonOptions));
        }
        catch (Exception ex)
        {
            await writer.WriteLineAsync(JsonSerializer.Serialize(new
            {
                allowed = false,
                reason = "Agent error: " + ex.Message
            }, jsonOptions));
        }
    }
}

static void AppendAudit(string path, AuditRecord record, JsonSerializerOptions options)
{
    var directory = Path.GetDirectoryName(path);
    if (!string.IsNullOrWhiteSpace(directory))
        Directory.CreateDirectory(directory);

    var compact = new JsonSerializerOptions(options) { WriteIndented = false };
    File.AppendAllText(path, JsonSerializer.Serialize(record, compact) + Environment.NewLine, Encoding.UTF8);
}

static string ResolvePolicyPath()
{
    var fromEnv = Environment.GetEnvironmentVariable("AIGUARD_POLICY");
    if (!string.IsNullOrWhiteSpace(fromEnv))
        return Path.GetFullPath(fromEnv);

    var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    if (string.IsNullOrWhiteSpace(baseDirectory))
        baseDirectory = AppContext.BaseDirectory;

    return Path.Combine(baseDirectory, "ROOOMTECH", "AIGuard", "policy.json");
}

static string ResolveAuditPath()
{
    var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    if (string.IsNullOrWhiteSpace(baseDirectory))
        baseDirectory = AppContext.BaseDirectory;

    return Path.Combine(baseDirectory, "ROOOMTECH", "AIGuard", "audit.jsonl");
}

static void EnsureInitialPolicy(string policyPath)
{
    if (File.Exists(policyPath))
        return;

    var defaultPolicy = new GuardPolicy
    {
        ProtectedPaths = [@"C:\AI_Guard_Protected"],
        AllowedApplications =
        [
            new AllowedApplication { ExecutablePath = @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE" },
            new AllowedApplication { ExecutablePath = @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE" },
            new AllowedApplication { ExecutablePath = @"C:\Program Files\Microsoft Office\root\Office16\POWERPNT.EXE" }
        ],
        DenyByDefault = true
    };

    JsonPolicyStore.Save(policyPath, defaultPolicy);
    Console.WriteLine($"Created initial policy: {policyPath}");
}

static void PrintHelp()
{
    Console.WriteLine("""
ROOOMTECH AI Guard

Commands:
  AIGuard check <file-path> <process-path> [operation]
      Evaluate one access request and write an audit record.

  AIGuard sync-driver
      Load the installed minifilter if needed and push the current policy.

  AIGuard serve
      Start the local named-pipe policy agent and synchronize the driver.

  AIGuard service
      Run under the Windows Service Control Manager.

Environment:
  AIGUARD_POLICY
      Optional path to policy.json.

Exit codes:
  0   Allowed / success
  10  Denied
  20  Driver synchronization failed
  2   Invalid command
""");
}
