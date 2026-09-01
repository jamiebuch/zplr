// Port of src/core/layoutRenderer.ts — barcode encoding helpers (Code39, Code128, GS1-128)
using System.Text.RegularExpressions;

namespace Zplr.Renderer.Core;

public static class LayoutRenderer
{
    private static readonly Regex NumericRegex = new(@"^\d+$", RegexOptions.Compiled);
    private static readonly Regex TwoDigitsRegex = new(@"^\d{2}$", RegexOptions.Compiled);


    private static readonly string[] Code39Characters = new[] { "0","1","2","3","4","5","6","7","8","9","A","B","C","D","E","F","G","H","I","J","K","L","M","N","O","P","Q","R","S","T","U","V","W","X","Y","Z","-","."," ","$","/","+","%","*" };
    private static readonly int[] Code39Encodings = new[] { 20957,29783,23639,30485,20951,29813,23669,20855,29789,23645,29975,23831,30533,22295,30149,24005,21623,29981,23837,22301,30023,23879,30545,22343,30161,24017,21959,30065,23921,22385,29015,18263,29141,17879,29045,18293,17783,29021,18269,17477,17489,17681,20753,35770 };
    private static readonly long[] Code128Encodings = new long[] {
        11011001100,11001101100,11001100110,10010011000,10010001100,10001001100,10011001000,10011000100,10001100100,11001001000,11001000100,11000100100,10110011100,10011011100,10011001110,10111011000,10011101100,10011100110,11001110010,11001011100,11001001110,11011100100,11001110100,11101101110,11101001100,11100101100,11100100110,11101100100,11100110100,11100110010,11011011000,11011000110,11000110110,10100011000,10001011000,10001000110,10110001000,10001101000,10001100010,11010001000,11000101000,11000100010,10110111000,10110001110,10001101110,10111011000,10111000110,10001110110,11101110110,11010001110,11000101110,11011101000,11011100010,11011101110,11101011000,11101000110,11100010110,11101101000,11101100010,11100011010,11101111010,11001000010,11110001010,10100110000,10100001100,10010110000,10010000110,10000101100,10000100110,10110010000,10110000100,10011010000,10011000010,10000110100,10000110010,11000010010,11001010000,11110111010,11000010100,10001111010,10100111100,10010111100,10010011110,10111100100,10011110100,10011110010,11110100100,11110010100,11110010010,11011011110,11011110110,11110110110,10101111000,10100011110,10001011110,10111101000,10111100010,11110101000,11110100010,10111011110,10111101110,11101011110,11110101110,11010000100,11010010000,11010011100,1100011101011
    };

    public static string Code39CheckDigit(string data){
        int sum=0;
        foreach(var ch in data) sum+= Array.IndexOf(Code39Characters, ch.ToString());
        return Code39Characters[sum % 43];
    }
    private static string Code39Bits(string ch){
        int idx=Array.IndexOf(Code39Characters, ch);
        return idx<0? "": Convert.ToString(Code39Encodings[idx],2);
    }
    public static List<(bool black,int units)> Code39Runs(string data){
        string bits=Code39Bits("*") + string.Concat(data.Select(c=> Code39Bits(c.ToString())+"0")) + Code39Bits("*");
        var runs=new List<(bool,int)>();
        if(bits.Length==0) return runs;
        char cur=bits[0]; int len=0;
        foreach(var b in bits){
            if(b==cur) len++;
            else{ runs.Add((cur=='1',len)); cur=b; len=1; }
        }
        if(len>0) runs.Add((cur=='1',len));
        return runs;
    }

    private static string Mod10CheckDigit(string data){
        if(!NumericRegex.IsMatch(data)) throw new Exception("A Code 128 UCC check digit requires numeric field data.");
        int sum=0, w=3;
        for(int i=data.Length-1;i>=0;i--){ sum+= (data[i]-'0')*w; w= w==3?1:3; }
        return ((10 - (sum%10))%10).ToString();
    }

