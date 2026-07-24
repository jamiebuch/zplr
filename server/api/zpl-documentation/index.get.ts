import {
  zplCommandGuides,
  zplDocumentationCoverage,
} from "../../../web/zplDocumentation";

const initialCommandLimit = 60;

export default defineEventHandler(() => ({
  coverage: zplDocumentationCoverage,
  initialCommandLimit,
  categories: [...new Set(zplCommandGuides.map(({ category }) => category))].sort(),
  guides: zplCommandGuides.slice(0, initialCommandLimit).map((guide) => ({
    canonical: guide.canonical,
    slug: guide.slug,
    title: guide.title,
    summary: guide.summary,
    category: guide.category,
    effect: guide.effect,
    scope: guide.scope,
    status: guide.status,
    parameterTerms: guide.signatures.flatMap((signature) =>
      signature.parameters.flatMap((parameter) => [
        parameter.key,
        parameter.name,
      ]),
    ).join(" ").toLowerCase(),
    parameterCount: guide.signatures.reduce(
      (total, signature) => total + signature.parameters.length,
      0,
    ),
  })),
}));
