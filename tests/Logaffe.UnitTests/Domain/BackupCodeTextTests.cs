using Logaffe.Domain.Operators;
using Logaffe.Domain.Tokens;

namespace Logaffe.UnitTests.Domain;

public sealed class BackupCodeTextTests
{
    [Fact]
    public void A_minted_code_is_written_in_the_alphabet_a_person_can_transcribe()
    {
        var code = BackupCodeText.Mint();

        Assert.Equal(BackupCodeText.Length, code.Symbols.Length);
        // The characters a person confuses reading off a sheet of paper are not
        // in it, which is the whole reason that alphabet exists.
        Assert.True(TokenAlphabet.Covers(code.Symbols));
    }

    [Fact]
    public void A_code_is_shown_in_groups_and_hashed_without_them()
    {
        var code = BackupCodeText.Mint();

        Assert.Equal(19, code.Display.Length);
        Assert.Equal(3, code.Display.Count(character => character == '-'));
        Assert.Equal(code.Symbols, code.Display.Replace("-", string.Empty));
    }

    [Fact]
    public void A_code_is_read_back_however_it_was_typed()
    {
        var code = BackupCodeText.Mint();

        // Refusing the operator over a dash, a space or a capital is refusing
        // them their way back in.
        foreach (var typed in new[]
        {
            code.Symbols,
            code.Display,
            code.Display.ToUpperInvariant(),
            $"  {code.Display.Replace("-", " ")} ",
        })
        {
            Assert.True(BackupCodeText.TryParse(typed, out var parsed));
            Assert.Equal(code.Symbols, parsed.Symbols);
            Assert.Equal(code.Hash, parsed.Hash);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcd-efgh-jkmn-pqr")]
    [InlineData("abcd-efgh-jkmn-pqrst")]
    // `l`, `o`, `0` and `1` are not in the alphabet, so they are not in a code.
    [InlineData("abcd-efgh-jkmn-pqr1")]
    public void What_is_not_a_code_is_refused_before_anything_is_fetched(string? typed) =>
        Assert.False(BackupCodeText.TryParse(typed, out _));

    [Fact]
    public void A_code_carries_nothing_into_a_log_line()
    {
        var code = BackupCodeText.Mint();

        Assert.DoesNotContain(code.Symbols, code.ToString());
    }
}
