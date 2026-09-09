using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Channels;
using TridentCore.Core.Engines.Launching;
using TridentCore.Core.Utilities;

namespace TridentCore.Core.Engines;

public sealed class LaunchEngine : IAsyncDisposable
{
    private readonly Process _process;
    private readonly bool _managed;
    private readonly Channel<string> _output = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false
    });
    private int _readersRemaining = 2;
    private bool _started;
    private bool _detached;
    private bool _disposed;

    public LaunchEngine(ProcessStartInfo startInfo, bool managed = true)
    {
        _managed = managed;
        _process = new Process { StartInfo = startInfo };
        if (managed)
        {
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            _process.OutputDataReceived += OnOutput;
            _process.ErrorDataReceived += OnOutput;
        }
    }

    public (int ProcessId, DateTimeOffset StartedAt) Start()
    {
        if (_started)
        {
            throw new InvalidOperationException("The game process has already started");
        }

        if (!_process.Start())
        {
            throw new InvalidOperationException("The game process could not be started");
        }

        _started = true;
        _detached = !_managed;
        var started = (_process.Id, DateTimeOffset.Now);
        if (_managed)
        {
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        return started;
    }

    public async Task<Result> WaitAsync(
        Action<Scrap> report,
        Func<bool> preserveProcess,
        CancellationToken cancellationToken = default)
    {
        if (!_started)
        {
            throw new InvalidOperationException("The game process has not started");
        }

        if (!_managed)
        {
            return new(LaunchOutcome.Detached, null);
        }

        try
        {
            await foreach (var line in _output.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                report(ScrapHelper.Parse(line));
            }

            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!_process.HasExited)
            {
                if (preserveProcess())
                {
                    _detached = true;
                    return new(LaunchOutcome.Detached, null);
                }

                await TerminateAsync().ConfigureAwait(false);
                return new(LaunchOutcome.Aborted, _process.ExitCode);
            }
        }

        var exitCode = _process.ExitCode;
        return new(exitCode == 0 ? LaunchOutcome.Exited : LaunchOutcome.Crashed, exitCode);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_started && !_detached && !_process.HasExited)
            {
                await TerminateAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _process.OutputDataReceived -= OnOutput;
            _process.ErrorDataReceived -= OnOutput;
            _output.Writer.TryComplete();
            _process.Dispose();
        }
    }

    private async Task TerminateAsync()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when ((ex is InvalidOperationException or Win32Exception) && _process.HasExited)
        {
        }

        await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is { } line)
        {
            if (line.Length > 0)
            {
                _output.Writer.TryWrite(line);
            }
        }
        else if (Interlocked.Decrement(ref _readersRemaining) == 0)
        {
            // NOTE: Process exit can precede the final output callbacks; close only after both pipes reach EOF.
            _output.Writer.TryComplete();
        }
    }

    public readonly record struct Result(LaunchOutcome Outcome, int? ExitCode);
}