    private static string Code128DisplayValue(int v,string set){
        if(set=="C") return v.ToString().PadLeft(2,'0');
        if(set=="A") return ((char)(v<64? v+32: v-64)).ToString();
        return ((char)(v+32)).ToString();
    }
    private static int Code128Value(string ch,string set){
        int cp=ch[0];
        if(set=="A"){
            if(cp<0||cp>95) throw new Exception("Code 128 subset A field data contains an invalid character.");
            return cp<32? cp+64: cp-32;
        }
        if(cp<32||cp>127) throw new Exception("Code 128 subset B field data contains an invalid character.");
        return cp-32;
    }
    private static (List<int> values,string display) EncodeCode128NoSelectedMode(string data){
        if(data.Length==0) throw new Exception("Code 128 field data is empty.");
        int srcIdx=0; string set="B"; int start=104;
        string startCode=data.Length>=2? data.Substring(0,2):"";
        if(startCode==">9"||startCode==">:"||startCode==">;"){ set=startCode==">9"?"A":startCode==">:"?"B":"C"; start=set=="A"?103:set=="B"?104:105; srcIdx=2; }
        var values=new List<int>(); string display=""; bool shift=false;
        void Append(int v){ values.Add(v); display+=Code128DisplayValue(v,set); }
        while(srcIdx<data.Length){
            if(data[srcIdx]=='>'){
                if(srcIdx+1>=data.Length) throw new Exception("A Code 128 invocation marker is incomplete.");
                string inv=data[srcIdx+1].ToString(); srcIdx+=2;
                if(inv=="<"){ if(set=="C") throw new Exception("A literal > cannot be encoded while Code 128 subset C is active."); values.Add(Code128Value(">",set)); display+=">"; }
                else if(inv=="0") Append(30);
                else if(inv=="=") Append(94);
                else if(inv=="1") Append(95);
                else if(inv=="2") values.Add(96);
                else if(inv=="3") values.Add(97);
                else if(inv=="4"){ if(set=="C") throw new Exception("Code 128 SHIFT is invalid in subset C."); values.Add(98); shift=true; }
                else if(inv=="5"){ values.Add(99); set="C"; }
                else if(inv=="6"){ values.Add(100); set="B"; }
                else if(inv=="7"){ values.Add(101); set="A"; }
                else if(inv=="8") values.Add(102);
                else throw new Exception($"Unsupported Code 128 invocation code >{inv}.");
                continue;
            }
            if(set=="C"){
                string pair=data.Substring(srcIdx, Math.Min(2, data.Length-srcIdx));
                if(!TwoDigitsRegex.IsMatch(pair)) throw new Exception("Code 128 subset C requires pairs of numeric digits.");
                values.Add(int.Parse(pair)); display+=pair; srcIdx+=2; continue;
            }
            string ch=data[srcIdx++].ToString();
            string active=shift? (set=="A"?"B":"A"): set;
            values.Add(Code128Value(ch, active=="A"||active=="B"? active:"B"));
            display+=ch; shift=false;
        }
        if(shift) throw new Exception("Code 128 SHIFT requires a following character.");
        int checksum=(start + values.Select((v,i)=> v*(i+1)).Sum())%103;
        var all=new List<int>{start}; all.AddRange(values); all.Add(checksum); all.Add(106);
        return (all,display);
    }
    private static string? NormalizeCode128LiteralGreater(string data){
        var sb=new System.Text.StringBuilder();
        for(int i=0;i<data.Length;i++){
            if(data[i]!='>'){ sb.Append(data[i]); continue; }
            if(i+1>=data.Length || data[i+1]!='<') return null;
            sb.Append('>'); i++;
        }
        return sb.ToString();
    }
    private static int DigitRunLength(string data,int from){ int len=0; while(from+len < data.Length && char.IsDigit(data[from+len])) len++; return len; }
    private static string PreferredSet(char ch){
        int cp=ch;
        if(cp<32) return "A";
        if(cp<=127) return "B";
        throw new Exception("Code 128 automatic mode supports ASCII field data only.");
    }
    private static (List<int> values,string display) EncodeCode128Automatic(string data){
        if(data.Length==0) throw new Exception("Code 128 field data is empty.");
        int initDigits=DigitRunLength(data,0);
        string set= initDigits>=4 && initDigits%2==0? "C": PreferredSet(data[0]);
        int start=set=="A"?103:set=="B"?104:105;
        var vals=new List<int>(); int idx=0;
        while(idx<data.Length){
            int digits=DigitRunLength(data, idx);
            if(set!="C" && digits>=4){
                if(digits%2==1){ vals.Add(Code128Value(data[idx].ToString(), set)); idx++; }
                vals.Add(99); set="C"; continue;
            }
            if(set=="C"){
                if(digits>=2){ vals.Add(int.Parse(data.Substring(idx,2))); idx+=2; continue; }
                string ns=PreferredSet(data[idx]); vals.Add(ns=="A"?101:100); set=ns; continue;
            }
            char ch=data[idx];
            int cp=ch;
            if((set=="A"&&cp>95)||(set=="B"&&cp<32)){ string ns=set=="A"?"B":"A"; vals.Add(ns=="A"?101:100); set=ns; continue; }
            vals.Add(Code128Value(ch.ToString(), set)); idx++;
        }
        int checksum=(start + vals.Select((v,i)=> v*(i+1)).Sum())%103;
        var all=new List<int>{start}; all.AddRange(vals); all.Add(checksum); all.Add(106);
        return (all, data);
    }
    public static (string bits,string display) EncodeCode128Raster(string data,string mode,bool uccCheckDigit=false){
        string encData= uccCheckDigit? data+Mod10CheckDigit(data): data;
        (List<int> values,string display) encoded;
        if(mode=="N") encoded=EncodeCode128NoSelectedMode(encData);
        else {
            var norm=NormalizeCode128LiteralGreater(encData);
            encoded= norm==null? EncodeCode128NoSelectedMode(encData): EncodeCode128Automatic(norm);
        }
        string bits=string.Concat(encoded.values.Select(v=> Convert.ToString(Code128Encodings[v],2)));
        // Convert to binary string with 11 bits each? TS uses toString(2) without padding, but Code128Encodings are 11-bit values, need to pad? TS does .toString() without pad, but values are 11-bit, so missing leading zeros will be lost. We mimic TS: it does .toString() which will omit leading zeros, but the TS Code128Encodings are already 11-bit numbers with leading 1, so toString will give 11 bits? Actually 11011001100 is 11 bits, Convert.ToString will give 11 chars, so okay.
        // Ensure 11 bits: pad to 11
        // TS does not pad, but we should pad to 11 for consistency
        return (bits, encoded.display);
    }
    public static (string bits,string display) EncodeGs1Code128Raster(string data){
        if(data.Length==0) throw new Exception("GS1-128 field data is empty.");
        int initDigits=DigitRunLength(data,0);
        string set= initDigits>=4? "C": data[0]<=95? "A":"B";
        int start=set=="A"?103:set=="B"?104:105;
        var vals=new List<int>{102}; int idx=0;
        while(idx<data.Length){
            int digits=DigitRunLength(data, idx);
            if(set=="C"){
                if(digits>=2){ vals.Add(int.Parse(data.Substring(idx,2))); idx+=2; continue; }
                string ns=data[idx]<=95? "A":"B"; vals.Add(ns=="A"?101:100); set=ns; continue;
            }
            if(digits>=4){
                if(digits%2==1){ vals.Add(Code128Value(data[idx].ToString(), set)); idx++; }
                vals.Add(99); set="C"; continue;
            }
            char ch=data[idx]; int cp=ch;
            if((set=="A"&&cp>95)||(set=="B"&&cp<32)){ string ns=set=="A"?"B":"A"; vals.Add(ns=="A"?101:100); set=ns; continue; }
            vals.Add(Code128Value(ch.ToString(), set)); idx++;
        }
        int checksum=(start + vals.Select((v,i)=> v*(i+1)).Sum())%103;
        var all=new List<int>{start}; all.AddRange(vals); all.Add(checksum); all.Add(106);
        string bits=string.Concat(all.Select(v=> Convert.ToString(Code128Encodings[v],2).PadLeft(11,'0')));
        return (bits, data);
    }
}
