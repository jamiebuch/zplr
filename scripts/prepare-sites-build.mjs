import assert from "node:assert/strict";
import { cp, mkdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import path from "node:path";

const repositoryRoot = path.resolve(import.meta.dirname, "..");
const staticOutput = path.join(repositoryRoot, ".output", "public");
const clientOutput = path.join(repositoryRoot, "dist", "client");
const serverOutput = path.join(repositoryRoot, "dist", "server");

await stat(path.join(staticOutput, "index.html"));
await stat(path.join(staticOutput, "zpl-commands.html"));
await stat(path.join(staticOutput, "zpl-commands", "caret-fo.html"));

await rm(clientOutput, { recursive: true, force: true });
await rm(serverOutput, { recursive: true, force: true });
await mkdir(serverOutput, { recursive: true });
await cp(staticOutput, clientOutput, { recursive: true });

const workerSource = `const htmlRequest = /\\/$|\\/[^/.]+$/;

async function assetResponse(request, env) {
  const response = await env.ASSETS.fetch(request);
  if (response.status !== 404 || !htmlRequest.test(new URL(request.url).pathname)) {
    return response;
  }

  const url = new URL(request.url);
  url.pathname = url.pathname.endsWith("/")
    ? \`\${url.pathname}index.html\`
    : \`\${url.pathname}.html\`;
  const htmlResponse = await env.ASSETS.fetch(new Request(url, request));
  if (htmlResponse.status !== 404) return htmlResponse;

  const notFoundUrl = new URL("/404.html", request.url);
  const notFound = await env.ASSETS.fetch(new Request(notFoundUrl, request));
  return new Response(notFound.body, {
    status: 404,
    headers: notFound.headers,
  });
}

export default {
  async fetch(request, env) {
    if (!env.ASSETS?.fetch) {
      return new Response("Static asset binding is unavailable.", { status: 503 });
    }
    return assetResponse(request, env);
  },
};
`;
await writeFile(path.join(serverOutput, "index.js"), workerSource);

const clientCommandIndex = JSON.parse(
  await readFile(path.join(clientOutput, "zpl-command-index.json"), "utf8"),
);
assert.equal(clientCommandIndex.length, 223);

console.log("Prepared Sites output in dist/ from the finalized Nuxt static build.");
