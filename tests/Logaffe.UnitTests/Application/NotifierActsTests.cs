using System.Text;
using Logaffe.Application.Operations;
using Logaffe.Application.Ports;

namespace Logaffe.UnitTests.Application;

/// <summary>
/// The operator's side of the notifier: naming one, keeping the secret they
/// cannot see while they correct the topic beside it, reading it back, and
/// proving it works.
/// </summary>
public sealed class NotifierActsTests
{
    private readonly InMemoryInstallation _installation = new();
    private readonly ReversingCipher _cipher = new();

    [Fact]
    public async Task A_server_and_a_topic_are_what_a_notifier_is()
    {
        await Change().ExecuteAsync("https://ntfy.sh", "logaffe", "", TestContext.Current.CancellationToken);

        Assert.Equal("https://ntfy.sh/", _installation.Notifier?.Server.ToString());
        Assert.Equal("logaffe", _installation.Notifier?.Topic);
        Assert.Null(_installation.Notifier?.EncryptedAccessToken);
    }

    /// <summary>
    /// ADR 0022, in the one place it is easiest to get wrong: the row holds
    /// bytes the cipher made, and a row holding the token as it was typed would
    /// pass every other assertion here.
    /// </summary>
    [Fact]
    public async Task The_token_is_sealed_rather_than_stored()
    {
        await Change().ExecuteAsync(
            "https://ntfy.sh", "logaffe", "tk_secret", TestContext.Current.CancellationToken);

        var stored = _installation.Notifier?.EncryptedAccessToken;

        Assert.NotNull(stored);
        Assert.DoesNotContain("tk_secret", Encoding.UTF8.GetString(stored));
        Assert.Equal("tk_secret", _cipher.Decrypt(stored));
    }

    /// <summary>
    /// A screen cannot show a secret it is about to overwrite, so an operator
    /// correcting a topic sends no token and keeps the one they sealed.
    /// </summary>
    [Fact]
    public async Task A_token_that_is_not_supplied_is_the_one_already_sealed()
    {
        await Change().ExecuteAsync(
            "https://ntfy.sh", "logaffe", "tk_secret", TestContext.Current.CancellationToken);
        await Change().ExecuteAsync(
            "https://ntfy.sh", "logaffe-alerts", null, TestContext.Current.CancellationToken);

        Assert.Equal("logaffe-alerts", _installation.Notifier?.Topic);
        Assert.Equal("tk_secret", _cipher.Decrypt(_installation.Notifier!.EncryptedAccessToken!));
    }

    /// <summary>And moving to a public topic is saying so, not saying nothing.</summary>
    [Fact]
    public async Task An_empty_token_is_no_token_at_all()
    {
        await Change().ExecuteAsync(
            "https://ntfy.sh", "logaffe", "tk_secret", TestContext.Current.CancellationToken);
        await Change().ExecuteAsync(
            "https://ntfy.sh", "logaffe", "", TestContext.Current.CancellationToken);

        Assert.Null(_installation.Notifier?.EncryptedAccessToken);
    }

    [Fact]
    public async Task Clearing_takes_the_token_with_it()
    {
        await Change().ExecuteAsync(
            "https://ntfy.sh", "logaffe", "tk_secret", TestContext.Current.CancellationToken);
        await Change().ClearAsync(TestContext.Current.CancellationToken);

        Assert.Null(_installation.Notifier);
    }

    [Fact]
    public async Task What_is_not_a_notifier_is_refused_rather_than_stored()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Change().ExecuteAsync(
            "ntfy.sh", "logaffe", null, TestContext.Current.CancellationToken));

        Assert.Null(_installation.Notifier);
    }

    [Fact]
    public async Task The_notifier_comes_back_with_its_token_in_the_clear()
    {
        await Change().ExecuteAsync(
            "https://ntfy.sh", "logaffe", "tk_secret", TestContext.Current.CancellationToken);

        var read = await new ReadTheNotifier(_installation, _cipher)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new TheNotifier("https://ntfy.sh/", "logaffe", "tk_secret"), read);
    }

    [Fact]
    public async Task An_installation_with_no_notifier_reads_back_as_none() =>
        Assert.Null(await new ReadTheNotifier(_installation, _cipher)
            .ExecuteAsync(TestContext.Current.CancellationToken));

    /// <summary>
    /// The test send is the operator's and belongs to no condition, so what it
    /// answers is what the notifier said rather than a line in a log file
    /// nobody is reading at that moment.
    /// </summary>
    [Theory]
    [InlineData(NotifierProof.Sent)]
    [InlineData(NotifierProof.NoNotifier)]
    [InlineData(NotifierProof.Refused)]
    [InlineData(NotifierProof.Unreachable)]
    public async Task The_test_send_answers_the_operator(NotifierProof proof)
    {
        var notifier = new RecordingNotifier { Proof = proof };

        var answer = await new SendATestNotification(notifier)
            .ExecuteAsync(TestContext.Current.CancellationToken);

        Assert.Equal(proof, answer);
        Assert.Equal(1, notifier.Tests);
        Assert.Empty(notifier.Sent);
    }

    private ChangeTheNotifier Change() => new(_installation, _cipher);
}
