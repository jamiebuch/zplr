// Port of src/core/jobRenderer.ts — expanded phase (session resources for ~DG/~DB/~DY, ^DF/^XF, ^ID/^TO, persistent)
using System.Text;
using System.Text.RegularExpressions;
using Zplr.Renderer.Helper.Rendering;
using Zplr.Renderer.Types;

namespace Zplr.Renderer.Core;

public static class JobRenderer
{
    private static readonly HashSet<string> PersistentCommands = new() { "BY","CF","CI","CV","FW","LH","LL","LR","LS","LT","ML","MN","MC","MU","PO","PA","PW","SE" };
    private static readonly Dictionary<string,string> DownloadObjectExtensions = new() { ["T"]="TTF", ["E"]="TTE", ["P"]="PNG", ["B"]="BMP", ["X"]="PCX", ["G"]="GRF", ["NRD"]="NRD", ["PAC"]="PAC", ["C"]="WML", ["F"]="HTM", ["H"]="GET" };
    private const int MaxSerializationFieldSize = 3 * 1024;
    // Perf: compile once - these are hot in job rendering (every ~DG/~DY/^DF command)
    private static readonly Regex DecimalIntRegex = new(@"^-?\d+$", RegexOptions.Compiled);
    private static readonly Regex ExtensionRegex = new(@"\.[A-Z0-9]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex FontAliasRegex = new(@"^[A-Z0-9]$", RegexOptions.Compiled);
    private static readonly Regex PatternWildRegex = new(@"\.[A-Z0-9*?]+$", RegexOptions.Compiled);
    private static readonly Regex DriveRegex = new(@"^[ABER]:", RegexOptions.Compiled);
    private static readonly Regex WrappedPrefixRegex = new(@"^:(?:B64|Z64):", RegexOptions.Compiled);
    private static readonly Regex DigitRunRegex = new(@"\d+", RegexOptions.Compiled);
    private static readonly Regex BitmapFontRegex = new(@"#([0-9A-Fa-f]{1,4})\.(-?\d+)\.(-?\d+)\.(-?\d+)\.(-?\d+)\.(-?\d+)\.([\s\S]*?)(?=#(?:[0-9A-Fa-f]{1,4})\.|$)", RegexOptions.Compiled);

    private sealed class StoredFormat { public List<ZplCommandNode> Commands = new(); public int Bytes; public SourceSpan DefinitionSpan = new(0,0); public int DocumentId; }
    private sealed class StoredObject { public byte[] Data = Array.Empty<byte>(); public string Kind="binary"; }

    private sealed class RtcOffset { public int Months, Days, Years, Hours, Minutes, Seconds; }
    private sealed class RtcState { public object Mode="S"; public int Language=1; public Dictionary<int,RtcOffset> Offsets=new(); public DateTime? Fixed; }

    private sealed class SessionState {
        public ZplSyntaxState Syntax = new("^","~",",");
        public Dictionary<string, Interpreter.StoredGraphic> Graphics=new();
        public Dictionary<string, StoredFormat> Formats=new();
        public Dictionary<string, string> FontAliases=new();
        public int ResourceBytes=0;
        public MonochromeRaster? RetainedRaster;
        public Dictionary<string,string> MemoryAliases=new(){{"A","A"},{"B","B"},{"E","E"},{"R","R"}};
        public Dictionary<string, StoredObject> Objects=new();
        public Dictionary<string, DownloadedBitmapFont> BitmapFonts=new();
        public Dictionary<string, IReadOnlyDictionary<int,int>> Encodings=new();
        public Dictionary<string, List<string>> FontLinks=new();
        public Dictionary<string, (ZplCommandNode Command,int DocumentId,int Bytes)> Persistent=new();
        public RtcState Rtc=new(){ Mode="S", Language=1, Offsets=new() };
        public int NextDocumentId=0;
    }

    private static SessionState NewState() => new();

    private static MonochromeRaster CloneRaster(MonochromeRaster r) => new(r.Width,r.Height,r.Stride,r.BitOrder,(byte[])r.Data.Clone());
    private static DateTime ClockNow(RenderJobOptions opts){
        if(opts.Clock is Func<DateTime> fn) return fn();
        if(opts.Clock is DateTime dt) return dt;
        return DateTime.UtcNow;
    }
    private static Match? RightmostNumber(string value){
        var ms=DigitRunRegex.Matches(value);
        return ms.Count>0? ms[^1]: null;
    }
    private static string SerializeNumber(string value, string increment, bool leadingZeros, int step){
        var m=RightmostNumber(value);
        if(m==null) return value;
        string digits=m.Value[^Math.Min(12,m.Value.Length)..];
        int digitsStart=m.Index + m.Value.Length - digits.Length;
        if(!long.TryParse(digits, out var start)) start=0;
        long delta=1;
        try{ delta=long.Parse((increment.Trim()==""?"1":increment.Trim())); } catch{ delta=1; }
        long next=start+delta*step;
        string sign=next<0?"-":"";
        string abs=Math.Abs(next).ToString();
        string rendered=leadingZeros? sign+abs.PadLeft(digits.Length,'0'): next.ToString();
        return value.Substring(0,digitsStart)+rendered+value.Substring(digitsStart+digits.Length);
    }
    private static string? SerializationAlphabet(string mask)=> mask switch{ "D"=>"0123456789", "d"=>"0123456789", "H"=>"0123456789ABCDEF", "h"=>"0123456789abcdef", "O"=>"01234567", "o"=>"01234567", "A"=>"ABCDEFGHIJKLMNOPQRSTUVWXYZ", "a"=>"abcdefghijklmnopqrstuvwxyz", "N"=>"0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ", "n"=>"0123456789abcdefghijklmnopqrstuvwxyz", _=>null };
    private static string SerializeMasked(string value, string mask, string increment, int step){
        var chars=value.ToCharArray().ToList(); var masks=mask.ToCharArray(); var incs=increment.ToCharArray();
        int start=chars.Count - masks.Length;
        var positions=new List<(int charIdx,int maskIdx,string alphabet)>();
        for(int mi=0;mi<masks.Length;mi++){
            var alpha=SerializationAlphabet(masks[mi].ToString());
            int ci=start+mi;
            if(alpha!=null && ci>=0 && ci<chars.Count) positions.Add((ci,mi,alpha));
        }
        if(positions.Count==0 || step==0) return value;
        long current=0, addition=0, mult=1;
        for(int i=positions.Count-1;i>=0;i--){
            var (ci,mi,alpha)=positions[i];
            int baseLen=alpha.Length;
            int curDigit=Math.Max(0, alpha.IndexOf(chars[ci]));
            current+= curDigit*mult;
            int incIdx=incs.Length - masks.Length + mi;
            char incChar= incIdx>=0 && incIdx<incs.Length? incs[incIdx]: '\0';
            int incDigit= incChar=='%'?0: Math.Max(0, alpha.IndexOf(incChar.ToString()));
            addition+= incDigit*mult;
            mult*= baseLen;
        }
        if(string.IsNullOrWhiteSpace(increment)) addition=1;
        long next=(current + addition*step) % mult; if(next<0) next+=mult;
        for(int i=positions.Count-1;i>=0;i--){
            var (ci,_,alpha)=positions[i];
            int baseLen=alpha.Length;
            int digit=(int)(next % baseLen); next/=baseLen;
            chars[ci]=alpha[digit];
        }
        return new string(chars.ToArray());
    }
    private static int SerializationFieldSize(ZplCommandNode cmd)=> (cmd.Parameters.ElementAtOrDefault(0)?.Length??0)+(cmd.Parameters.ElementAtOrDefault(1)?.Length??0);
    private static string Pad(int v,int len=2)=> v.ToString().PadLeft(len,'0');
    private static int DayOfYear(DateTime d){ var day= new DateTime(d.Year,d.Month,d.Day,0,0,0,DateTimeKind.Utc); var start=new DateTime(d.Year,1,1,0,0,0,DateTimeKind.Utc); return (int)((day-start).TotalDays)+1; }
    private static readonly string[] RtcLocales = new[]{"en","es","fr","de","it","nb","pt","sv","da","es","nl","fi","ja","ko","zh-CN","zh-TW","ru","pl"};
    private static string? RtcToken(DateTime date, string token, int language){
        string locale=RtcLocales[Math.Clamp(language,1,18)-1]??"en";
        int hour=date.Hour; int ordinal=DayOfYear(date);
        if(token=="a") return new System.Globalization.CultureInfo(locale).DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek);
        if(token=="A") return new System.Globalization.CultureInfo(locale).DateTimeFormat.GetDayName(date.DayOfWeek);
        if(token=="b") return new System.Globalization.CultureInfo(locale).DateTimeFormat.GetAbbreviatedMonthName(date.Month);
        if(token=="B") return new System.Globalization.CultureInfo(locale).DateTimeFormat.GetMonthName(date.Month);
        if(token=="d") return Pad(date.Day);
        if(token=="H") return Pad(hour);
        if(token=="I") return Pad(hour%12==0?12:hour%12);
        if(token=="j") return Pad(ordinal,3);
        if(token=="m") return Pad(date.Month);
        if(token=="M") return Pad(date.Minute);
        if(token=="p") return hour<12?"AM":"PM";
        if(token=="S") return Pad(date.Second);
        if(token=="w") return Pad((int)date.DayOfWeek);
        if(token=="y") return Pad(date.Year%100);
        if(token=="Y") return date.Year.ToString();
        if(token=="U") return Pad((ordinal -1 +7 - (int)date.DayOfWeek)/7);
        if(token=="W"){ int monday=( (int)date.DayOfWeek+6)%7; return Pad((ordinal -1 +7 - monday)/7); }
        return null;
    }
    private static DateTime ApplyRtcOffset(DateTime date, RtcOffset? off){
        if(off==null) return date;
        return date.AddYears(off.Years).AddMonths(off.Months).AddDays(off.Days).AddHours(off.Hours).AddMinutes(off.Minutes).AddSeconds(off.Seconds);
    }
    private static string FormatRtcField(string value, IReadOnlyList<string> indicators, RtcState rtc, DateTime sourceDate){
        var clocks=new[]{ sourceDate, ApplyRtcOffset(sourceDate, rtc.Offsets.TryGetValue(2, out var o2)? o2:null), ApplyRtcOffset(sourceDate, rtc.Offsets.TryGetValue(3, out var o3)? o3:null) };
        var sb=new StringBuilder();
        for(int i=0;i<value.Length;i++){
            int clockIdx=-1;
            for(int k=0;k<indicators.Count;k++) if(indicators[k]!="" && value[i].ToString()==indicators[k]){ clockIdx=k; break; }
            string? token=i+1<value.Length? value[i+1].ToString(): null;
            if(clockIdx<0 || token==null){ sb.Append(value[i]); continue; }
            var rep=RtcToken(clocks[clockIdx], token, rtc.Language);
            if(rep==null) sb.Append(value[i]); else { sb.Append(rep); i++; }
        }
        return sb.ToString();
    }
    private static string RtcIndicator(string? value, HashSet<string> forbidden, HashSet<string>? used=null){
        if(string.IsNullOrEmpty(value) || forbidden.Contains(value) || (used!=null && used.Contains(value))) return "";
        int code=value[0]; return code>=0x20 && code<=0x7e? value: "";
    }
    private static int BoundedInteger(string? v,int fallback,int min,int max){ var p=DecimalInteger(v); return p!=null && p>=min && p<=max? p.Value: fallback; }
    private static void ApplyRtcCommand(ZplCommandNode cmd, RtcState rtc, RenderJobOptions opts){
        var args=cmd.Parameters;
        if(cmd.Code=="SL"){
            string mode=(args.ElementAtOrDefault(0)?.Trim().ToUpperInvariant() ?? "S");
            var tol=DecimalInteger(mode);
            if(mode=="S"||mode=="T") rtc.Mode=mode;
            else if(tol!=null && tol>=0 && tol<=999) rtc.Mode=tol.Value;
            else rtc.Mode="S";
            var lang=DecimalInteger(args.ElementAtOrDefault(1));
            if(lang!=null && lang>=1 && lang<=18) rtc.Language=lang.Value;
        } else if(cmd.Code=="KL"){ var lang=DecimalInteger(args.ElementAtOrDefault(0)); if(lang!=null && lang>=1&&lang<=18) rtc.Language=lang.Value; }
        else if(cmd.Code=="SO"){ var clock=DecimalInteger(args.ElementAtOrDefault(0)); if(clock!=null && (clock==2||clock==3)) rtc.Offsets[clock.Value]=new RtcOffset{ Months=BoundedInteger(args.ElementAtOrDefault(1),0,-32000,32000), Days=BoundedInteger(args.ElementAtOrDefault(2),0,-32000,32000), Years=BoundedInteger(args.ElementAtOrDefault(3),0,-32000,32000), Hours=BoundedInteger(args.ElementAtOrDefault(4),0,-32000,32000), Minutes=BoundedInteger(args.ElementAtOrDefault(5),0,-32000,32000), Seconds=BoundedInteger(args.ElementAtOrDefault(6),0,-32000,32000)}; }
        else if(cmd.Code=="ST"){
            var cur=rtc.Fixed??ClockNow(opts);
            int month=BoundedInteger(args.ElementAtOrDefault(0), cur.Month,1,12);
            int day=BoundedInteger(args.ElementAtOrDefault(1), cur.Day,1,31);
            int year=BoundedInteger(args.ElementAtOrDefault(2), cur.Year,1998,2097);
            int hour=BoundedInteger(args.ElementAtOrDefault(3), cur.Hour,0,23);
            int minute=BoundedInteger(args.ElementAtOrDefault(4), cur.Minute,0,59);
            int second=BoundedInteger(args.ElementAtOrDefault(5), cur.Second,0,59);
            string fmt=(args.ElementAtOrDefault(6)?.Trim().ToUpperInvariant() ?? "M");
            string mer=fmt=="A"||fmt=="P"? fmt:"M";
            if(mer=="P" && hour<12) hour+=12;
            if(mer=="A" && hour==12) hour=0;
            rtc.Fixed=new DateTime(year,month,day,hour,minute,second, DateTimeKind.Utc);
        }
    }

