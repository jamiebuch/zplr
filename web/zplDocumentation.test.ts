import { describe, expect, it } from "vitest";
import { parseDocument } from "../src/core/documentParser";
import { renderZpl } from "../src/index.node";
import {
  getZplCommandPreviewExample,
  getZplCommandGuide,
  getZplDocumentationExample,
  zplCommandIndexEntry,
  zplCommandGuides,
  zplDocumentationCoverage,
} from "./zplDocumentation";

function flattenedCommands(source: string): string[] {
  return parseDocument(source).items.flatMap((item) =>
    item.kind === "label"
      ? item.commands.map(({ canonical }) => canonical)
      : [item.canonical]);
}

function allDocumentationExamples() {
  return zplCommandGuides.flatMap((guide) => [
    ...(guide.featuredExample ? [guide.featuredExample] : []),
    ...guide.signatures.flatMap((signature) => [
      ...signature.examples,
      ...signature.parameters.flatMap((parameter) => parameter.examples),
    ]),
  ]);
}

type RenderedRaster = Awaited<ReturnType<typeof renderZpl>>["labels"][number]["raster"];

function previewInkAnalysis(raster: RenderedRaster) {
  const height = Math.min(raster.height, 300);
  let count = 0;
  let minX = raster.width;
  let minY = height;
  let maxX = -1;
  let maxY = -1;
  let hash = 2_166_136_261;

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < raster.width; x++) {
      const dark = Boolean(
        raster.data[y * raster.stride + (x >> 3)]! & (1 << (7 - (x & 7))),
      );
      hash = Math.imul(hash ^ Number(dark), 16_777_619) >>> 0;
      if (!dark) continue;
      count++;
      minX = Math.min(minX, x);
      minY = Math.min(minY, y);
      maxX = Math.max(maxX, x);
      maxY = Math.max(maxY, y);
    }
  }
  return { count, minX, minY, maxX, maxY, hash, height };
}

const qualityCheckedComparisonCommands = new Set([
  "^A",
  "^A@",
  "^CF",
  "^CI",
  "^FB",
  "^FC",
  "^FD",
  "^FE",
  "^FH",
  "^FO",
  "^FP",
  "^FR",
  "^FS",
  "^FT",
  "^FV",
  "^FW",
  "^GB",
  "^GC",
  "^GD",
  "^GE",
  "^GS",
  "^LR",
  "^PM",
  "^PO",
  "^SN",
  "^TB",
]);

function qualityCheckedComparison(command: string, parameterKey: string): boolean {
  if (!qualityCheckedComparisonCommands.has(command)) return false;
  if (command === "^A@" && ["d", "f", "x"].includes(parameterKey)) return false;
  if (["^CI", "^FE", "^FH", "^TB"].includes(command)) return false;
  if (["^GB", "^GC", "^GD", "^GE"].includes(command) && parameterKey === "c") return false;
  if (command === "^SN" && parameterKey === "n") return false;
  if (command === "^FW" && parameterKey === "z") return false;
  if (command === "^GD" && parameterKey === "o") return false;
  return true;
}

