import type {
  CommandCapabilityStatus,
  CommandCategory,
  CommandEffect,
  CommandPersistenceScope,
} from "../src/types/ZplDocument";
import {
  zplCommandDocumentationSeeds,
  type ZplCommandDocumentationSeed,
  type ZplDocumentationParameterSeed,
  type ZplDocumentationSignatureSeed,
} from "./zplDocumentationData.generated";
import {
  commandSlug,
  commandSummary,
} from "./zplDocumentationSummary";

export interface ZplParameterGuide {
  id: string;
  key: string;
  name: string;
  description: string;
  defaultValue?: string;
  choices: readonly string[];
  enumValues?: readonly string[];
  range?: { readonly min: number; readonly max: number };
  required: boolean;
  repeatable: boolean;
  examples: readonly ZplDocumentationExample[];
}

export interface ZplSignatureGuide {
  id: string;
  syntax: string;
  label?: string;
  parameters: readonly ZplParameterGuide[];
  examples: readonly ZplDocumentationExample[];
}

export interface ZplDocumentationExample {
  id: string;
  command: string;
  signatureIndex: number;
  parameterId?: string;
  title: string;
  description: string;
  value?: string;
  source: string;
  filename: string;
  cursorOffset: number;
  preview: boolean;
}

export interface ZplCommandGuide {
  canonical: string;
  slug: string;
  title: string;
  summary: string;
  reference: string;
  category: CommandCategory;
  effect: CommandEffect;
  scope: CommandPersistenceScope;
  status: Exclude<CommandCapabilityStatus, "unknown">;
  limitations: readonly string[];
  signatures: readonly ZplSignatureGuide[];
  searchText: string;
}

const barcodePayloads: Readonly<Record<string, string>> = {
  "^B0": "HELLO AZTEC",
  "^B1": "1234567890",
  "^B2": "123456",
  "^B3": "ZPLR-39",
  "^B4": "CODE49",
  "^B5": "01234567890",
  "^B7": "PDF417 FROM ZPLR",
  "^B8": "1234567",
  "^B9": "0421000",
  "^BA": "ZPLR-93",
  "^BB": "CODABLOCK",
  "^BC": "ABC123456",
  "^BD": "MAXICODE",
  "^BE": "590123412345",
  "^BF": "MICRO PDF",
  "^BI": "123456",
  "^BJ": "123456",
  "^BK": "123456",
  "^BL": "LOGMARS",
  "^BM": "123456",
  "^BO": "HELLO AZTEC",
  "^BP": "1234ABCD",
  "^BQ": "QA,ZPLR QR EXAMPLE",
  "^BR": "0952123454321",
  "^BS": "05",
  "^BT": "123456,SERIAL123",
  "^BU": "03600029145",
  "^BX": "ZPLR-DATAMATRIX",
  "^BZ": "01234",
};

const directTextCommands = new Set([
  "^A",
  "^A@",
  "^FB",
  "^FC",
  "^FH",
  "^FP",
  "^PA",
  "^SF",
  "^SN",
  "^TB",
]);

const fieldOriginCommands = new Set(["^FM", "^FO", "^FT"]);
const directGraphicCommands = new Set(["^GB", "^GC", "^GD", "^GE", "^GS"]);

