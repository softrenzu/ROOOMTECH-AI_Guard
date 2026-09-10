using System.Diagnostics;
using System.ServiceProcess;

namespace Rooomtech.AIGuard.Agent;

internal sealed class AiGuardWindowsService : ServiceBase
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _worker;
    private Process? _child;

    public AiGuardWindowsService()
    {
        ServiceName = "AIGuardAgent";
        CanStop = true;
        CanShutdown = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        _worker = Task.Run(() => RunAgentLoopAsync(_stopping.Token));
    }

    protected override void OnStop()
    {
        StopAgent();
    }

    protected override void OnShutdown()
    {
        StopAgent();
        base.OnShutdown();
    }

    private async Task RunAgentLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
                return;

            using var child = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };
            child.StartInfo.ArgumentList.Add("serve");
            child.OutputDataReceived += (_, _) => { };
            child.ErrorDataReceived += (_, _) => { };

            lock (this)
                _child = child;

            try
            {
                if (!child.Start())
                    return;

                child.BeginOutputReadLine();
                child.BeginErrorReadLine();
                await child.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryKill(child);
                return;
            }
            catch
            {
                TryKill(child);
            }
            finally
            {
                lock (this)
                {
                    if (ReferenceEquals(_child, child))
                        _child = null;
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void StopAgent()
    {
        if (!_stopping.IsCancellationRequested)
            _stopping.Cancel();

        Process? child;
        lock (this)
            child = _child;

        if (child is not null)
            TryKill(child);

        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(10));
        }
        catch
        {
            // Service stop must remain best-effort and bounded.
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process may already have exited.
        }
    }
}
