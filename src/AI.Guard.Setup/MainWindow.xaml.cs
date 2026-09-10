using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Rooomtech.AIGuard.Core;

namespace Rooomtech.AIGuard.Setup;

public partial class MainWindow : Window
{
    private string? _validatedBusinessLicensePath;

    public MainWindow()
    {
        InitializeComponent();
        UpdateFormState();
    }

    private bool IsBusiness => BusinessRadio.IsChecked == true;

    private void UsageChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        BusinessLicensePanel.IsEnabled = IsBusiness;
        BusinessLicensePanel.Opacity = IsBusiness ? 1.0 : 0.55;
        if (!IsBusiness)
        {
            _validatedBusinessLicensePath = null;
            LicenseValidationText.Text = "法人利用ではROOOMTECH株式会社発行のライセンスが必要です。";
        }
        UpdateFormState();
    }

    private void FormChanged(object sender, RoutedEventArgs e) => UpdateFormState();

    private void BrowseLicense_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "ROOOMTECH AI Guard Businessライセンスを選択",
            Filter = "AI Guard license (*.json)|*.json|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        LicensePathText.Text = dialog.FileName;
        ValidateBusinessLicense(dialog.FileName);
        UpdateFormState();
    }

    private void ValidateBusinessLicense(string path)
    {
        try
        {
            var license = ProductLicensing.LoadLicense(path);
            var result = ProductLicensing.ValidateBusinessLicense(license);
            if (result.IsValid && result.CommercialUseAllowed)
            {
                _validatedBusinessLicensePath = Path.GetFullPath(path);
                LicenseValidationText.Text = result.Status;
            }
            else
            {
                _validatedBusinessLicensePath = null;
                LicenseValidationText.Text = "利用不可: " + result.Status;
            }
        }
        catch (Exception ex)
        {
            _validatedBusinessLicensePath = null;
            LicenseValidationText.Text = "利用不可: " + ex.Message;
        }
    }

    private void UpdateFormState()
    {
        if (InstallButton is null || AcceptLicenseCheck is null)
            return;

        var licenseReady = !IsBusiness || !string.IsNullOrWhiteSpace(_validatedBusinessLicensePath);
        InstallButton.IsEnabled = AcceptLicenseCheck.IsChecked == true && licenseReady;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        PersonalRadio.IsEnabled = false;
        BusinessRadio.IsEnabled = false;
        AcceptLicenseCheck.IsEnabled = false;
        StatusText.Text = "インストールしています...";

        try
        {
            var packageRoot = ResolvePackageRoot();
            var installScript = Path.Combine(packageRoot, "scripts", "install.ps1");
            if (!File.Exists(installScript))
                throw new FileNotFoundException("インストールスクリプトが見つかりません。ZIPをすべて展開してからSetupを実行してください。", installScript);

            if (IsBusiness && string.IsNullOrWhiteSpace(_validatedBusinessLicensePath))
                throw new InvalidOperationException("有効なBusinessライセンスを選択してください。");

            var result = await Task.Run(() => RunInstaller(installScript, packageRoot));
            if (result.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);

            StatusText.Text = "インストールが完了しました。ROOOMTECH AI Guardを起動できます。";
            MessageBox.Show(
                "ROOOMTECH AI Guard のインストールが完了しました。",
                "ROOOMTECH AI Guard Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            var desktopExe = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "ROOOMTECH",
                "AI Guard",
                "Desktop",
                "AIGuard.Desktop.exe");
            if (File.Exists(desktopExe))
            {
                Process.Start(new ProcessStartInfo(desktopExe) { UseShellExecute = true });
            }

            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = "インストールに失敗しました: " + ex.Message;
            MessageBox.Show(
                ex.Message,
                "ROOOMTECH AI Guard Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            PersonalRadio.IsEnabled = true;
            BusinessRadio.IsEnabled = true;
            AcceptLicenseCheck.IsEnabled = true;
            UpdateFormState();
        }
    }

    private (int ExitCode, string Output, string Error) RunInstaller(string installScript, string packageRoot)
    {
        var info = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(installScript);
        info.ArgumentList.Add("-PackageRoot");
        info.ArgumentList.Add(packageRoot);
        info.ArgumentList.Add("-Usage");
        info.ArgumentList.Add(IsBusiness ? "Business" : "Personal");
        info.ArgumentList.Add("-AcceptLicense");

        if (IsBusiness)
        {
            info.ArgumentList.Add("-LicensePath");
            info.ArgumentList.Add(_validatedBusinessLicensePath!);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("インストーラーを開始できませんでした。");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Trim(), error.Trim());
    }

    private static string ResolvePackageRoot()
    {
        var setupDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var parent = Directory.GetParent(setupDirectory.TrimEnd(Path.DirectorySeparatorChar));
        if (parent is null)
            throw new InvalidOperationException("配布パッケージのルートを特定できません。");
        return parent.FullName;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
