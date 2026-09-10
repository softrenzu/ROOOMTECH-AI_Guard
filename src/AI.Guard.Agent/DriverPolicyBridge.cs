using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Rooomtech.AIGuard.Core;

namespace Rooomtech.AIGuard.Agent;

internal static class DriverPolicyBridge
{
    private const string PortName = @"\ROOOMTECHAIGuardPort";
    private const string FilterName = "AIGuardFilter";
    private const int MaxProtectedPaths = PolicySafety.MaxProtectedPaths;
    private const int MaxAllowedApps = PolicySafety.MaxAllowedApplications;
    private const int PathChars = 520;
    private const uint PolicyVersion = 1;
    private const int DriverConnectAttempts = 10;
    private const int DriverConnectDelayMs = 250;

    public static bool TryPushPolicy(GuardPolicy policy, out string message)
    {
        try
        {
            PolicySafety.ValidateForEnforcement(policy);

            var protectedPaths = policy.ProtectedPaths.Select(ToNtPath).ToArray();
            var allowedApps = policy.AllowedApplications.Select(ToVerifiedNtPath).ToArray();
            var payload = BuildPayload(protectedPaths, allowedApps);

            var hr = FilterConnectCommunicationPort(PortName, 0, IntPtr.Zero, 0, IntPtr.Zero, out var port);
            if (hr != 0)
            {
                // The minifilter is demand-start. On boot the Windows service can run before
                // the filter has been loaded. Ask Filter Manager to load it, then tolerate a
                // short startup window before declaring synchronization unavailable.
                _ = FilterLoad(FilterName);

                for (var attempt = 0; attempt < DriverConnectAttempts && hr != 0; attempt++)
                {
                    Thread.Sleep(DriverConnectDelayMs);
                    hr = FilterConnectCommunicationPort(PortName, 0, IntPtr.Zero, 0, IntPtr.Zero, out port);
                }
            }

            if (hr != 0)
            {
                message = $"Kernel Driverに接続できません (HRESULT 0x{hr:X8})";
                return false;
            }

            try
            {
                hr = FilterSendMessage(port, payload, (uint)payload.Length, IntPtr.Zero, 0, out _);
                if (hr != 0)
                {
                    message = $"Kernel Driverへのポリシー送信に失敗しました (HRESULT 0x{hr:X8})";
                    return false;
                }
            }
            finally
            {
                CloseHandle(port);
            }

            message = $"Kernel Driverへ反映済み（保護{protectedPaths.Length}件 / 許可{allowedApps.Length}件）";
            return true;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return false;
        }
    }

    private static string ToVerifiedNtPath(AllowedApplication app)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(app.ExecutablePath.Trim().Trim('"')));
        if (!string.IsNullOrWhiteSpace(app.Sha256))
        {
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("SHA-256固定済みの許可アプリが見つかりません。", fullPath);

            using var stream = File.OpenRead(fullPath);
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!string.Equals(actual, app.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"許可アプリのSHA-256が変更されています。再登録してください: {fullPath}");
        }

        return ToNtPath(fullPath);
    }

    private static byte[] BuildPayload(IReadOnlyList<string> protectedPaths, IReadOnlyList<string> allowedApps)
    {
        const int headerBytes = 12;
        var slotBytes = PathChars * sizeof(char);
        var totalBytes = headerBytes + ((MaxProtectedPaths + MaxAllowedApps) * slotBytes);
        var buffer = new byte[totalBytes];

        BitConverter.GetBytes(PolicyVersion).CopyTo(buffer, 0);
        BitConverter.GetBytes((uint)protectedPaths.Count).CopyTo(buffer, 4);
        BitConverter.GetBytes((uint)allowedApps.Count).CopyTo(buffer, 8);

        var offset = headerBytes;
        for (var i = 0; i < MaxProtectedPaths; i++, offset += slotBytes)
            WriteFixedUnicode(buffer, offset, i < protectedPaths.Count ? protectedPaths[i] : string.Empty);
        for (var i = 0; i < MaxAllowedApps; i++, offset += slotBytes)
            WriteFixedUnicode(buffer, offset, i < allowedApps.Count ? allowedApps[i] : string.Empty);

        return buffer;
    }

    private static void WriteFixedUnicode(byte[] buffer, int offset, string value)
    {
        if (value.Length >= PathChars)
            throw new InvalidOperationException($"パスが長すぎます: {value}");

        var bytes = Encoding.Unicode.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
    }

    private static string ToNtPath(string path)
    {
        var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));

        if (full.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\Device\Mup" + full[1..];

        if (full.Length < 2 || full[1] != ':')
            return full;

        var drive = full[..2];
        var target = new StringBuilder(32768);
        if (QueryDosDevice(drive, target, target.Capacity) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"ドライブをNTパスへ変換できません: {drive}");

        var device = target.ToString();
        var nullIndex = device.IndexOf('\0');
        if (nullIndex >= 0)
            device = device[..nullIndex];

        return device.TrimEnd('\\') + full[2..];
    }

    [DllImport("fltlib.dll", CharSet = CharSet.Unicode)]
    private static extern int FilterConnectCommunicationPort(
        string lpPortName,
        uint dwOptions,
        IntPtr lpContext,
        ushort wSizeOfContext,
        IntPtr lpSecurityAttributes,
        out IntPtr hPort);

    [DllImport("fltlib.dll", CharSet = CharSet.Unicode)]
    private static extern int FilterLoad(string lpFilterName);

    [DllImport("fltlib.dll")]
    private static extern int FilterSendMessage(
        IntPtr hPort,
        byte[] lpInBuffer,
        uint dwInBufferSize,
        IntPtr lpOutBuffer,
        uint dwOutBufferSize,
        out uint lpBytesReturned);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);
}
