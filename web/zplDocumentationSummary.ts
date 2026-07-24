import type {
  CommandEffect,
  CommandPersistenceScope,
} from "../src/types/ZplDocument";

interface CommandSummaryInput {
  canonical: string;
  title: string;
  effect: CommandEffect;
  scope: CommandPersistenceScope;
}

const prefixNames = {
  "^": "caret",
  "~": "tilde",
} as const;

const effectDescriptions: Readonly<Record<CommandEffect, string>> = {
  raster: "can change pixels in the rendered label",
  job: "changes job or printer-session state used while processing labels",
  device: "controls printer hardware or connectivity rather than label pixels",
};

const scopeDescriptions: Readonly<Record<CommandPersistenceScope, string>> = {
  field: "the current field",
  format: "the current label format",
  job: "the current print job",
  session: "the printer session and subsequent formats",
};

export function commandSlug(canonical: string): string {
  const prefix = prefixNames[canonical[0] as keyof typeof prefixNames];
  const code = canonical
    .slice(1)
    .toLowerCase()
    .replaceAll("@", "-at")
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "");
  return `${prefix}-${code}`;
}

export function commandSummary(command: CommandSummaryInput): string {
  const title = command.title.replace(/\s+/g, " ").trim();
  return `${command.canonical} configures ${title[0]?.toLowerCase() ?? ""}${title.slice(1)}. It applies to ${scopeDescriptions[command.scope]} and ${effectDescriptions[command.effect]}.`;
}