    private static ZplCommandNode CloneCommand(ZplCommandNode cmd) => cmd.Clone();
    private static ZplLabelNode CloneLabel(ZplLabelNode label, List<ZplCommandNode> cmds){
        for(int i=0;i<cmds.Count;i++) cmds[i].Index=i;
        return new ZplLabelNode{ Explicit=label.Explicit, Span=new SourceSpan(label.Span.Start, label.Span.End), Commands=cmds };
    }
    private static ZplCommandNode ReplacementDataCommand(ZplCommandNode src, string value){
        var c=CloneCommand(src);
        c.Code="FD"; c.Canonical="^FD"; c.PrefixKind=ZplPrefixKind.Format; c.RawParameters=value; c.Parameters=new List<string>{value}; c.Capability=CommandCapabilityStatus.Supported;
        return c;
    }
    private static ZplLabelNode DynamicLabel(ZplLabelNode label, SessionState state, RenderJobOptions opts, IReadOnlyDictionary<string,string> fieldValues, int serialStep, DateTime labelStart, bool commitRtc){
        var rtc=new RtcState{ Mode=state.Rtc.Mode, Language=state.Rtc.Language, Offsets=new Dictionary<int,RtcOffset>(state.Rtc.Offsets), Fixed=state.Rtc.Fixed };
        var output=new List<ZplCommandNode>();
        List<string>? indicators=null;
        int lastDataIndex=-1;
        bool firstFieldOriginSeen=false;
        DateTime? queuedTime=null;
        (ZplCommandNode command,string value,bool resolved)? pendingFieldValue=null;
        DateTime FieldClock(){
            if(rtc.Fixed!=null) return rtc.Fixed.Value;
            if(rtc.Mode is string s && s=="S") return labelStart;
            queuedTime ??= ClockNow(opts);
            if(rtc.Mode is string t && t=="T") return queuedTime.Value;
            int tol= rtc.Mode is int i? Math.Max(1,i):1;
            return (queuedTime.Value - labelStart).TotalMilliseconds > tol*1000? queuedTime.Value: labelStart;
        }
        void FinishPending(){
            if(pendingFieldValue==null || pendingFieldValue.Value.resolved){ pendingFieldValue=null; return; }
            string v= indicators!=null? FormatRtcField(pendingFieldValue.Value.value, indicators, rtc, FieldClock()): pendingFieldValue.Value.value;
            output.Add(ReplacementDataCommand(pendingFieldValue.Value.command, v));
            lastDataIndex=output.Count-1;
            pendingFieldValue=null;
        }
        foreach(var orig in label.Commands){
            var cmd=CloneCommand(orig);
            if(cmd.Capability!=CommandCapabilityStatus.Supported && cmd.Capability!=CommandCapabilityStatus.Partial){ output.Add(cmd); continue; }
            if(cmd.Code=="FO") firstFieldOriginSeen=true;
            if(cmd.Code!="SL" || !firstFieldOriginSeen) ApplyRtcCommand(cmd, rtc, opts);
            if(cmd.Code=="FN"){
                FinishPending();
                var num=FieldNumber.Parse(cmd.Parameters.ElementAtOrDefault(0))?.Number;
                string? val=num!=null && fieldValues.TryGetValue(num, out var fv)? fv: null;
                pendingFieldValue= val==null? null: (cmd, val, false);
            }
            var args=cmd.Parameters;
            if(cmd.Code=="FC"){
                var forbidden=new HashSet<string>{"^","~",cmd.Prefix, cmd.Delimiter};
                string primary=RtcIndicator(args.ElementAtOrDefault(0)?.Substring(0,1)??"%", forbidden) ?? "%";
                if(string.IsNullOrEmpty(primary)) primary="%";
                var used=new HashSet<string>{primary};
                string secondary=RtcIndicator(args.ElementAtOrDefault(1)?.Substring(0,1), forbidden, used);
                if(!string.IsNullOrEmpty(secondary)) used.Add(secondary);
                string tertiary=RtcIndicator(args.ElementAtOrDefault(2)?.Substring(0,1), forbidden, used);
                indicators=new List<string>{primary, secondary, tertiary};
                output.Add(cmd); continue;
            }
            if(cmd.Code=="FS"){ FinishPending(); indicators=null; lastDataIndex=-1; output.Add(cmd); continue; }
            if(pendingFieldValue!=null && !pendingFieldValue.Value.resolved && (cmd.Code=="FD"||cmd.Code=="FV"||cmd.Code=="SN")){
                string v= indicators!=null? FormatRtcField(pendingFieldValue.Value.value, indicators, rtc, FieldClock()): pendingFieldValue.Value.value;
                output.Add(ReplacementDataCommand(cmd, v));
                pendingFieldValue=(pendingFieldValue.Value.command, pendingFieldValue.Value.value, true);
                lastDataIndex=output.Count-1; continue;
            }
            if(cmd.Code=="SF" && pendingFieldValue!=null && !pendingFieldValue.Value.resolved) FinishPending();
            if(cmd.Code=="SN"){
                string ser=SerializeNumber(args.ElementAtOrDefault(0)??"1", args.ElementAtOrDefault(1)??"1", (args.ElementAtOrDefault(2)?.Trim().ToUpperInvariant()??"N")=="Y", serialStep);
                string v= indicators!=null? FormatRtcField(ser, indicators, rtc, FieldClock()): ser;
                output.Add(ReplacementDataCommand(cmd, v));
                lastDataIndex=output.Count-1; continue;
            }
            if(cmd.Code=="FD"||cmd.Code=="FV"){
                string v= indicators!=null? FormatRtcField(cmd.RawParameters, indicators, rtc, FieldClock()): cmd.RawParameters;
                output.Add(v==cmd.RawParameters? cmd: ReplacementDataCommand(cmd, v));
                lastDataIndex=output.Count-1; continue;
            }
            if(cmd.Code=="SF"){
                if(lastDataIndex>=0 && SerializationFieldSize(cmd) <= MaxSerializationFieldSize){
                    var data=output[lastDataIndex];
                    string v=SerializeMasked(data.RawParameters, args.ElementAtOrDefault(0)??"", args.ElementAtOrDefault(1)??"", serialStep);
                    output[lastDataIndex]=ReplacementDataCommand(data, v);
                }
                continue;
            }
            output.Add(cmd);
        }
        FinishPending();
        if(commitRtc) state.Rtc=rtc;
        return CloneLabel(label, output);
    }

