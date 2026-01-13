using System.IO;
using System.Text;
using System.Windows.Threading;

namespace PcTest.Ui.Services;

/// <summary>
/// Service for tailing log files in real-time with throttled UI updates.
/// Reads new content incrementally from stdout.log/stderr.log during test execution.
/// </summary>
public sealed class LogTailService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(200);
    private readonly TimeSpan _uiThrottleInterval = TimeSpan.FromMilliseconds(100);

    private CancellationTokenSource? _tailCts;
    private Task? _tailTask;
    private bool _disposed;

    // Current tail state
    private string? _stdoutPath;
    private string? _stderrPath;
    private long _stdoutOffset;
    private long _stderrOffset;

    // Throttling buffer
    private readonly StringBuilder _pendingOutput = new();
    private readonly object _bufferLock = new();
    private DateTime _lastUiUpdate = DateTime.MinValue;

    /// <summary>
    /// Event fired when new console content is available.
    /// Content is incremental (new text only).
    /// </summary>
    public event EventHandler<string>? ContentReceived;

    public LogTailService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Starts tailing the stdout.log and stderr.log files in the specified run folder.
    /// </summary>
    public void StartTailing(string runFolder)
    {
        StopTailing();

        _stdoutPath = Path.Combine(runFolder, "stdout.log");
        _stderrPath = Path.Combine(runFolder, "stderr.log");
        _stdoutOffset = 0;
        _stderrOffset = 0;

        _tailCts = new CancellationTokenSource();
        _tailTask = Task.Run(() => TailLoopAsync(_tailCts.Token));
    }

    /// <summary>
    /// Stops tailing and performs a final read to catch any remaining content.
    /// </summary>
    public async Task StopTailingAsync()
    {
        if (_tailCts is null) return;

        _tailCts.Cancel();

        if (_tailTask is not null)
        {
            try
            {
                await _tailTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch
            {
                // Ignore errors on shutdown
            }
        }

        // Final read to catch any remaining content
        _stdoutOffset = await ReadAndEmitAsync(_stdoutPath, _stdoutOffset);
        _stderrOffset = await ReadAndEmitAsync(_stderrPath, _stderrOffset);

        // Flush any pending buffered content
        FlushPendingOutput(force: true);

        _tailCts.Dispose();
        _tailCts = null;
        _tailTask = null;
    }

    /// <summary>
    /// Stops tailing synchronously. Use StopTailingAsync when possible.
    /// </summary>
    public void StopTailing()
    {
        if (_tailCts is null) return;

        _tailCts.Cancel();
        _tailCts.Dispose();
        _tailCts = null;
        _tailTask = null;
    }

    private async Task TailLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Read new content from both files
                _stdoutOffset = await ReadAndEmitAsync(_stdoutPath, _stdoutOffset);
                _stderrOffset = await ReadAndEmitAsync(_stderrPath, _stderrOffset);

                // Throttled flush to UI
                FlushPendingOutput(force: false);

                // Wait before next poll
                await Task.Delay(_pollInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue polling even if one read fails
            }
        }
    }

    private async Task<long> ReadAndEmitAsync(string? filePath, long offset)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return offset;

        try
        {
            // Open with FileShare.ReadWrite to allow Runner to continue writing
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            var fileLength = stream.Length;
            if (fileLength <= offset)
                return offset; // No new content

            stream.Seek(offset, SeekOrigin.Begin);

            var bytesToRead = (int)(fileLength - offset);
            var buffer = new byte[bytesToRead];
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead));

            if (bytesRead > 0)
            {
                var newOffset = offset + bytesRead;
                var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                lock (_bufferLock)
                {
                    _pendingOutput.Append(text);
                }
                
                return newOffset;
            }
            
            return offset;
        }
        catch (IOException)
        {
            // File may be locked or not exist yet - retry on next poll
            return offset;
        }
        catch (UnauthorizedAccessException)
        {
            // Permission issue - retry on next poll
            return offset;
        }
    }

    private void FlushPendingOutput(bool force)
    {
        string? textToEmit = null;

        lock (_bufferLock)
        {
            if (_pendingOutput.Length == 0)
                return;

            var now = DateTime.Now;
            var timeSinceLastUpdate = now - _lastUiUpdate;

            // Only emit if forced or enough time has passed (throttling)
            if (force || timeSinceLastUpdate >= _uiThrottleInterval)
            {
                textToEmit = _pendingOutput.ToString();
                _pendingOutput.Clear();
                _lastUiUpdate = now;
            }
        }

        if (textToEmit is not null)
        {
            // Invoke on UI thread via dispatcher
            _dispatcher.BeginInvoke(() =>
            {
                ContentReceived?.Invoke(this, textToEmit);
            }, DispatcherPriority.Background);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopTailing();
    }
}