const commandDefaultOverrides: Readonly<Record<string, Readonly<Record<string, string>>>> = {
  "^A@": { d: "R", f: "MISSING", x: "TTF" },
  "^B0": { a: "N", b: "3", c: "N", d: "0", e: "N", f: "1", g: "0" },
  "^B4": { o: "N", h: "8", f: "N", m: "A" },
  "^B7": { o: "N", h: "3", s: "0", c: "6", r: "8", t: "N" },
  "^BB": { o: "N", h: "8", s: "Y", c: "8", r: "0", m: "F" },
  "^BD": { m: "4", o: "1", h: "1" },
  "^BF": { o: "N", h: "3", m: "0" },
  "^BK": { o: "N", e: "N", h: "60", f: "N", g: "N", k: "A", l: "B" },
  "^BQ": { a: "N", b: "2", c: "4", d: "Q", e: "7" },
  "^BR": { o: "N", a: "1", b: "2", c: "1", d: "50", e: "22" },
  "^BT": { o: "N", w: "2", r: "2", h: "50", s: "2", c: "4" },
  "^BX": { o: "N", h: "5", s: "200", c: "0", r: "0", f: "6", g: "_", a: "1" },
  "^FC": { a: "%", b: "{", c: "#" },
  "^GF": { a: "A", b: "8", c: "8", d: "1", data: "FF818181818181FF" },
  "^IL": { d: "R", o: "ICON", x: "GRF" },
  "^IM": { d: "R", o: "ICON", x: "GRF" },
  "^XG": { d: "R", o: "ICON", x: "GRF", mx: "4", my: "4" },
  "~DG": { d: "R", o: "ICON", x: "GRF", t: "8", w: "1", data: "FF818181818181FF" },
};

const commandExampleValueOverrides: Readonly<Record<string, Readonly<Record<string, readonly string[]>>>> = {
  "^FC": {
    a: ["%", "@"],
    b: ["{", "&"],
    c: ["#", "!"],
  },
};

function sentenceName(value: string): string {
  const normalized = value
    .replace(/\s+/g, " ")
    .replace(/\s*[-–—:,.]+\s*$/g, "")
    .trim();
  if (!normalized) return "this parameter";
  return normalized[0]!.toLowerCase() + normalized.slice(1);
}

function parameterDescription(parameter: ZplDocumentationParameterSeed): string {
  const requirement = parameter.required ? " It must be supplied." : " It can be omitted when the documented default is suitable.";
  const repetition = parameter.repeatable ? " The value can repeat to continue the documented pattern." : "";
  return `Sets ${sentenceName(parameter.name)}.${requirement}${repetition}`;
}

function unique(values: readonly string[]): string[] {
  return [...new Set(values.map((value) => value.trim()).filter(Boolean))];
}

function alternateValue(parameter: ZplDocumentationParameterSeed, initial: string): string {
  if (/^[+-]?\d+(?:\.\d+)?$/.test(initial)) {
    const number = Number(initial);
    if (Number.isFinite(number)) return String(number === 0 ? 1 : number + 1);
  }
  if (/character|indicator/i.test(parameter.name) || initial.length === 1) {
    return initial === "_" ? "#" : initial === "X" ? "Z" : "X";
  }
  if (/drive|device|location/i.test(parameter.name)) return initial.startsWith("R") ? "E:" : "R:";
  if (/name|object|file|font/i.test(parameter.name)) return "EXAMPLE";
  if (/data|text|comment|prompt|key|value/i.test(parameter.name)) return "SAMPLE";
  return `${initial || "VALUE"}2`;
}

function curatedValues(
  parameter: ZplDocumentationParameterSeed,
  preview: boolean,
): string[] {
  const documented = unique([
    ...(parameter.enumValues ?? []),
    ...parameter.choices,
  ]);
  const safeDocumented = preview
    ? documented.filter((value) => !/^[+-]?\d+(?:\.\d+)?$/.test(value) || Math.abs(Number(value)) <= 1_000)
    : documented;
  const candidates = safeDocumented.length ? safeDocumented : documented;
  const values = unique(candidates);
  if (values.length === 0) values.push("");
  if (values.length === 1) values.push(alternateValue(parameter, values[0]!));

  const includeThird =
    (parameter.enumValues?.length ?? 0) >= 3 ||
    (parameter.range !== undefined && values.length >= 3) ||
    values.length >= 4;
  const result = [values[0]!, values[1]!];
  if (includeThird) {
    const third = values.at(-1);
    if (third && !result.includes(third)) result.push(third);
  }
  return result.slice(0, 3);
}

function exampleValues(
  command: string,
  parameter: ZplDocumentationParameterSeed,
  preview: boolean,
): string[] {
  const override = commandExampleValueOverrides[command]?.[parameter.key];
  return override ? [...override] : curatedValues(parameter, preview);
}

