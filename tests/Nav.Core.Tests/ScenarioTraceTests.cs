using System.Text.Json;

using Xunit.Abstractions;

namespace Nav.Core.Tests;

/// <summary>
/// The JSONL trace: one self-contained line per tick, byte-identical across
/// runs, and reducible to a bounded digest without re-simulating.
/// </summary>
public sealed class ScenarioTraceTests(ITestOutputHelper output)
{
    private static string Trace(string name)
    {
        var (scenario, grid) = Fixtures.Load(name);
        using var writer = new StringWriter();
        ScenarioTrace.Write(scenario, grid, writer, name);
        return writer.ToString();
    }

    [Fact]
    public void TheFirstLineIsAVersionedHeader()
    {
        var lines = Trace("headon").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        using var header = JsonDocument.Parse(lines[0]);
        Assert.Equal(ScenarioTrace.Version, header.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("corridor.map", header.RootElement.GetProperty("map").GetString());
        Assert.Equal(2, header.RootElement.GetProperty("agents").GetInt32());
    }

    [Fact]
    public void ThereIsOneLinePerTickAndEveryLineParses()
    {
        var (scenario, _) = Fixtures.Load("headon");
        var lines = Trace("headon").Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Header plus ticks 0..EndTick inclusive.
        Assert.Equal(scenario.EndTick + 2, lines.Length);

        for (var i = 1; i < lines.Length; i++)
        {
            using var doc = JsonDocument.Parse(lines[i]);
            Assert.Equal(i - 1, doc.RootElement.GetProperty("tick").GetInt32());
            Assert.Equal(2, doc.RootElement.GetProperty("agents").GetArrayLength());
        }
    }

    [Fact]
    public void TwoRunsProduceByteIdenticalTraces()
    {
        // This is the determinism check as a file property: a diff of two trace
        // files shows WHERE divergence began, which the in-memory comparison in
        // the playback tests cannot.
        Assert.Equal(Trace("chokepoint"), Trace("chokepoint"));
    }

    [Fact]
    public void TracedPositionsAreTheTrajectoriesThePlaybackChecks()
    {
        var (scenario, grid) = Fixtures.Load("group");
        using var writer = new StringWriter();
        var outcome = ScenarioTrace.Write(scenario, grid, writer, "group");

        var lines = writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (var tick = 0; tick < outcome.Ticks; tick++)
        {
            using var doc = JsonDocument.Parse(lines[tick + 1]);
            foreach (var agent in doc.RootElement.GetProperty("agents").EnumerateArray())
            {
                var id = agent.GetProperty("id").GetInt32();
                var traced = grid.Index(agent.GetProperty("x").GetInt32(), agent.GetProperty("y").GetInt32());
                Assert.Equal(outcome.Trajectories[id].Plan.Cells[tick], traced);
            }
        }
    }

    [Fact]
    public void TheDigestIsBoundedAndNamesTheDeadlock()
    {
        using var reader = new StringReader(Trace("headon"));

        var digest = ScenarioTrace.Summarize(reader);
        output.WriteLine(digest);

        var lines = digest.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.InRange(lines.Length, 3, 30);
        Assert.Contains("2 agents", digest, StringComparison.Ordinal);
        Assert.Contains("stalled: 2 at the end", digest, StringComparison.Ordinal);
        Assert.Contains("never moved", digest, StringComparison.Ordinal);
        Assert.Contains("look at:", digest, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDigestOfACleanRunReportsTheArrivals()
    {
        using var reader = new StringReader(Trace("group"));

        var digest = ScenarioTrace.Summarize(reader);
        output.WriteLine(digest);

        Assert.Contains("arrived: 12 of 12", digest, StringComparison.Ordinal);
    }

    [Fact]
    public void AWrongVersionIsRefusedNotMisread()
    {
        using var reader = new StringReader("""{"version":99,"agents":1}""");

        var error = Assert.Throws<InvalidDataException>(() => ScenarioTrace.Summarize(reader));

        Assert.Contains("99", error.Message, StringComparison.Ordinal);
    }
}
