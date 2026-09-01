// Port of src/core/interpreter.ts — minimal phase 1 implementation covering common commands
// Full port will mirror TS file line-for-line; this stub handles FO, A, A@, CF, FH, FD, FS, GB, etc. for smoke tests
using Zplr.Renderer.Types;

namespace Zplr.Renderer.Core;

public static class Interpreter
{
    public sealed record StoredGraphic(byte[] Data, int BytesPerRow, int Width, int Height);
    public sealed record InterpretResourceContext(
        IReadOnlyDictionary<string, StoredGraphic> Graphics,
        IReadOnlyDictionary<string, string> FontAliases,
        IReadOnlyDictionary<string, string> MemoryAliases,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int,int>> Encodings,
        LayoutFontResources FontResources
    );
    public sealed class InterpretOptions
    {
        public int Dpi { get; set; } = 200;
        public int LabelIndex { get; set; } = 0;
        public IReadOnlyDictionary<string, StoredGraphic>? Graphics { get; set; }
        public int? MaxGraphicBytes { get; set; }
        public IReadOnlyDictionary<string, string>? FontAliases { get; set; }
        public IReadOnlyDictionary<string, string>? MemoryAliases { get; set; }
        public IReadOnlyDictionary<string, IReadOnlyDictionary<int,int>>? Encodings { get; set; }
        public LayoutFontResources? FontResources { get; set; }
        public Func<ZplCommandNode, InterpretResourceContext?>? ResourcesAt { get; set; }
    }

    private abstract class PendingBarcode { public string Symbology=""; public int CommandIndex; public Orientation Orientation; public int ModuleWidth, Height; public double Ratio=3; public bool PrintBelow, PrintAbove; public LayoutFont InterpretationFont=null!; }
    private sealed class PendingCode39 : PendingBarcode { public bool Mod43; }
    private sealed class PendingCode128 : PendingBarcode { public bool Ucc; public string Mode="N"; }
    private sealed class PendingQr : PendingBarcode { public string Model="2"; public int Magnification; public string Reliability="Q"; public int Mask; public new int ModuleWidth; public new int Height; }
    private sealed class PendingExtended : PendingBarcode { public string Encoder=""; public bool Matrix; }

    public static string NormalizeResourceName(string value, string defaultExtension = "GRF", IReadOnlyDictionary<string,string>? memoryAliases = null)
    {
        var normalized = value.Trim().ToUpperInvariant();
        if (!normalized.Contains(":")) normalized = $"R:{normalized}";
        var sep = normalized.IndexOf(':');
        var device = normalized[..sep];
        if (string.IsNullOrEmpty(device)) device = "R";
        var obj = normalized[(sep+1)..];
        if (obj == "") obj = "UNKNOWN";
        else if (obj.StartsWith(".")) obj = $"UNKNOWN{obj}";
        normalized = $"{device}:{obj}";
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\.[A-Z0-9]+$")) normalized += $".{defaultExtension}";
        var drive = normalized[0].ToString();
        if (memoryAliases != null && memoryAliases.TryGetValue(drive, out var mapped))
            normalized = $"{mapped}:{normalized[(normalized.IndexOf(':')+1)..]}";
        return normalized;
    }

    private static readonly string Cp850High = "ÇüéâäàåçêëèïîìÄÅÉæÆôöòûùÿÖÜø£Ø×ƒáíóúñÑªº¿®¬½¼¡«»░▒▓│┤ÁÂÀ©╣║╗╝¢¥┐└┴┬├─┼ãÃ╚╔╩╦╠═╬¤ðÐÊËÈıÍÎÏ┘┌█▄¦Ì▀ÓßÔÒõÕµþÞÚÛÙýÝ¯´\u00AD±‗¾¶§÷¸°¨·¹³²■\u00A0";
    private static readonly List<Dictionary<int,string>> InternationalReplacements = new(){
        new(), new(), new Dictionary<int,string>{{35,"£"}}, new Dictionary<int,string>{{35,"£"},{64,"¾"},{91,"ĳ"},{92,"½"},{93,"|"},{123,"¨"},{124,"ƒ"},{125,"¼"},{126,"´"}},
        new Dictionary<int,string>{{35,"£"},{91,"Æ"},{92,"Ø"},{93,"Å"},{123,"æ"},{124,"ø"},{125,"å"}},
        new Dictionary<int,string>{{64,"É"},{91,"Ä"},{92,"Ö"},{93,"Å"},{94,"Ü"},{96,"é"},{123,"ä"},{124,"ö"},{125,"å"},{126,"ü"}},
        new Dictionary<int,string>{{64,"§"},{91,"Ä"},{92,"Ö"},{93,"Ü"},{123,"ä"},{124,"ö"},{125,"ü"},{126,"ß"}},
        new Dictionary<int,string>{{35,"£"},{64,"à"},{91,"°"},{92,"ç"},{93,"§"},{123,"é"},{124,"ù"},{125,"è"},{126,"¨"}},
        new Dictionary<int,string>{{35,"£"},{64,"à"},{91,"°"},{92,"ç"},{93,"§"},{123,"é"},{124,"ù"},{125,"è"},{126,"¨"}},
        new Dictionary<int,string>{{35,"£"},{64,"§"},{91,"°"},{92,"ç"},{93,"é"},{96,"ù"},{123,"à"},{124,"ò"},{125,"è"},{126,"ì"}},
        new Dictionary<int,string>{{35,"£"},{64,"§"},{91,"¡"},{92,"Ñ"},{93,"¿"},{123,"°"},{124,"ñ"},{125,"ç"}},
        new(), new Dictionary<int,string>{{92,"¥"},{126,"‾"}},
    };
    private static readonly Dictionary<int,string> ZebraControlGlyphs = new(){{21,"€"}};