function defaultValue(
  command: string,
  parameter: ZplDocumentationParameterSeed,
): string {
  return commandDefaultOverrides[command]?.[parameter.key]
    ?? parameter.choices[0]
    ?? parameter.enumValues?.[0]
    ?? "";
}

function commandFromSignature(
  command: string,
  signature: ZplDocumentationSignatureSeed,
  target: ZplDocumentationParameterSeed | undefined,
  requestedValue: string | undefined,
): string {
  if (command === "^WE") {
    const values = ["OFF", "1", "O", "H", "KEY1", "KEY2", "KEY3", "KEY4"];
    if (target) {
      if (target.key === "e,_f,_g,_h") values.splice(4, 4, requestedValue ?? "KEY");
      else values[target.slot] = requestedValue ?? "";
    }
    return `^WE${values.join(",")}`;
  }
  if (command === "^RB") {
    const values = ["96", ...Array.from({ length: 16 }, () => "6")];
    if (target) {
      const partition = /^p(\d+)$/.exec(target.key)?.[1];
      const slot = partition === undefined ? target.slot : Number(partition) + 1;
      values[slot] = requestedValue ?? "";
    }
    return `^RB${values.join(",")}`;
  }

  const parameters = [...signature.parameters]
    .filter((parameter) => parameter.syntaxStart !== undefined && parameter.syntaxEnd !== undefined)
    .sort((left, right) => left.syntaxStart! - right.syntaxStart!);
  let cursor = 0;
  let result = "";
  for (const parameter of parameters) {
    result += signature.syntax.slice(cursor, parameter.syntaxStart);
    result += parameter === target
      ? requestedValue ?? ""
      : defaultValue(command, parameter);
    cursor = parameter.syntaxEnd!;
  }
  result += signature.syntax.slice(cursor);
  return result.replace(/\s*\.\.\.\s*/g, "");
}

function previewEnabled(capability: ZplCommandDocumentationSeed): boolean {
  return capability.effect !== "device" && capability.status !== "unsupported";
}

function zplCaption(command: string): string {
  return `${command.replace("^", "Caret ").replace("~", "Tilde ")} parameter example`;
}

function fieldClockBody(rawCommand: string, footer: string): string {
  const [primary = "%", secondary = "{", tertiary = "#"] = rawCommand.slice(3).split(",");
  const clockLine = (label: string, indicator: string): string =>
    `${label} [${indicator}]  ${indicator}Y-${indicator}m-${indicator}d ${indicator}H:${indicator}M:${indicator}S`;

  return [
    "^ST07,20,2026,02,05,06,P",
    "^SO2,0,0,0,1,0,0",
    "^SO3,0,0,0,2,0,0",
    "^FO36,55^A0N,30,26^FB568,3,28,L,0",
    rawCommand,
    `^FD${[
      clockLine("Primary", primary || "%"),
      clockLine("Secondary", secondary || "{"),
      clockLine("Third", tertiary || "#"),
    ].join("\\&")}^FS`,
    footer,
  ].join("\n");
}

