namespace AZOTH.Core.Files;

/// <summary>Register one shared instance for ECHO cleanup and every ORDO move/undo operation.
/// The lease covers the whole validation/mutation operation. It does not lock external applications.</summary>
public sealed class FileMutationCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(_gate);
    }
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
