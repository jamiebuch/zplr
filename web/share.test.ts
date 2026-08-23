import { describe, expect, it } from "vitest";
import {
  decodeSharedLabel,
  encodeSharedLabel,
  maxSharedLabelTokenLength,
  sharedLabelHashPrefix,
  sharedLabelTokenFromHash,
} from "./share";

const sampleSource = "^XA\n^CI28\n^PW812\n^LL1218\n^FO40,40^A0N,42,24^FDDeterministic label^FS\n^XZ\n";

describe("shared label links", () => {
  it("round-trips a label through a URL-safe token", () => {
    const token = encodeSharedLabel({ name: "shipping-label.zpl", source: sampleSource });
    expect(token).toMatch(/^[A-Za-z0-9_-]+$/);
    expect(decodeSharedLabel(token!)).toEqual({ name: "shipping-label.zpl", source: sampleSource });
  });

  it("defaults a missing filename", () => {
    const token = encodeSharedLabel({ source: sampleSource });
    expect(decodeSharedLabel(token!)).toEqual({ name: "shared-label.zpl", source: sampleSource });
  });

  it("round-trips optional variable data", () => {
    const data = {
      datasets: [
        {
          id: "d1",
          name: "Customers",
          columns: [{ id: "c1", name: "Customer", field: 1 }],
          records: [{ id: "r1", name: "Record 1", values: { c1: "Ada" } }],
        },
      ],
      activeDatasetId: "d1",
      activeRecordId: "r1",
    };
    const token = encodeSharedLabel({ name: "label.zpl", source: sampleSource, data });
    expect(decodeSharedLabel(token!)).toEqual({ name: "label.zpl", source: sampleSource, data });
  });

  it("keeps tokens within the documented size limit", () => {
    const largeSource = `^XA\n${" ^FO40,40^A0N,42,24^FDLine of label text^FS\n".repeat(20_000)}^XZ\n`;
    const token = encodeSharedLabel({ name: "large.zpl", source: largeSource });
    expect(token).toBeDefined();
    expect(token!.length).toBeLessThanOrEqual(maxSharedLabelTokenLength);
    expect(decodeSharedLabel(token!).source).toBe(largeSource);
  });

  it("rejects malformed, empty, and corrupted tokens", () => {
    expect(decodeSharedLabel("")).toBeUndefined();
    expect(decodeSharedLabel("!!!not-base64url!!!")).toBeUndefined();
    expect(decodeSharedLabel("abcd")).toBeUndefined();
    const token = encodeSharedLabel({ name: "label.zpl", source: sampleSource })!;
    const flip = (value: string) => (value === "A" ? "B" : "A");
    const mid = Math.floor(token.length / 2);
    const corrupted = token.slice(0, mid) + flip(token[mid]!) + token.slice(mid + 1);
    expect(decodeSharedLabel(corrupted)).toBeUndefined();
  });

  it("rejects empty sources and oversized labels", () => {
    expect(encodeSharedLabel({ name: "empty.zpl", source: "   " })).toBeUndefined();
    expect(encodeSharedLabel({ name: "empty.zpl", source: "" })).toBeUndefined();
    expect(encodeSharedLabel({ name: "huge.zpl", source: "x".repeat(8_000_001) })).toBeUndefined();
  });

  it("extracts tokens from URL hashes", () => {
    const token = encodeSharedLabel({ name: "label.zpl", source: sampleSource })!;
    expect(sharedLabelTokenFromHash(`${sharedLabelHashPrefix}${token}`)).toBe(token);
    expect(sharedLabelTokenFromHash("#example=caret-fo")).toBeUndefined();
    expect(sharedLabelTokenFromHash("")).toBeUndefined();
  });
});
