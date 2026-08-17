import {
  zplCommandIndexEntry,
  zplCommandGuides,
  zplDocumentationCoverage,
} from "../../../web/zplDocumentation";

const initialCommandLimit = 60;

export default defineEventHandler(() => ({
  coverage: zplDocumentationCoverage,
  initialCommandLimit,
  categories: [...new Set(zplCommandGuides.map(({ category }) => category))].sort(),
  guides: zplCommandGuides.slice(0, initialCommandLimit).map(zplCommandIndexEntry),
  directory: zplCommandGuides.map(({ canonical, slug, title, category }) => ({
    canonical,
    slug,
    title,
    category,
  })),
}));
