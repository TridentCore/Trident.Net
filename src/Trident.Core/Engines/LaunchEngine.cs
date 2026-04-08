using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Trident.Core.Engines.Launching;
using Trident.Core.Utilities;

namespace Trident.Core.Engines;

public partial class LaunchEngine : IAsyncEnumerable<Scrap>
{
    private readonly Process _inner;

    public LaunchEngine(Process inner)
    {
        _inner = inner;
        _inner.StartInfo.RedirectStandardError = true;
        _inner.StartInfo.RedirectStandardOutput = true;
        _inner.EnableRaisingEvents = true;
    }

    #region IAsyncEnumerable<Scrap> Members

    public IAsyncEnumerator<Scrap> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(_inner);
        return new LaunchEngineEnumerator(_inner, cancellationToken);
    }

    #endregion

    #region Nested type: LaunchEngineEnumerator

    public partial class LaunchEngineEnumerator : IAsyncEnumerator<Scrap>
    {
        private readonly CancellationToken _cancellationToken;
        private readonly Channel<Scrap> _channel = Channel.CreateUnbounded<Scrap>();
        private readonly Process _inner;

        internal LaunchEngineEnumerator(Process process, CancellationToken token = default)
        {
            _cancellationToken = token;
            _inner = process;
            process.OutputDataReceived += ProcessOnOutputDataReceived;
            process.ErrorDataReceived += ProcessOnErrorDataReceived;
            process.Exited += ProcessOnExited;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        #region IAsyncEnumerator<Scrap> Members

        public Scrap Current { get; private set; } = null!;

        public ValueTask DisposeAsync()
        {
            _channel.Writer.TryComplete();
            _inner.CancelErrorRead();
            _inner.CancelOutputRead();

            _inner.EnableRaisingEvents = false;
            _inner.OutputDataReceived -= ProcessOnOutputDataReceived;
            _inner.ErrorDataReceived -= ProcessOnErrorDataReceived;
            _inner.Exited -= ProcessOnExited;

            // inner.Close()
            // it throws exception for some reason
            return ValueTask.CompletedTask;
        }

        public async ValueTask<bool> MoveNextAsync()
        {
            try
            {
                _cancellationToken.ThrowIfCancellationRequested();

                if (await _channel.Reader.WaitToReadAsync(_cancellationToken).ConfigureAwait(false))
                {
                    if (_channel.Reader.TryRead(out var piece))
                    {
                        Current = piece;
                        return true;
                    }
                }

                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        #endregion

        private void ProcessOnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _channel.Writer.TryWrite(ScrapHelper.Parse(e.Data));
            }
        }

        private void ProcessOnErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _channel.Writer.TryWrite(ScrapHelper.Parse(e.Data));
            }
        }

        private void ProcessOnExited(object? sender, EventArgs e) => _channel.Writer.TryComplete();

    #endregion
    }
}