    private static string SessionResourceName(string value, string extension, SessionState state) => Interpreter.NormalizeResourceName(value, extension, state.MemoryAliases);

    private static int Utf8ByteLength(string s) => Encoding.UTF8.GetByteCount(s);
    private static int ResourceCost(string name, int payloadBytes) => Utf8ByteLength(name) + Math.Max(1, payloadBytes);
    private static bool DisabledResource(string name) => name.StartsWith("NONE:");
    private static int NamedResourceCost(SessionState state, string name){
        int c=0;
        if(state.Graphics.TryGetValue(name, out var g)) c+= ResourceCost(name, g.Data.Length);
        if(state.Formats.TryGetValue(name, out var f)) c+= ResourceCost(name, f.Bytes);
        if(state.Objects.TryGetValue(name, out var o)) c+= ResourceCost(name, o.Data.Length);
        return c;
    }
    private static void ClearNamedResource(SessionState state, string name){
        if(state.Graphics.TryGetValue(name, out var g)) { state.ResourceBytes-= ResourceCost(name, g.Data.Length); state.Graphics.Remove(name); }
        if(state.Formats.TryGetValue(name, out var f)) { state.ResourceBytes-= ResourceCost(name, f.Bytes); state.Formats.Remove(name); }
        if(state.Objects.TryGetValue(name, out var o)) { state.ResourceBytes-= ResourceCost(name, o.Data.Length); state.Objects.Remove(name); state.BitmapFonts.Remove(name); state.Encodings.Remove(name); }
    }
    private static void ReplaceNamedResource(SessionState state, string name, int bytes, Action store){
        ClearNamedResource(state, name); store(); state.ResourceBytes+= bytes;
    }

    private static string AliasedPathUsing(string value, IReadOnlyDictionary<string,string> aliases){
        var n=value.Trim().ToUpperInvariant(); if(!n.Contains(":")) n=$"R:{n}"; var mapped=aliases.TryGetValue(n[0].ToString(), out var m)? m:null; return mapped!=null? $"{mapped}:{n.Substring(n.IndexOf(':')+1)}": n;
    }
    private static string AliasedPath(string value, SessionState state)=> AliasedPathUsing(value, state.MemoryAliases);
    private static void ChangeMemoryAliases(ZplCommandNode cmd, SessionState state){
        var logical=new[]{"B","E","R","A"};
        var requested=logical.Select((l,i)=> (cmd.Parameters.ElementAtOrDefault(i)?.Trim().ToUpperInvariant().Replace(":","") ?? l)).ToArray();
        if(requested.Any(v=> v!="NONE" && !logical.Contains(v))) return;
        bool multiple=cmd.Parameters.ElementAtOrDefault(4)?.Trim().ToUpperInvariant()=="M";
        var active=requested.Where(v=>v!="NONE").ToArray();
        if(!multiple && new HashSet<string>(active).Count!=active.Length){ foreach(var l in logical) state.MemoryAliases[l]=l; return; }
        for(int i=0;i<logical.Length;i++) state.MemoryAliases[logical[i]]=requested[i];
    }

    private static ZplDiagnostic SemanticDiagnostic(string code,string msg,ZplCommandNode? node,string phase="semantic",string severity="error", IReadOnlyList<SourceSpan>? related=null){
        var sev= severity=="error"? ZplDiagnosticSeverity.Error: severity=="warning"? ZplDiagnosticSeverity.Warning: ZplDiagnosticSeverity.Info;
        var ph= phase=="render"? ZplDiagnosticPhase.Render: phase=="semantic"? ZplDiagnosticPhase.Semantic: ZplDiagnosticPhase.Parse;
        return new ZplDiagnostic(code, sev, ph, msg, node?.Span, related, node?.Canonical, null);
    }

