using Logaffe.Domain.Projects;
using ModelContextProtocol;

namespace Logaffe.Api.Mcp;

/// <summary>
/// The refusals the nineteen acts share, in one place because they are the same
/// sentence every time. The other two of the twenty-one refuse nothing: they
/// read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every one of them names the argument and, where there is one, the tool to
/// call instead.</b> A model correcting itself needs something to correct: told
/// only that a call failed it will try the same call again, and told which
/// argument was wrong it will fix it. This is the same reading
/// <c>EntryTools</c> gives a filter that is not a filter.
/// </para>
/// <para>
/// <b>None of them says whether the caller could have succeeded with another
/// token.</b> A window moved the wrong way is refused identically whether or not
/// the token may destroy, so a refusal is never a probe for what the operator
/// withheld — what a token may do is what its tool list says, and nothing has to
/// be inferred from an error.
/// </para>
/// </remarks>
internal static class Refused
{
    /// <remarks>
    /// It says the project is not there and nothing else. A project the operator
    /// deleted in another tab looks like this, and so does one an agent
    /// invented; neither is worth telling apart.
    /// </remarks>
    public static McpException NoSuchProject(Guid id) =>
        new($"projectId: There is no project {id} in this installation. Call get_settings.");

    public static McpException NoSuchGroup(Guid id) =>
        new($"groupId: There is no group {id} in this installation. Call get_settings.");

    public static McpException NoSuchHost(Guid id) =>
        new($"hostId: There is no host {id} in this installation. Call get_settings.");

    public static McpException NoSuchToken(Guid id) =>
        new($"tokenId: There is no token {id} in this installation. Call get_settings.");

    /// <remarks>
    /// A name that is taken is the one refusal worth acting on, so it says which
    /// name and where — the alternative, renaming something the operator did not
    /// ask to rename, is a decision no act here gets to make for them.
    /// </remarks>
    public static McpException ProjectNameTaken(string name) =>
        new($"name: A project called \"{name}\" is already listed there. "
            + "A project's name is unique within its group, and among the "
            + "projects in no group.");

    /// <remarks>
    /// It does not repeat the name back, because the name it would repeat is the
    /// project's own and the caller passed an identity rather than a name. What
    /// it says instead is where the collision is, which is the part the caller
    /// does not have.
    /// </remarks>
    public static McpException NameTakenWhereItWasGoing() =>
        new("groupId: A project by that name is already listed there. A project's "
            + "name is unique within its group, so rename one of the two first — "
            + "moving would leave the operator with the pair the rule exists to "
            + "prevent.");

    public static McpException GroupNameTaken(string name) =>
        new($"name: A group called \"{name}\" already exists.");

    public static McpException HostNameTaken(string name) =>
        new($"name: A host called \"{name}\" already exists.");

    public static McpException NotAName(int maximum) =>
        new($"name: A name is one to {maximum} characters and not only spaces.");

    public static McpException NotAWindow() =>
        new($"retentionDays: A retention window is between "
            + $"{RetentionWindow.MinimumDays} and {RetentionWindow.MaximumDays} days.");

    public static McpException AlreadyHoldsTwo(int maximum, string tool) =>
        new($"It already holds {maximum} tokens, which is as many as there is "
            + $"ever a reason for: two is what makes a rotation possible without "
            + $"a gap. Call {tool} on one of them first.");

    /// <summary>
    /// A window call that would move the window the other way, refused by naming
    /// the tool that does it.
    /// </summary>
    /// <remarks>
    /// <b>It reads the same on either token.</b> The tool being named may be one
    /// this caller was never offered, and saying so would turn every refusal into
    /// a question about what the operator withheld. Which tools a token has is
    /// what its list says.
    /// </remarks>
    public static McpException WrongDirection(int now, int asked, string tool) =>
        new($"retentionDays: The window is {now} days and you asked for {asked}. "
            + $"{tool} is the tool that moves it that way.");
}

/// <summary>
/// The two readings every act shares: a name, and a window.
/// </summary>
/// <remarks>
/// They are checked here rather than left to the use case underneath, which
/// throws for the same two things. The difference is what the agent is handed:
/// an exception out of the domain says a value was out of range, and this says
/// which argument of which tool to fix. It is the adapter's whole job on the way
/// in, and it decides nothing — the bounds are the domain's
/// (<see cref="RetentionWindow"/>, <c>Project.NameMaxLength</c>).
/// </remarks>
internal static class Given
{
    public static string AName(string? name, int maximum) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > maximum
            ? throw Refused.NotAName(maximum)
            : name.Trim();

    public static RetentionWindow AWindow(int days) =>
        RetentionWindow.TryOfDays(days, out var window) ? window : throw Refused.NotAWindow();
}