function previewSource(
  command: string,
  category: CommandCategory,
  rawCommand: string,
): { source: string; cursorOffset: number } {
  const labelStart = "^XA\n^PW640\n^LL360\n";
  const footer = `^FO28,318^A0N,22,22^FD${zplCaption(command)}^FS\n^XZ`;
  const graphicResource = "~DGR:ICON.GRF,8,1,FF818181818181FF\n";
  let body: string;

  if (command === "^XA") {
    body = `^PW640\n^LL360\n^FO28,24^GB584,270,3,B,16^FS\n^FO48,125^A0N,42,38^FDStart format^FS\n${footer.replace(/^\^FO/, "^FO")}`;
    return { source: `${rawCommand}\n${body}`, cursorOffset: 0 };
  }
  if (command === "^XZ") {
    const source = `${labelStart}^FO28,24^GB584,270,3,B,16^FS\n^FO48,125^A0N,42,38^FDEnd format^FS\n${rawCommand}`;
    return { source, cursorOffset: source.lastIndexOf(rawCommand) };
  }

  if (category === "barcode" && command.startsWith("^B") && command !== "^BY") {
    const payload = barcodePayloads[command] ?? "1234567890";
    body = `^FO36,32\n^BY2,2,90\n${rawCommand}\n^FD${payload}^FS\n${footer}`;
  } else if (command === "^GF") {
    body = `^FO70,50\n${rawCommand}^FS\n${footer}`;
  } else if (directGraphicCommands.has(command)) {
    body = `^FO90,55\n${rawCommand}^FS\n${footer}`;
  } else if (command === "^IL" || command === "^IM" || command === "^XG") {
    body = `^FO90,55\n${rawCommand}^FS\n${footer}`;
  } else if (fieldOriginCommands.has(command)) {
    body = `${rawCommand}\n^A0N,42,38^FDPositioned field^FS\n${footer}`;
  } else if (command === "^FD" || command === "^FV") {
    body = `^FO48,90^A0N,42,38\n${rawCommand}^FS\n${footer}`;
  } else if (command === "^FR") {
    body = `^FO28,24^GB584,270,584,B,0^FS\n^FO80,125^A0N,42,38\n${rawCommand}\n^FDReverse field^FS\n${footer}`;
  } else if (command === "^FC") {
    body = fieldClockBody(rawCommand, footer);
  } else if (directTextCommands.has(command)) {
    body = `^FO48,75\n${rawCommand}\n^FDThe quick brown fox wraps across the label.^FS\n${footer}`;
  } else if (category === "graphic") {
    body = `^FO90,55\n${rawCommand}^FS\n${footer}`;
  } else if (category === "text") {
    body = `${rawCommand}\n^FO48,95^A0N,42,38^FDText command example^FS\n${footer}`;
  } else if (category === "format") {
    body = `${rawCommand}\n^FO28,24^GB584,270,3,B,16^FS\n^FO58,125^A0N,42,38^FDFormat command^FS\n${footer}`;
  } else {
    body = `^FO28,24^GB584,270,3,B,16^FS\n^FO58,125^A0N,42,38^FDJob command^FS\n${footer}\n${rawCommand}`;
  }

  const prefix = command === "^IL" || command === "^IM" || command === "^XG"
    ? graphicResource
    : "";
  const source = `${prefix}${labelStart}${body}`;
  return { source, cursorOffset: source.indexOf(rawCommand) };
}

function codeOnlySource(rawCommand: string): { source: string; cursorOffset: number } {
  return { source: `${rawCommand}\n`, cursorOffset: 0 };
}

function exampleIdPart(value: string): string {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 42) || "value";
}

function exampleFor(
  capability: ZplCommandDocumentationSeed,
  signature: ZplDocumentationSignatureSeed,
  signatureIndex: number,
  parameter: ZplDocumentationParameterSeed | undefined,
  parameterId: string | undefined,
  value: string | undefined,
  variantIndex: number,
): ZplDocumentationExample {
  const preview = previewEnabled(capability);
  const rawCommand = commandFromSignature(capability.canonical, signature, parameter, value);
  const prepared = preview
    ? previewSource(capability.canonical, capability.category, rawCommand)
    : codeOnlySource(rawCommand);
  const valueLabel = value === "" ? "Omitted value" : value ?? "Basic form";
  const exampleKey = parameter
    ? `${parameterId}-${variantIndex + 1}-${exampleIdPart(valueLabel)}`
    : `basic-${signatureIndex + 1}`;
  const slug = commandSlug(capability.canonical);
  return {
    id: `${slug}-${exampleKey}`,
    command: capability.canonical,
    signatureIndex,
    parameterId,
    title: parameter ? `${parameter.key} = ${valueLabel}` : `${capability.canonical} basic example`,
    description: parameter
      ? `Uses ${valueLabel} for ${sentenceName(parameter.name)} while the other command values stay at representative defaults.`
      : `Shows the ${signature.label?.toLowerCase() ?? "parameterless command form"} in a complete ZPL sample.`,
    value,
    source: prepared.source,
    filename: `${slug}-${parameter ? exampleIdPart(parameter.key) : "basic"}-${variantIndex + 1}.zpl`,
    cursorOffset: prepared.cursorOffset,
    preview,
  };
}