    private static string DecodeTableBytes(List<int> bytes, int characterSet, IReadOnlyDictionary<int,int> encoding){
        var sb=new System.Text.StringBuilder();
        for(int i=0;i<bytes.Count;){
            if(characterSet==26 && bytes[i]<=0x7F){ sb.Append((char)bytes[i++]); continue; }
            bool pairMode=characterSet==14||characterSet==26;
            int input= pairMode? ((bytes[i]<<8)|(i+1<bytes.Count? bytes[i+1]:0)): bytes[i];
            if(encoding.TryGetValue(input, out var mapped)) sb.Append(char.ConvertFromUtf32(mapped)); else sb.Append("\uFFFD");
            i+= pairMode?2:1;
        }
        return sb.ToString();
    }
    private static string DecodeFieldBytes(List<int> input, int characterSet, IReadOnlyDictionary<int,int> remap, IReadOnlyDictionary<int,int>? encoding){
        var bytes=input.Select(b=> remap.TryGetValue(b, out var v)? v:b).ToList();
        if(new[]{14,24,26}.Contains(characterSet) && encoding!=null) return DecodeTableBytes(bytes, characterSet, encoding);
        string? decoderName = characterSet==15?"shift_jis": characterSet==16?"euc-jp": characterSet==17||characterSet==29?"utf-16BE": characterSet==30?"utf-16LE": characterSet==31?"windows-1250": characterSet==33?"windows-1251": characterSet==34?"windows-1253": characterSet==35?"windows-1254": characterSet==36?"windows-1255": characterSet==27?"windows-1252": characterSet==28?"utf-8": characterSet==26?"gb18030": null;
        if(decoderName!=null){
            try{ var enc=System.Text.Encoding.GetEncoding(decoderName); return enc.GetString(bytes.Select(b=>(byte)b).ToArray()); } catch{}
        }
        var sb2=new System.Text.StringBuilder();
        foreach(var b in bytes){
            if(ZebraControlGlyphs.TryGetValue(b, out var ctrl)) sb2.Append(ctrl);
            else if(characterSet < InternationalReplacements.Count && InternationalReplacements[characterSet].TryGetValue(b, out var rep)) sb2.Append(rep);
            else if(b<0x80) sb2.Append((char)b);
            else {
                int idx=b-0x80;
                sb2.Append(idx>=0 && idx<Cp850High.Length? Cp850High[idx].ToString(): "\uFFFD");
            }
        }
        return sb2.ToString();
    }
    private static string ApplyEncodingTable(string value, IReadOnlyDictionary<int,int>? mapping){
        if(mapping==null) return value;
        var sb=new System.Text.StringBuilder();
        foreach(var ch in value.EnumerateRunes()){
            int cp=ch.Value;
            if(mapping.TryGetValue(cp, out var mapped)) sb.Append(char.ConvertFromUtf32(mapped)); else sb.Append(ch.ToString());
        }
        return sb.ToString();
    }
    private static string ConcatenateFieldData(string data, string indicator, IReadOnlyDictionary<string,string> values){
        if(string.IsNullOrEmpty(indicator)) return data;
        var result=new System.Text.StringBuilder();
        int cursor=0;
        while(cursor < data.Length){
            int start=data.IndexOf(indicator, cursor);
            if(start<0){ result.Append(data.Substring(cursor)); break; }
            int end=data.IndexOf(indicator, start+indicator.Length);
            if(end<0){ result.Append(data.Substring(cursor)); break; }
            result.Append(data.Substring(cursor, start-cursor));
            string descriptor=data.Substring(start+indicator.Length, end-start-indicator.Length);
            var parts=descriptor.Split(',').Select(p=>p.Trim()).ToArray();
            var numStr=parts.ElementAtOrDefault(0)?.Trim() ?? "";
            if(int.TryParse(numStr, out var n) && n>=0 && n<=9999 && values.TryGetValue(n.ToString(), out var src)){
                if(parts.Length==1) result.Append(src);
                else {
                    string dir=parts.ElementAtOrDefault(1)?.ToLower() ?? "";
                    bool posOk=int.TryParse(parts.ElementAtOrDefault(2), out var pos);
                    bool cntOk=int.TryParse(parts.ElementAtOrDefault(3), out var cnt);
                    if((dir=="f"||dir=="b") && posOk && cntOk && pos>0 && cnt>=0){
                        if(dir=="f") result.Append(src.Substring(Math.Min(pos-1, src.Length), Math.Min(cnt, Math.Max(0, src.Length-(pos-1)))));
                        else { int last=src.Length-pos; result.Append(src.Substring(Math.Max(0, last-cnt+1), Math.Min(cnt, last+1))); }
                    }
                }
            } else {
                result.Append(data.Substring(start, end-start+indicator.Length));
            }
            cursor=end+indicator.Length;
        }
        return result.ToString();
    }
    private static string DecodeHexFieldData(string data, string? indicator, int characterSet, IReadOnlyDictionary<int,int> remap, IReadOnlyDictionary<int,int>? encoding, ZplCommandNode node, List<ZplDiagnostic> diagnostics, int labelIndex){
        bool tableDecodesBytes= encoding!=null && new[]{14,24,26}.Contains(characterSet);
        string DecodeBytes(List<int> bytes){ var dec=DecodeFieldBytes(bytes, characterSet, remap, encoding); return tableDecodesBytes? dec: ApplyEncodingTable(dec, encoding); }
        string MapDirect(string ch){
            int cp=char.ConvertToUtf32(ch,0);
            if(cp<=255 && remap.TryGetValue(cp, out var mapped)){
                var dec=DecodeFieldBytes(new List<int>{mapped}, characterSet, new Dictionary<int,int>(), encoding);
                return tableDecodesBytes? dec: ApplyEncodingTable(dec, encoding);
            }
            return ApplyEncodingTable(ch, encoding);
        }
        if(string.IsNullOrEmpty(indicator)) return string.Concat(data.Select(c=> MapDirect(c.ToString())));
        var result=new System.Text.StringBuilder();
        var encoded=new List<int>();
        void Flush(){
            if(encoded.Count==0) return;
            try{ result.Append(DecodeBytes(encoded)); } catch{ diagnostics.Add(new ZplDiagnostic("INVALID_ENCODED_FIELD_DATA", ZplDiagnosticSeverity.Error, ZplDiagnosticPhase.Semantic, $"Field hexadecimal bytes are not valid for ^CI{characterSet}.", node.Span, null, node.Canonical, labelIndex)); result.Append("\uFFFD"); }
            encoded.Clear();
        }
        for(int i=0;i<data.Length;i++){
            if(data[i].ToString()!=indicator){ Flush(); int cp=char.ConvertToUtf32(data, i); string ch=char.ConvertFromUtf32(cp); result.Append(MapDirect(ch)); i+=ch.Length-1; continue; }
            string hex=i+2<data.Length? data.Substring(i+1,2): "";
            if(System.Text.RegularExpressions.Regex.IsMatch(hex, "^[0-9A-Fa-f]{2}$")){ encoded.Add(Convert.ToInt32(hex,16)); i+=2; }
            else{ Flush(); diagnostics.Add(new ZplDiagnostic("INVALID_HEX_ESCAPE", ZplDiagnosticSeverity.Error, ZplDiagnosticPhase.Semantic, $"Expected two hexadecimal digits after {indicator}.", node.Span, null, node.Canonical, labelIndex)); result.Append(indicator); }
        }
        Flush();
        return result.ToString();
    }

