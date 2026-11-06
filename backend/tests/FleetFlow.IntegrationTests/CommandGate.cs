using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FleetFlow.IntegrationTests;

// Ensures both competing operations reach the relevant database boundary before either continues.
internal sealed class CommandGate : DbCommandInterceptor
{
    private TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;
    public string? Match { get; set; }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Match is { } match && command.CommandText.Contains(match, StringComparison.Ordinal))
        {
            var count = Interlocked.Increment(ref arrivals);
            if (count == 2) release.TrySetResult();
            if (count <= 2) await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }
        return result;
    }
}
