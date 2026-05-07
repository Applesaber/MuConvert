using System.Text;
using MuConvert.generator;
using MuConvert.utils;

namespace MuConvert.chu;

public class UgcGenerator : IGenerator<ChuChart>
{
    private static int RSL = 480 * 4;

    public (string, List<Alert>) Generate(ChuChart chart)
    {
        var alerts = new List<Alert>();
        var text = Serialize(chart, alerts);
        return (text, alerts);
    }

    private static string Serialize(ChuChart ugc, List<Alert> alerts)
    {
        ugc.Sort();
        
        var sb = new StringBuilder();
        sb.AppendLine("@VER\t8");
        if (!string.IsNullOrEmpty(ugc.Title)) sb.AppendLine($"@TITLE\t{ugc.Title}");
        if (!string.IsNullOrEmpty(ugc.Artist)) sb.AppendLine($"@ARTIST\t{ugc.Artist}");
        if (!string.IsNullOrEmpty(ugc.Designer)) sb.AppendLine($"@DESIGN\t{ugc.Designer}");
        sb.AppendLine($"@DIFF\t{ugc.Difficulty}");
        sb.AppendLine($"@LEVEL\t{ugc.DisplayLevel}");
        sb.AppendLine($"@CONST\t{ugc.Level:F5}");
        sb.AppendLine($"@SONGID\t{ugc.MusicId}");
        sb.AppendLine($"@TICKS\t{RSL / 4}");
        foreach (var met in ugc.MetList)
        {
            var (m, _) = Utils.BarAndTick(met.Time, RSL);
            sb.AppendLine($"@BEAT\t{m}\t{met.Numerator}\t{met.Denominator}");
        }
        foreach (var b in ugc.BpmList)
        {
            var (m, o) = Utils.BarAndTick(b.Time, RSL);
            sb.AppendLine($"@BPM\t{m}'{o}\t{b.Bpm:F5}");
        }
        sb.AppendLine("@TIL\t0\t0'0\t1.00000");

        foreach (var s in ugc.SflList.OrderBy(x => x.Time)) 
        { 
            var (m, o) = Utils.BarAndTick(s.Time, RSL); 
            sb.AppendLine($"@SPDMOD\t{m}'{o}\t{s.Multiplier:0.00000}");
        }

        sb.AppendLine("@MAINTIL\t0");
        sb.AppendLine("@ENDHEAD");
        sb.AppendLine();

        // UGC Slide / AIR-SLIDE (v8):
        // - Chains (ChuNote.Previous) serialize as ONE parent line + follower lines (#OffsetTick from parent time).
        // - Ground slide: parent `s`, followers `>s` / `>c` + end cell/width.
        // - Air slide: parent `S` + cell/width + hh (base-36 ×2, C2S/UGC height units) + N/I; followers `>s`/`>c` + xw + hh.
        // - First segment may attach to TAP/HLD via Previous; only skip emit when Previous is another segment of the same chain.
        var slideChains = BuildSlideChains(ugc.Notes);

        foreach (var n in ugc.Notes)
        {
            if (IsSlideChainNote(n.Type) && n.Previous != null && IsSlideChainNote(n.Previous.Type))
                continue; // 是Slide且不是第一段Slide，则应当已经被处理过了，直接跳过

            var (m, o) = Utils.BarAndTick(n.Time, RSL);
            var ucode = UCode(n);
            if (ucode == "")
            {
                alerts.Add(new Alert(Alert.LEVEL.Warning, $"UGC Generator遇到了不支持的音符类型: {n.Type}", n.Time, (double)ugc.ToSecond(n.Time)));
                continue;
            }
            sb.Append($"#{m}'{o}:{ucode}");
            sb.AppendLine();

            if (IsSlideChainNote(n.Type))
            {
                if (slideChains.TryGetValue(n, out var segments))
                {
                    var isAir = IsAirSlideType(n.Type);
                    foreach (var seg in segments)
                    {
                        var endTicks = Utils.Tick(seg.EndTime - n.Time, RSL);
                        if (endTicks <= 0) continue;
                        if (isAir)
                            sb.AppendLine($"#{endTicks}>{SlideFollowerMarker(seg.Type)}{IntToHex(seg.EndCell)}{IntToHex(seg.EndWidth)}{EncodeUgcAirHeight2(AirSlideFollowerHeight(seg))}");
                        else
                            sb.AppendLine($"#{endTicks}>{SlideFollowerMarker(seg.Type)}{IntToHex(seg.EndCell)}{IntToHex(seg.EndWidth)}");
                    }
                }
                continue;
            }

            var durTicks = Utils.Tick(n.Duration, RSL);
            if (n.Type is "HLD" or "HXD" && durTicks > 0)
                sb.AppendLine($"#{durTicks}>s");
            else if (n.Type is "AHD" or "AHX" && durTicks > 0)
            {
                var marker = (n.Type == "AHX") ? 'c' : 's';
                sb.AppendLine($"#{durTicks}>{marker}");
            }
        }
        return sb.ToString();
    }

