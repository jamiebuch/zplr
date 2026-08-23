import { strFromU8, strToU8, unzlibSync, zlibSync } from "fflate";

export interface SharedLabel {
  name: string;
  source: string;
  data?: unknown;
}

interface SharedLabelPayload {
  v: 1;
  name: string;
  source: string;
  data?: unknown;
}

/** URL hash prefix for shared labels, e.g. `#s=<token>`. */
export const sharedLabelHashPrefix = "#s=";

/** Keep share links comfortably under common browser URL limits. */
export const maxSharedLabelTokenLength = 65_536;

/** Matches the 8 MB per-ZPL-source workspace import limit. */
const maxSharedLabelSourceLength = 8_000_000;

const base64UrlAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

function encodeBase64Url(bytes: Uint8Array): string {
  let output = "";
  for (let index = 0; index < bytes.length; index += 3) {
    const first = bytes[index]!;
    const second = bytes[index + 1];
    const third = bytes[index + 2];
    output += base64UrlAlphabet[first >> 2];
    output += base64UrlAlphabet[((first & 3) << 4) | ((second ?? 0) >> 4)];
    if (second !== undefined) output += base64UrlAlphabet[((second & 15) << 2) | ((third ?? 0) >> 6)];
    if (third !== undefined) output += base64UrlAlphabet[third & 63];
  }
  return output;
}

function decodeBase64Url(value: string): Uint8Array | undefined {
  if (!value || !/^[A-Za-z0-9_-]+$/.test(value) || value.length % 4 === 1) return undefined;
  const output: number[] = [];
  let buffer = 0;
  let bits = 0;
  for (const character of value) {
    const digit = base64UrlAlphabet.indexOf(character);
    if (digit < 0) return undefined;
    buffer = (buffer << 6) | digit;
    bits += 6;
    if (bits >= 8) {
      bits -= 8;
      output.push((buffer >> bits) & 0xff);
      buffer &= bits === 0 ? 0 : (1 << bits) - 1;
    }
  }
  if (buffer !== 0) return undefined;
  return Uint8Array.from(output);
}

/** Compress a label into a URL-safe share token. Returns undefined when it would be too large. */
export function encodeSharedLabel(label: SharedLabel): string | undefined {
  const source = label.source;
  if (typeof source !== "string" || !source.trim() || source.length > maxSharedLabelSourceLength) return undefined;
  const name = typeof label.name === "string" && label.name.trim() ? label.name.trim() : "shared-label.zpl";
  const payload: SharedLabelPayload = { v: 1, name, source };
  if (label.data !== undefined) payload.data = label.data;
  const token = encodeBase64Url(zlibSync(strToU8(JSON.stringify(payload)), { level: 9 }));
  return token.length <= maxSharedLabelTokenLength ? token : undefined;
}

/** Restore a label from a share token. Returns undefined for malformed or oversized tokens. */
export function decodeSharedLabel(token: string): SharedLabel | undefined {
  if (typeof token !== "string" || !token || token.length > maxSharedLabelTokenLength) return undefined;
  const bytes = decodeBase64Url(token);
  if (!bytes) return undefined;
  try {
    const payload = JSON.parse(strFromU8(unzlibSync(bytes))) as Partial<SharedLabelPayload>;
    if (payload.v !== 1 || typeof payload.source !== "string" || !payload.source) return undefined;
    const name = typeof payload.name === "string" && payload.name.trim() ? payload.name.trim() : "shared-label.zpl";
    return payload.data === undefined ? { name, source: payload.source } : { name, source: payload.source, data: payload.data };
  } catch {
    return undefined;
  }
}

/** Extract a shared-label token from a URL hash such as `#s=<token>`. */
export function sharedLabelTokenFromHash(hash: string): string | undefined {
  if (!hash.startsWith(sharedLabelHashPrefix)) return undefined;
  try {
    return decodeURIComponent(hash.slice(sharedLabelHashPrefix.length)).trim() || undefined;
  } catch {
    return undefined;
  }
}