    private static int? DecimalInteger(string? v){
        var s=v?.Trim()??""; if(!DecimalIntRegex.IsMatch(s)) return null;
        if(long.TryParse(s, out var l) && l>= -9007199254740991 && l<=9007199254740991) return (int)l;
        return null;
    }

    private static string ObjectPath(string value, string extension, SessionState state){
        var n=AliasedPath(value.Length==0? "R:UNKNOWN": value, state);
        if(!ExtensionRegex.IsMatch(n)) n+=$".{extension}";
        return n.ToUpperInvariant();
    }

    private static bool StoreObjectResource(string name, StoredObject obj, SessionState state, RenderLimits limits, ZplCommandNode cmd, List<ZplDiagnostic> diags){
        if(DisabledResource(name)) return false;
        int prev=NamedResourceCost(state,name);
        int bytes=ResourceCost(name, obj.Data.Length);
        if(state.ResourceBytes - prev + bytes > limits.MaxSessionBytes){
            diags.Add(SemanticDiagnostic("SESSION_RESOURCE_LIMIT_EXCEEDED",$"Storing {name} would exceed the {limits.MaxSessionBytes}-byte session limit.",cmd));
            return false;
        }
        ReplaceNamedResource(state,name,bytes, ()=> state.Objects[name]=obj);
        return true;
    }

    private static void DownloadDiagnostic(Exception ex, ZplCommandNode cmd, List<ZplDiagnostic> diags){
        string code = ex is GraphicDecodeError gde? gde.Code: "INVALID_OBJECT_DATA";
        diags.Add(SemanticDiagnostic(code, ex.Message, cmd));
    }

    private static void ProcessDownloadGraphic(ZplCommandNode cmd, SessionState state, RenderLimits limits, List<ZplDiagnostic> diags){
        var name=SessionResourceName(cmd.Parameters.ElementAtOrDefault(0)??"", "GRF", state);
        if(DisabledResource(name)) return;
        var expected=DecimalInteger(cmd.Parameters.ElementAtOrDefault(1));
        var bpr=DecimalInteger(cmd.Parameters.ElementAtOrDefault(2));
        if(expected==null || bpr==null) return;
        try{
            var graphic=GraphicDecoder.DecodeGraphic(string.Join(cmd.Delimiter, cmd.Parameters.Skip(3)), bpr.Value, expected.Value, limits.MaxGraphicBytes);
            int prev=NamedResourceCost(state,name);
            int bytes=ResourceCost(name, graphic.Data.Length);
            if(state.ResourceBytes - prev + bytes > limits.MaxSessionBytes){
                diags.Add(SemanticDiagnostic("SESSION_RESOURCE_LIMIT_EXCEEDED",$"Storing {name} would exceed the {limits.MaxSessionBytes}-byte session limit.",cmd));
                return;
            }
            ReplaceNamedResource(state,name,bytes, ()=> state.Graphics[name]= new Interpreter.StoredGraphic(graphic.Data, graphic.BytesPerRow, graphic.Width, graphic.Height));
        } catch(Exception ex){ diags.Add(SemanticDiagnostic(ex is GraphicDecodeError g? g.Code: "INVALID_GRAPHIC_DATA", ex.Message, cmd)); }
    }

    private static void ProcessDownloadObject(ZplCommandNode cmd, SessionState state, RenderLimits limits, List<ZplDiagnostic> diags){
        var format=(cmd.Parameters.ElementAtOrDefault(1)?.Trim().ToUpperInvariant()??"");
        var extCode=(cmd.Parameters.ElementAtOrDefault(2)?.Trim().ToUpperInvariant() ?? (format=="P"?"P":"G"));
        if(!DownloadObjectExtensions.TryGetValue(extCode, out var ext)) ext="GRF";
        var name=ObjectPath(cmd.Parameters.ElementAtOrDefault(0)??"", ext, state);
        if(DisabledResource(name)) return;
        var expected=DecimalInteger(cmd.Parameters.ElementAtOrDefault(3));
        var bpr=DecimalInteger(cmd.Parameters.ElementAtOrDefault(4));
        var source=string.Join(cmd.Delimiter, cmd.Parameters.Skip(5));
        if(expected==null) return;
        try{
            if(format=="C") throw new GraphicDecodeError("UNSUPPORTED_GRAPHIC_FORMAT","~DY AR-compressed BAR-ONE payloads are not supported.");
            if(!new[]{"A","B","P"}.Contains(format)) throw new GraphicDecodeError("UNSUPPORTED_GRAPHIC_FORMAT","~DY download format must be A, B, C, or P.");
            if(ext=="GRF" && format=="A"){
                if(bpr==null) throw new GraphicDecodeError("INVALID_GRAPHIC_DIMENSIONS","Bytes per row required for ~DY GRF A");
                var graphic=GraphicDecoder.DecodeGraphic(source, bpr.Value, expected.Value, limits.MaxGraphicBytes);
                int prev=NamedResourceCost(state,name);
                int bytes=ResourceCost(name, graphic.Data.Length);
                if(state.ResourceBytes - prev + bytes > limits.MaxSessionBytes) throw new GraphicDecodeError("SESSION_RESOURCE_LIMIT_EXCEEDED",$"Storing {name} exceeds the session resource limit.");
                ReplaceNamedResource(state,name,bytes, ()=> state.Graphics[name]= new Interpreter.StoredGraphic(graphic.Data, graphic.BytesPerRow, graphic.Width, graphic.Height));
                return;
            }
            byte[] bytesData = format=="B"? DecodeRaw(source, expected.Value, limits.MaxGraphicBytes) : GraphicDecoder.DecodeDownloadData(source, expected.Value, limits.MaxGraphicBytes);
            if(bytesData.Length!=expected.Value) throw new Exception($"Object declared {expected} bytes but decoded {bytesData.Length}.");
            if(ext=="GRF"){
                if(bpr==null) throw new GraphicDecodeError("INVALID_GRAPHIC_DIMENSIONS","Bytes per row required");
                var geom=GraphicDecoder.ValidateGraphicGeometry(bpr.Value, bytesData.Length, limits.MaxGraphicBytes);
                // Validate geometry already
                int prev=NamedResourceCost(state,name);
                int bytes=ResourceCost(name, bytesData.Length);
                if(state.ResourceBytes - prev + bytes > limits.MaxSessionBytes) throw new GraphicDecodeError("SESSION_RESOURCE_LIMIT_EXCEEDED",$"Storing {name} exceeds the session resource limit.");
                var graphic=new Interpreter.StoredGraphic(bytesData, bpr.Value, geom.width, geom.height);
                ReplaceNamedResource(state,name,bytes, ()=> state.Graphics[name]=graphic);
            } else if(new[]{"PNG","BMP","PCX"}.Contains(ext)){
                Interpreter.StoredGraphic graphic = ext=="PNG"? PngDecoder.DecodePng(bytesData, limits.MaxGraphicBytes): ext=="BMP"? ImageDecoder.DecodeBmp(bytesData, limits.MaxGraphicBytes): ImageDecoder.DecodePcx(bytesData, limits.MaxGraphicBytes);
                int prev=NamedResourceCost(state,name);
                int bytes=ResourceCost(name, graphic.Data.Length);
                if(state.ResourceBytes - prev + bytes > limits.MaxSessionBytes) throw new GraphicDecodeError("SESSION_RESOURCE_LIMIT_EXCEEDED",$"Storing {name} exceeds the session resource limit.");
                ReplaceNamedResource(state,name,bytes, ()=> state.Graphics[name]=graphic);
            } else {
                string kind = ext=="TTF"? "opentype": ext=="TTE"? "truetype-extension":"binary";
                StoreObjectResource(name, new StoredObject{ Data=bytesData, Kind=kind }, state, limits, cmd, diags);
            }
        } catch(Exception ex){ DownloadDiagnostic(ex,cmd,diags); }
    }

