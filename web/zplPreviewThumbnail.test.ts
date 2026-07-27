import { describe, expect, it } from "vitest";
import { previewInkBounds } from "./zplPreviewThumbnail";

function whiteImage(width: number, height: number): Uint8ClampedArray {
  const pixels = new Uint8ClampedArray(width * height * 4);
  pixels.fill(255);
  return pixels;
}

function setBlack(pixels: Uint8ClampedArray, width: number, x: number, y: number): void {
  const offset = (y * width + x) * 4;
  pixels[offset] = 0;
  pixels[offset + 1] = 0;
  pixels[offset + 2] = 0;
}

describe("overview preview cropping", () => {
  it("trims white label space while preserving a requested margin", () => {
    const pixels = whiteImage(10, 8);
    setBlack(pixels, 10, 3, 2);
    setBlack(pixels, 10, 5, 4);

    expect(previewInkBounds(pixels, 10, 8, 1)).toEqual({
      x: 2,
      y: 1,
      width: 5,
      height: 5,
    });
  });

  it("returns no crop when the label has no rendered ink", () => {
    expect(previewInkBounds(whiteImage(10, 8), 10, 8, 1)).toBeUndefined();
  });
});
