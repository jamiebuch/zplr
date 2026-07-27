export interface PreviewInkBounds {
  x: number;
  y: number;
  width: number;
  height: number;
}

export function previewInkBounds(
  pixels: Uint8ClampedArray,
  width: number,
  height: number,
  padding = Math.max(6, Math.min(18, Math.round(Math.min(width, height) * 0.035))),
): PreviewInkBounds | undefined {
  if (width <= 0 || height <= 0 || pixels.length < width * height * 4) return undefined;

  let minX = width;
  let minY = height;
  let maxX = -1;
  let maxY = -1;
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const offset = (y * width + x) * 4;
      if (pixels[offset + 3]! === 0 || pixels[offset]! >= 128) continue;
      minX = Math.min(minX, x);
      minY = Math.min(minY, y);
      maxX = Math.max(maxX, x);
      maxY = Math.max(maxY, y);
    }
  }
  if (maxX < minX || maxY < minY) return undefined;

  const x = Math.max(0, minX - padding);
  const y = Math.max(0, minY - padding);
  const right = Math.min(width, maxX + padding + 1);
  const bottom = Math.min(height, maxY + padding + 1);
  return {
    x,
    y,
    width: right - x,
    height: bottom - y,
  };
}
