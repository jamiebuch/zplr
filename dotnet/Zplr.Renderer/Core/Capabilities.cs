// Port of src/core/capabilities.ts
using Zplr.Renderer.Types;

namespace Zplr.Renderer.Core;

public static class Capabilities
{
    private const string Reference = "https://docs.zebra.com/us/en/printers/software/zpl-pg/c-zpl-zpl-commands.html";

    private sealed record CapabilitySeed(string Name, CommandCategory Category, CommandEffect Effect, CommandPersistenceScope Scope, CommandCapabilityStatus Status, IReadOnlyList<string>? Limitations = null);

    private static CapabilitySeed Raster(string name, CommandCategory category, CommandPersistenceScope scope)
        => new(name, category, CommandEffect.Raster, scope, CommandCapabilityStatus.Supported);

    private static CapabilitySeed Job(string name, CommandCategory category, CommandPersistenceScope scope)
        => new(name, category, CommandEffect.Job, scope, CommandCapabilityStatus.Supported);

    private static CapabilitySeed Partial(string name, CommandCategory category, CommandPersistenceScope scope, IReadOnlyList<string> limitations, CommandEffect effect = CommandEffect.Raster)
        => new(name, category, effect, scope, CommandCapabilityStatus.Partial, limitations);

    private static readonly Dictionary<string, CapabilitySeed> CapabilitySeeds = new()
    {
        ["^A"] = Raster("Scalable/Bitmapped Font", CommandCategory.Text, CommandPersistenceScope.Field),
        ["^A@"] = Raster("Font by Name", CommandCategory.Text, CommandPersistenceScope.Field),
        ["^B0"] = Raster("Aztec Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^B1"] = Raster("Code 11 Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^B2"] = Raster("Interleaved 2 of 5 Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^B3"] = Raster("Code 39 Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^B4"] = Raster("Code 49 Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^B5"] = Raster("Planet Code Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^B7"] = Raster("PDF417 Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^B8"] = Raster("EAN-8 Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^B9"] = Raster("UPC-E Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BA"] = Raster("Code 93 Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BB"] = Raster("CODABLOCK Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BC"] = Raster("Code 128 Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BD"] = Raster("UPS MaxiCode Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BE"] = Raster("EAN-13 Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BF"] = Raster("MicroPDF417 Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BI"] = Raster("Industrial 2 of 5 Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BJ"] = Raster("Standard 2 of 5 Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BK"] = Raster("ANSI Codabar Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BL"] = Raster("LOGMARS Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BM"] = Raster("MSI Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BO"] = Raster("Aztec Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BP"] = Raster("Plessey Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BQ"] = Raster("QR Code Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BR"] = Raster("GS1 DataBar Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BS"] = Raster("UPC/EAN Extension Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BT"] = Raster("TLC39 Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BU"] = Raster("UPC-A Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BX"] = Raster("Data Matrix Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^BY"] = Raster("Bar Code Field Default", CommandCategory.Barcode, CommandPersistenceScope.Session),
        ["^BZ"] = Raster("Postal Bar Code", CommandCategory.Barcode, CommandPersistenceScope.Field),
        ["^CC"] = Job("Change Caret", CommandCategory.Format, CommandPersistenceScope.Session),
        ["~CC"] = Job("Change Caret", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^CD"] = Job("Change Delimiter", CommandCategory.Format, CommandPersistenceScope.Session),
        ["~CD"] = Job("Change Delimiter", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^CF"] = Raster("Change Default Font", CommandCategory.Text, CommandPersistenceScope.Session),
        ["^CI"] = Partial("Change International Font/Encoding", CommandCategory.Text, CommandPersistenceScope.Session, new[] { "Table-specific EUC-CN and non-GB18030 ^CI16/^CI26 variants are not inferred without a compatible downloaded mapping; standard Unicode, Western, Shift-JIS, EUC-JP, and GB18030 paths are implemented." }),
        ["^CV"] = Raster("Code Validation", CommandCategory.Barcode, CommandPersistenceScope.Session),
        ["^CM"] = Job("Change Memory Letter Designation", CommandCategory.Storage, CommandPersistenceScope.Session),
        ["^CT"] = Job("Change Tilde", CommandCategory.Format, CommandPersistenceScope.Session),
        ["~CT"] = Job("Change Tilde", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^CW"] = Job("Font Identifier", CommandCategory.Text, CommandPersistenceScope.Session),
        ["~DB"] = Job("Download Bitmap Font", CommandCategory.Storage, CommandPersistenceScope.Session),
        ["~DE"] = Job("Download Encoding", CommandCategory.Storage, CommandPersistenceScope.Session),
        ["^DF"] = Job("Download Format", CommandCategory.Storage, CommandPersistenceScope.Session),
        ["~DG"] = Job("Download Graphics", CommandCategory.Storage, CommandPersistenceScope.Session),
        ["~DS"] = Job("Download Intellifont", CommandCategory.Storage, CommandPersistenceScope.Session),
        ["~DT"] = Job("Download Bounded TrueType Font", CommandCategory.Storage, CommandPersistenceScope.Session),
        ["~DU"] = Job("Download Unbounded TrueType Font", CommandCategory.Storage, CommandPersistenceScope.Session),
        ["~DY"] = Partial("Download Object", CommandCategory.Storage, CommandPersistenceScope.Session, new[] { "Legacy BAR-ONE AR-compressed (C) payloads are diagnosed but not decoded.", "Image decoding covers non-interlaced 1/2/4/8-bit grayscale or indexed PNG plus 8-bit-per-channel grayscale-alpha, RGB, or RGBA PNG; Windows BMP and OS/2 1.x core-header BMP; and 1-bit planar, 8-bit indexed/grayscale, or 24-bit RGB PCX." }, CommandEffect.Job),
        ["~EG"] = Job("Erase Download Graphics", CommandCategory.Storage, CommandPersistenceScope.Session),
        ["^FB"] = Raster("Field Block", CommandCategory.Text, CommandPersistenceScope.Field),
        ["^FC"] = Partial("Field Clock", CommandCategory.Text, CommandPersistenceScope.Field, new[] { "Localized weekday and month names use host Intl locale data; numeric clock fields remain deterministic, but localized wording can vary by runtime and printer firmware." }),
        ["^FD"] = Raster("Field Data", CommandCategory.Text, CommandPersistenceScope.Field),
        ["^FE"] = Raster("Field End", CommandCategory.Text, CommandPersistenceScope.Field),
        ["^FH"] = Raster("Field Hexadecimal Indicator", CommandCategory.Text, CommandPersistenceScope.Field),
        ["^FN"] = Raster("Field Number", CommandCategory.Storage, CommandPersistenceScope.Field),
        ["^FO"] = Raster("Field Origin", CommandCategory.Format, CommandPersistenceScope.Field),
        ["^FP"] = Partial("Field Parameter", CommandCategory.Text, CommandPersistenceScope.Field, new[] { "Vertical and reverse layout operates on Unicode code points rather than firmware combining semantic clusters." }),
        ["^FR"] = Raster("Field Reverse Print", CommandCategory.Format, CommandPersistenceScope.Field),
        ["^FS"] = Raster("Field Separator", CommandCategory.Format, CommandPersistenceScope.Field),
        ["^FT"] = Partial("Field Typeset", CommandCategory.Format, CommandPersistenceScope.Field, new[] { "Explicit text origins and text justification are modeled; omitted-coordinate continuation and printer-specific baseline/justification interactions for every field type are not fully reproduced." }),
        ["^FV"] = Raster("Field Variable", CommandCategory.Text, CommandPersistenceScope.Field),
        ["^FL"] = Job("Font Linking", CommandCategory.Text, CommandPersistenceScope.Session),
        ["^FM"] = Raster("Multiple Field Origin Locations", CommandCategory.Format, CommandPersistenceScope.Field),
        ["^FW"] = Raster("Field Orientation", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^FX"] = Job("Comment", CommandCategory.Format, CommandPersistenceScope.Field),
        ["^GB"] = Raster("Graphic Box", CommandCategory.Graphic, CommandPersistenceScope.Field),
        ["^GC"] = Raster("Graphic Circle", CommandCategory.Graphic, CommandPersistenceScope.Field),
        ["^GD"] = Raster("Graphic Diagonal Line", CommandCategory.Graphic, CommandPersistenceScope.Field),
        ["^GE"] = Raster("Graphic Ellipse", CommandCategory.Graphic, CommandPersistenceScope.Field),
        ["^GF"] = Partial("Graphic Field", CommandCategory.Graphic, CommandPersistenceScope.Field, new[] { "Zebra compressed-binary (C) payloads are diagnosed but not decoded." }),
        ["^GS"] = Raster("Graphic Symbol", CommandCategory.Graphic, CommandPersistenceScope.Field),
        ["^ID"] = Job("Object Delete", CommandCategory.Storage, CommandPersistenceScope.Session),
        ["^IL"] = Raster("Image Load", CommandCategory.Graphic, CommandPersistenceScope.Field),
        ["^KL"] = Partial("Define Language", CommandCategory.Text, CommandPersistenceScope.Session, new[] { "Localized weekday and month names use host Intl locale data and can vary by runtime and printer firmware." }, CommandEffect.Job),
        ["^IM"] = Raster("Image Move", CommandCategory.Graphic, CommandPersistenceScope.Field),
        ["^IS"] = Job("Image Save", CommandCategory.Storage, CommandPersistenceScope.Session),
        ["^LH"] = Raster("Label Home", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^LL"] = Raster("Label Length", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^LR"] = Raster("Label Reverse Print", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^LS"] = Raster("Label Shift", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^LT"] = Raster("Label Top", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^MC"] = Raster("Map Clear", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^ML"] = Raster("Maximum Label Length", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^MN"] = Raster("Media Tracking", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^MU"] = Raster("Set Units of Measurement", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^PM"] = Raster("Printing Mirror Image", CommandCategory.Format, CommandPersistenceScope.Format),
        ["^PA"] = Partial("Advanced Text Properties", CommandCategory.Text, CommandPersistenceScope.Session, new[] { "Bidirectional ordering and Arabic shaping use a deterministic subset rather than a full Unicode/OpenType layout engine; the default-glyph switch is not modeled." }),
        ["^PO"] = Raster("Print Orientation", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^PQ"] = Job("Print Quantity", CommandCategory.Format, CommandPersistenceScope.Format),
        ["^PW"] = Raster("Print Width", CommandCategory.Format, CommandPersistenceScope.Session),
        ["^SE"] = Raster("Select Encoding Table", CommandCategory.Text, CommandPersistenceScope.Session),
        ["^SF"] = Partial("Serialization Field", CommandCategory.Text, CommandPersistenceScope.Field, new[] { "Serialization is Unicode-code-point based; newer printer-firmware combining-cluster and control/bidirectional-character behavior is not modeled." }),
        ["^SL"] = Partial("Set Mode and Language", CommandCategory.Text, CommandPersistenceScope.Session, new[] { "Localized weekday and month names use host Intl locale data and can vary by runtime and printer firmware." }),
        ["^SN"] = Raster("Serialization Data", CommandCategory.Text, CommandPersistenceScope.Field),
        ["^SO"] = Raster("Set Offset", CommandCategory.Text, CommandPersistenceScope.Session),
        ["^ST"] = Job("Set Date and Time", CommandCategory.Text, CommandPersistenceScope.Session),
        ["^TB"] = Partial("Text Blocks", CommandCategory.Text, CommandPersistenceScope.Field, new[] { "Rotation, bounds, wrapping, truncation, and literal << escapes are modeled; other firmware complex-layout escape instructions are skipped." }),
        ["^TO"] = Job("Transfer Object", CommandCategory.Storage, CommandPersistenceScope.Session),
        ["^XA"] = Job("Start Format", CommandCategory.Format, CommandPersistenceScope.Format),
        ["^XF"] = Job("Recall Format", CommandCategory.Storage, CommandPersistenceScope.Field),
        ["^XG"] = Raster("Recall Graphic", CommandCategory.Graphic, CommandPersistenceScope.Field),
        ["^XZ"] = Job("End Format", CommandCategory.Format, CommandPersistenceScope.Format),
    };

    private static IReadOnlyList<string> Words(string value)
    {
        var t = value.Trim();
        return string.IsNullOrEmpty(t) ? Array.Empty<string>() : t.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static readonly IReadOnlyList<string> ZplCommands = Words(@"
^A ^A@ ^B0 ^B1 ^B2 ^B3 ^B4 ^B5 ^B7 ^B8 ^B9 ^BA ^BB ^BC ^BD ^BE ^BF ^BI ^BJ
^BK ^BL ^BM ^BO ^BP ^BQ ^BR ^BS ^BT ^BU ^BX ^BY ^BZ ^CC ~CC ^CD ~CD ^CF ^CI
^CM ^CN ^CO ^CP ^CT ~CT ^CV ^CW ~DB ~DE ^DF ~DG ~DN ~DS ~DT ~DU ~DY ~EG ^FB
^FC ^FD ^FE ^FH ^FL ^FM ^FN ^FO ^FP ^FR ^FS ^FT ^FV ^FW ^FX ^GB ^GC ^GD ^GE
^GF ^GS ~HB ~HD ^HF ^HG ^HH ~HI ~HM ~HQ ~HS ^HT ~HU ^HV ^HW ^HY ^HZ ^ID ^IL
^IM ^IS ~JA ^JB ~JB ~JC ~JD ~JE ~JF ~JG ^JH ^JI ~JI ^JJ ~JL ^JM ~JN ~JO ~JP
~JQ ~JR ^JS ~JS ^JT ^JU ^JW ~JX ^JZ ~KB ^KD ^KL ^KN ^KP ^KV ^LF ^LH ^LL ^LR
^LS ^LT ^MA ^MC ^MD ^MF ^MI ^ML ^MM ^MN ^MP ^MT ^MU ^MW ^PA ^PF ^PH ~PH ~PL
^PM ~PM ^PN ^PO ^PP ~PP ^PQ ~PR ^PR ~PS ^PW ~RO ^SC ~SD ^SE ^SF ^SI ^SL ^SN
^SO ^SP ^SQ ^SR ^SS ^ST ^SX ^SZ ~TA ^TB ^TO ~WC ^WD ~WQ ^XA ^XB ^XF ^XG ^XS
^XZ ^ZZ
");

    private static readonly IReadOnlyList<string> NetworkCommands = Words(@"
^KC ^NB ^NC ~NC ^ND ^NI ^NN ^NP ~NR ^NS ^NT ~NT ^NW ^WA ^WE ^WL ~WL ^WP ^WR
~WR ^WS ^WX
");

    private static readonly IReadOnlyList<string> RfidCommands = Words(@"^HL ~HL ^HR ^RB ^RF ^RL ^RS ^RU ^RW");

    private static readonly Dictionary<string, string> KnownNames = new()
    {
        ["^B0"] = "Aztec Bar Code",
        ["^B1"] = "Code 11 Bar Code",
        ["^B2"] = "Interleaved 2 of 5 Bar Code",
        ["^B4"] = "Code 49 Bar Code",
        ["^B5"] = "Planet Code Bar Code",
        ["^BA"] = "Code 93 Bar Code",
        ["^BB"] = "CODABLOCK Bar Code",
        ["^BD"] = "UPS MaxiCode Bar Code",
        ["^BF"] = "MicroPDF417 Bar Code",
        ["^BI"] = "Industrial 2 of 5 Bar Code",
        ["^BJ"] = "Standard 2 of 5 Bar Code",
        ["^BK"] = "ANSI Codabar Bar Code",
        ["^BL"] = "LOGMARS Bar Code",
        ["^BM"] = "MSI Bar Code",
        ["^BO"] = "Aztec Bar Code",
        ["^BP"] = "Plessey Bar Code",
        ["^BR"] = "GS1 DataBar Bar Code",
        ["^BS"] = "UPC/EAN Extension Bar Code",
        ["^BT"] = "TLC39 Bar Code",
        ["^BZ"] = "Postal Bar Code",
        ["^CM"] = "Change Memory Letter Designation",
        ["~DB"] = "Download Bitmap Font",
        ["~DE"] = "Download Encoding",
        ["~DS"] = "Download Intellifont",
        ["~DT"] = "Download Bounded TrueType Font",
        ["~DU"] = "Download Unbounded TrueType Font",
        ["~DY"] = "Download Object",
        ["~EG"] = "Erase Download Graphics",
        ["^FC"] = "Field Clock",
        ["^FE"] = "Field End",
        ["^FL"] = "Font Linking",
        ["^FM"] = "Multiple Field Origin Locations",
        ["^FP"] = "Field Parameter",
        ["^GS"] = "Graphic Symbol",
        ["^IL"] = "Image Load",
        ["^IM"] = "Image Move",
        ["^IS"] = "Image Save",
        ["^MC"] = "Map Clear",
        ["^ML"] = "Maximum Label Length",
        ["^MU"] = "Set Units of Measurement",
        ["^PA"] = "Advanced Text Properties",
        ["^SE"] = "Select Encoding Table",
        ["^SF"] = "Serialization Field",
        ["^SN"] = "Serialization Data",
        ["^TB"] = "Text Blocks",
        ["^TO"] = "Transfer Object",
        ["^SZ"] = "Set ZPL Version",
        ["^MD"] = "Media Darkness",
        ["^MM"] = "Print Mode",
        ["^MN"] = "Media Tracking",
        ["^MT"] = "Media Type",
        ["^PQ"] = "Print Quantity",
        ["^PR"] = "Print Rate",
        ["~JA"] = "Cancel All",
        ["~JC"] = "Set Media Sensor Calibration",
        ["~TA"] = "Tear-Off Adjust",
    };

    private static CommandCapability CanonicalCapability(string canonical, CapabilitySeed seed)
        => new(canonical, canonical[0].ToString(), canonical[1..], seed.Name, seed.Category, seed.Effect, seed.Scope, seed.Status, seed.Limitations, Reference);

    private static CommandCapability DefaultCapability(string canonical, CommandCategory category, CommandCapabilityStatus status, CommandPersistenceScope scope = CommandPersistenceScope.Session)
    {
        var name = KnownNames.TryGetValue(canonical, out var n) ? n : $"Recognized ZPL command {canonical}";
        var effect = status == CommandCapabilityStatus.NonRendering ? CommandEffect.Device : CommandEffect.Raster;
        var limitations = status == CommandCapabilityStatus.NonRendering
            ? new[] { "Recognized by the renderer but has no label-raster effect." }
            : new[] { "Recognized by the modern profile but not rendered by this release." };
        var scopeResolved = scope;
        return new CommandCapability(canonical, canonical[0].ToString(), canonical[1..], name, category, effect, scopeResolved, status, limitations, Reference);
    }

    public static readonly IReadOnlyList<CommandCapability> CommandCapabilities;

    private static readonly Dictionary<string, CommandCapability> CapabilityMap;

    static Capabilities()
    {
        var allCanonical = new HashSet<string>(ZplCommands.Concat(NetworkCommands).Concat(RfidCommands));
        var list = new List<CommandCapability>();
        foreach (var canonical in allCanonical)
        {
            if (CapabilitySeeds.TryGetValue(canonical, out var seed))
            {
                // seed currently missing Canonical/Prefix/Code, fill from key
                var c = new CommandCapability(canonical, canonical[0].ToString(), canonical[1..], seed.Name, seed.Category, seed.Effect, seed.Scope, seed.Status, seed.Limitations, Reference);
                list.Add(c);
            }
            else if (NetworkCommands.Contains(canonical))
                list.Add(DefaultCapability(canonical, CommandCategory.Network, CommandCapabilityStatus.NonRendering));
            else if (RfidCommands.Contains(canonical))
                list.Add(DefaultCapability(canonical, CommandCategory.Rfid, CommandCapabilityStatus.NonRendering));
            else if (canonical.StartsWith("^B"))
                list.Add(DefaultCapability(canonical, CommandCategory.Barcode, CommandCapabilityStatus.Unsupported, CommandPersistenceScope.Field));
            else
                list.Add(DefaultCapability(canonical, CommandCategory.Printer, CommandCapabilityStatus.NonRendering));
        }

        // Add explicit JM/SZ unsupported not in allCanonical set? They are in ZplCommands, already handled; need special cases
        // Override JM and SZ to unsupported with specific limitations
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Canonical == "^JM")
                list[i] = new CommandCapability("^JM", "^", "JM", "Set Dots per Millimeter", CommandCategory.Format, CommandEffect.Job, CommandPersistenceScope.Session, CommandCapabilityStatus.Unsupported, new[] { "Changing printhead resolution inside a ZPL job is not modeled; select printDensity in the render options." }, Reference);
            if (list[i].Canonical == "^SZ")
                list[i] = new CommandCapability("^SZ", "^", "SZ", "Set ZPL Version", CommandCategory.Format, CommandEffect.Job, CommandPersistenceScope.Session, CommandCapabilityStatus.Unsupported, new[] { "ZPL mode 1 compatibility semantics are not modeled." }, Reference);
        }

        CommandCapabilities = list;
        CapabilityMap = list.ToDictionary(c => c.Canonical, c => c);
    }

    public static CommandCapability? GetCommandCapability(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var normalized = command.Trim().ToUpperInvariant();
        if (!normalized.StartsWith("^") && !normalized.StartsWith("~")) return null;
        return CapabilityMap.TryGetValue(normalized, out var cap) ? cap : null;
    }

    public static CommandCapabilityStatus GetCommandCapabilityStatus(string command)
        => GetCommandCapability(command)?.Status ?? CommandCapabilityStatus.Unknown;

    public static CommandEffect? GetCommandEffect(string command)
        => GetCommandCapability(command)?.Effect;
}