    private static byte[] DecodeRaw(string source, int expected, int maxBytes){
        if(expected<0 || expected>maxBytes) throw new GraphicDecodeError("OBJECT_BYTE_COUNT_MISMATCH",$"Downloaded object declares an invalid byte count: {expected}.");
        var bytes=new byte[expected];
        int off=0;
        foreach(var ch in source){
            if(off>=expected) throw new GraphicDecodeError("OBJECT_BYTE_COUNT_MISMATCH",$"Object declared {expected} bytes but supplied more data.");
            int v=ch; if(v>0xFF) throw new GraphicDecodeError("INVALID_OBJECT_DATA","Raw downloaded object data must contain byte-valued characters only.");
            bytes[off++]= (byte)v;
        }
        if(off!=expected) throw new GraphicDecodeError("OBJECT_BYTE_COUNT_MISMATCH",$"Object declared {expected} bytes but decoded {off}.");
        return bytes;
    }

    private static string NormalizeLegacyHexZeros(string source) => WrappedPrefixRegex.IsMatch(source.TrimStart()) ? source : source.Replace('O','0').Replace('o','0');
    private static void ProcessDownloadEncoding(ZplCommandNode cmd, SessionState state, RenderLimits limits, List<ZplDiagnostic> diags){
        var name=ObjectPath(cmd.Parameters.ElementAtOrDefault(0)??"", "DAT", state);
        if(DisabledResource(name)) return;
        var expected=DecimalInteger(cmd.Parameters.ElementAtOrDefault(1));
        if(expected==null) return;
        try{
            var data=GraphicDecoder.DecodeDownloadData(string.Join(cmd.Delimiter, cmd.Parameters.Skip(2)), expected.Value, limits.MaxGraphicBytes);
            if(data.Length%4!=0) throw new GraphicDecodeError("INVALID_OBJECT_DATA","Downloaded encoding data must contain complete four-byte mappings.");
            if(!StoreObjectResource(name, new StoredObject{ Data=data, Kind="encoding" }, state, limits, cmd, diags)) return;
            var mapping=new Dictionary<int,int>();
            for(int off=0; off+3 < data.Length; off+=4){
                int output=(data[off]<<8)|data[off+1];
                int input=(data[off+2]<<8)|data[off+3];
                mapping[input]=output;
            }
            state.Encodings[name]=mapping;
        } catch(Exception ex){ DownloadDiagnostic(ex,cmd,diags); }
    }
    private static void ProcessDownloadOutlineFont(ZplCommandNode cmd, SessionState state, RenderLimits limits, List<ZplDiagnostic> diags){
        string ext= cmd.Code=="DT"? "DAT":"FNT";
        var name=ObjectPath(cmd.Parameters.ElementAtOrDefault(0)??"", ext, state);
        if(DisabledResource(name)) return;
        var expected=DecimalInteger(cmd.Parameters.ElementAtOrDefault(1));
        if(expected==null) return;
        try{
            var rawSource=string.Join(cmd.Delimiter, cmd.Parameters.Skip(2));
            string source= cmd.Code=="DS"? NormalizeLegacyHexZeros(rawSource): rawSource;
            var data=GraphicDecoder.DecodeDownloadData(source, expected.Value, limits.MaxGraphicBytes);
            StoreObjectResource(name, new StoredObject{ Data=data, Kind= cmd.Code=="DS"? "intellifont": cmd.Code=="DT"? "bounded-truetype":"unbounded-truetype" }, state, limits, cmd, diags);
        } catch(Exception ex){ DownloadDiagnostic(ex,cmd,diags); }
    }
    private static void ProcessDownloadBitmapFont(ZplCommandNode cmd, SessionState state, RenderLimits limits, List<ZplDiagnostic> diags){
        var name=ObjectPath(cmd.Parameters.ElementAtOrDefault(0)??"", "FNT", state);
        if(DisabledResource(name)) return;
        var cellHeight=DecimalInteger(cmd.Parameters.ElementAtOrDefault(2));
        var cellWidth=DecimalInteger(cmd.Parameters.ElementAtOrDefault(3));
        var baseline=DecimalInteger(cmd.Parameters.ElementAtOrDefault(4));
        var spaceWidth=DecimalInteger(cmd.Parameters.ElementAtOrDefault(5));
        var expectedChars=DecimalInteger(cmd.Parameters.ElementAtOrDefault(6));
        var source=string.Join(cmd.Delimiter, cmd.Parameters.Skip(8));
        var orientation=cmd.Parameters.ElementAtOrDefault(1)?.Trim().ToUpperInvariant()??"";
        var copyright=cmd.Parameters.ElementAtOrDefault(7)??"";
        var glyphs=new Dictionary<int, DownloadedBitmapGlyph>();
        var matcher=BitmapFontRegex;
        try{
            if(orientation!="N" || copyright.Length<1 || copyright.Length>63 || cellHeight==null || cellWidth==null || baseline==null || spaceWidth==null || expectedChars==null || cellHeight<=0 || cellWidth<=0 || baseline<=0 || baseline>cellHeight || spaceWidth<=0 || spaceWidth>Math.Min(32000, limits.MaxDimension) || expectedChars<=0 || expectedChars>256 || cellHeight>Math.Min(32000, limits.MaxDimension) || cellWidth>Math.Min(32000, limits.MaxDimension)){
                throw new GraphicDecodeError("INVALID_OBJECT_DATA","Downloaded bitmap font header metrics are invalid.");
            }
            int totalBytes=0;
            bool matchedSource=false;
            foreach(Match m in matcher.Matches(source)){
                if(!matchedSource && source.Substring(0, m.Index).Trim()!="") throw new GraphicDecodeError("INVALID_OBJECT_DATA","Downloaded bitmap font contains data before its first glyph.");
                matchedSource=true;
                int cp=Convert.ToInt32(m.Groups[1].Value,16);
                int h=int.Parse(m.Groups[2].Value), w=int.Parse(m.Groups[3].Value), xOff=int.Parse(m.Groups[4].Value), yOff=int.Parse(m.Groups[5].Value), adv=int.Parse(m.Groups[6].Value);
                if(cp<0||cp>0xFFFF||glyphs.ContainsKey(cp)|| h<=0||w<=0||adv<0|| h>limits.MaxDimension||w>limits.MaxDimension||adv>limits.MaxDimension||Math.Abs(xOff)>limits.MaxDimension||Math.Abs(yOff)>limits.MaxDimension) throw new GraphicDecodeError("INVALID_OBJECT_DATA","Downloaded bitmap font glyph metrics are invalid or duplicated.");
                int bpr=(w+7)/8;
                int expBytes=bpr*h;
                totalBytes+=expBytes;
                if(totalBytes>limits.MaxGraphicBytes) throw new GraphicDecodeError("GRAPHIC_LIMIT_EXCEEDED",$"Downloaded bitmap font exceeds the {limits.MaxGraphicBytes}-byte graphic limit.");
                var data=GraphicDecoder.DecodeDownloadData(NormalizeLegacyHexZeros(m.Groups[7].Value), expBytes, limits.MaxGraphicBytes);
                glyphs[cp]=new DownloadedBitmapGlyph(cp, w, h, xOff, yOff, adv, bpr, data);
            }
            if(!matchedSource || glyphs.Count==0) throw new GraphicDecodeError("INVALID_OBJECT_DATA","Downloaded bitmap font does not contain any valid glyphs.");
            if(glyphs.Count!=expectedChars) throw new GraphicDecodeError("INVALID_OBJECT_DATA",$"Bitmap font declared {expectedChars} characters but supplied {glyphs.Count}.");
            int prev=NamedResourceCost(state,name);
            int bytes=ResourceCost(name, totalBytes);
            if(state.ResourceBytes - prev + bytes > limits.MaxSessionBytes){ diags.Add(SemanticDiagnostic("SESSION_RESOURCE_LIMIT_EXCEEDED",$"Storing {name} would exceed the {limits.MaxSessionBytes}-byte session limit.",cmd)); return; }
            var raw=new byte[totalBytes];
            int off=0;
            var storedGlyphs=new Dictionary<int, DownloadedBitmapGlyph>();
            foreach(var kv in glyphs){
                Array.Copy(kv.Value.Data, 0, raw, off, kv.Value.Data.Length);
                var end=off+kv.Value.Data.Length;
                storedGlyphs[kv.Key]= new DownloadedBitmapGlyph(kv.Value.CodePoint, kv.Value.Width, kv.Value.Height, kv.Value.XOffset, kv.Value.YOffset, kv.Value.Advance, kv.Value.BytesPerRow, raw.AsSpan(off, kv.Value.Data.Length).ToArray());
                // Actually need to slice raw subarray, but we copy correctly
                // For simplicity, keep data as slice of raw
                off=end;
            }
            // Need to fix glyph data to be slices of raw
            var finalGlyphs=new Dictionary<int, DownloadedBitmapGlyph>();
            off=0;
            foreach(var kv in glyphs){
                int len=kv.Value.Data.Length;
                finalGlyphs[kv.Key]= new DownloadedBitmapGlyph(kv.Value.CodePoint, kv.Value.Width, kv.Value.Height, kv.Value.XOffset, kv.Value.YOffset, kv.Value.Advance, kv.Value.BytesPerRow, raw.AsSpan(off, len).ToArray());
                off+=len;
            }
            if(!StoreObjectResource(name, new StoredObject{ Data=raw, Kind="bitmap-font" }, state, limits, cmd, diags)) return;
            state.BitmapFonts[name]= new DownloadedBitmapFont(cellWidth.Value, cellHeight.Value, baseline.Value, spaceWidth.Value, finalGlyphs);
        } catch(Exception ex){ DownloadDiagnostic(ex,cmd,diags); }
    }
    private static int FontLinkCost(string b, IReadOnlyList<string> links) => Utf8ByteLength(b) + links.Sum(l=> Utf8ByteLength(l)+1) +1;
    private static void ProcessFontLink(ZplCommandNode cmd, SessionState state, RenderLimits limits, List<ZplDiagnostic> diags){
        var ext=AliasedPath(cmd.Parameters.ElementAtOrDefault(0)?.Trim()??"", state);
        var b=AliasedPath(cmd.Parameters.ElementAtOrDefault(1)?.Trim()??"", state);
        if(string.IsNullOrEmpty(ext) || string.IsNullOrEmpty(b)) return;
        if(DisabledResource(ext) || DisabledResource(b)) return;
        var links= state.FontLinks.TryGetValue(b, out var l)? new List<string>(l): new List<string>();
        bool enabled=(cmd.Parameters.ElementAtOrDefault(2)?.Trim()=="1");
        var filtered=links.Where(n=> n!=ext).ToList();
        if(enabled) filtered.Add(ext);
        var next=filtered.TakeLast(5).ToList();
        int prevBytes= links.Count>0? FontLinkCost(b, links):0;
        int nextBytes= next.Count>0? FontLinkCost(b, next):0;
        if(state.ResourceBytes - prevBytes + nextBytes > limits.MaxSessionBytes){ diags.Add(SemanticDiagnostic("SESSION_RESOURCE_LIMIT_EXCEEDED",$"Updating font links for {b} would exceed the {limits.MaxSessionBytes}-byte session limit.",cmd)); return; }
        if(next.Count>0) state.FontLinks[b]=next; else state.FontLinks.Remove(b);
        state.ResourceBytes+= nextBytes - prevBytes;
    }
    private static void ProcessFontAlias(ZplCommandNode cmd, SessionState state, RenderLimits limits, List<ZplDiagnostic> diags){
        var id=(cmd.Parameters.ElementAtOrDefault(0)?.Trim().ToUpperInvariant()??"");
        var req=(cmd.Parameters.ElementAtOrDefault(1)?.Trim()??"");
        if(!FontAliasRegex.IsMatch(id) || string.IsNullOrEmpty(req)) return;
        var name=AliasedPath(req, state);
        var prevName=state.FontAliases.TryGetValue(id, out var pn)? pn: null;
        int prevBytes= prevName!=null? Utf8ByteLength(id)+Utf8ByteLength(prevName)+1:0;
        int nextBytes= Utf8ByteLength(id)+Utf8ByteLength(name)+1;
        if(state.ResourceBytes - prevBytes + nextBytes > limits.MaxSessionBytes){
            diags.Add(SemanticDiagnostic("SESSION_RESOURCE_LIMIT_EXCEEDED",$"Assigning font identifier {id} would exceed the {limits.MaxSessionBytes}-byte session limit.",cmd));
            return;
        }
        state.FontAliases[id]=name;
        state.ResourceBytes+= nextBytes - prevBytes;
    }

