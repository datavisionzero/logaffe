namespace Logaffe.UnitTests.Client;

/// <summary>
/// The tests that read what the process writes to a channel it has only one of:
/// Serilog's <c>SelfLog</c>, and standard error.
/// </summary>
/// <remarks>
/// Two of these running beside each other collect each other's output and put it
/// back underneath one another — which is what a green run on one machine and a
/// red one on the runner looked like the first time. A collection is what makes
/// them run one at a time. They are quick, and nothing else in this project
/// writes there.
/// </remarks>
[CollectionDefinition(nameof(ProcessOutputCollection))]
public sealed class ProcessOutputCollection;