describe("interactive ZPL documentation", () => {
  it("covers the complete pinned command catalog", () => {
    expect(zplDocumentationCoverage).toMatchObject({
      commands: 223,
      signatures: 225,
      parameters: 630,
    });
    expect(zplDocumentationCoverage.examples).toBeGreaterThan(1_200);
    expect(zplDocumentationCoverage.previewExamples).toBeGreaterThan(600);

    const slugs = zplCommandGuides.map(({ slug }) => slug);
    expect(new Set(slugs).size).toBe(slugs.length);
    for (const guide of zplCommandGuides) {
      expect(getZplCommandGuide(guide.slug)).toBe(guide);
      expect(guide.summary).toContain(guide.canonical);
      expect(guide.reference).toMatch(/^https:\/\/docs\.zebra\.com\//);
    }
  });

  it("provides two or three variants for every parameter", () => {
    for (const guide of zplCommandGuides) {
      for (const signature of guide.signatures) {
        if (signature.parameters.length === 0) {
          expect(signature.examples, `${guide.canonical} ${signature.syntax}`).toHaveLength(1);
        }
        for (const parameter of signature.parameters) {
          expect(
            parameter.examples.length,
            `${guide.canonical} ${signature.syntax} ${parameter.key}`,
          ).toBeGreaterThanOrEqual(2);
          expect(
            parameter.examples.length,
            `${guide.canonical} ${signature.syntax} ${parameter.key}`,
          ).toBeLessThanOrEqual(3);
        }
      }
    }
  });

  it("provides one representative overview preview for every visual command", () => {
    const previewedCommands: string[] = [];
    for (const guide of zplCommandGuides) {
      const example = getZplCommandPreviewExample(guide);
      const indexEntry = zplCommandIndexEntry(guide);
      if (indexEntry.previewSource) {
        previewedCommands.push(guide.canonical);
        expect(guide.effect, guide.canonical).toBe("raster");
        expect(indexEntry.previewSource, guide.canonical).not.toMatch(
          /Job command|Text command example|Format command/,
        );
        expect(
          flattenedCommands(indexEntry.previewSource),
          guide.canonical,
        ).toContain(guide.canonical);
      } else {
        expect(guide.featuredExample, guide.canonical).toBeUndefined();
      }
      if (
        guide.effect === "device" ||
        guide.effect === "job" ||
        guide.status === "unsupported"
      ) {
        expect(example, guide.canonical).toBeUndefined();
        expect(indexEntry.previewSource, guide.canonical).toBeUndefined();
      }
    }
    expect(previewedCommands).toContain("^A");
    expect(previewedCommands).toContain("^BQ");
    expect(previewedCommands).toContain("^GB");
    expect(previewedCommands).toContain("^TB");
    expect(previewedCommands).not.toContain("^CC");
    expect(previewedCommands).not.toContain("^CV");
    expect(previewedCommands).not.toContain("^FM");
    expect(previewedCommands.length).toBeGreaterThan(50);
  });

  it("keeps normal raster examples rendered and device or job examples code-only", () => {
    const examples = allDocumentationExamples();
    expect(new Set(examples.map(({ id }) => id)).size).toBe(examples.length);

    for (const example of examples) {
      expect(getZplDocumentationExample(example.id)).toBe(example);
      expect(example.id).toMatch(/^[a-z0-9-]+$/);
      expect(example.filename).toMatch(/^[a-z0-9-]+\.zpl$/);
      expect(example.cursorOffset).toBeGreaterThanOrEqual(0);
      expect(example.cursorOffset).toBeLessThan(example.source.length);
      expect(flattenedCommands(example.source), example.id).toContain(example.command);

      const guide = zplCommandGuides.find(({ canonical }) => canonical === example.command)!;
      const shouldPreview = guide.effect === "raster" && guide.status !== "unsupported";
      expect(example.preview, example.id).toBe(shouldPreview);
      if (example.preview && !example.id.endsWith("-recommended")) {
        expect(example.source, example.id).toContain("^XA");
        expect(example.source, example.id).toContain("^XZ");
      }
    }
  });

  it("keeps every normal ^B0 variation rendered as a complete, visible label", async () => {
    const guide = getZplCommandGuide("caret-b0");
    expect(guide).toBeDefined();
    const examples = guide!.signatures.flatMap((signature) =>
      signature.parameters.flatMap((parameter) => parameter.examples),
    );
    expect(examples.length).toBeGreaterThan(10);

    for (const example of examples) {
      expect(example.preview, example.id).toBe(true);
      expect(example.source, example.id).toContain("^XA");
      expect(example.source, example.id).toContain("^FDHELLO AZTEC^FS");
      expect(example.source, example.id).toContain("^XZ");

      const result = await renderZpl(example.source);
      const label = result.labels[0];
      expect(label, example.id).toBeDefined();
      const analysis = previewInkAnalysis(label!.raster);
      expect(analysis.count, example.id).toBeGreaterThanOrEqual(40);
      expect(analysis.minX, example.id).toBeGreaterThan(0);
      expect(analysis.minY, example.id).toBeGreaterThan(0);
      expect(analysis.maxX, example.id).toBeLessThan(label!.width - 1);
      expect(analysis.maxY, example.id).toBeLessThan(analysis.height - 1);
    }
  });

  it("creates executable ^FC examples with valid, visible clock indicators", async () => {
    const guide = getZplCommandGuide("caret-fc");
    expect(guide).toBeDefined();
    const parameters = guide!.signatures[0]!.parameters;
    expect(parameters.map(({ key, examples }) => ({
      key,
      values: examples.map(({ value }) => value),
    }))).toEqual([
      { key: "a", values: ["%", "@"] },
      { key: "b", values: ["{", "&"] },
      { key: "c", values: ["#", "!"] },
    ]);

    for (const example of parameters.flatMap(({ examples }) => examples)) {
      const rawCommand = example.source.match(/\^FC[^\r\n]*/)?.[0];
      expect(rawCommand, example.id).toBeDefined();
      const indicators = rawCommand!.slice(3).split(",");
      expect(new Set(indicators).size, example.id).toBe(3);
      expect(indicators.every((indicator) => indicator.length === 1), example.id).toBe(true);

      let resolvedSource = example.source.replaceAll(`${rawCommand}\n`, "");
      const clockValues = [
        ["2026", "07", "20", "14", "05", "06"],
        ["2026", "07", "20", "15", "05", "06"],
        ["2026", "07", "20", "16", "05", "06"],
      ];
      for (const [indicatorIndex, indicator] of indicators.entries()) {
        for (const [tokenIndex, token] of ["Y", "m", "d", "H", "M", "S"].entries()) {
          resolvedSource = resolvedSource.replaceAll(
            `${indicator}${token}`,
            clockValues[indicatorIndex]![tokenIndex]!,
          );
        }
      }

      const [clocked, resolved] = await Promise.all([
        renderZpl(example.source),
        renderZpl(resolvedSource),
      ]);
      expect(clocked.labels[0]!.raster.data, example.id).toEqual(
        resolved.labels[0]!.raster.data,
      );
    }
  });

  it("keeps rendered parameter comparisons visible, unclipped, and distinct", async () => {
    for (const guide of zplCommandGuides) {
      for (const signature of guide.signatures) {
        const groups = [
          ...signature.parameters.map(({ key, examples }) => ({ key, examples })),
          ...(signature.parameters.length === 0
            ? [{ key: "basic", examples: signature.examples }]
            : []),
        ];
        for (const { key, examples } of groups) {
          if (!qualityCheckedComparison(guide.canonical, key)) continue;
          const previewExamples = examples.filter(({ preview }) => preview);
          const analyses = [];
          for (const example of previewExamples) {
            const result = await renderZpl(example.source);
            const analysis = previewInkAnalysis(result.labels[0]!.raster);
            analyses.push(analysis);
            expect(analysis.count, example.id).toBeGreaterThanOrEqual(40);
            expect(analysis.minX, example.id).toBeGreaterThan(0);
            expect(analysis.minY, example.id).toBeGreaterThan(0);
            expect(analysis.maxX, example.id).toBeLessThan(result.labels[0]!.width - 1);
            expect(analysis.maxY, example.id).toBeLessThan(analysis.height - 1);
          }
          if (analyses.length > 1) {
            expect(
              new Set(analyses.map(({ hash }) => hash)).size,
              `${guide.canonical} ${key}`,
            ).toBe(analyses.length);
          }
        }
      }
    }
  }, 30_000);

  it("renders every advertised visual example to a label", async () => {
    const previewExamples = allDocumentationExamples().filter(({ preview }) => preview);
    const failures: string[] = [];

    for (const example of previewExamples) {
      const result = await renderZpl(example.source, {
        printDensity: 8,
        strict: false,
        limits: {
          maxDimension: 1_200,
          maxPixels: 720_000,
          maxGraphicBytes: 1_000_000,
          maxSessionBytes: 2_000_000,
          maxTemplateDepth: 6,
          maxExpandedCommands: 5_000,
          maxLabels: 8,
        },
      });
      const label = result.labels[0];
      if (!label || label.width <= 0 || label.height <= 0) {
        failures.push(example.id);
      } else if (
        example.id.endsWith("-recommended") &&
        previewInkAnalysis(label.raster).count < 40
      ) {
        failures.push(example.id);
      }
    }

    expect(previewExamples).toHaveLength(zplDocumentationCoverage.previewExamples);
    expect(failures).toEqual([]);
  }, 30_000);
});
