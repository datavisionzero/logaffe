using Logaffe.Application.Operations;
using Logaffe.Application.Ports;
using Logaffe.Domain.History;

namespace Logaffe.IntegrationTests;

/// <summary>
/// The recorder handed to acts in tests that are about something other than the
/// history.
/// </summary>
/// <remarks>
/// Nothing is admitted to it, and an act performed with nobody behind it records
/// nothing (<c>RecordAChange</c>) — so these tests neither write history rows nor
/// need a table to write them into. What the history itself does is
/// <c>HistoryActsTests</c> in the unit tests and <c>HistorySchemaTests</c> here.
/// </remarks>
internal static class Recording
{
    public static RecordAChange Nobody() =>
        new(new NothingIsWrittenDown(), new TheActor(), TimeProvider.System);

    private sealed class NothingIsWrittenDown : IHistory
    {
        public Task RecordAsync(Change change, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "An act performed by nobody recorded a change.");

        public Task<IReadOnlyList<Change>> ListAsync(
            long? before, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