    private static void StorePersistent(ZplCommandNode cmd, SessionState state, int docId, RenderLimits limits, List<ZplDiagnostic> diags){
        int bytes=Math.Max(1, Utf8ByteLength(cmd.Canonical + cmd.RawParameters));
        int prev=state.Persistent.TryGetValue(cmd.Code, out var p)? p.Bytes:0;
        if(state.ResourceBytes - prev + bytes > limits.MaxSessionBytes){
            diags.Add(SemanticDiagnostic("SESSION_RESOURCE_LIMIT_EXCEEDED",$"Persisting {cmd.Canonical} would exceed the {limits.MaxSessionBytes}-byte session limit.",cmd));
            return;
        }
        state.Persistent.Remove(cmd.Code);
        state.Persistent[cmd.Code]=(cmd.Clone(), docId, bytes);
        state.ResourceBytes+= bytes - prev;
    }

    private static void ProcessJobCommand(ZplCommandNode cmd, SessionState state, int docId, RenderLimits limits, List<ZplDiagnostic> diags){
        if(cmd.Capability!=CommandCapabilityStatus.Supported && cmd.Capability!=CommandCapabilityStatus.Partial) return;
        if(cmd.Canonical=="~DG") ProcessDownloadGraphic(cmd, state, limits, diags);
        else if(cmd.Canonical=="~DB") ProcessDownloadBitmapFont(cmd, state, limits, diags);
        else if(cmd.Canonical=="~DE") ProcessDownloadEncoding(cmd, state, limits, diags);
        else if(cmd.Canonical=="~DS"||cmd.Canonical=="~DT"||cmd.Canonical=="~DU") ProcessDownloadOutlineFont(cmd, state, limits, diags);
        else if(cmd.Canonical=="~DY") ProcessDownloadObject(cmd, state, limits, diags);
        else if(cmd.Canonical=="~EG") { foreach(var kv in state.Graphics.ToList()){ state.ResourceBytes-=ResourceCost(kv.Key, kv.Value.Data.Length); } state.Graphics.Clear(); }
        else if(cmd.Canonical=="^ID") {
            var pattern=(cmd.Parameters.ElementAtOrDefault(0)??"R:*.*").Trim().ToUpperInvariant();
            if(!pattern.Contains(":")) pattern=$"R:{pattern}";
            if(!PatternWildRegex.IsMatch(pattern)) pattern+=".GRF";
            var matcher=new Regex("^"+Regex.Escape(pattern).Replace("\\*",".*").Replace("\\?",".")+"$");
            foreach(var k in state.Graphics.Keys.Where(k=> matcher.IsMatch(k)).ToList()){ state.ResourceBytes-=ResourceCost(k, state.Graphics[k].Data.Length); state.Graphics.Remove(k); }
            foreach(var k in state.Formats.Keys.Where(k=> matcher.IsMatch(k)).ToList()){ state.ResourceBytes-=ResourceCost(k, state.Formats[k].Bytes); state.Formats.Remove(k); }
            foreach(var k in state.Objects.Keys.Where(k=> matcher.IsMatch(k)).ToList()){ state.ResourceBytes-=ResourceCost(k, state.Objects[k].Data.Length); state.Objects.Remove(k); state.BitmapFonts.Remove(k); state.Encodings.Remove(k); }
        }
        else if(cmd.Canonical=="^TO"){
            // Transfer object - simplified: handle graphics/formats/objects transfer via wildcard
            var srcPat=(cmd.Parameters.ElementAtOrDefault(0)??"").Trim().ToUpperInvariant();
            var dstPat=(cmd.Parameters.ElementAtOrDefault(1)??"").Trim().ToUpperInvariant();
            if(DriveRegex.IsMatch(srcPat) && DriveRegex.IsMatch(dstPat)){
                var srcRegex=new Regex("^"+Regex.Escape(srcPat).Replace("\\*",".*").Replace("\\?",".")+"$");
                foreach(var kv in state.Graphics.Where(kv=> srcRegex.IsMatch(kv.Key)).ToList()){
                    var dst=dstPat.Replace("*", kv.Key.Split(':')[1]);
                    if(dst.StartsWith("NONE:")) continue;
                    int prev=NamedResourceCost(state,dst);
                    int need=ResourceCost(dst, kv.Value.Data.Length);
                    if(state.ResourceBytes - prev + need > limits.MaxSessionBytes){ diags.Add(SemanticDiagnostic("SESSION_RESOURCE_LIMIT_EXCEEDED",$"Transferring {dst} would exceed session limit.",cmd)); continue; }
                    ReplaceNamedResource(state,dst,need, ()=> state.Graphics[dst]=new Interpreter.StoredGraphic((byte[])kv.Value.Data.Clone(), kv.Value.BytesPerRow, kv.Value.Width, kv.Value.Height));
                }
            }
        }
        else if(cmd.Canonical=="^CM") ChangeMemoryAliases(cmd, state);
        else if(cmd.Canonical=="^CW") ProcessFontAlias(cmd, state, limits, diags);
        else if(cmd.Canonical=="^FL") ProcessFontLink(cmd, state, limits, diags);
        else if(cmd.Canonical=="^SE"){
            // Select encoding - handled in interpreter, but need to track resource
            var name=ObjectPath(cmd.Parameters.ElementAtOrDefault(0)??"", "DAT", state);
            // No resource cost, just validation
        }
        else if(new[]{"^KL","^SL","^SO","^ST"}.Contains(cmd.Canonical)){
            // RTC commands affect session Rtc state
            ApplyRtcCommand(cmd, state.Rtc, new RenderJobOptions());
        }
        if(PersistentCommands.Contains(cmd.Code)) StorePersistent(cmd, state, docId, limits, diags);
    }

