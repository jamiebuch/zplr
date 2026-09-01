// Port of src/core/rasterRenderer.ts — minimal implementation for text + graphics
using QRCoder;
using ZXing;
using ZXing.Common;
using Zplr.Renderer.Types;

namespace Zplr.Renderer.Core;

public sealed class RasterRenderResult
{
    public MonochromeRaster Raster { get; set; } = null!;
    public List<ZplDiagnostic> Diagnostics { get; set; } = new();
    public List<HighlightRegion> HighlightRegions { get; set; } = new();
}

public static class RasterRenderer
{
    public static async Task<RasterRenderResult> RenderLayoutToRasterAsync(LabelLayout layout, int width, int height, int labelIndex, RasterRenderContext? ctx = null)
    {
        var raster = Raster.CreateMonochromeRaster(width, height);
        var diagnostics = new List<ZplDiagnostic>(layout.Diagnostics);
        var highlightRegions = new List<HighlightRegion>();

        // Apply initial raster if present (e.g., ^MC handling)
        if (ctx?.InitialRaster != null)
        {
            var src = ctx.InitialRaster;
            for (int y=0; y<Math.Min(src.Height, raster.Height); y++)
                for (int x=0; x<Math.Min(src.Width, raster.Width); x++)
                    if (Raster.GetDot(src, x, y)) Raster.SetDot(raster, x, y);
        }

        // Handle reverse label
        bool labelReverse = layout.Settings?.Reverse ?? false;
        if (labelReverse)
        {
            // Invert background: fill white then invert? Actually label reverse means initial raster inverted. Simplified: fill black then fields will xor.
            for (int y=0; y<raster.Height; y++)
                for (int x=0; x<raster.Width; x++)
                    Raster.SetDot(raster, x, y);
        }

        var fontEngine = new FontEngine(ctx?.FontProvider);

        foreach (var field in layout.Fields)
        {
            try
            {
                switch (field)
                {
                    case TextLayoutField tf:
                        await RenderTextField(raster, tf, fontEngine, labelIndex, diagnostics, highlightRegions);
                        break;
                    case BoxLayoutField bf:
                        RenderBox(raster, bf);
                        highlightRegions.Add(new HighlightRegion(HighlightRegionType.Box, bf.SourceSpan, bf.X, bf.Y, bf.Width, bf.Height));
                        break;
                    case CircleLayoutField cf:
                        Raster.StrokeCircle(raster, cf.X, cf.Y, cf.Diameter, cf.Thickness, cf.Reverse ? "xor" : "set");
                        break;
                    case EllipseLayoutField ef:
                        Raster.StrokeEllipse(raster, ef.X, ef.Y, ef.Width, ef.Height, ef.Thickness, ef.Reverse ? "xor" : "set");
                        break;
                    case DiagonalLayoutField df:
                        Raster.DrawDiagonal(raster, df.X, df.Y, df.Width, df.Height, df.Thickness, df.Direction, df.Reverse ? "xor" : "set");
                        break;
                    case BitmapLayoutField bmp:
                        RenderBitmap(raster, bmp);
                        break;
                    case GraphicSymbolLayoutField gs:
                        await RenderGraphicSymbol(raster, gs);
                        break;
                    case Code39LayoutField c39:
                        RenderCode39(raster, c39);
                        break;
                    case Code128LayoutField c128:
                        RenderCode128(raster, c128);
                        break;
                    case QrLayoutField qr:
                        RenderQr(raster, qr);
                        break;
                    case ExtendedBarcodeLayoutField ext:
                        if (ext.Encoder == "datamatrix" || ext.Encoder == "datamatrixrectangular" || ext.Symbology == "BX") RenderDataMatrix(raster, ext);
                        else if (ext.Encoder == "pdf417" || ext.Symbology == "B7") RenderPdf417(raster, ext);
                        else if (ext.Encoder == "micropdf417" || ext.Symbology == "BF") RenderPdf417(raster, ext);
                        else if (ext.Encoder == "code39" || ext.Symbology == "BL") RenderCode39(raster, new Code39LayoutField(ext.X, ext.Y, ext.Orientation, ext.Reverse, ext.CommandIndex, ext.SourceSpan, ext.Data, ext.ModuleWidth, ext.Height, ext.PrintInterpretationBelow, ext.PrintInterpretationAbove, ext.InterpretationFont, ext.Validation, ext.Ratio ?? 3, false));
                        else if (ext.Encoder.Contains("code128") || ext.Encoder.Contains("databar") || ext.Encoder.Contains("ean") || ext.Encoder.Contains("upc")) {
                            RenderCode128(raster, new Code128LayoutField(ext.X, ext.Y, ext.Orientation, ext.Reverse, ext.CommandIndex, ext.SourceSpan, ext.Data, ext.ModuleWidth, ext.Height, ext.PrintInterpretationBelow, ext.PrintInterpretationAbove, ext.InterpretationFont, ext.Validation, false, "N"));
                        } else {
                            Raster.FillRect(raster, ext.X, ext.Y, Math.Max(10, ext.Height), 10, ext.Reverse ? "xor" : "set");
                        }
                        break;
                    default:
                        if (field is BarcodeLayoutField bf2)
                        {
                            Raster.FillRect(raster, bf2.X, bf2.Y, Math.Max(10, bf2.Height), 10, bf2.Reverse ? "xor" : "set");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(new ZplDiagnostic("RENDER_FAILED", ZplDiagnosticSeverity.Error, ZplDiagnosticPhase.Render, ex.Message, field.SourceSpan, null, null, labelIndex));
            }
        }

        // Apply mirror/rotate if needed
        if (layout.Settings?.Mirror == true || layout.Settings?.Rotate180 == true)
        {
            raster = Raster.TransformRaster(raster, false, layout.Settings.Mirror, layout.Settings.Rotate180);
        }

        // Crop to content height if variable?
        return new RasterRenderResult { Raster = raster, Diagnostics = diagnostics, HighlightRegions = highlightRegions };
    }

    private static int MeasureText(string value, LayoutFont font){
        bool proportional = font.Key == "0" || font.Name != null;
        int w=0;
        foreach(var ch in value){
            string s=ch.ToString();
            w+= proportional? BitmapFont.GlyphAdvance(s, font.Width, true): BitmapFont.ResidentAdvanceWidth(font.Key, font.Width);
        }
        return w;
    }
    private static int MeasureFieldText(string value, TextLayoutField field){
        var chars = value.ToCharArray();
        int w = MeasureText(value, field.Font);
        if(chars.Length>1) w += (chars.Length-1)* Math.Max(0, field.CharacterGap);
        return w;
    }
    private static string VisibleText(string s) => s.Replace("\u00AD", "");
    private static string ParseBlockEscapes(string data){
        var sb=new System.Text.StringBuilder();
        for(int i=0;i<data.Length;i++){
            if(data[i]!='\\'){ sb.Append(data[i]=='\u00AD'? "-": data[i].ToString()); continue; }
            char nxt=i+1<data.Length? data[i+1]: '\0';
            if(nxt=='&'){ sb.Append("\n"); i++; }
            else if(nxt=='\\'){ sb.Append("\\"); i++; }
            else if(nxt!=0 && char.IsLetterOrDigit(nxt)){ sb.Append("\u00AD"); i++; }
            else sb.Append("\\");
        }
        return sb.ToString();
    }
    private static string ParseTextBlockEscapes(string data){
        var sb=new System.Text.StringBuilder();
        for(int i=0;i<data.Length;i++){
            if(data[i]!='<'){ if(data[i]!='\u00AD') sb.Append(data[i]); continue; }
            if(i+1<data.Length && data[i+1]=='<'){ sb.Append("<"); i++; continue; }
            int end=data.IndexOf('>', i+1);
            if(end<0) sb.Append("<"); else i=end;
        }
        return sb.ToString();
    }
    private static (string head,string tail) SplitLongWord(string word, int available, TextLayoutField field){
        var chars=word.ToCharArray().ToList();
        int bestSoft=-1;
        for(int i=0;i<chars.Count;i++) if(chars[i]=='\u00AD'){
            string prefix=VisibleText(new string(chars.Take(i).ToArray()));
            string cand=prefix+"-";
            if(!string.IsNullOrEmpty(prefix) && MeasureFieldText(cand, field) <= available) bestSoft=i;
        }
        if(bestSoft>=0){
            string head=VisibleText(new string(chars.Take(bestSoft).ToArray()))+"-";
            string tail=new string(chars.Skip(bestSoft+1).ToArray());
            return (head,tail);
        }
        var visible=VisibleText(word).ToCharArray();
        int count=0;
        while(count<visible.Length){
            string cand=new string(visible.Take(count+1).ToArray());
            if(count>0 && MeasureFieldText(cand, field) > available) break;
            count++;
        }
        count=Math.Max(1, Math.Min(count, visible.Length));
        if(count>=visible.Length) return (new string(visible), "");
        int split=0, rem=count;
        while(split<chars.Count && rem>0){ if(chars[split]!='\u00AD') rem--; split++; }
        while(split<chars.Count && chars[split]=='\u00AD') split++;
        return (new string(visible.Take(count).ToArray()), new string(chars.Skip(split).ToArray()));
    }
    private sealed class TextLine { public string Text=""; public int Width; public int Indent; public bool ParagraphEnd; public List<TextLine>? Overprints; }
    private static List<TextLine> WrapParagraph(string paragraph, TextLayoutField field, int lineOffset){
        var block=field.Block!;
        if(paragraph.Length==0){
            int indent= lineOffset==0?0:block.HangingIndent;
            return new List<TextLine>{ new TextLine{ Text="", Width=0, Indent=indent, ParagraphEnd=true } };
        }
        var words=paragraph.Split(new[]{' ','\t'}, StringSplitOptions.RemoveEmptyEntries).ToList();
        var lines=new List<TextLine>();
        string current="";
        int wordIdx=0;
        int Indent()=> lineOffset+lines.Count==0?0:block.HangingIndent;
        while(wordIdx<words.Count){
            string word=words[wordIdx];
            string cand= string.IsNullOrEmpty(current)? VisibleText(word): current+" "+VisibleText(word);
            int avail=Math.Max(1, block.Width - Indent());
            if(MeasureFieldText(cand, field) <= avail){ current=cand; wordIdx++; continue; }
            if(!string.IsNullOrEmpty(current)){
                lines.Add(new TextLine{ Text=current, Width=MeasureFieldText(current, field), Indent=Indent(), ParagraphEnd=false });
                current=""; continue;
            }
            var split=SplitLongWord(word, avail, field);
            lines.Add(new TextLine{ Text=split.head, Width=MeasureFieldText(split.head, field), Indent=Indent(), ParagraphEnd=false });
            if(!string.IsNullOrEmpty(split.tail)) words[wordIdx]=split.tail; else wordIdx++;
        }
        if(!string.IsNullOrEmpty(current) || lines.Count==0) lines.Add(new TextLine{ Text=current, Width=MeasureFieldText(current, field), Indent=Indent(), ParagraphEnd=true });
        else lines[lines.Count-1].ParagraphEnd=true;
        return lines;
    }
    private static List<TextLine> LayoutTextLines(TextLayoutField field){
        if(field.Block==null) return new List<TextLine>{ new TextLine{ Text=field.Data, Width=MeasureFieldText(field.Data, field), Indent=0, ParagraphEnd=true } };
        string normalized = field.Block.Mode=="TB"? ParseTextBlockEscapes(field.Data): ParseBlockEscapes(field.Data.Substring(0, Math.Min(field.Data.Length, 3*1024)));
        int naturalWidth = Math.Max(field.Font.Width, normalized.Split(new[]{"\r\n","\n"}, StringSplitOptions.None).Max(p=> MeasureFieldText(p, field)));
        int blockWidth = field.Block.Width>0? Math.Max(field.Font.Width, field.Block.Width): field.Block.Mode=="FB" && field.Block.HangingIndent>0? naturalWidth: field.Font.Width;
        string blockJust = field.Block.Width>0? field.Block.Justification: "L";
        var layoutField = blockWidth==field.Block.Width && blockJust==field.Block.Justification? field: new TextLayoutField(field.X, field.Y, field.Orientation, field.Reverse, field.CommandIndex, field.SourceSpan, field.Data, field.Font, new LayoutFieldBlock(blockWidth, field.Block.MaxLines, field.Block.LineSpacing, blockJust, field.Block.HangingIndent, field.Block.Height, field.Block.Mode), field.Typeset, field.Direction, field.CharacterGap, field.OriginJustification, field.AdvancedText);
        var lines=new List<TextLine>();
        foreach(var para in normalized.Split(new[]{"\r\n","\n"}, StringSplitOptions.None)) lines.AddRange(WrapParagraph(para, layoutField, lines.Count));
        int lineStep=Math.Max(1, field.Font.Height + (field.Block.LineSpacing));
        int heightLines = field.Block.Height==null? field.Block.MaxLines: Math.Max(1, (field.Block.Height.Value - field.Font.Height)/lineStep +1);
        int maxLines=Math.Min(field.Block.MaxLines, heightLines);
        if(lines.Count<=maxLines) return lines;
        if(field.Block.Mode=="TB") return lines.Take(maxLines).ToList();
        var retained=lines.Take(maxLines).ToList();
        if(retained.Count==0) return retained;
        var overflow=lines.Skip(maxLines-1).ToList();
        retained[retained.Count-1]=new TextLine{ Text=overflow[0].Text, Width=overflow[0].Width, Indent=overflow[0].Indent, ParagraphEnd=overflow[0].ParagraphEnd, Overprints=overflow.Skip(1).ToList() };
        return retained;
    }

    private static async Task RenderTextField(MonochromeRaster target, TextLayoutField field, FontEngine engine, int labelIndex, List<ZplDiagnostic> diagnostics, List<HighlightRegion> highlights)
    {
        var font = field.Font;
        bool proportional = font.Key == "0" || font.Name != null;
        int x = field.X;
        int y = field.Y;
        if (field.Typeset == true) y = Math.Max(0, y - font.Height);
        // Use FB/TB wrapping via LayoutTextLines
        var lines = LayoutTextLines(field);
        int lineStep = Math.Max(1, field.Font.Height + (field.Block?.LineSpacing ?? 0));
        int logicalWidth = lines.Count==0? field.Font.Width: Math.Max(field.Font.Width, lines.Max(l=> l.Indent + l.Width));
        if(field.Block!=null) logicalWidth = Math.Max(logicalWidth, field.Block.Width);
        int logicalHeight = field.Block?.Mode=="TB" && field.Block.Height!=null? Math.Min(field.Block.Height.Value, field.Font.Height + Math.Max(0, lines.Count-1)*lineStep) : field.Font.Height + Math.Max(0, lines.Count-1)*lineStep;
        logicalWidth=Math.Max(1, logicalWidth);
        logicalHeight=Math.Max(1, logicalHeight);
        // For simple case without block, keep old path
        if(field.Block==null){
            int cursorX = x;
            highlights.Add(new HighlightRegion(HighlightRegionType.Text, field.SourceSpan, x, y, null, null, null, null));
            foreach(var ch in field.Data ?? ""){
                string s=ch.ToString();
                if(s=="\n"||s=="\r") continue;
                MonochromeRaster? glyphRaster=null;
                if(font.Name!=null) glyphRaster=await engine.RasterizeAsync(font.Name, s, font.Width, font.Height);
                else if(font.Key=="0") glyphRaster=await engine.RasterizeBuiltInAsync(s, font.Width, font.Height);
                else if(BitmapFont.IsResidentFontKey(font.Key)){
                    bool usesOutline=BitmapFont.ResidentUsesOutlineFace(font.Key);
                    if(usesOutline){ var builtIn=await engine.RasterizeBuiltInAsync(BitmapFont.ResidentCharacter(font.Key,s)??s, font.Width, font.Height); glyphRaster= builtIn ?? BitmapFont.RasterizeGlyph(s, font.Width, font.Height, proportional, font.Key); }
                    else glyphRaster=BitmapFont.RasterizeGlyph(s, font.Width, font.Height, proportional, font.Key);
                } else glyphRaster=BitmapFont.RasterizeGlyph(s, font.Width, font.Height, proportional, "A");
                if(glyphRaster==null) glyphRaster=Raster.CreateMonochromeRaster(font.Width, font.Height);
                Raster.BlitRaster(target, glyphRaster, cursorX, y, field.Font.Orientation, 1, 1, field.Reverse ? "xor" : "set");
                int adv= proportional? BitmapFont.GlyphAdvance(s, font.Width, true): BitmapFont.ResidentAdvanceWidth(font.Key, font.Width);
                if(field.Font.Orientation== Orientation.N) cursorX+=adv;
                else if(field.Font.Orientation== Orientation.R) y+=adv;
                else if(field.Font.Orientation== Orientation.I) cursorX-=adv;
                else if(field.Font.Orientation== Orientation.B) y-=adv;
            }
            return;
        }
        // FB/TB rendering - render each line
        highlights.Add(new HighlightRegion(HighlightRegionType.Text, field.SourceSpan, x, y, null, null, null, null));
        for(int lineIdx=0; lineIdx<lines.Count; lineIdx++){
            var line=lines[lineIdx];
            int avail=logicalWidth - line.Indent;
            int cursorX=line.Indent;
            if(field.Block?.Justification=="C") cursorX+= Math.Max(0, (avail - line.Width)/2);
            else if(field.Block?.Justification=="R") cursorX+= Math.Max(0, avail - line.Width);
            foreach(var ch in line.Text){
                string s=ch.ToString();
                MonochromeRaster? glyphRaster=null;
                if(font.Name!=null) glyphRaster=await engine.RasterizeAsync(font.Name, s, font.Width, font.Height);
                else if(font.Key=="0") glyphRaster=await engine.RasterizeBuiltInAsync(s, font.Width, font.Height);
                else if(BitmapFont.IsResidentFontKey(font.Key)){
                    bool usesOutline=BitmapFont.ResidentUsesOutlineFace(font.Key);
                    if(usesOutline){ var builtIn=await engine.RasterizeBuiltInAsync(BitmapFont.ResidentCharacter(font.Key,s)??s, font.Width, font.Height); glyphRaster= builtIn ?? BitmapFont.RasterizeGlyph(s, font.Width, font.Height, proportional, font.Key); }
                    else glyphRaster=BitmapFont.RasterizeGlyph(s, font.Width, font.Height, proportional, font.Key);
                } else glyphRaster=BitmapFont.RasterizeGlyph(s, font.Width, font.Height, proportional, "A");
                if(glyphRaster==null) glyphRaster=Raster.CreateMonochromeRaster(font.Width, font.Height);
                Raster.BlitRaster(target, glyphRaster, x+cursorX, y+lineIdx*lineStep, field.Font.Orientation, 1, 1, field.Reverse ? "xor" : "set");
                int adv= proportional? BitmapFont.GlyphAdvance(s, font.Width, true): BitmapFont.ResidentAdvanceWidth(font.Key, font.Width);
                cursorX+=adv + (s==" " && field.Block?.Justification=="J" && !line.ParagraphEnd && lineIdx!=lines.Count-1? Math.Max(0, (avail - line.Width)/ Math.Max(1, line.Text.Count(c=>c==' '))):0);
                if(field.CharacterGap!=0 && ch!=line.Text.Last()) cursorX+= field.CharacterGap;
            }
            if(line.Overprints!=null) foreach(var op in line.Overprints){
                int opX=line.Indent;
                if(field.Block?.Justification=="C") opX+= Math.Max(0, (avail - op.Width)/2);
                else if(field.Block?.Justification=="R") opX+= Math.Max(0, avail - op.Width);
                foreach(var ch in op.Text){
                    string s=ch.ToString();
                    var g= BitmapFont.RasterizeGlyph(s, font.Width, font.Height, proportional, font.Key);
                    Raster.BlitRaster(target, g, x+opX, y+lineIdx*lineStep, field.Font.Orientation, 1, 1, field.Reverse ? "xor" : "set");
                    int adv= proportional? BitmapFont.GlyphAdvance(s, font.Width, true): BitmapFont.ResidentAdvanceWidth(font.Key, font.Width);
                    opX+=adv;
                }
            }
        }
    }

    private static void RenderBox(MonochromeRaster target, BoxLayoutField bf)
    {
        var op = bf.Reverse ? "xor" : (bf.Color == "W" ? "clear" : "set");
        if (bf.Rounding > 0) Raster.StrokeRoundedRect(target, bf.X, bf.Y, bf.Width, bf.Height, bf.Thickness, bf.Rounding, op);
        else Raster.StrokeRect(target, bf.X, bf.Y, bf.Width, bf.Height, bf.Thickness, op);
    }

    private static void RenderBitmap(MonochromeRaster target, BitmapLayoutField bmp)
    {
        var raster = Raster.CreateMonochromeRaster(bmp.Width, bmp.Height);
        for (int y=0;y<bmp.Height;y++)
            for (int x=0;x<bmp.Width;x++)
            {
                var b = bmp.Data[y * bmp.BytesPerRow + (x>>3)];
                if ((b & (0x80 >> (x &7))) !=0) Raster.SetDot(raster, x, y);
            }
        Raster.BlitRaster(target, raster, bmp.X, bmp.Y, bmp.Orientation, bmp.ScaleX, bmp.ScaleY, bmp.Reverse ? "xor" : "set");
    }

    private static async Task RenderGraphicSymbol(MonochromeRaster target, GraphicSymbolLayoutField gs)
    {
        var data = Zplr.Renderer.Assets.ZebraGraphicSymbols.GetData(gs.Code);
        if (data == null) return;
        // Zebra symbols are 80x60, scaling logic from TS graphicSymbolRaster
        int srcW = Zplr.Renderer.Assets.ZebraGraphicSymbols.Width;
        int srcH = Zplr.Renderer.Assets.ZebraGraphicSymbols.Height;
        int srcBpr = Zplr.Renderer.Assets.ZebraGraphicSymbols.BytesPerRow;
        var srcRaster = Raster.CreateMonochromeRaster(srcW, srcH);
        for (int y=0;y<srcH;y++)
            for (int x=0;x<srcW;x++)
            {
                var b = data[y * srcBpr + (x>>3)];
                if ((b & (0x80 >> (x &7))) !=0) Raster.SetDot(srcRaster, x, y);
            }
        int w = Math.Max(1, (int)Math.Ceiling(gs.Width * srcW / 60.0));
        int h = Math.Max(1, gs.Height);
        // Scale nearest dot
        var scaled = Raster.CreateMonochromeRaster(w, h);
        for (int y=0;y<h;y++)
        {
            int sy = Math.Min(srcH-1, (int)Math.Floor(y * (double)srcH / h));
            for (int x=0;x<w;x++)
            {
                int sx = Math.Min(srcW-1, (int)Math.Floor(x * (double)srcW / w));
                if (Raster.GetDot(srcRaster, sx, sy)) Raster.SetDot(scaled, x, y);
            }
        }
        Raster.BlitRaster(target, scaled, gs.X, gs.Y, gs.Orientation, 1, 1, gs.Reverse ? "xor" : "set");
        await Task.CompletedTask;
    }

    private static void RenderCode39(MonochromeRaster target, Code39LayoutField f)
    {
        var runs = LayoutRenderer.Code39Runs(f.Data);
        int x = f.X;
        int y = f.Y;
        string op = f.Reverse ? "xor" : "set";
        // For Code39, narrow = moduleWidth, wide = moduleWidth * ratio
        // Runs are in units of narrow modules, but our Code39Runs currently returns runs where each unit is 1 narrow module?
        // We will treat each run's units as narrow modules * ratio if wide? Simplified: use moduleWidth for all
        foreach(var (black, units) in runs){
            int w = units * f.ModuleWidth;
            // For wide bars, if units corresponds to wide (ratio), but our runs doesn't differentiate, we approximate
            // If ratio !=1, we could scale wide bars: but our runs currently treats all as 1, so we need to handle ratio manually via checking if units>1? For now use w as is
            if(black) Raster.FillRect(target, x, y, w, f.Height, op);
            x += w;
        }
    }

    private static void RenderCode128(MonochromeRaster target, Code128LayoutField f)
    {
        var (bits, display) = LayoutRenderer.EncodeCode128Raster(f.Data, f.Mode == "N" ? "N" : "A", f.UccCheckDigit);
        int x = f.X;
        int y = f.Y;
        string op = f.Reverse ? "xor" : "set";
        foreach(char bit in bits){
            if(bit=='1') Raster.FillRect(target, x, y, f.ModuleWidth, f.Height, op);
            x += f.ModuleWidth;
        }
    }

    private static void RenderQr(MonochromeRaster target, QrLayoutField f)
    {
        try{
            var gen = new QRCodeGenerator();
            var ecc = f.Reliability switch{ "L"=>QRCodeGenerator.ECCLevel.L, "M"=>QRCodeGenerator.ECCLevel.M, "Q"=>QRCodeGenerator.ECCLevel.Q, "H"=>QRCodeGenerator.ECCLevel.H, _=>QRCodeGenerator.ECCLevel.M };
            var data = gen.CreateQrCode(f.Data, ecc);
            var matrix = data.ModuleMatrix;
            int size = matrix.Count;
            int moduleSize = Math.Max(1, f.ModuleWidth);
            for(int r=0;r<size;r++) for(int c=0;c<size;c++) if(matrix[r][c]){
                int px = f.X + c*moduleSize;
                int py = f.Y + r*moduleSize;
                Raster.FillRect(target, px, py, moduleSize, moduleSize, f.Reverse ? "xor" : "set");
            }
        } catch{
            Raster.FillRect(target, f.X, f.Y, Math.Max(10, f.Height), 10, f.Reverse ? "xor" : "set");
        }
    }

    private static void RenderDataMatrix(MonochromeRaster target, ExtendedBarcodeLayoutField f)
    {
        try{
            var writer = new ZXing.Datamatrix.DataMatrixWriter();
            var hints = new Dictionary<EncodeHintType, object>{ { EncodeHintType.MARGIN, 0 } };
            var matrix = writer.encode(f.Data, BarcodeFormat.DATA_MATRIX, 0, 0, hints);
            int w = matrix.Width, h = matrix.Height;
            int mod = Math.Max(1, f.ModuleWidth);
            for(int y=0;y<h;y++) for(int x=0;x<w;x++) if(matrix[x,y]){
                Raster.FillRect(target, f.X + x*mod, f.Y + y*mod, mod, mod, f.Reverse ? "xor" : "set");
            }
        } catch{
            Raster.FillRect(target, f.X, f.Y, Math.Max(10, f.Height), 10, f.Reverse ? "xor" : "set");
        }
    }

    private static void RenderPdf417(MonochromeRaster target, ExtendedBarcodeLayoutField f)
    {
        try{
            var writer = new ZXing.PDF417.PDF417Writer();
            var hints = new Dictionary<EncodeHintType, object>{ { EncodeHintType.MARGIN, 0 }, { EncodeHintType.PDF417_COMPACT, false } };
            // Try to set dimensions from f.EncoderOptions if available
            if(f.EncoderOptions.TryGetValue("columns", out var cols) && cols is int c && c>0) hints[EncodeHintType.PDF417_DIMENSIONS] = new ZXing.PDF417.Internal.Dimensions(c, 1, c, 90);
            var matrix = writer.encode(f.Data, BarcodeFormat.PDF_417, 0, 0, hints);
            int w = matrix.Width, h = matrix.Height;
            int mod = Math.Max(1, f.ModuleWidth);
            // PDF417 height is overallHeight or Height
            int targetH = f.Height;
            int scaleY = Math.Max(1, targetH / Math.Max(1, h));
            for(int y=0;y<h;y++) for(int x=0;x<w;x++) if(matrix[x,y]){
                Raster.FillRect(target, f.X + x*mod, f.Y + y*scaleY, mod, scaleY, f.Reverse ? "xor" : "set");
            }
        } catch{
            Raster.FillRect(target, f.X, f.Y, Math.Max(10, f.Height), 10, f.Reverse ? "xor" : "set");
        }
    }
}

public sealed class RasterRenderContext
{
    public IReadOnlyDictionary<string, Interpreter.StoredGraphic>? Graphics { get; set; }
    public IReadOnlyDictionary<string, string>? FontAliases { get; set; }
    public IReadOnlyDictionary<string, string>? MemoryAliases { get; set; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<int,int>>? Encodings { get; set; }
    public IFontProvider? FontProvider { get; set; }
    public MonochromeRaster? InitialRaster { get; set; }
    public IReadOnlyDictionary<string, DownloadedBitmapFont>? BitmapFonts { get; set; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? FontLinks { get; set; }
    public int MaxFieldPixels { get; set; } = 40000000;
    public int? MinimumHeight { get; set; }
}
