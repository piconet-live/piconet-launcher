using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Online.Protocol;

namespace Launcher.Core;

/// <summary>
/// The machine setting's curve and the energy boost's length read back from a modified main module
/// (the protocol's handbook/stat-pack-constants.md, "The import"). The setting, fn_1_22B10, reads every number of its curve through r4
/// from the pool (MODULE_DASH_POOL): a mod changes a number by pointing a load at another word of the pool (F-Zero
/// GX+: .text+0x22B48, lfs f4,0x7C(r4) to lfs f4,0xE8(r4), the speed side's drift loss 1.8 to 1.0), or by changing a
/// word of the pool that only this function reads. Either is carried as the slot the load's role has. A changed
/// word another routine reads too, a load turned into another instruction, and a change to a load that tests the
/// setting's side are not: each is a line, and left out.
/// </summary>
public static partial class StatPackImport
{
    private const int CurveAt = (int)GameFacts.Files.ModuleSettingCurve;
    private const int BoostLengthAt = (int)GameFacts.Files.ModuleBoostLength;

    // lfs f2, 0x258(r31): the energy boost's 60.0 (MODULE_BOOST_LENGTH)
    private const uint BoostLengthLoad = 0xC05F0258;

    // fn_1_22B10's loads through r4 (MODULE_SETTING_CURVE + offset) and the game's word of each; the six of 0.0 that
    // test the setting's side are among them (Sign)
    private static readonly (int At, uint Stock, bool Sign)[] CurveLoads =
    [
        (0x00C, 0xC004001C, false),
        (0x010, 0xC064000C, true),
        (0x020, 0xC0040014, false),
        (0x038, 0xC084007C, false),
        (0x054, 0xC0640014, false),
        (0x058, 0xC0240080, false),
        (0x064, 0xC0840078, false),
        (0x080, 0xC0640084, false),
        (0x090, 0xC0840088, false),
        (0x0A8, 0xC0A40088, false),
        (0x0C4, 0xC064008C, false),
        (0x0D4, 0xC064000C, true),
        (0x0E4, 0xC064001C, false),
        (0x0F8, 0xC0640068, false),
        (0x110, 0xC084000C, true),
        (0x130, 0xC0640090, false),
        (0x134, 0xC0E40014, false),
        (0x140, 0xC0E40094, false),
        (0x148, 0xC064001C, false),
        (0x158, 0xC0640098, false),
        (0x16C, 0xC0E4009C, false),
        (0x170, 0xC06400A0, false),
        (0x180, 0xC06400A4, false),
        (0x184, 0xC08400A8, false),
        (0x18C, 0xC0640014, false),
        (0x1A0, 0xC0640068, false),
        (0x1A4, 0xC0840090, false),
        (0x1AC, 0xC0A400AC, false),
        (0x1C0, 0xC0640038, false),
        (0x1CC, 0xC064000C, true),
        (0x1D8, 0xC02400B0, false),
        (0x1DC, 0xC0640078, false),
        (0x204, 0xC88400B8, false),
        (0x22C, 0xC0240090, false),
        (0x238, 0xC02400C0, false),
        (0x23C, 0xC0C40014, false),
        (0x248, 0xC0240084, false),
        (0x258, 0xC02400C4, false),
        (0x264, 0xC0240084, false),
        (0x26C, 0xC024000C, true),
        (0x278, 0xC0240078, false),
        (0x284, 0xC08400B0, false),
        (0x28C, 0xC02400C8, false),
        (0x2BC, 0xC02400CC, false),
        (0x2CC, 0xC02400B0, false),
        (0x2E0, 0xC024000C, true),
        (0x2F4, 0xC0040038, false),
        (0x2F8, 0xC0240014, false),
        (0x314, 0xC0040068, false),
        (0x318, 0xC0240014, false),
        (0x330, 0xC0040060, false),
        (0x338, 0xC0640014, false),
        (0x360, 0xC0040018, false),
    ];