    private static bool HasJobEffect(ZplCommandNode cmd) => new[]{"~DG","~DB","~DE","~DS","~DT","~DU","~DY","~EG","^ID","^CM","^CW","^FL","^TO","^SE","^KL","^SL","^SO","^ST"}.Contains(cmd.Canonical) && (cmd.Capability==CommandCapabilityStatus.Supported || cmd.Capability==CommandCapabilityStatus.Partial);

    private static Dictionary<string,string> SnapshotFontAliases(SessionState state)=> new(state.FontAliases);
    private static bool CommandReadsResources(ZplCommandNode cmd) => new[]{"SE","CF","A","A@","XG","IM","IL"}.Contains(cmd.Code);

    private static ZplDiagnostic SDiag(string code,string msg,ZplCommandNode? node)=> SemanticDiagnostic(code,msg,node);

    private static (int quantity,int replicates,ZplCommandNode? command) PrintQuantity(ZplLabelNode label){
        var cmd=label.Commands.AsEnumerable().Reverse().FirstOrDefault(c=>c.Canonical=="^PQ");
        int qty=Math.Clamp(DecimalInteger(cmd?.Parameters.ElementAtOrDefault(0)??"1")??1,1,99999999);
        int rep=Math.Max(1, DecimalInteger(cmd?.Parameters.ElementAtOrDefault(2)??"0")??0);
        if(rep==0) rep=1;
        return (qty,rep,cmd);
    }
    private static Dictionary<string,string> OptionFieldValues(RenderJobOptions opts, List<ZplDiagnostic> diags){
        var dict=new Dictionary<string,string>();
        if(opts.FieldValues==null) return dict;
        foreach(var kv in opts.FieldValues){
            var parsed=FieldNumber.Parse(kv.Key);
            if(parsed==null || kv.Value==null){
                diags.Add(SemanticDiagnostic("INVALID_FIELD_VALUE_KEY",$"Render field value {System.Text.Json.JsonSerializer.Serialize(kv.Key)} was ignored; keys must be integers from 0 through 9999 and values must be strings.",null));
                continue;
            }
            dict[parsed.Number]=kv.Value;
        }
        return dict;
    }

    private static ZplLabelNode CloneLabel2(ZplLabelNode label, List<ZplCommandNode> cmds){
        var l=new ZplLabelNode{ Explicit=label.Explicit, Span=new SourceSpan(label.Span.Start, label.Span.End), Commands=cmds };
        for(int i=0;i<cmds.Count;i++) cmds[i].Index=i;
        return l;
    }

    public static async Task<RenderJobResult<TCanvas>> RenderZplWithPlatformAsync<TCanvas>(string source, RenderJobOptions? options, ICanvasPlatform<TCanvas> platform) where TCanvas : ICanvas
    {
        var session = CreateRenderSessionWithPlatform(platform, options);
        return await session.RenderAsync(source, options);
    }

    public static IZplRenderSession<TCanvas> CreateRenderSessionWithPlatform<TCanvas>(ICanvasPlatform<TCanvas> platform, RenderJobOptions? baseOptions = null) where TCanvas : ICanvas
    {
        return new ZplRenderSession<TCanvas>(platform, baseOptions);
    }

    private sealed class ZplRenderSession<TCanvas> : IZplRenderSession<TCanvas> where TCanvas : ICanvas
    {
        private readonly ICanvasPlatform<TCanvas> _platform;
        private readonly RenderJobOptions? _baseOptions;
        private SessionState _state = NewState();
        private Task _queue = Task.CompletedTask;

        public ZplRenderSession(ICanvasPlatform<TCanvas> platform, RenderJobOptions? baseOptions){ _platform=platform; _baseOptions=baseOptions; }

        private RenderJobOptions Merge(RenderJobOptions? a, RenderJobOptions? b){
            if(a==null) return b?? new RenderJobOptions();
            if(b==null) return a;
            return new RenderJobOptions{ Width=b.Width??a.Width, Height=b.Height??a.Height, PrintDensity=b.PrintDensity??a.PrintDensity, FallbackSize=b.FallbackSize??a.FallbackSize, Strict=b.Strict||a.Strict, Limits=b.Limits??a.Limits, FieldValues=b.FieldValues??a.FieldValues, Clock=b.Clock??a.Clock, FontProvider=b.FontProvider??a.FontProvider, Profile=b.Profile??a.Profile, InitialSyntax=b.InitialSyntax??a.InitialSyntax };
        }

        public Task<RenderJobResult<TCanvas>> RenderAsync(string source, RenderJobOptions? options=null){
            var tcs=new TaskCompletionSource<RenderJobResult<TCanvas>>();
            var prev=_queue;
            _queue = prev.ContinueWith(async _=>{
                try{
                    var merged=Merge(_baseOptions, options);
                    var doc=DocumentParser.ParseDocument(source, new ParseDocumentOptions{ Profile=merged.Profile, InitialSyntax=_state.Syntax });
                    _state.Syntax=doc.Syntax;
                    var res=await RenderDocumentInternalAsync(doc, merged);
                    tcs.SetResult(res);
                } catch(Exception ex){ tcs.SetException(ex); }
            }).Unwrap();
            return tcs.Task;
        }

        public Task<RenderJobResult<TCanvas>> RenderDocumentAsync(ZplDocument document, RenderJobOptions? options=null){
            var tcs=new TaskCompletionSource<RenderJobResult<TCanvas>>();
            var prev=_queue;
            _queue = prev.ContinueWith(async _=>{
                try{
                    var merged=Merge(_baseOptions, options);
                    _state.Syntax=document.Syntax;
                    var res=await RenderDocumentInternalAsync(document, merged);
                    tcs.SetResult(res);
                } catch(Exception ex){ tcs.SetException(ex); }
            }).Unwrap();
            return tcs.Task;
        }

