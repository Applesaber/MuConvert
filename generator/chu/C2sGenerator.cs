using System.Text;
using MuConvert.generator;
using MuConvert.utils;

namespace MuConvert.chu;

public class C2sGenerator : IGenerator<ChuChart>
{
    private const int RSL = 384;
    
    public (string, List<Alert>) Generate(ChuChart chart)
    {
        var alerts = new List<Alert>();
        var text = Serialize(chart, alerts);
        return (text, alerts);
    }

    private static string Serialize(ChuChart chart, List<Alert> alerts)
    {
        chart.Sort();
        
        int.TryParse(chart.MusicId, out var musicId);
        var sb = new StringBuilder();
        sb.AppendLine($"VERSION\t1.08.00\t1.08.00");
        sb.AppendLine($"MUSIC\t{musicId}");
        sb.AppendLine("SEQUENCEID\t0");
        sb.AppendLine($"DIFFICULT\t{chart.Difficulty:D2}");
        sb.AppendLine("LEVEL\t0.0");
        sb.AppendLine($"CREATOR\t{chart.Designer}");
        var bpm_def = chart.BpmList.BPM_DEF();
        sb.AppendLine($"BPM_DEF\t{bpm_def.Item1}\t{bpm_def.Item2}\t{bpm_def.Item3}\t{bpm_def.Item4}");
        sb.AppendLine("MET_DEF\t4\t4");
        sb.AppendLine($"RESOLUTION\t{RSL}");
        sb.AppendLine($"CLK_DEF\t{RSL}");
        sb.AppendLine("PROGJUDGE_BPM\t240.000");
        sb.AppendLine("PROGJUDGE_AER\t0.999");
        sb.AppendLine("TUTORIAL\t0");
        sb.AppendLine();

        foreach (var b in chart.BpmList)
        {
            var (m, o) = Utils.BarAndTick(b.Time, RSL);
            sb.AppendLine($"BPM\t{m}\t{o}\t{b.Bpm:0.000}");
        }

        foreach (var met in chart.MetList)
        {
            var (m, o) = Utils.BarAndTick(met.Time, RSL);
            sb.AppendLine($"MET\t{m}\t{o}\t{met.Denominator}\t{met.Numerator}");
        }

        foreach (var s in chart.SflList.OrderBy(s => s.Time))
        {
            var (m, o) = Utils.BarAndTick(s.Time, RSL);
            var durTicks = Utils.Tick(s.Duration, RSL);
            sb.AppendLine($"SFL\t{m}\t{o}\t{durTicks}\t{s.Multiplier:0.000000}");
        }
        sb.AppendLine();

        foreach (var n in chart.Notes)
        {
            var line = FormatNote(n, RSL, alerts);
            if (line != null) sb.AppendLine(line);
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private static List<string> allowedAirColors = ["DEF"]; // TODO 搞清楚UGC里的'I'颜色，在C2S里，对应的字符串是什么
    private static string AirColorTag(ChuNote n)
    {
        if (allowedAirColors.Contains(n.Tag)) return n.Tag;
        else return "DEF";
    }

    private static string? FormatNote(ChuNote n, int tpm, List<Alert> alerts)
    {
        var (m, o) = Utils.BarAndTick(n.Time, tpm);
        var durTicks = Utils.Tick(n.Duration, tpm);
        return n.Type switch
        {
            "TAP" => $"TAP\t{m}\t{o}\t{n.Cell}\t{n.Width}",
            "CHR" => $"CHR\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.Tag}",
            "HLD" or "HXD" => $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{durTicks}",
            "SLD" or "SLC" or "SXD" or "SXC" => $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{durTicks}\t{n.EndCell}\t{n.EndWidth}",
            "FLK" => $"FLK\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.Tag}",
            "AIR" or "AUR" or "AUL" or "ADW" or "ADR" or "ADL" =>
                    $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.TargetNote}\t{AirColorTag(n)}",
            "AHD" or "AHX" => $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.TargetNote}\t{durTicks}\t{AirColorTag(n)}",
            "ASD" or "ASC" => FormatAsdAsc(n, m, o, durTicks),
            "ALD" => FormatAld(n, m, o),
            "MNE" => $"MNE\t{m}\t{o}\t{n.Cell}\t{n.Width}",
            _ => alert(),
        };

        string? alert()
        {
            alerts.Add(new Alert(Alert.LEVEL.Warning, Locale.C2SUnknownNoteType, n.Time));
            return null;
        }
    }

    private static string FormatAsdAsc(ChuNote n, int m, int o, int durTicks)
    {
        var e0 = n.ExtraData.Count > 0 ? n.ExtraData[0] : 5;
        var e1 = n.ExtraData.Count > 1 ? n.ExtraData[1] : 5;
        return $"{n.Type}\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{n.TargetNote}\t{e0}\t{durTicks}\t{n.EndCell}\t{n.EndWidth}\t{e1}\t{AirColorTag(n)}";
    }

    private static string FormatAld(ChuNote n, int m, int o)
    {
        var a = n.ExtraData.Count > 0 ? n.ExtraData[0] : 0;
        var b = n.ExtraData.Count > 1 ? n.ExtraData[1] : 0;
        var c = n.ExtraData.Count > 2 ? n.ExtraData[2] : 0;
        var tail = n.ExtraData.Count > 3 ? n.ExtraData[3] : 0;
        return $"ALD\t{m}\t{o}\t{n.Cell}\t{n.Width}\t{a}\t{b}\t{c}\t{n.EndCell}\t{n.EndWidth}\t{tail}";
    }
}
