using Logaffe.Collector;

namespace Logaffe.UnitTests.Collector;

/// <summary>
/// What the collector reads off a machine, asked of files rather than of a
/// machine.
/// </summary>
/// <remarks>
/// This is the whole of what a substitute cannot vouch for elsewhere
/// (<c>docs/codebase.md</c>): the shape of <c>/proc</c> is a kernel's and not
/// this product's, and every one of these files is the text a real one writes.
/// </remarks>
public sealed class ProcMachineTests
{
    /// <summary>The summary line as a modern kernel writes it, guests and all.</summary>
    private const string Stat = """
        cpu  100 20 30 800 40 5 5 0 12 3
        cpu0 50 10 15 400 20 2 3 0 6 1
        intr 12345
        """;

    [Fact]
    public void The_processor_is_the_share_of_the_interval_that_was_not_idle()
    {
        var before = ProcMachine.ReadTicks("cpu  0 0 0 0 0 0 0 0");
        var after = ProcMachine.ReadTicks("cpu  30 0 10 50 10 0 0 0");

        // Forty of a hundred ticks busy: idle and iowait are the sixty that
        // were not.
        Assert.Equal(0.4, ProcMachine.Share(before, after), 6);
    }

    [Fact]
    public void Waiting_on_a_disk_is_not_the_processor_doing_something()
    {
        var before = ProcMachine.ReadTicks("cpu  0 0 0 0 0 0 0 0");

        // A machine hammering a disk reads as a machine with a disk problem
        // rather than a busy one, which is what `top` says as well.
        Assert.Equal(
            0,
            ProcMachine.Share(before, ProcMachine.ReadTicks("cpu  0 0 0 0 100 0 0 0")),
            6);
    }

    [Fact]
    public void A_guest_is_not_counted_twice()
    {
        var ticks = ProcMachine.ReadTicks(Stat);

        // `guest` and `guest_nice` are already inside `user` and `nice`, so the
        // total is the eight fields before them: adding the last two would make
        // a busy hypervisor look idle.
        Assert.Equal(100ul + 20 + 30 + 800 + 40 + 5 + 5 + 0, ticks.Total);
    }

    [Fact]
    public void A_counter_that_went_backwards_is_no_time_passing_rather_than_a_refused_sample()
    {
        var before = ProcMachine.ReadTicks("cpu  500 0 0 500 0 0 0 0");
        var after = ProcMachine.ReadTicks("cpu  10 0 0 10 0 0 0 0");

        // A machine restored from a suspend, or a container migrated between
        // hosts. The installation refuses a share outside 0 to 1 and would cost
        // the whole reading over it.
        var share = ProcMachine.Share(before, after);

        Assert.InRange(share, 0, 1);
    }

    [Fact]
    public void A_kernel_older_than_a_field_simply_has_not_moved_it()
    {
        // Fewer fields than today's kernel writes, which is what an absent
        // counter is.
        var ticks = ProcMachine.ReadTicks("cpu  10 0 5 85");

        Assert.Equal(100ul, ticks.Total);
        Assert.Equal(85ul, ticks.Unused);
    }

    [Fact]
    public void There_being_no_cpu_line_is_not_a_machine_reporting_nothing()
    {
        // It is a `/proc` that is not one, and reporting zeros for it would be
        // a flat band nobody could tell from an idle machine.
        Assert.Throws<FormatException>(() => ProcMachine.ReadTicks("intr 1\nctxt 2"));
    }

    [Fact]
    public void Memory_in_use_is_what_is_not_available_rather_than_what_is_not_free()
    {
        var (used, total) = ProcMachine.ReadMemory([
            "MemTotal:       16000000 kB",
            "MemFree:          200000 kB",
            "MemAvailable:   10000000 kB",
            "Buffers:          100000 kB",
        ]);

        // Free memory on a machine up a week is nearly nothing — the kernel has
        // spent it on cache it hands back on demand — so free would draw every
        // healthy machine at the ceiling.
        Assert.Equal(16_000_000L * 1024, total);
        Assert.Equal(6_000_000L * 1024, used);
    }

    [Fact]
    public void A_kernel_without_MemAvailable_is_read_by_what_it_does_write()
    {
        var (used, total) = ProcMachine.ReadMemory([
            "MemTotal:       16000000 kB",
            "MemFree:         4000000 kB",
        ]);

        Assert.Equal(16_000_000L * 1024, total);
        Assert.Equal(12_000_000L * 1024, used);
    }

    [Fact]
    public void The_load_is_the_three_averages_and_not_the_process_counts_after_them()
    {
        var (one, five, fifteen) = ProcMachine.ReadLoad("0.52 0.61 0.58 1/532 12345\n");

        Assert.Equal(0.52, one);
        Assert.Equal(0.61, five);
        Assert.Equal(0.58, fifteen);
    }

    [Fact]
    public void The_first_reading_has_no_processor_share_to_state()
    {
        var proc = ADirectoryThatLooksLikeProc();
        var machine = new ProcMachine(proc);

        // There is no such thing as one reading of a counter since boot, so the
        // first one seeds and answers nothing.
        Assert.Null(machine.Read());

        var read = machine.Read();

        Assert.NotNull(read);
        Assert.Equal(16_000_000L * 1024, read.MemoryTotal);
        Assert.Equal(0.52, read.Load1);
    }

    [Fact]
    public void A_proc_that_is_not_mounted_is_a_misconfiguration_and_says_so()
    {
        var machine = new ProcMachine(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));

        // Left to throw on purpose: a collector whose mount is missing reports
        // zeros for a machine it cannot see, unless somebody stops it.
        Assert.ThrowsAny<IOException>(() => machine.Read());
    }

    private static string ADirectoryThatLooksLikeProc()
    {
        var at = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        Directory.CreateDirectory(at);
        File.WriteAllText(Path.Combine(at, "stat"), Stat);
        File.WriteAllText(
            Path.Combine(at, "meminfo"),
            "MemTotal:       16000000 kB\nMemAvailable:   10000000 kB\n");
        File.WriteAllText(Path.Combine(at, "loadavg"), "0.52 0.61 0.58 1/532 12345\n");

        return at;
    }
}