        private async Task<RenderJobResult<TCanvas>> RenderDocumentInternalAsync(ZplDocument doc, RenderJobOptions opts){
            var limits=RenderDocument.ResolveRenderLimits(opts.Limits!=null? new RenderLimits(opts.Limits.MaxDimension, opts.Limits.MaxPixels, opts.Limits.MaxGraphicBytes, opts.Limits.MaxSessionBytes, opts.Limits.MaxTemplateDepth, opts.Limits.MaxExpandedCommands, opts.Limits.MaxLabels): null);
            var jobDiags=new List<ZplDiagnostic>(doc.Diagnostics);
            // Process job-level commands (outside labels) for resource storage
            int docId=_state.NextDocumentId++;
            foreach(var item in doc.Items.Where(i=> i is ZplCommandNode)){
                var cmd=(ZplCommandNode)item;
                ProcessJobCommand(cmd, _state, docId, limits, jobDiags);
            }
            var fieldValues=OptionFieldValues(opts, jobDiags);
            var allRendered=new List<RenderedLabel<TCanvas>>();
            var pixelBudget=new RenderDocument.PixelBudget{ Remaining=limits.MaxPixels };
            int generatedLabels=0;
            foreach(var label in doc.Labels){
                var hasDf=label.Commands.Any(c=> c.Canonical=="^DF");
                if(hasDf){
                    var name=SessionResourceName(label.Commands.First(c=>c.Canonical=="^DF").Parameters.ElementAtOrDefault(0)??"", "ZPL", _state);
                    if(name.StartsWith("NONE:")) continue;
                    var cmds=label.Commands.Where(c=> !new[]{"^XA","^XZ","^DF"}.Contains(c.Canonical)).ToList();
                    int bytes=cmds.Sum(c=> Utf8ByteLength(c.Canonical+c.RawParameters));
                    int prev= _state.Formats.TryGetValue(name, out var pf)? ResourceCost(name, pf.Bytes):0;
                    int need=ResourceCost(name, bytes);
                    if(_state.ResourceBytes - prev + need > limits.MaxSessionBytes){
                        jobDiags.Add(SDiag("SESSION_RESOURCE_LIMIT_EXCEEDED",$"Storing {name} would exceed the {limits.MaxSessionBytes}-byte session limit.", label.Commands.First(c=>c.Canonical=="^DF")));
                        continue;
                    }
                    if(_state.Formats.ContainsKey(name)) _state.ResourceBytes-= ResourceCost(name, _state.Formats[name].Bytes);
                    _state.Formats[name]=new StoredFormat{ Commands=cmds.Select(c=>c.Clone()).ToList(), Bytes=bytes, DefinitionSpan=label.Commands.First(c=>c.Canonical=="^DF").Span, DocumentId=docId };
                    _state.ResourceBytes+= need;
                    continue;
                }
                var expanded=label;
                if(label.Commands.Any(c=> c.Canonical=="^XF")){
                    var newCmds=new List<ZplCommandNode>();
                    foreach(var c in label.Commands){
                        if(c.Canonical!="^XF") newCmds.Add(c);
                        else{
                            var rName=SessionResourceName(c.Parameters.ElementAtOrDefault(0)??"", "ZPL", _state);
                            if(_state.Formats.TryGetValue(rName, out var fmt)){
                                newCmds.AddRange(fmt.Commands.Select(x=>x.Clone()));
                            } else jobDiags.Add(SDiag("MISSING_STORED_FORMAT",$"Stored format {rName} is not present in this render session.",c));
                        }
                    }
                    expanded=CloneLabel(label, newCmds);
                }
                var persistentCmds=_state.Persistent.Values.Select(v=>v.Command.Clone()).ToList();
                if(persistentCmds.Count>0){
                    var start=expanded.Commands.FirstOrDefault()?.Canonical=="^XA"? new List<ZplCommandNode>{expanded.Commands[0]}: new List<ZplCommandNode>();
                    var rest=expanded.Commands.Skip(start.Count).ToList();
                    expanded=CloneLabel(label, start.Concat(persistentCmds).Concat(rest).ToList());
                }
                var (quantity,replicates, pqCmd)=PrintQuantity(expanded);
                int available=Math.Max(0, limits.MaxLabels - generatedLabels);
                int qty=Math.Min(quantity, available);
                if(qty < quantity){
                    string subj= pqCmd!=null? $"^PQ requested {quantity} label{(quantity==1?"":"s")}": $"This format would generate {quantity} label{(quantity==1?"":"s")}";
                    jobDiags.Add(new ZplDiagnostic("LABEL_QUANTITY_LIMIT_EXCEEDED", ZplDiagnosticSeverity.Error, ZplDiagnosticPhase.Semantic, $"{subj}, but only {available} of the {limits.MaxLabels}-label job limit {(available==1?"remains":"remain")}.", pqCmd?.Span, null, pqCmd?.Canonical, null));
                }
                var labelStart=_state.Rtc.Fixed??ClockNow(opts);
                for(int copyIndex=0; copyIndex<qty; copyIndex++){
                    int serialStep= copyIndex / Math.Max(1,replicates);
                    var renderedLabel=DynamicLabel(expanded, _state, opts, fieldValues, serialStep, labelStart, copyIndex==qty-1);
                    var labelDoc=new ZplDocument{ Source=doc.Source, Profile=doc.Profile, Items=new List<object>{renderedLabel}, Labels=new List<ZplLabelNode>{renderedLabel}, Syntax=doc.Syntax, Diagnostics=new List<ZplDiagnostic>() };
                    var renderOpts=new RenderDocumentOptions{ Width=opts.Width, Height=opts.Height, PrintDensity=opts.PrintDensity, FallbackSize=opts.FallbackSize, Strict=opts.Strict, Limits=opts.Limits!=null? new Types.RenderLimits(opts.Limits.MaxDimension, opts.Limits.MaxPixels, opts.Limits.MaxGraphicBytes, opts.Limits.MaxSessionBytes, opts.Limits.MaxTemplateDepth, opts.Limits.MaxExpandedCommands, opts.Limits.MaxLabels): null };
                    var ctx2=new RenderDocument.RenderDocumentContext{ Graphics=_state.Graphics, FontAliases=_state.FontAliases, MemoryAliases=_state.MemoryAliases, BitmapFonts=_state.BitmapFonts, FontLinks=_state.FontLinks.ToDictionary(kv=>kv.Key, kv=> (IReadOnlyList<string>)kv.Value), Encodings=_state.Encodings, FontProvider=opts.FontProvider, PixelBudget=pixelBudget };
                    var res=await RenderDocument.RenderDocumentWithPlatformAsync(labelDoc, renderOpts, _platform, ctx2);
                    foreach(var r in res){
                        // Handle ^IS (image save) and ^MC (map clear) - simplified
                        var isCmd=renderedLabel.Commands.AsEnumerable().Reverse().FirstOrDefault(c=>c.Canonical=="^IS");
                        bool printImage=true;
                        if(isCmd!=null && r.Raster.Data.Length>0){
                            // For now, skip storing, just print
                            printImage=true;
                        }
                        var mcCmd=renderedLabel.Commands.AsEnumerable().Reverse().FirstOrDefault(c=>c.Canonical=="^MC");
                        if(r.Raster.Data.Length>0 && (mcCmd?.Parameters.ElementAtOrDefault(0)?.Trim().ToUpperInvariant() ?? "Y")=="N"){
                            // retain raster (simplified, not tracking resource bytes accurately)
                            _state.RetainedRaster=CloneRaster(r.Raster);
                        } else _state.RetainedRaster=null;
                        if(printImage) allRendered.Add(new RenderedLabel<TCanvas>(r.Raster, r.Width, r.Height, r.PrintDensity, r.Diagnostics, r.HighlightRegions, r.Canvas));
                        else jobDiags.AddRange(r.Diagnostics);
                    }
                    generatedLabels++;
                    if(generatedLabels>=limits.MaxLabels) break;
                }
                foreach(var c in expanded.Commands.Where(c=> PersistentCommands.Contains(c.Code))) StorePersistent(c, _state, docId, limits, jobDiags);
                if(generatedLabels>=limits.MaxLabels) break;
            }
            var diags2=jobDiags.Concat(allRendered.SelectMany(l=>l.Diagnostics)).ToList();
            return new RenderJobResult<TCanvas>(doc, allRendered, diags2);
        }

        public Task ResetAsync(){ _state=NewState(); return Task.CompletedTask; }
    }
}
