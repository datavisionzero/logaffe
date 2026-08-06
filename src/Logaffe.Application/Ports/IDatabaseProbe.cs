namespace Logaffe.Application.Ports;

/// <summary>
/// What the application needs to know about the database in order to answer
/// whether the installation can serve. It answers two questions and draws no
/// conclusion from them — the conclusion is
/// <see cref="Operations.CheckReadiness"/>.
/// </summary>
public interface IDatabaseProbe
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);

    Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken);
}