    // each slot of the curve and the load whose number it is (a role the game's register carried on from a load:
    // that load's, port/image/sites_curve.py)
    private static readonly (string Slot, int Load)[] CurveSlots =
    [
        ("SettingMiddle", 0x00C),
        ("DriftFastFloor", 0x020),
        ("AccelFastTarget", 0x010),
        ("SpeedFastRef", 0x020),
        ("DriftFastLoss", 0x038),
        ("SpeedSlowRef", 0x010),
        ("DriftSlowEdge1", 0x054),
        ("AccelSlowTarget", 0x058),
        ("DriftSlowTarget1", 0x064),
        ("DriftSlowGain1", 0x064),
        ("DriftSlowEdge2", 0x080),
        ("DriftSlowGain2", 0x090),
        ("DriftSlowGain3", 0x0A8),
        ("DriftCap", 0x0C4),
        ("BoostKeepAccel", 0x0E4),
        ("BoostKeepSpeed", 0x0F8),
        ("SpeedFastAccelEdge1", 0x130),
        ("SpeedFastGainMid", 0x134),
        ("SpeedFastGainLow", 0x140),
        ("SpeedFastAccelEdge2", 0x148),
        ("SpeedFastSpeedEdge", 0x158),
        ("SpeedFastGainHighSlow", 0x110),
        ("SpeedFastGainHigh", 0x16C),
        ("SpeedFastScale", 0x170),
        ("SpeedSlowOffset", 0x180),
        ("SpeedSlowSpan", 0x184),
        ("SpeedSlowClamp", 0x18C),
        ("SpeedSlowSlope", 0x1A0),
        ("SpeedSlowBase", 0x1A4),
        ("SpeedSlowScale", 0x1AC),
        ("AccelSlowHighEdge", 0x1C0),
        ("AccelSlowHighTarget", 0x1D8),
        ("AccelSlowHighGain", 0x1DC),
        ("AccelGain", 0x204),
        ("TurnDecelAccelEdge", 0x22C),
        ("TurnDecelLowAccelEdge", 0x238),
        ("TurnDecelBase", 0x23C),
        ("SpeedLowAccelFactor", 0x248),
        ("TurnDecelLowAccelFactor", 0x248),
        ("TurnDecelHighEdge", 0x258),
        ("TurnDecelHighFactor", 0x264),
        ("TurnDecelSlowFactor", 0x278),
        ("TurnDecelGain", 0x284),
        ("TurnDecelFloor", 0x28C),
        ("LightWeight", 0x2BC),
        ("LightAccelCap", 0x2CC),
        ("TurnMoveFastLoss", 0x2F4),
        ("TurnMoveFastBase", 0x2F8),
        ("TurnMoveSlowGain", 0x314),
        ("TurnMoveSlowBase", 0x318),
        ("GripGain", 0x330),
        ("GripBase", 0x338),
        ("BoostGain", 0x360),
        ("BoostBase", 0x338),
    ];

    // the pool's words read by fn_1_22B10 alone in main.rel (every load of the game's code whose address is the word):
    // a mod's change to one is the curve's alone
    private static readonly HashSet<int> CurveOwnWords = [0x80, 0x8C, 0x94, 0x9C, 0xA0, 0xA8, 0xAC, 0xC0, 0xC4, 0xCC];

