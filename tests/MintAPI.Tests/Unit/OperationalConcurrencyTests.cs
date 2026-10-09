using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MintAPI.Configuration;
using MintAPI.Services;
using MintOsuApi.Enums;
using MintOsuApi.Models;

namespace MintAPI.Tests.Unit;

public sealed class OperationalConcurrencyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Bp_fix_honors_configured_parallelism(int limit)
    {
        var files = new Files(limit);
        var service = new BpFixService(files, new Calculator(), NullLogger<BpFixService>.Instance,
            Options.Create(new ConcurrencyOptions { PpCalculation = limit }));
        var scores = Enumerable.Range(1, 4).Select(id => new Score
        {
            Passed = true, Pp = 100, Rank = Grade.A, MaxCombo = 10,
            Beatmap = new Beatmap { Id = id, BeatmapsetId = 1, MaxCombo = 100 }
        }).ToList();
        var pending = service.AnalyzeAsync(new User(), scores);
        try
        {
            await files.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(limit, files.Active);
            Assert.False(pending.IsCompleted);
        }
        finally { files.Release.TrySetResult(); }
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(limit, files.Maximum);
        Assert.Equal(4, files.Calls);
    }

    private sealed class Files(int limit) : IBeatmapFileService
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Active;
        public int Maximum;
        public int Calls;
        public async Task<string> GetOsuFilePathAsync(int set, int map)
        {
            Interlocked.Increment(ref Calls);
            var active = Interlocked.Increment(ref Active);
            Interlocked.Exchange(ref Maximum, Math.Max(active, Maximum));
            if (active == limit) Entered.TrySetResult();
            try { await Release.Task; return "fixture.osu"; }
            finally { Interlocked.Decrement(ref Active); }
        }
        public Task<byte[]> GetMapBgAsync(int set, int map, string? name = null) => throw new NotSupportedException();
        public Task<byte[]?> GetListCoverAsync(int set, CancellationToken ct = default) => throw new NotSupportedException();
        public string GetBgFilename(string path) => throw new NotSupportedException();
    }
    private sealed class Calculator : IPpCalculatorService
    {
        public PpResult CalculateFixed(Score score, string path) => new(120, 5, 100);
        public PpResult Calculate(Score score, string path) => throw new NotSupportedException();
        public (double IfPp, double SsPp) CalculateIfFcAndSs(Score score, string path) => throw new NotSupportedException();
        public PpResult CalculateSs(string path, int mode, uint mods = 0) => throw new NotSupportedException();
        public (double NewPp, int Position) FindOptimalNewPp(List<double> values, double increase) => throw new NotSupportedException();
    }
}
