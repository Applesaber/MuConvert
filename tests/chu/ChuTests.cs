using System.Reflection;
using MuConvert.chu;
using MuConvert.utils;
using Rationals;

namespace MuConvert.Tests.chu;

public class ChuTests
{
    private static string TestsetDir => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "chu", "testset");
    private static string OfficialDir => Path.Combine(TestsetDir, "官谱");
    private static string CustomDir => Path.Combine(TestsetDir, "自制谱");

    public static IEnumerable<object[]> OfficialC2sChartPaths()
    {
        return Directory.EnumerateFiles(OfficialDir, "*.c2s", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(path => (object[])[path]);
    }

    public static IEnumerable<object[]> CustomUgcChartPaths()
    {
        return Directory.EnumerateFiles(CustomDir, "*.ugc", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(path => (object[])[path]);
    }

    [Theory]
    [MemberData(nameof(OfficialC2sChartPaths))]
    public void C2sRoundTrip(string c2sPath)
    {
        var (chart, _) = new C2sParser().Parse(File.ReadAllText(c2sPath));
        var (rt, _) = new C2sGenerator().Generate(chart);
        var (reparsed, _) = new C2sParser().Parse(rt);

        Assert.Equal(chart.Notes.Count, reparsed.Notes.Count);

        var originalSnapshots = chart.Notes
            .Select(SnapshotNote)
            .OrderBy(s => s)
            .ToArray();

        var reparsedSnapshots = reparsed.Notes
            .Select(SnapshotNote)
            .OrderBy(s => s)
            .ToArray();

        Assert.Equal(originalSnapshots, reparsedSnapshots);
    }

    /// <summary>
    /// Builds a stable, comparable string from a note's public instance properties (name-sorted)
    /// so round-trip tests verify no field loss without hard-coding each property in the test.
    /// Omits <see cref="ChuNote.ExtraData"/> and <see cref="ChuNote.EndTime"/> (redundant with Time/Duration or not stable across formats).
    /// </summary>
    private static string SnapshotNote(ChuNote note)
    {
        string F(object? v, string field) => v switch
        {
            Rational r => r.CanonicalForm.ToString(),
            List<int> list => string.Join(",", list),
            string s => s switch
            {
                "DEF" => "",
                "A" when note.Type == "FLK" => "L",
                _ => s
            },
            null => "",
            _ => v.ToString() ?? "",
        };

        var firstParts = new[]
        {
            $"Type={note.Type}", 
            $"Time={note.Time}", 
            $"Cell={note.Cell}",
            $"Width={note.Width}",
        };
        var propParts = typeof(ChuNote).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .OrderBy(p => p.Name)
            .Where(p => p.Name is not ("EndTime" or "Type" or "Time" or "Cell" or "Width"))
            .Select(p => $"{p.Name}={F(p.GetValue(note), p.Name)}");
        var fieldParts = typeof(ChuNote).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .OrderBy(f => f.Name)
            .Select(f => $"{f.Name}={F(f.GetValue(note), f.Name)}");
        return string.Join("|", firstParts.Concat(propParts).Concat(fieldParts));
    }

    /// <summary>
    /// 将 UGC 网格上的 <see cref="ChuNote"/> 的 Time / Duration 投影为「经 C2S 生成器写出再解析」后等价的分数（C2S 小节 tick = <paramref name="c2sResolution"/>）。
    /// </summary>
    private static ChuNote UgcNoteScaledToC2sTicks(ChuNote n, int ugcTicksPerBeat, int c2sResolution)
    {
        var tpmUgc = ugcTicksPerBeat * 4;
        var (m, oU) = Utils.BarAndTick(n.Time, tpmUgc);
        var oC = (int)Math.Round((double)oU * c2sResolution / tpmUgc);
        var time = m + new Rational(oC, c2sResolution);
        var dur = new Rational(Utils.Tick(n.Duration, c2sResolution), c2sResolution);
        return CloneChuNoteWithTiming(n, time, dur);
    }

    /// <summary>
    /// 将 C2S 网格上的音符投影为「经 UGC 生成器写出再解析」后等价的分数（UGC 小节 tick = <paramref name="ugcTicksPerBeat"/> × 4）。
    /// </summary>
    private static ChuNote C2sNoteScaledToUgcTicks(ChuNote n, int ugcTicksPerBeat, int c2sResolution)
    {
        var tpmUgc = ugcTicksPerBeat * 4;
        var (m, oC) = Utils.BarAndTick(n.Time, c2sResolution);
        var oU = (int)Math.Round((double)oC * tpmUgc / c2sResolution);
        var time = m + new Rational(oU, tpmUgc);
        var dur = new Rational(Utils.Tick(n.Duration, tpmUgc), tpmUgc);
        return CloneChuNoteWithTiming(n, time, dur);
    }

    private static ChuNote CloneChuNoteWithTiming(ChuNote n, Rational time, Rational duration) => new()
    {
        Type = n.Type,
        Time = time,
        Cell = n.Cell,
        Width = n.Width,
        Duration = duration,
        EndCell = n.EndCell,
        EndWidth = n.EndWidth,
        Previous = n.Previous,
        Tag = n.Tag,
        Height = n.Height,
        EndHeight = n.EndHeight,
        CrushInterval = n.CrushInterval,
    };

    /// <summary>
    /// 比较 UGC 与 C2S 的音符 IR：因 tick 网格不同，在各自「经对方格式写回再解析」的量化意义下比较快照。
    /// </summary>
    private static void AssertUgcNotesEquivalentToReparsedC2s(ChuChart ugc, ChuChart c2s, bool isUgcReference)
    {
        if (isUgcReference)
        {
            var ugcSnaps = ugc.Notes.Where(n=>n.Type != "CLICK")
                .OrderBy(n=>n.Time).ThenBy(n=>n.Cell).ThenBy(n=>n.Width).ThenBy(n=>n.Duration).ThenBy(n=>n.Type)
                .Select(n => SnapshotNote(UgcNoteScaledToC2sTicks(n, 480, 384)))
                .ToArray();
            var c2sSnaps = c2s.Notes
                .OrderBy(n=>n.Time).ThenBy(n=>n.Cell).ThenBy(n=>n.Width).ThenBy(n=>n.Duration).ThenBy(n=>n.Type)
                .Select(SnapshotNote)
                .ToArray();
            Assert.Equal(ugcSnaps, c2sSnaps);
        }
        else
        {
            var ugcSnaps = ugc.Notes.Where(n=>n.Type != "CLICK")
                .OrderBy(n=>n.Time).ThenBy(n=>n.Cell).ThenBy(n=>n.Width).ThenBy(n=>n.Duration).ThenBy(n=>n.Type).ThenBy(n=>n.TargetNote)
                .Select(SnapshotNote)
                .ToArray();
            var c2sSnaps = c2s.Notes
                .OrderBy(n=>n.Time).ThenBy(n=>n.Cell).ThenBy(n=>n.Width).ThenBy(n=>n.Duration).ThenBy(n=>n.Type).ThenBy(n=>n.TargetNote)
                .Select(n => SnapshotNote(C2sNoteScaledToUgcTicks(n, 480, 384)))
                .ToArray();
            Assert.Equal(c2sSnaps, ugcSnaps);
        }
    }

    [Theory]
    [MemberData(nameof(CustomUgcChartPaths))]
    public void UgcToC2sViaGenerator(string ugcPath)
    {
        var (ugc, _) = new UgcParser().Parse(File.ReadAllText(ugcPath));
        Assert.NotEmpty(ugc.Notes);

        var (c2sText, _) = new C2sGenerator().Generate(ugc);
        Assert.Contains("VERSION", c2sText);
        Assert.Contains("TAP\t", c2sText);

        // 再把转出来的c2s，parse回去，比较是否和一开始的ugc等价（注意不是文本 round-trip，而是 IR 等价，允许字段重排但不允许信息丢失）
        var (c2sChart, _) = new C2sParser().Parse(c2sText);
        Assert.NotEmpty(c2sChart.Notes);
        AssertUgcNotesEquivalentToReparsedC2s(ugc, c2sChart, true);
    }

    [Theory]
    [MemberData(nameof(OfficialC2sChartPaths))]
    public void C2sToUgcViaGenerator(string c2sPath)
    {
        var (c2s, _) = new C2sParser().Parse(File.ReadAllText(c2sPath));
        Assert.NotEmpty(c2s.Notes);
        
        var (ugcText, _) = new UgcGenerator().Generate(c2s);
        Assert.Contains("@VER", ugcText);
        Assert.Contains("#5'0", ugcText);

        // 再把转出来的ugc，parse回去，比较是否和一开始的c2s等价
        var (ugcReparsed, _) = new UgcParser().Parse(ugcText);
        Assert.NotEmpty(ugcReparsed.Notes);
        AssertUgcNotesEquivalentToReparsedC2s(ugcReparsed, c2s, false);
    }
}
