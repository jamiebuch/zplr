import { mkdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { Canvas, loadImage } from "skia-canvas";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const publicDirectory = path.join(repositoryRoot, "public");
const pwaIconDirectory = path.join(publicDirectory, "pwa");
const sourcePath = path.join(publicDirectory, "favicon.svg");
const source = await readFile(sourcePath);
const image = await loadImage(source);
const appBackground = "#18181b";

async function renderPng(size) {
  const canvas = new Canvas(size, size);
  const context = canvas.getContext("2d");
  context.clearRect(0, 0, size, size);
  context.imageSmoothingEnabled = true;
  context.imageSmoothingQuality = "high";
  context.drawImage(image, 0, 0, size, size);
  return canvas.toBuffer("png");
}

async function renderPwaIcon(size, { maskable = false } = {}) {
  const canvas = new Canvas(size, size);
  const context = canvas.getContext("2d");
  context.fillStyle = appBackground;
  context.fillRect(0, 0, size, size);
  const logo = new Canvas(size, size);
  const logoContext = logo.getContext("2d");
  logoContext.drawImage(image, 0, 0, size, size);
  logoContext.globalCompositeOperation = "source-in";
  logoContext.fillStyle = "#ffffff";
  logoContext.fillRect(0, 0, size, size);
  // Maskable icons must keep all content inside the central 80% safe zone.
  // The logo reaches 1.15x the safe radius at full size, so maskable variants
  // are scaled to 0.65 to leave margin around the rounded bar corners.
  const scale = maskable ? 0.65 : 1;
  const drawSize = size * scale;
  const offset = (size - drawSize) / 2;
  context.imageSmoothingEnabled = true;
  context.imageSmoothingQuality = "high";
  context.drawImage(logo, offset, offset, drawSize, drawSize);
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
const pwaIcon192 = await renderPwaIcon(192);
const pwaIcon512 = await renderPwaIcon(512);
const pwaIcon192Maskable = await renderPwaIcon(192, { maskable: true });
const pwaIcon512Maskable = await renderPwaIcon(512, { maskable: true });

await mkdir(pwaIconDirectory, { recursive: true });
await Promise.all([
  writeFile(path.join(publicDirectory, "favicon-96x96.png"), favicon96),
  writeFile(path.join(publicDirectory, "apple-touch-icon.png"), appleTouchIcon),
  writeFile(path.join(publicDirectory, "favicon.ico"), wrapPngAsIco(favicon48, 48)),
  writeFile(path.join(pwaIconDirectory, "icon-192.png"), pwaIcon192),
  writeFile(path.join(pwaIconDirectory, "icon-512.png"), pwaIcon512),
  writeFile(path.join(pwaIconDirectory, "icon-192-maskable.png"), pwaIcon192Maskable),
  writeFile(path.join(pwaIconDirectory, "icon-512-maskable.png"), pwaIcon512Maskable),
]);

console.log("Generated stable 96px, 180px, ICO, and PWA icon assets.");
