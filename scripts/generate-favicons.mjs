import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { Canvas, loadImage } from "skia-canvas";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const publicDirectory = path.join(repositoryRoot, "public");
const sourcePath = path.join(publicDirectory, "favicon.svg");
const source = await readFile(sourcePath);
const image = await loadImage(source);

async function renderPng(size) {
  const canvas = new Canvas(size, size);
  const context = canvas.getContext("2d");
  context.clearRect(0, 0, size, size);
  context.imageSmoothingEnabled = true;
  context.imageSmoothingQuality = "high";
  context.drawImage(image, 0, 0, size, size);
  return canvas.toBuffer("png");
}

function wrapPngAsIco(png, size) {
  const header = Buffer.alloc(22);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(1, 4);
  header.writeUInt8(size === 256 ? 0 : size, 6);
  header.writeUInt8(size === 256 ? 0 : size, 7);
  header.writeUInt8(0, 8);
  header.writeUInt8(0, 9);
  header.writeUInt16LE(1, 10);
  header.writeUInt16LE(32, 12);
  header.writeUInt32LE(png.length, 14);
  header.writeUInt32LE(header.length, 18);
  return Buffer.concat([header, png]);
}

const favicon96 = await renderPng(96);
const appleTouchIcon = await renderPng(180);
const favicon48 = await renderPng(48);

await Promise.all([
  writeFile(path.join(publicDirectory, "favicon-96x96.png"), favicon96),
  writeFile(path.join(publicDirectory, "apple-touch-icon.png"), appleTouchIcon),
  writeFile(path.join(publicDirectory, "favicon.ico"), wrapPngAsIco(favicon48, 48)),
]);

console.log("Generated stable 96px, 180px, and ICO favicon assets.");