function signatureGuides(
  capability: ZplCommandDocumentationSeed,
): ZplSignatureGuide[] {
  return capability.signatures.map((signature, signatureIndex) => {
    const parameters = signature.parameters.map((parameter) => {
      const parameterId = `${signatureIndex}-${parameter.slot}-${parameter.component}-${exampleIdPart(parameter.key)}`;
      const values = exampleValues(capability.canonical, parameter, previewEnabled(capability));
      return {
        id: parameterId,
        key: parameter.key,
        name: parameter.name,
        description: parameterDescription(parameter),
        defaultValue: parameter.defaultValue,
        choices: parameter.choices,
        enumValues: parameter.enumValues,
        range: parameter.range,
        required: Boolean(parameter.required),
        repeatable: Boolean(parameter.repeatable),
        examples: values.map((value, variantIndex) => exampleFor(
          capability,
          signature,
          signatureIndex,
          parameter,
          parameterId,
          value,
          variantIndex,
        )),
      } satisfies ZplParameterGuide;
    });
    const examples = parameters.length === 0
      ? [exampleFor(capability, signature, signatureIndex, undefined, undefined, undefined, 0)]
      : [];
    return {
      id: `${commandSlug(capability.canonical)}-signature-${signatureIndex + 1}`,
      syntax: signature.syntax,
      label: signature.label,
      parameters,
      examples,
    };
  });
}

export const zplCommandGuides: readonly ZplCommandGuide[] = zplCommandDocumentationSeeds.map((capability) => {
  const signatures = signatureGuides(capability);
  const summary = commandSummary(capability);
  return {
    canonical: capability.canonical,
    slug: commandSlug(capability.canonical),
    title: capability.title,
    summary,
    reference: capability.reference,
    category: capability.category,
    effect: capability.effect,
    scope: capability.scope,
    status: capability.status,
    limitations: capability.limitations ?? [],
    signatures,
    searchText: [
      capability.canonical,
      capability.title,
      summary,
      capability.category,
      capability.effect,
      capability.scope,
      capability.status,
      ...signatures.flatMap((signature) =>
        signature.parameters.flatMap((parameter) => [
          parameter.key,
          parameter.name,
          parameter.description,
        ])),
    ].join(" ").toLowerCase(),
  };
});

const guidesBySlug = new Map(zplCommandGuides.map((guide) => [guide.slug, guide]));
const examplesById = new Map(
  zplCommandGuides.flatMap((guide) =>
    guide.signatures.flatMap((signature) => [
      ...signature.examples,
      ...signature.parameters.flatMap((parameter) => parameter.examples),
    ]).map((example) => [example.id, example] as const)),
);

export function getZplCommandGuide(slug: string): ZplCommandGuide | undefined {
  return guidesBySlug.get(slug.trim().toLowerCase());
}

export function getZplDocumentationExample(id: string): ZplDocumentationExample | undefined {
  return examplesById.get(id.trim().toLowerCase());
}

export function zplCommandRoute(guide: Pick<ZplCommandGuide, "slug">): string {
  return `/zpl-commands/${guide.slug}`;
}

export const zplDocumentationCoverage = Object.freeze({
  commands: zplCommandGuides.length,
  signatures: zplCommandGuides.reduce((total, guide) => total + guide.signatures.length, 0),
  parameters: zplCommandGuides.reduce(
    (total, guide) => total + guide.signatures.reduce((sum, signature) => sum + signature.parameters.length, 0),
    0,
  ),
  examples: examplesById.size,
  previewExamples: [...examplesById.values()].filter((example) => example.preview).length,
});
