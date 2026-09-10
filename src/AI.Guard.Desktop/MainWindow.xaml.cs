using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.Win32;
using Rooomtech.AIGuard.Core;
using Forms = System.Windows.Forms;

namespace Rooomtech.AIGuard.Desktop;

public partial class MainWindow : Window
{
    private readonly string _baseDirectory;
    private readonly string _policyPath;
    private readonly string _auditPath;
    private GuardPolicy _policy = new();

    public MainWindow()
    {
        InitializeComponent();

        _baseDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ROOOMTECH",
            "AIGuard");
        _policyPath = Path.Combine(_baseDirectory, "policy.json");
        _auditPath = Path.Combine(_baseDirectory, "audit.jsonl");

        Loaded += async (_, _) =>
        {
            LoadPolicy();
            LoadAudit();
            await RefreshStatusAsync();
        };
    }

    private void LoadPolicy()
    {
        try
        {
            Directory.CreateDirectory(_baseDirectory);
            if (!File.Exists(_policyPath))
            {
                _policy = CreateDefaultPolicy();
                JsonPolicyStore.Save(_policyPath, _policy);
            }
            else
            {
                _policy = JsonPolicyStore.Load(_policyPath);
            }

            RefreshPolicyLists();
            FooterStatusText.Text = $"設定: {_policyPath}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"設定の読み込みに失敗しました。\n{ex.Message}", "AI Guard", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static GuardPolicy CreateDefaultPolicy() => new()
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

    private void RefreshPolicyLists()
    {
        ProtectedPathsList.ItemsSource = null;
        ProtectedPathsList.ItemsSource = _policy.ProtectedPaths;

        AllowedAppsList.ItemsSource = null;
        AllowedAppsList.ItemsSource = _policy.AllowedApplications
            .Select(a => new AllowedAppRow(a, FormatAllowedApp(a)))
            .ToList();
    }

    private static string FormatAllowedApp(AllowedApplication app)
    {
        var hash = string.IsNullOrWhiteSpace(app.Sha256)
            ? "SHA-256未固定"
            : $"SHA-256 {app.Sha256[..Math.Min(12, app.Sha256.Length)]}...";
        return $"{app.ExecutablePath}  [{hash}]";
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "AI Guardで保護するフォルダを選択してください",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;

        var fullPath = Path.GetFullPath(dialog.SelectedPath);
        if (!_policy.ProtectedPaths.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            _policy.ProtectedPaths.Add(fullPath);

        RefreshPolicyLists();
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ProtectedPathsList.SelectedItem is not string selected)
            return;

        _policy.ProtectedPaths.RemoveAll(p => string.Equals(p, selected, StringComparison.OrdinalIgnoreCase));
        RefreshPolicyLists();
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "保護ファイルへのアクセスを許可するアプリを選択",
            Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var path = Path.GetFullPath(dialog.FileName);
            using var stream = File.OpenRead(path);
            var sha256 = Convert.ToHexString(SHA256.HashData(stream));

            _policy.AllowedApplications.RemoveAll(a =>
                string.Equals(a.ExecutablePath, path, StringComparison.OrdinalIgnoreCase));
            _policy.AllowedApplications.Add(new AllowedApplication
            {
                ExecutablePath = path,
                Sha256 = sha256
            });
            RefreshPolicyLists();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"アプリ情報を取得できませんでした。\n{ex.Message}", "AI Guard", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveApp_Click(object sender, RoutedEventArgs e)
    {
        if (AllowedAppsList.SelectedItem is not AllowedAppRow selected)
            return;

        _policy.AllowedApplications.Remove(selected.Application);
        RefreshPolicyLists();
    }

    private void SavePolicy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            JsonPolicyStore.Save(_policyPath, _policy);
            FooterStatusText.Text = $"保存しました: {DateTime.Now:yyyy/MM/dd HH:mm:ss}";
            MessageBox.Show(
                "設定を保存しました。Kernel Driverが導入済みの場合、Driver側へのポリシー反映機構が有効な製品ビルドで保護設定が適用されます。",
                "AI Guard",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存に失敗しました。\n{ex.Message}", "AI Guard", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ReloadPolicy_Click(object sender, RoutedEventArgs e) => LoadPolicy();

    private void LoadAudit()
    {
        try
        {
            if (!File.Exists(_auditPath))
            {
                AuditList.ItemsSource = new[] { "監査ログはまだありません。" };
                return;
            }

            var lines = File.ReadLines(_auditPath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .TakeLast(300)
                .Reverse()
                .ToList();
            AuditList.ItemsSource = lines;
        }
        catch (Exception ex)
        {
            AuditList.ItemsSource = new[] { $"監査ログを読み込めません: {ex.Message}" };
        }
    }

    private void ReloadAudit_Click(object sender, RoutedEventArgs e) => LoadAudit();

    private void OpenAuditFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_baseDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", _baseDirectory) { UseShellExecute = true });
    }

    private async void RefreshStatus_Click(object sender, RoutedEventArgs e) => await RefreshStatusAsync();

    private async Task RefreshStatusAsync()
    {
        DriverStatusText.Text = await Task.Run(GetDriverStatus);
        AgentStatusText.Text = await Task.Run(GetAgentStatus);
    }

    private static string GetDriverStatus()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "query AIGuardFilter",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return "未確認";

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            if (process.ExitCode != 0)
                return "未導入（Kernel保護は無効）";
            if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                return "稼働中";
            return "導入済み・停止中";
        }
        catch
        {
            return "確認失敗";
        }
    }

    private static string GetAgentStatus()
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", "ROOOMTECH_AIGuard", PipeDirection.InOut);
            pipe.Connect(250);
            return pipe.IsConnected ? "稼働中" : "停止中";
        }
        catch
        {
            return "停止中";
        }
    }

    private sealed record AllowedAppRow(AllowedApplication Application, string Display);
}
