// Port of src/index.node.ts — Node renderer public API, now using SkiaSharp
using Zplr.Renderer.Core;
using Zplr.Renderer.Helper.Rendering;
using Zplr.Renderer.Types;

namespace Zplr.Renderer;

public static class ZplRenderer
{
    private static readonly SkiaCanvasPlatform DefaultPlatform = new();

    public static Task<RenderJobResult<SkiaCanvas>> RenderZplAsync(string source, RenderJobOptions? options = null)
        => JobRenderer.RenderZplWithPlatformAsync(source, options, DefaultPlatform);

    public static IZplRenderSession<SkiaCanvas> CreateRenderSession(RenderJobOptions? options = null)
        => JobRenderer.CreateRenderSessionWithPlatform(DefaultPlatform, options);

    public static async Task<byte[][]> RenderZplPngAsync(string source, RenderJobOptions? options = null)
    {
        var result = await RenderZplAsync(source, options);
        var pngs = new List<byte[]>();
        foreach (var label in result.Labels)
        {
            pngs.Add(label.Canvas.ToPngBytes());
            label.Canvas.Dispose();
        }
        return pngs.ToArray();
    }

    public static ZplDocument ParseDocument(string source, ParseDocumentOptions? options = null)
        => DocumentParser.ParseDocument(source, options);
}
