using Logaffe.Api.Cli;

namespace Logaffe.IntegrationTests;

/// <summary>
/// One binary is the server and the command line both, so what separates the two
/// is load-bearing: a mistake read as "no verb given" starts a server that
/// reports itself healthy while the command has not run.
/// </summary>
/// <remarks>
/// Here rather than in the unit tests because this is <c>Logaffe.Api</c>, which
/// only this project references. It needs no database.
/// </remarks>
public sealed class VerbsTests
{
    [Theory]
    [InlineData("backup")]
    [InlineData("restore")]
    [InlineData("recover")]
    public void A_verb_is_read_from_the_first_argument(string word)
    {
        Assert.True(Verbs.TryRead([word, "--yes"], out var verb));
        Assert.Equal(word, verb);
    }

    [Fact]
    public void No_arguments_is_the_server()
    {
        Assert.False(Verbs.TryRead([], out _));
        Assert.False(Verbs.WasMeantAsAVerb([]));
    }

    /// <summary>
    /// The failure this is for. <c>docker compose run</c> hands what follows the
    /// service name to the entrypoint, which is this binary already, so the
    /// documented restore arrived as <c>logaffe restore --yes</c> — a first
    /// argument that is not a verb. It started a server and discarded the
    /// artifact on standard input (logaffe#67).
    /// </summary>
    [Fact]
    public void The_binary_named_twice_is_refused_rather_than_served()
    {
        string[] args = ["logaffe", "restore", "--yes"];

        Assert.False(Verbs.TryRead(args, out _));
        Assert.True(Verbs.WasMeantAsAVerb(args));
    }

    [Fact]
    public void A_misspelled_verb_is_refused_rather_than_served()
    {
        Assert.False(Verbs.TryRead(["resotre", "--yes"], out _));
        Assert.True(Verbs.WasMeantAsAVerb(["resotre", "--yes"]));
    }

    /// <summary>
    /// Configuration reaches the server as flags, and a flag's value is a bare
    /// word standing second. Refusing on any bare word anywhere would turn
    /// <c>--urls http://+:8080</c> into a usage error.
    /// </summary>
    [Fact]
    public void A_flags_value_is_not_mistaken_for_a_verb()
    {
        string[] args = ["--urls", "http://+:8080"];

        Assert.False(Verbs.TryRead(args, out _));
        Assert.False(Verbs.WasMeantAsAVerb(args));
    }

    [Fact]
    public void The_refusal_names_the_word_it_refused()
    {
        var message = Verbs.NotAVerb("logaffe");

        Assert.Contains("'logaffe'", message, StringComparison.Ordinal);
        Assert.Contains("backup, restore, recover", message, StringComparison.Ordinal);
    }
}
