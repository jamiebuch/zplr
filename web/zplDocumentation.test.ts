import { describe, expect, it } from "vitest";
import { parseDocument } from "../src/core/documentParser";
import { renderZpl } from "../src/index.node";
import {
  getZplCommandGuide,
  getZplDocumentationExample,
  zplCommandGuides,
  zplDocumentationCoverage,
} from "./zplDocumentation";

function flattenedCommands(source: string): string[] {
  return parseDocument(source).items.flatMap((item) =>
    item.kind === "label"
      ? item.commands.map(({ canonical }) => canonical)
      : [item.canonical]);
}

describe("interactive ZPL documentation", () => {
  it("covers the complete pinned command catalog", () => {
    expect(zplDocumentationCoverage).toMatchObject({
      commands: 223,
      signatures: 225,
      parameters: 630,
    });
    expect(zplDocumentationCoverage.examples).toBeGreaterThan(1_200);
    expect(zplDocumentationCoverage.previewExamples).toBeGreaterThan(700);

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

  it("creates unique, resolvable, parseable examples with safe preview rules", () => {
    const examples = zplCommandGuides.flatMap((guide) =>
      guide.signatures.flatMap((signature) => [
        ...signature.examples,
        ...signature.parameters.flatMap((parameter) => parameter.examples),
      ]));
    expect(new Set(examples.map(({ id }) => id)).size).toBe(examples.length);

    for (const example of examples) {
      expect(getZplDocumentationExample(example.id)).toBe(example);
      expect(example.id).toMatch(/^[a-z0-9-]+$/);
      expect(example.filename).toMatch(/^[a-z0-9-]+\.zpl$/);
      expect(example.cursorOffset).toBeGreaterThanOrEqual(0);
      expect(example.cursorOffset).toBeLessThan(example.source.length);
      expect(flattenedCommands(example.source), example.id).toContain(example.command);

      const guide = zplCommandGuides.find(({ canonical }) => canonical === example.command)!;
      expect(example.preview).toBe(guide.effect !== "device" && guide.status !== "unsupported");
    }
  });

  it("renders every advertised visual example to a label", async () => {
    const previewExamples = zplCommandGuides.flatMap((guide) =>
      guide.signatures.flatMap((signature) => [
        ...signature.examples,
        ...signature.parameters.flatMap((parameter) => parameter.examples),
      ]),
    ).filter(({ preview }) => preview);
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
      }
    }

    expect(previewExamples).toHaveLength(zplDocumentationCoverage.previewExamples);
    expect(failures).toEqual([]);
  }, 30_000);
});
