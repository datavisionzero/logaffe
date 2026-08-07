using Logaffe.Domain.Operators;

namespace Logaffe.UnitTests.Domain;

public sealed class BackupCodeTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_minted_set_is_shown_once_and_stored_hashed()
    {
        var operatorId = Guid.CreateVersion7();

        var minted = BackupCode.MintSet(operatorId, Now);

        Assert.Equal(BackupCode.SetSize, minted.Shown.Count);
        Assert.Equal(BackupCode.SetSize, minted.Stored.Count);
        Assert.Equal(BackupCode.SetSize, minted.Shown.Select(c => c.Symbols).Distinct().Count());
        Assert.All(minted.Stored, code =>
        {
            Assert.Equal(operatorId, code.OperatorId);
            Assert.Equal(Now, code.IssuedAt);
            Assert.False(code.IsSpent);
            Assert.Equal(BackupCode.HashLength, code.Hash.Length);
        });
    }

    [Fact]
    public void Each_stored_code_is_the_one_that_was_shown()
    {
        var minted = BackupCode.MintSet(Guid.CreateVersion7(), Now);

        // The pairing is what makes a set usable at all: the operator holds the
        // paper, the installation holds these hashes, and nothing else connects
        // them.
        foreach (var (shown, stored) in minted.Shown.Zip(minted.Stored))
        {
            Assert.True(stored.Matches(shown));
        }
    }

    [Fact]
    public void A_code_from_another_set_matches_nothing()
    {
        var minted = BackupCode.MintSet(Guid.CreateVersion7(), Now);
        var someoneElses = BackupCode.MintSet(Guid.CreateVersion7(), Now);

        Assert.All(minted.Stored, code => Assert.False(code.Matches(someoneElses.Shown[0])));
    }

    [Fact]
    public void A_code_is_consumed_by_a_timestamp()
    {
        var code = BackupCode.MintSet(Guid.CreateVersion7(), Now).Stored[0];

        code.ConsumeAt(Now.AddDays(2));

        // Not a deletion: "how many remain" is a filtered count, and a spent
        // code stays visibly spent (ADR 0032).
        Assert.True(code.IsSpent);
        Assert.Equal(Now.AddDays(2), code.UsedAt);
    }

    [Fact]
    public void A_code_is_used_once()
    {
        var code = BackupCode.MintSet(Guid.CreateVersion7(), Now).Stored[0];
        code.ConsumeAt(Now);

        Assert.Throws<InvalidOperationException>(() => code.ConsumeAt(Now.AddMinutes(1)));
    }

    [Fact]
    public void A_spent_code_still_matches()
    {
        var minted = BackupCode.MintSet(Guid.CreateVersion7(), Now);
        var code = minted.Stored[0];
        code.ConsumeAt(Now);

        // Matching says nothing about being good: a code offered twice has to
        // cost what a code offered once costs, and refusing it is the caller's.
        Assert.True(code.Matches(minted.Shown[0]));
    }
}
