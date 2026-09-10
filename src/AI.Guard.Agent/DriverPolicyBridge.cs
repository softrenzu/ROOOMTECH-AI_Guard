using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Rooomtech.AIGuard.Core;

namespace Rooomtech.AIGuard.Agent;

internal static class DriverPolicyBridge
{
    private const string PortName = @"\ROOOMTECHAIGuardPort";
    private const int MaxProtectedPaths = 8;
    private const int MaxAllowedApps = 32;
    private const int PathChars = 520;
    private const uint PolicyVersion = 1;

    public static bool TryPushPolicy(GuardPolicy policy, out string message)
    {
        try
        {
            if (policy.ProtectedPaths.Count > MaxProtectedPaths)
                throw new InvalidOperationException($"保護フォルダは最大{MaxProtectedPaths}件です。");
            if (policy.AllowedApplications.Count > MaxAllowedApps)
                throw new InvalidOperationException($"許可アプリは最大{MaxAllowedApps}件です。");

            var protectedPaths = policy.ProtectedPaths.Select(ToNtPath).ToArray();
            var allowedApps = policy.AllowedApplications.Select(a => ToNtPath(a.ExecutablePath)).ToArray();
            var payload = BuildPayload(protectedPaths, allowedApps);

            var hr = FilterConnectCommunicationPort(PortName, 0, IntPtr.Zero, 0, IntPtr.Zero, out var port);
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
        if (full.Length < 2 || full[1] != ':')
            return full;

        var drive = full[..2];
        var target = new StringBuilder(32768);
        if (QueryDosDevice(drive, target, target.Capacity) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"ドライブをNTパスへ変換できません: {drive}");

        var device = target.ToString().Split('\0', StringSplitOptions.RemoveEmptyEntries)[0];
        return device.TrimEnd('\') + full[2..];
    }

    [DllImport("fltlib.dll", CharSet = CharSet.Unicode)]
    private static extern int FilterConnectCommunicationPort(
        string lpPortName,
        uint dwOptions,
        IntPtr lpContext,
        ushort wSizeOfContext,
        IntPtr lpSecurityAttributes,
        out IntPtr hPort);

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
