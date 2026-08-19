namespace Logaffe.UnitTests.Client;

/// <summary>
/// The tests that turn Serilog's <c>SelfLog</c> on.
/// </summary>
/// <remarks>
/// It is one global writer for the whole process, so two of these running beside
/// each other would collect each other's reports and disable it underneath one
/// another. A collection is what makes them run one at a time — they are quick,
/// and nothing else in this project touches it.
/// </remarks>
[CollectionDefinition(nameof(SelfLogCollection))]
public sealed class SelfLogCollection;