    private static Dictionary<ChuNote, List<ChuNote>> BuildSlideChains(List<ChuNote> notes)
    {
        var chains = new Dictionary<ChuNote, List<ChuNote>>();
        foreach (var n in notes)
        {
            if (!IsSlideChainNote(n.Type)) continue;
            var head = GetSlideHead(n);
            if (!chains.TryGetValue(head, out var list))
                chains[head] = list = [];
            list.Add(n);
        }

        // Order segments by their end time so follower ticks are increasing.
        foreach (var (_, segs) in chains)
        {
            segs.Sort((a, b) =>
            {
                var t = a.EndTime.CompareTo(b.EndTime);
                if (t != 0) return t;
                // stable-ish tie-breakers
                t = a.Time.CompareTo(b.Time);
                if (t != 0) return t;
                t = string.CompareOrdinal(a.Type, b.Type);
                if (t != 0) return t;
                return 0;
            });
        }

        // For a valid chain, follower ticks should be strictly increasing; if the chart has
        // degenerate segments, later code simply skips non-positive offsets.
        return chains;
    }

    private static ChuNote GetSlideHead(ChuNote n)
    {
        var cur = n;
        while (cur.Previous != null && IsSlideChainNote(cur.Previous.Type))
            cur = cur.Previous;
        return cur;
    }

    private static bool IsSlideType(string t) => t is "SLD" or "SLC" or "SXD" or "SXC";
    private static bool IsAirSlideType(string t) => t is "ASD" or "ASC";
    private static bool IsSlideChainNote(string t) => IsSlideType(t) || IsAirSlideType(t);
    private static char SlideFollowerMarker(string t) => t is "SLC" or "SXC" or "ASC" ? 'c' : 's';

    /// <summary> C2S col.6 / follower height: integer stored as two base-36 digits (Umiguri v8 AIR-SLIDE). </summary>
    private static string EncodeUgcAirHeight2(int value)
    {
        var v = Math.Clamp(value * 10, 0, 35 * 36 + 35);
        var hi = v / 36;
        var lo = v % 36;
        return $"{IntToHex(hi)}{IntToHex(lo)}";
    }

    private static int AirSlideParentStartHeight(ChuNote head) => 8; // TODO 现在暂时写死，之后应该改成从ExtraData等地方读取
    private static int AirSlideFollowerHeight(ChuNote seg) => 8; // TODO 现在暂时写死，之后应该改成从ExtraData等地方读取

    private static Dictionary<string, string> AirDirections = new()
    {
        ["AIR"] = "UC", ["AUR"] = "UR", ["AUL"] = "UL", ["ADW"] = "DC", ["ADR"] = "DR", ["ADL"] = "DL",
    };
    private static string UCode(ChuNote n)
    {
        string c = IntToHex(n.Cell), w = IntToHex(n.Width);
        var targetNote = string.IsNullOrEmpty(n.TargetNote) ? "N" : n.TargetNote;
        return n.Type switch
        {
            "TAP" => $"t{c}{w}",
            "CHR" => $"x{c}{w}{n.Tag}",
            "HLD" or "HXD" => $"h{c}{w}",
            "SLD" or "SXD" => $"s{c}{w}",
            "SLC" or "SXC" => $"s{c}{w}",
            "FLK" => $"f{c}{w}A",
            "MNE" => $"d{c}{w}",
            // AIR-SLIDE (v8): #BarTick:S x w hh c
            "ASD" or "ASC" => $"S{c}{w}{EncodeUgcAirHeight2(AirSlideParentStartHeight(n))}{AirHoldColorSuffix(n)}",
            "AIR" or "AUR" or "AUL" or "ADW" or "ADR" or "ADL" => $"a{c}{w}{AirDirections[n.Type]}{targetNote}{AirHoldColorSuffix(n)}",
            // AIR-HOLD (v8): #BarTick:H x w c + 子行 #OffsetTick:s / :c（见 Umiguri Chart v8 doc）
            "AHD" or "AHX" => $"H{c}{w}{AirHoldColorSuffix(n)}",
            _ => ""
        };
    }

    private static string IntToHex(int v) => "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[Math.Clamp(v, 0, 35)].ToString();

    private static readonly Dictionary<string, string> AirColor = new()
    {
        ["DEF"] = "N",
        ["I"] = "I", // TODO 搞清楚UGC里的'I'颜色，在C2S里，对应的字符串是什么
    };
    private static string AirHoldColorSuffix(ChuNote n) => AirColor.GetValueOrDefault(n.Tag, "N");
}
