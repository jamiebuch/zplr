using Zplr.Renderer;
using Zplr.Renderer.Core;
using Zplr.Renderer.Types;

namespace Zplr.Renderer.Tests;

public class SmokeTests
{
    [Fact]
    public async Task RenderSimpleTextProducesPng()
    {
        var zpl = "^XA^FO50,50^ADN,36,20^FDHello World^FS^XZ";
        var pngs = await ZplRenderer.RenderZplPngAsync(zpl);
        Assert.Single(pngs);
        Assert.True(pngs[0].Length > 100);
        // PNG signature
        Assert.Equal(0x89, pngs[0][0]);
        Assert.Equal(0x50, pngs[0][1]);
        Assert.Equal(0x4E, pngs[0][2]);
        Assert.Equal(0x47, pngs[0][3]);
    }

    [Fact]
    public async Task RenderBoxProducesPng()
    {
        var zpl = "^XA^FO20,20^GB100,100,2^FS^XZ";
        var pngs = await ZplRenderer.RenderZplPngAsync(zpl);
        Assert.Single(pngs);
        Assert.True(pngs[0].Length > 100);
    }

    [Fact]
    public void ParseDocumentProducesLabel()
    {
        var doc = DocumentParser.ParseDocument("^XA^FO0,0^FDtest^FS^XZ");
        Assert.Single(doc.Labels);
    }

    [Fact]
    public async Task SessionRetainsSyntax()
    {
        var session = ZplRenderer.CreateRenderSession();
        var r1 = await session.RenderAsync("^XA^FO10,10^ADN,18,10^FDOne^FS^XZ");
        var r2 = await session.RenderAsync("^XA^FO10,10^ADN,18,10^FDTwo^FS^XZ");
        Assert.Single(r1.Labels);
        Assert.Single(r2.Labels);
    }

    [Fact]
    public void ZplNumbersParse()
    {
        Assert.Equal(42, ZplNumbers.ZplNumber("42"));
        Assert.Null(ZplNumbers.ZplNumber("abc"));
        Assert.Equal(2, ZplNumbers.ZplDotConversion("150", "300", 1), 5);
        Assert.Equal(1, ZplNumbers.ZplDotConversion("999", "999", 1), 5);
    }

    [Fact]
    public void GraphicDecoderValidate()
    {
        var (w,h) = GraphicDecoder.ValidateGraphicGeometry(10, 100, 16*1024*1024);
        Assert.Equal(80, w);
        Assert.Equal(10, h);
    }

    [Fact]
    public void CapabilitiesLookup()
    {
        var cap = Capabilities.GetCommandCapability("^FO");
        Assert.NotNull(cap);
        Assert.Equal(CommandCategory.Format, cap!.Category);
        // ^ZZ is known printer command -> non-rendering; unknown is e.g. ^QQ
        Assert.Equal(CommandCapabilityStatus.NonRendering, Capabilities.GetCommandCapabilityStatus("^ZZ"));
        Assert.Equal(CommandCapabilityStatus.Unknown, Capabilities.GetCommandCapabilityStatus("^QQ"));
    }

    [Fact]
    public async Task RenderFieldBlockProducesPng()
    {
        var zpl = "^XA^FO0,0^FB400,3,0,L,0^FDThis is a long text that should wrap across multiple lines when using field block^FS^XZ";
        var pngs = await ZplRenderer.RenderZplPngAsync(zpl);
        Assert.Single(pngs);
        Assert.True(pngs[0].Length > 500);
    }

    [Fact]
    public async Task RenderTextBlockProducesPng()
    {
        var zpl = "^XA^FO0,0^TB N,400,200^FDTest Block^FS^XZ";
        var pngs = await ZplRenderer.RenderZplPngAsync(zpl);
        Assert.Single(pngs);
        Assert.True(pngs[0].Length > 500);
    }

    [Fact]
    public async Task RenderBarcodePlaceholderProducesPng()
    {
        var zpl = "^XA^FO50,50^BCN,100,Y,N,N^FD123456^FS^XZ";
        var pngs = await ZplRenderer.RenderZplPngAsync(zpl);
        Assert.Single(pngs);
        Assert.True(pngs[0].Length > 500);
    }

    [Fact]
    public void InterpreterHandlesExtendedCommands()
    {
        var doc = DocumentParser.ParseDocument("^XA^FO10,10^BY2,3,10^FO20,20^FB100,2,0,L^FDtest^FS^FO30,30^GB50,50,2^FS^XZ");
        var layout = Interpreter.InterpretLabel(doc.Labels[0]);
        Assert.True(layout.Fields.Count >= 2);
        Assert.True(layout.Origins.Count >= 2);
    }

    [Fact]
    public async Task RenderSerialNumberWithQuantityProducesMultipleLabels()
    {
        var zpl = "^XA^FO50,50^ADN,36,20^SN001,1,Y^FS^PQ3^XZ";
        var res = await ZplRenderer.RenderZplAsync(zpl);
        Assert.Equal(3, res.Labels.Count);
    }

    [Fact]
    public async Task RenderRtcFieldProducesPng()
    {
        var zpl = "^XA^FO50,50^ADN,36,20^FC% ^FD%Y-%m-%d^FS^XZ";
        var opts = new RenderJobOptions{ Clock = new DateTime(2023,5,17,12,0,0, DateTimeKind.Utc) };
        var pngs = await ZplRenderer.RenderZplPngAsync(zpl, opts);
        Assert.Single(pngs);
        Assert.True(pngs[0].Length > 500);
    }

    [Fact]
    public async Task RenderCode39ProducesRealBars()
    {
        var zpl = "^XA^FO50,50^B3N,N,100,Y,N^FD123ABC^FS^XZ";
        var res = await ZplRenderer.RenderZplAsync(zpl);
        var raster = res.Labels[0].Raster;
        int black=0;
        for(int y=0;y<raster.Height;y++) for(int x=0;x<raster.Width;x++) if((raster.Data[y*raster.Stride + (x>>3)] & (0x80 >> (x&7))) !=0) black++;
        // Code39 with height 100 should produce >1000 black dots (not placeholder 70)
        Assert.True(black > 1000, $"black dots {black} should be >1000");
    }

    [Fact]
    public async Task RenderQrProducesMatrix()
    {
        var zpl = "^XA^FO50,50^BQN,2,10^FDQA,Hello QR^FS^XZ";
        var res = await ZplRenderer.RenderZplAsync(zpl);
        var raster = res.Labels[0].Raster;
        int black=0;
        for(int y=0;y<raster.Height;y++) for(int x=0;x<raster.Width;x++) if((raster.Data[y*raster.Stride + (x>>3)] & (0x80 >> (x&7))) !=0) black++;
        Assert.True(black > 1000, $"QR black dots {black} should be >1000");
    }
}