    public static LabelLayout InterpretLabel(ZplLabelNode label, InterpretOptions? options = null)
    {
        options ??= new InterpretOptions();
        var fields = new List<LayoutField>();
        var origins = new List<LayoutOrigin>();
        var diagnostics = new List<ZplDiagnostic>();

        var defaultFont = new LayoutFont("A", null, 9, 5, Orientation.N);
        var defaultOrientation = Orientation.N;
        int homeX = 0, homeY = 0;
        bool labelReverse = false;
        int currentX = 0, currentY = 0;
        LayoutFont? currentFont = null;
        string? hexIndicator = null;
        string? pendingData = null;
        int? pendingDataIndex = null;
        int? currentXSet = null, currentYSet = null;
        bool reverse = false;
        string? fontName = null;
        PendingBarcode? pendingBarcode = null;
        int characterSet = 0;
        ZplCommandNode? characterSetNode = null;
        var characterRemap = new Dictionary<int,int>();
        IReadOnlyDictionary<int,int>? encoding = null;
        // Extended state for full port
        var barcodeDefaults = new { ModuleWidth = 2, Ratio = 3.0, Height = 10 };
        int barcodeModuleWidth = barcodeDefaults.ModuleWidth;
        double barcodeRatio = barcodeDefaults.Ratio;
        int barcodeHeight = barcodeDefaults.Height;
        string measurementUnit = "D";
        double dotConversion = 1;
        var fieldBlock = (LayoutFieldBlock?)null;
        string? fieldDirection = null;
        int fieldGap = 0;
        string? fieldOriginJustification = null;
        var multipleOrigins = new List<(int x,int y)?>();
        string? fieldHex = null;
        int dotsPerMillimeter = options.Dpi == 150 ? 6 : options.Dpi == 200 ? 8 : options.Dpi == 300 ? 12 : 24;
        double MeasurementScale() => measurementUnit == "I" ? dotsPerMillimeter * 25.4 : measurementUnit == "M" ? dotsPerMillimeter : dotConversion;

        int Dpi() => options.Dpi;
        int DotValue(string? v, int fallback, int min, int max)
        {
            var t = v?.Trim() ?? "";
            if (t == "") return fallback;
            var d = ZplNumbers.ZplNumber(t);
            if (d == null) return fallback;
            var scaled = d.Value * MeasurementScale();
            return Math.Clamp((int)Math.Round(scaled), min, max);
        }

        // Diagnostics for unsupported/partial etc. are handled in DocumentParser; here we just interpret
        bool fieldSeparated = false;
        foreach (var node in label.Commands)
        {
            if (node.Capability != CommandCapabilityStatus.Supported && node.Capability != CommandCapabilityStatus.Partial) {
                if (node.Code.StartsWith("B")) {
                    // Mark unsupported barcode selection for diagnostics later
                }
                continue;
            }
            var args = node.Parameters;
            // Handle field-separated flag for LL etc.
            if (node.Canonical == "^FS") fieldSeparated = true;

            switch (node.Code)
            {
                case "MU": {
                    var unit = (args.ElementAtOrDefault(0)?.Trim().ToUpperInvariant() ?? "");
                    if (unit == "") measurementUnit = "D";
                    else if (unit == "D" || unit == "I" || unit == "M") measurementUnit = unit;
                    // dotConversion uses ZplNumbers logic
                    dotConversion = ZplNumbers.ZplDotConversion(args.ElementAtOrDefault(1), args.ElementAtOrDefault(2), dotConversion);
                    break;
                }
                case "LH":
                    homeX = DotValue(args.ElementAtOrDefault(0), 0, 0, 32000);
                    homeY = DotValue(args.ElementAtOrDefault(1), 0, 0, 32000);
                    break;
                case "FO":
                    currentX = homeX + DotValue(args.ElementAtOrDefault(0), 0, 0, 32000);
                    currentY = homeY + DotValue(args.ElementAtOrDefault(1), 0, 0, 32000);
                    fieldOriginJustification = (args.ElementAtOrDefault(2)?.Trim() ?? "") == "1" ? "R" : (args.ElementAtOrDefault(2)?.Trim() == "2" ? "A" : "L");
                    currentXSet = currentX; currentYSet = currentY;
                    origins.Add(new LayoutOrigin(currentX, currentY, node.Index, node.Span));
                    break;
                case "FT":
                    currentX = homeX + DotValue(args.ElementAtOrDefault(0), currentX, 0, 32000);
                    currentY = homeY + DotValue(args.ElementAtOrDefault(1), currentY, 0, 32000);
                    fieldOriginJustification = (args.ElementAtOrDefault(2)?.Trim() ?? "") == "1" ? "R" : (args.ElementAtOrDefault(2)?.Trim() == "2" ? "A" : "L");
                    currentXSet = currentX; currentYSet = currentY;
                    origins.Add(new LayoutOrigin(currentX, currentY, node.Index, node.Span));
                    break;
                case "A":
                    {
                        var fo = args.ElementAtOrDefault(0) ?? "";
                        var key = fo.Length > 0 ? fo[0].ToString() : defaultFont.Key;
                        var orientChar = fo.Length > 1 ? fo[1].ToString() : null;
                        var orient = orientChar != null ? OrientationExtensions.FromZplCode(orientChar, defaultOrientation) : defaultOrientation;
                        var h = DotValue(args.ElementAtOrDefault(1), 0, 0, 32000);
                        var w = DotValue(args.ElementAtOrDefault(2), 0, 0, 32000);
                        if (h == 0 && w == 0) { h = defaultFont.Height; w = defaultFont.Width; }
                        else if (h == 0) h = defaultFont.Height;
                        else if (w == 0) w = defaultFont.Width;
                        currentFont = new LayoutFont(key, options.FontAliases != null && options.FontAliases.TryGetValue(key, out var alias) ? alias : null, h, w, orient, options.FontResources);
                    }
                    break;
                case "A@":
                    {
                        var orient = OrientationExtensions.FromZplCode(args.ElementAtOrDefault(0), defaultOrientation);
                        var h = DotValue(args.ElementAtOrDefault(1), 0, 0, 32000);
                        var w = DotValue(args.ElementAtOrDefault(2), 0, 0, 32000);
                        var name = args.ElementAtOrDefault(3)?.Trim();
                        if (!string.IsNullOrEmpty(name)) fontName = name;
                        var effName = fontName;
                        if (h == 0 && w == 0) { h = defaultFont.Height; w = defaultFont.Width; }
                        else if (h == 0) h = defaultFont.Height;
                        else if (w == 0) w = defaultFont.Width;
                        currentFont = new LayoutFont(effName != null ? "@" : defaultFont.Key, effName, h, w, orient, options.FontResources);
                    }
                    break;
                case "CF":
                    {
                        var k = args.ElementAtOrDefault(0)?.Trim() ?? "";
                        if (k == "") k = defaultFont.Key;
                        var h = DotValue(args.ElementAtOrDefault(1), 0, 0, 32000);
                        var w = DotValue(args.ElementAtOrDefault(2), 0, 0, 32000);
                        if (h == 0) h = defaultFont.Height;
                        if (w == 0) w = defaultFont.Width;
                        defaultFont = new LayoutFont(k, options.FontAliases != null && options.FontAliases.TryGetValue(k, out var alias) ? alias : null, h, w, defaultOrientation, options.FontResources);
                    }
                    break;
                case "FW":
                    defaultOrientation = OrientationExtensions.FromZplCode(args.ElementAtOrDefault(0), defaultOrientation);
                    defaultFont = new LayoutFont(defaultFont.Key, defaultFont.Name, defaultFont.Height, defaultFont.Width, defaultOrientation, defaultFont.Resources);
                    break;
                case "BY":
                    barcodeModuleWidth = Math.Clamp(DotValue(args.ElementAtOrDefault(0), barcodeModuleWidth, 1, 10), 1, 10);
                    barcodeRatio = Math.Round(Math.Clamp(double.TryParse(args.ElementAtOrDefault(1)?.Trim() ?? "", out var r) ? r : barcodeRatio, 2, 3) * 10) / 10;
                    barcodeHeight = DotValue(args.ElementAtOrDefault(2), barcodeHeight, 1, 32000);
                    break;
                case "FH":
                    hexIndicator = node.RawParameters.Length > 0 ? node.RawParameters[0].ToString() : "_";
                    break;
                case "FR":
                    reverse = true;
                    break;
                case "FD":
                case "FV":
                    {
                        // Handle ^FE concatenation
                        string raw = node.RawParameters;
                        var prevFE = node.Index > 0 ? label.Commands[node.Index - 1] : null;
                        string concatenated = raw;
                        if (node.Code == "FD" && prevFE != null && prevFE.Canonical == "^FE") {
                            string ind = prevFE.RawParameters.Length>0? prevFE.RawParameters[0].ToString(): "#";
                            var fnVals = new Dictionary<string,string>();
                            // Collect FN values for this label
                            foreach(var c in label.Commands){
                                if(c.Canonical=="^FN"){
                                    var num = c.Parameters.ElementAtOrDefault(0)?.Trim() ?? "";
                                    if(int.TryParse(num, out var n) && n>=0 && n<=9999){
                                        // Find following FD
                                        for(int idx=c.Index+1; idx<label.Commands.Count; idx++){
                                            var nxt=label.Commands[idx];
                                            if(nxt.Canonical=="^FD" || nxt.Canonical=="^FV"){ fnVals[n.ToString()]=nxt.RawParameters; break; }
                                            if(nxt.Canonical=="^FS"||nxt.Canonical=="^FN") break;
                                        }
                                    }
                                }
                            }
                            concatenated = ConcatenateFieldData(raw, ind, fnVals);
                        }
                        string decoded = DecodeHexFieldData(concatenated, hexIndicator, characterSet, characterRemap, encoding, node, diagnostics, 0);
                        pendingData = decoded;
                        pendingDataIndex = node.Index;
                        // Handle characterSetNode diagnostic for field
                        if(characterSetNode!=null){
                            diagnostics.Add(new ZplDiagnostic("UNSUPPORTED_CHARACTER_SET", ZplDiagnosticSeverity.Warning, ZplDiagnosticPhase.Semantic, $"{characterSetNode.Code}{characterSetNode.RawParameters} character-set mapping was not applied to this field.", node.Span, null, node.Canonical, null));
                        }
                    }
                    break;
                case "FB":
                    {
                        int w = DotValue(args.ElementAtOrDefault(0), 0, 0, 32000);
                        int maxLines = Math.Clamp(int.TryParse(args.ElementAtOrDefault(1)?.Trim() ?? "", out var ml) ? ml : 1, 1, 9999);
                        int lineSpacing = DotValue(args.ElementAtOrDefault(2), 0, -9999, 9999);
                        string just = new[]{"L","C","R","J"}.Contains(args.ElementAtOrDefault(3)?.Trim() ?? "") ? args.ElementAtOrDefault(3)!.Trim() : "L";
                        int hanging = DotValue(args.ElementAtOrDefault(4), 0, 0, 9999);
                        fieldBlock = new LayoutFieldBlock(w, maxLines, lineSpacing, just, hanging, null, "FB");
                    }
                    break;
                case "TB":
                    {
                        var orient = OrientationExtensions.FromZplCode(args.ElementAtOrDefault(0), currentFont?.Orientation ?? defaultOrientation);
                        if (currentFont != null) currentFont = currentFont with { Orientation = orient };
                        else currentFont = defaultFont with { Orientation = orient };
                        int w = DotValue(args.ElementAtOrDefault(1), 1, 1, 32000);
                        int h = DotValue(args.ElementAtOrDefault(2), 1, 1, 32000);
                        fieldBlock = new LayoutFieldBlock(w, 9999, 0, fieldOriginJustification == "R" ? "R" : "L", 0, h, "TB");
                    }
                    break;
                case "FP":
                    {
                        string dir = (args.ElementAtOrDefault(0)?.Trim().ToUpperInvariant() ?? "");
                        fieldDirection = new[]{"H","V","R"}.Contains(dir) ? dir : "H";
                        fieldGap = DotValue(args.ElementAtOrDefault(1), 0, 0, 9999);
                    }
                    break;
                case "FM":
                    {
                        multipleOrigins.Clear();
                        for(int i=0;i+1 < args.Count && multipleOrigins.Count < 60; i+=2){
                            if ((args[i]?.Trim().ToLower() == "e") || (args[i+1]?.Trim().ToLower() == "e")) multipleOrigins.Add(null);
                            else multipleOrigins.Add((homeX + DotValue(args[i],0,0,32000), homeY + DotValue(args[i+1],0,0,32000)));
                        }
                    }
                    break;
                case "GB":
                    {
                        var w = DotValue(args.ElementAtOrDefault(0), 0, 0, 32000);
                        var h = DotValue(args.ElementAtOrDefault(1), 0, 0, 32000);
                        var t = DotValue(args.ElementAtOrDefault(2), 1, 1, 32000);
                        if (currentXSet != null)
                        {
                            var color = (args.ElementAtOrDefault(3)?.Trim().ToUpperInvariant() ?? "B") == "W" ? "W" : "B";
                            var rounding = DotValue(args.ElementAtOrDefault(4), 0, 0, 8);
                            // If there's pendingData, GB is inside a field and will be rendered as field graphic; otherwise immediate
                            // For now, handle as immediate if no pending data
                            if (pendingData == null) {
                                fields.Add(new BoxLayoutField(currentXSet.Value, currentYSet ?? 0, Orientation.N, reverse||labelReverse, node.Index, node.Span, w, h, t, color, rounding));
                            } else {
                                // Store as pending graphic to be flushed at FS
                                // Simplified: create box field directly at FS time via pendingData path - for now just immediate
                                fields.Add(new BoxLayoutField(currentXSet.Value, currentYSet ?? 0, Orientation.N, reverse||labelReverse, node.Index, node.Span, w, h, t, color, rounding));
                            }
                        }
                    }
                    break;
                case "GC":
                    {
                        int d = DotValue(args.ElementAtOrDefault(0), 3, 3, 4095);
                        int t = DotValue(args.ElementAtOrDefault(1), 1, 1, 4095);
                        string col = (args.ElementAtOrDefault(2)?.Trim().ToUpperInvariant() ?? "B") == "W" ? "W" : "B";
                        if (currentXSet != null) fields.Add(new CircleLayoutField(currentXSet.Value, currentYSet ?? 0, Orientation.N, reverse||labelReverse, node.Index, node.Span, d, t, col));
                    }
                    break;
                case "GE":
                    {
                        int w = DotValue(args.ElementAtOrDefault(0), 3, 3, 4095);
                        int h = DotValue(args.ElementAtOrDefault(1), 3, 3, 4095);
                        int t = DotValue(args.ElementAtOrDefault(2), 1, 1, 4095);
                        string col = (args.ElementAtOrDefault(3)?.Trim().ToUpperInvariant() ?? "B") == "W" ? "W" : "B";
                        if (currentXSet != null) fields.Add(new EllipseLayoutField(currentXSet.Value, currentYSet ?? 0, Orientation.N, reverse||labelReverse, node.Index, node.Span, w, h, t, col));
                    }
                    break;
                case "GD":
                    {
                        int w = DotValue(args.ElementAtOrDefault(0), 3, 3, 32000);
                        int h = DotValue(args.ElementAtOrDefault(1), 3, 3, 32000);
                        int t = DotValue(args.ElementAtOrDefault(2), 1, 1, 32000);
                        string col = (args.ElementAtOrDefault(3)?.Trim().ToUpperInvariant() ?? "B") == "W" ? "W" : "B";
                        string dir = (args.ElementAtOrDefault(4)?.Trim().ToUpperInvariant() ?? "R") == "L" ? "L" : "R";
                        if (currentXSet != null) fields.Add(new DiagonalLayoutField(currentXSet.Value, currentYSet ?? 0, Orientation.N, reverse||labelReverse, node.Index, node.Span, w, h, t, col, dir));
                    }
                    break;
                case "GF":
                    {
                        // Graphic Field - decode hex/compressed data
                        var format = (args.ElementAtOrDefault(0)?.Trim().ToUpperInvariant() ?? "A");
                        if (!new[]{"A","B","C"}.Contains(format)) { diagnostics.Add(new ZplDiagnostic("UNSUPPORTED_GRAPHIC_FORMAT", ZplDiagnosticSeverity.Error, ZplDiagnosticPhase.Semantic, $"Graphic field format {format} is invalid; use A, B, or C.", node.Span, null, node.Canonical, null)); break; }
                        try {
                            int transmitted = DotValue(args.ElementAtOrDefault(1), 0, 1, 32000000);
                            int expected = DotValue(args.ElementAtOrDefault(2), transmitted, 1, 32000000);
                            int bpr = DotValue(args.ElementAtOrDefault(3), 0, 1, 32000);
                            string source = string.Join(node.Delimiter, args.Skip(4));
                            var decoded = format=="A" ? GraphicDecoder.DecodeGraphic(source, bpr, expected, options.MaxGraphicBytes ?? 16*1024*1024) : GraphicDecoder.DecodeBinaryGraphic(source, bpr, transmitted, expected, format=="C", options.MaxGraphicBytes ?? 16*1024*1024);
                            if (currentXSet != null) fields.Add(new BitmapLayoutField(currentXSet.Value, currentYSet ?? 0, Orientation.N, reverse||labelReverse, node.Index, node.Span, decoded.Width, decoded.Height, decoded.BytesPerRow, decoded.Data, 1, 1));
                        } catch(Exception ex){ diagnostics.Add(new ZplDiagnostic(ex is GraphicDecodeError gde? gde.Code: "INVALID_GRAPHIC_DATA", ZplDiagnosticSeverity.Error, ZplDiagnosticPhase.Semantic, ex.Message, node.Span, null, node.Canonical, null)); }
                    }
                    break;
                case "XG":
                    {
                        var name = NormalizeResourceName(args.ElementAtOrDefault(0)??"", "GRF", options.MemoryAliases);
                        if (options.Graphics != null && options.Graphics.TryGetValue(name, out var graphic) && currentXSet != null){
                            int sx = DotValue(args.ElementAtOrDefault(1), 1, 1, 10);
                            int sy = DotValue(args.ElementAtOrDefault(2), 1, 1, 10);
                            fields.Add(new BitmapLayoutField(currentXSet.Value, currentYSet ?? 0, Orientation.N, reverse||labelReverse, node.Index, node.Span, graphic.Width, graphic.Height, graphic.BytesPerRow, graphic.Data, sx, sy));
                        } else if (currentXSet != null) diagnostics.Add(new ZplDiagnostic("MISSING_GRAPHIC_RESOURCE", ZplDiagnosticSeverity.Error, ZplDiagnosticPhase.Semantic, $"Graphic {name} is not present in this render session.", node.Span, null, node.Canonical, null));
                    }
                    break;
                case "IM":
                    {
                        var name = NormalizeResourceName(args.ElementAtOrDefault(0)??"", "GRF", options.MemoryAliases);
                        if (options.Graphics != null && options.Graphics.TryGetValue(name, out var graphic) && currentXSet != null){
                            fields.Add(new BitmapLayoutField(currentXSet.Value, currentYSet ?? 0, Orientation.N, reverse||labelReverse, node.Index, node.Span, graphic.Width, graphic.Height, graphic.BytesPerRow, graphic.Data, 1, 1));
                        } else if (currentXSet != null) diagnostics.Add(new ZplDiagnostic("MISSING_GRAPHIC_RESOURCE", ZplDiagnosticSeverity.Error, ZplDiagnosticPhase.Semantic, $"Graphic {name} is not present in this render session.", node.Span, null, node.Canonical, null));
                    }
                    break;
                case "IL":
                    {
                        var name = NormalizeResourceName(args.ElementAtOrDefault(0)??"", "GRF", options.MemoryAliases);
                        if (options.Graphics != null && options.Graphics.TryGetValue(name, out var graphic)){
                            fields.Add(new BitmapLayoutField(0, 0, Orientation.N, labelReverse, node.Index, node.Span, graphic.Width, graphic.Height, graphic.BytesPerRow, graphic.Data, 1, 1));
                        } else diagnostics.Add(new ZplDiagnostic("MISSING_GRAPHIC_RESOURCE", ZplDiagnosticSeverity.Error, ZplDiagnosticPhase.Semantic, $"Image {name} is not present in this render session.", node.Span, null, node.Canonical, null));
                    }
                    break;
                case "GS":
                    {
                        string code = (pendingData?.Trim().ToUpperInvariant() ?? "").Length > 0 ? pendingData!.Trim().ToUpperInvariant()[0].ToString() : "";
                        if (new[]{"A","B","C","D","E"}.Contains(code) && currentXSet != null) {
                            int w2 = currentFont?.Width ?? defaultFont.Width;
                            int h2 = currentFont?.Height ?? defaultFont.Height;
                            // Use GS args for orientation/height/width
                            var orient = OrientationExtensions.FromZplCode(args.ElementAtOrDefault(0), defaultOrientation);
                            int h = DotValue(args.ElementAtOrDefault(1), h2, 0, 32000);
                            int w = DotValue(args.ElementAtOrDefault(2), w2, 0, 32000);
                            fields.Add(new GraphicSymbolLayoutField(currentXSet.Value, currentYSet ?? 0, orient, reverse||labelReverse, node.Index, node.Span, code, w, h));
                        }
                        pendingData = null; // GS consumes FD
                    }
                    break;
                case "FS":
                    {
                        if (pendingBarcode != null && pendingData != null && currentXSet != null)
                        {
                            // Create barcode field from pendingBarcode + pendingData
                            LayoutField? bf = null;
                            if (pendingBarcode is PendingCode39 c39) bf = new Code39LayoutField(currentXSet.Value, currentYSet ?? 0, c39.Orientation, reverse||labelReverse, c39.CommandIndex, c39.CommandIndex==pendingDataIndex? new SourceSpan(pendingDataIndex.Value,pendingDataIndex.Value): node.Span, pendingData, c39.ModuleWidth, c39.Height, c39.PrintBelow, c39.PrintAbove, c39.InterpretationFont, false, c39.Ratio, c39.Mod43);
                            else if (pendingBarcode is PendingCode128 c128) bf = new Code128LayoutField(currentXSet.Value, currentYSet ?? 0, c128.Orientation, reverse||labelReverse, c128.CommandIndex, node.Span, pendingData, c128.ModuleWidth, c128.Height, c128.PrintBelow, c128.PrintAbove, c128.InterpretationFont, false, c128.Ucc, c128.Mode);
                            else if (pendingBarcode is PendingQr qr) {
                                // Parse QR data minimally
                                bf = new QrLayoutField(currentXSet.Value, currentYSet ?? 0, qr.Orientation, reverse||labelReverse, qr.CommandIndex, node.Span, pendingData, qr.ModuleWidth, qr.Height, false, false, qr.InterpretationFont, false, qr.Model, qr.ModuleWidth, qr.Reliability, qr.Mask, "A", null, null, null);
                            } else if (pendingBarcode is PendingExtended ext) {
                                bf = new ExtendedBarcodeLayoutField(currentXSet.Value, currentYSet ?? 0, ext.Orientation, reverse||labelReverse, ext.CommandIndex, node.Span, pendingData, ext.ModuleWidth, ext.Height, false, false, ext.InterpretationFont, false, ext.Symbology, ext.Encoder, ext.Matrix, null, null, new Dictionary<string,object>());
                            }
                            if (bf != null) fields.Add(bf);
                        }
                        else if (pendingData != null && currentXSet != null)
                        {
                            var font = currentFont ?? defaultFont;
                            var block = fieldBlock;
                            var field = new TextLayoutField(
                                currentXSet.Value, currentYSet ?? 0, font.Orientation, reverse || labelReverse,
                                pendingDataIndex ?? node.Index, node.Span,
                                pendingData, font, block, null, fieldDirection ?? "H", fieldGap, fieldOriginJustification ?? "L", new AdvancedTextOptions(false,false,false,false)
                            );
                            fields.Add(field);
                        } else if (pendingData == null && fieldBlock != null) {
                            // FB without data? ignore
                        }
                        pendingData = null; pendingDataIndex = null;
                        pendingBarcode = null;
                        currentFont = null;
                        hexIndicator = null;
                        reverse = false;
                        fieldBlock = null;
                        fieldDirection = null;
                        fieldGap = 0;
                        fieldOriginJustification = null;
                    }
                    break;
                case "B3":
                    {
                        var orient = OrientationExtensions.FromZplCode(args.ElementAtOrDefault(0), defaultOrientation);
                        bool mod43 = (args.ElementAtOrDefault(1)?.Trim().ToUpperInvariant() ?? "N") == "Y";
                        int h = DotValue(args.ElementAtOrDefault(2), barcodeHeight, 1, 32000);
                        var (printBelow, printAbove) = (args.ElementAtOrDefault(3)?.Trim().ToUpperInvariant()=="Y", args.ElementAtOrDefault(4)?.Trim().ToUpperInvariant()=="Y");
                        // Simplified: printBelow if Y and not above
                        bool below = (args.ElementAtOrDefault(3)?.Trim().ToUpperInvariant()=="Y") && !(args.ElementAtOrDefault(4)?.Trim().ToUpperInvariant()=="Y");
                        bool above = (args.ElementAtOrDefault(3)?.Trim().ToUpperInvariant()=="Y") && (args.ElementAtOrDefault(4)?.Trim().ToUpperInvariant()=="Y");
                        pendingBarcode = new PendingCode39{ Symbology="B3", CommandIndex=node.Index, Orientation=orient, ModuleWidth=barcodeModuleWidth, Height=h, Ratio=barcodeRatio, Mod43=mod43, PrintBelow=below, PrintAbove=above, InterpretationFont=currentFont??defaultFont };
                    }
                    break;
                case "BC":
                    {
                        var orient = OrientationExtensions.FromZplCode(args.ElementAtOrDefault(0), defaultOrientation);
                        int h = DotValue(args.ElementAtOrDefault(1), barcodeHeight, 1, 32000);
                        string mode = (args.ElementAtOrDefault(5)?.Trim().ToUpperInvariant() ?? "N");
                        if(!new[]{"N","A","U","D"}.Contains(mode)) mode="N";
                        bool ucc = (args.ElementAtOrDefault(4)?.Trim().ToUpperInvariant() ?? "N")=="Y";
                        pendingBarcode = new PendingCode128{ Symbology="BC", CommandIndex=node.Index, Orientation=orient, ModuleWidth=barcodeModuleWidth, Height=h, Mode=mode, Ucc=ucc, PrintBelow=true, PrintAbove=false, InterpretationFont=currentFont??defaultFont };
                    }
                    break;
                case "BQ":
                    {
                        var orient = OrientationExtensions.FromZplCode(args.ElementAtOrDefault(0), defaultOrientation);
                        string model = (args.ElementAtOrDefault(1)?.Trim() ?? "2"); if(model!="1" && model!="2") model="2";
                        int mag = int.TryParse(args.ElementAtOrDefault(2)?.Trim() ?? "", out var m)? Math.Clamp(m,1,10): 2;
                        string rel = (args.ElementAtOrDefault(3)?.Trim().ToUpperInvariant() ?? "Q"); if(!new[]{"H","Q","M","L"}.Contains(rel)) rel="Q";
                        int mask = int.TryParse(args.ElementAtOrDefault(4)?.Trim() ?? "", out var mk)? Math.Clamp(mk,0,7): 7;
                        pendingBarcode = new PendingQr{ Symbology="BQ", CommandIndex=node.Index, Orientation=orient, ModuleWidth=mag, Height=mag, Model=model, Reliability=rel, Mask=mask, PrintBelow=false, PrintAbove=false, InterpretationFont=currentFont??defaultFont };
                    }
                    break;
                case "BX":
                    {
                        var orient = OrientationExtensions.FromZplCode(args.ElementAtOrDefault(0), defaultOrientation);
                        pendingBarcode = new PendingExtended{ Symbology="BX", CommandIndex=node.Index, Orientation=orient, ModuleWidth=barcodeModuleWidth, Height=barcodeHeight, Encoder="datamatrix", Matrix=true, InterpretationFont=currentFont??defaultFont };
                    }
                    break;
                case "B7":
                case "B0":
                case "BO":
                case "B1":
                case "B2":
                case "B4":
                case "B5":
                case "B8":
                case "B9":
                case "BE":
                case "BU":
                case "BA":
                case "BB":
                case "BD":
                case "BF":
                case "BI":
                case "BJ":
                case "BK":
                case "BL":
                case "BM":
                case "BP":
                case "BR":
                case "BS":
                case "BT":
                case "BZ":
                    {
                        var orient = OrientationExtensions.FromZplCode(args.ElementAtOrDefault(0), defaultOrientation);
                        pendingBarcode = new PendingExtended{ Symbology=node.Code, CommandIndex=node.Index, Orientation=orient, ModuleWidth=barcodeModuleWidth, Height=barcodeHeight, Encoder="code128", Matrix=false, InterpretationFont=currentFont??defaultFont };
                    }
                    break;
                case "CI":
                    {
                        var requested = (args.ElementAtOrDefault(0)?.Trim() ?? "0");
                        bool isNum = int.TryParse(requested, out var sel);
                        bool accepted = false;
                        if(isNum){
                            accepted = (sel>=0 && sel<=17) || sel==24 || sel==26 || (sel>=27 && sel<=31) || (sel>=33 && sel<=36);
                        }
                        if(accepted){
                            characterSet = sel;
                            characterSetNode = null;
                        } else {
                            characterSetNode = node;
                        }
                        var remap = new Dictionary<int,int>();
                        if(isNum && sel>=0 && sel<=13){
                            for(int idx=1; idx+1 < args.Count && remap.Count<256; idx+=2){
                                var srcStr = args[idx]?.Trim() ?? "";
                                var dstStr = args[idx+1]?.Trim() ?? "";
                                bool srcOk = int.TryParse(srcStr, out var src);
                                bool dstOk = int.TryParse(dstStr, out var dst);
                                if(srcOk && dstOk && src>=0 && src<=255 && dst>=0 && dst<=255 && dst!=32){
                                    remap[dst]=src;
                                }
                            }
                        }
                        characterRemap = remap;
                    }
                    break;
                case "SE":
                    {
                        var name = NormalizeResourceName(args.ElementAtOrDefault(0)??"", "DAT", options.MemoryAliases);
                        if(options.Encodings != null && options.Encodings.TryGetValue(name, out var enc)){
                            encoding = enc;
                        } else {
                            encoding = null;
                            diagnostics.Add(new ZplDiagnostic("MISSING_ENCODING_RESOURCE", ZplDiagnosticSeverity.Error, ZplDiagnosticPhase.Semantic, $"Encoding table {name} is not present in this render session.", node.Span, null, node.Canonical, null));
                        }
                    }
                    break;
                case "PA":
                    // Advanced text properties - store for field
                    // For now, just ignore but keep structure
                    break;
                case "CV":
                    // Code validation - handled as persistent, but also store
                    break;
                default:
                    break;
            }
        }
        return new LabelLayout { Fields = fields, Origins = origins, Diagnostics = diagnostics, Settings = new LabelSettings(null,null,0,0,false,false,labelReverse) };
    }
}