    /// <summary>The curve's and the boost length's constants where the modified module differs, the words it read
    /// added to <paramref name="taken"/>, and a line for each change not carried.</summary>
    [SuppressMessage("Maintainability", "CA1502:AvoidExcessiveComplexity",
        Justification = "Pre-existing when the code metrics were turned on: to be broken up when the class is next worked on")]
    private static void CurveDifferences(ReadOnlySpan<byte> disc, ReadOnlySpan<byte> changed, List<StatEntry> constants,
        List<string> ignored, HashSet<int> taken)
    {
        uint Word(ReadOnlySpan<byte> m, int at) => BinaryPrimitives.ReadUInt32BigEndian(m[at..]);
        var values = new Dictionary<int, (uint High, uint Low)?>();   // a load's number in the mod; null: not carried
        foreach (var (offset, stock, sign) in CurveLoads)
        {
            var at = CurveAt + offset;
            var size = stock >> 26 == 50 ? 8 : 4;
            var word = Word(changed, at);
            var place = (int)(word & 0xFFFF);
            var discPlace = DashPoolAt + (int)(Word(disc, at) & 0xFFFF);
            if (word == Word(disc, at) && (discPlace + size > changed.Length || discPlace + size > disc.Length
                                           || (Word(changed, discPlace) == Word(disc, discPlace)
                                               && (size == 4 || Word(changed, discPlace + 4) == Word(disc, discPlace + 4)))))
            {
                continue;   // the disc's load of the disc's number
            }
            if (Word(disc, at) != stock)
            {
                values[offset] = null;
                Say(ignored, string.Create(CultureInfo.InvariantCulture,
                    $"the disc's machine setting is not the one the launcher knows at .text+0x{CurveFunction + offset:X}: its change there is left out"));
                continue;
            }
            // the same load at another word of the pool (inside the file), or the game's own load
            var shape = (word & 0xFFFF0000) == (stock & 0xFFFF0000) && place < 0x8000 && place % 4 == 0
                        && DashPoolAt + place + size <= changed.Length;
            if (!shape)
            {
                values[offset] = null;
                Say(ignored, string.Create(CultureInfo.InvariantCulture,
                    $"its machine setting's load at .text+0x{CurveFunction + offset:X} is changed into another instruction: left out"));
                continue;
            }
            var pool = DashPoolAt + place;
            var number = (Word(changed, pool), size == 8 ? Word(changed, pool + 4) : 0u);
            var own = (Word(disc, pool), size == 8 ? Word(disc, pool + 4) : 0u);
            if (word == stock && number != own && !CurveOwnWords.Contains(place))
            {
                // the word changed for every routine that reads it: not the curve's alone
                values[offset] = null;
                Say(ignored, string.Create(CultureInfo.InvariantCulture,
                    $"its pool word .rodata+0x{0x840 + place:X}, which the machine setting and other routines read, is changed: left out"));
                // named here, so not counted again among the other words
                taken.Add(pool);
                if (size == 8)
                {
                    taken.Add(pool + 4);
                }
                continue;
            }
            values[offset] = number;
            if (word != stock)
            {
                taken.Add(at);
            }
            else if (number != own)
            {
                taken.Add(pool);
                if (size == 8)
                {
                    taken.Add(pool + 4);
                }
            }
            if (sign && number != (Word(disc, DashPoolAt + (int)(stock & 0xFFFF)), 0u))
            {
                values[offset] = null;
                Say(ignored, string.Create(CultureInfo.InvariantCulture,
                    $"its machine setting's test of the setting's side at .text+0x{CurveFunction + offset:X} reads another number: left out"));
            }
        }
        foreach (var (name, load) in CurveSlots)
        {
            var constant = StatPackConstants.Named(name)!;
            if (!values.TryGetValue(load, out var held) || held is not { } number)
            {
                continue;
            }
            var stockLow = constant.Kind == StatConstantKind.Number64High ? StatPackConstants.All[constant.Slot + 1].Stock : 0u;
            if (number.High == constant.Stock && number.Low == stockLow)
            {
                continue;
            }
            constants.Add(new StatEntry(StatPackConstants.Block, constant.Slot, number.High));
            if (constant.Kind == StatConstantKind.Number64High)
            {
                constants.Add(new StatEntry(StatPackConstants.Block, constant.Slot + 1, number.Low));
            }
            Say(ignored, string.Create(CultureInfo.InvariantCulture,
                $"its machine setting's {name} is {StatPackConstants.Text(constant, number.High, number.Low)} (the game's {StatPackConstants.Text(constant, constant.Stock, stockLow)}): carried"));
        }
        // the energy boost's 60.0: its load pointed at another word of the pool
        var boost = Word(changed, BoostLengthAt);
        if (boost != BoostLengthLoad && (boost & 0xFFFF0000) == (BoostLengthLoad & 0xFFFF0000) && (boost & 0xFFFF) < 0x8000
            && (boost & 3) == 0 && DashPoolAt + (int)(boost & 0xFFFF) + 4 <= changed.Length)
        {
            var scale = Word(changed, DashPoolAt + (int)(boost & 0xFFFF));
            taken.Add(BoostLengthAt);
            constants.Add(new StatEntry(StatPackConstants.Block, StatPackConstants.BoostLengthScale, scale));
            Say(ignored, string.Create(CultureInfo.InvariantCulture,
                $"its energy boost lasts int({BitConverter.UInt32BitsToSingle(scale).ToString("R", CultureInfo.InvariantCulture)} x boost duration) frames: carried as BoostLengthScale"));
        }
    }

    private static void Say(List<string> lines, string line)
    {
        if (!lines.Contains(line))
        {
            lines.Add(line);
        }
    }

    /// <summary>The longest boost duration a car data file gives a machine, or a custom machine (the sum of a body's,
    /// a cockpit's and a booster's, fn_1_14F6F8), in seconds.</summary>
    public static float LongestBoost(ReadOnlySpan<byte> carData)
    {
        float Duration(ReadOnlySpan<byte> data, int block) => BitConverter.UInt32BitsToSingle(Word(data, block, 0x38));
        var longest = 0f;
        for (var machine = 0; machine < StatPackRules.Machines; machine++)
        {
            longest = Math.Max(longest, Duration(carData, machine));
        }
        var kinds = StatPackRules.PartKinds.Count;
        var each = ((int)GameFacts.Files.CarParts) / kinds;
        var sum = 0f;
        for (var kind = 0; kind < kinds; kind++)
        {
            var most = float.MinValue;
            for (var part = 0; part < each; part++)
            {
                most = Math.Max(most, Duration(carData, StatPackRules.Machines + (kind * each) + part));
            }
            sum += most;
        }
        return Math.Max(longest, sum);
    }

    // fn_1_22B10's place in the code section (a .text+ offset)
    private const int CurveFunction = 0x22B10;
}
