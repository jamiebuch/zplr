import { fileURLToPath } from "node:url";
import packageJson from "./package.json";
import {
  zplCommandGuides,
  zplCommandRoute,
} from "./web/zplDocumentation";

export default defineNuxtConfig({
  compatibilityDate: "2026-07-21",
  ssr: true,
  devtools: { enabled: false },
  css: ["~/assets/css/main.css"],
  postcss: {
    plugins: {
      "@tailwindcss/postcss": {},
    },
  },
  alias: {
    "@": fileURLToPath(new URL("./src", import.meta.url)),
  },
  app: {
    head: {
      htmlAttrs: { lang: "en" },
      meta: [
        { charset: "utf-8" },
        { name: "viewport", content: "width=device-width, initial-scale=1" },
        { name: "color-scheme", content: "light dark" },
        { name: "theme-color", content: "#18181b" },
      ],
      link: [
        { rel: "icon", type: "image/png", sizes: "96x96", href: "/favicon-96x96.png" },
        { rel: "icon", type: "image/svg+xml", sizes: "any", href: "/favicon.svg" },
        { rel: "shortcut icon", href: "/favicon.ico" },
        { rel: "apple-touch-icon", sizes: "180x180", href: "/apple-touch-icon.png" },
      ],
    },
  },
  runtimeConfig: {
    public: {
      siteUrl: "https://zplr.de",
      packageVersion: packageJson.version,
    },
  },
  nitro: {
    preset: "static",
    prerender: {
      routes: [
        "/",
        "/editor",
        "/zpl-commands",
        ...zplCommandGuides.map(zplCommandRoute),
      ],
      autoSubfolderIndex: false,
      crawlLinks: false,
    },
  },
  typescript: {
    strict: true,
    typeCheck: false,
    includeWorkspace: false,
    tsConfig: {
      compilerOptions: {
        noUncheckedIndexedAccess: false,
        verbatimModuleSyntax: false,
      },
      include: [
        "../env.d.ts",
        "../src/types/**/*.d.ts",
      ],
      exclude: [
        "../src/**/*.test.ts",
        "../web/**/*.test.ts",
        "../tests/**/*",
        "../dist",
        "../.output",
      ],
    },
  },
  vite: {
    build: {
      chunkSizeWarningLimit: 2_000,
    },
  },
});
