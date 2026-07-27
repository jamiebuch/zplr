import type { CommandEffect } from "../src/types/ZplDocument";

const iconData = [
  "0000",
  "3FFC",
  "4002",
  "8001",
  "A5A5",
  "A5A5",
  "8001",
  "8181",
  "8241",
  "8421",
  "8811",
  "9009",
  "8001",
  "4002",
  "3FFC",
  "0000",
].join("");
const storedIcon = `~DGR:ICON.GRF,32,2,${iconData}`;

function label(body: string, width = 480, height = 180): string {
  return `^XA\n^PW${width}\n^LL${height}\n${body}\n^XZ`;
}

const overviewPreviewOverrides: Readonly<Record<string, string>> = {
  "^A": label("^FO30,45^AAN,54,40^FDFONT A^FS", 320, 150),
  "^A@": label("^FO30,55^A@N,48,42,R:MISSING.TTF^FDNamed font^FS"),
  "^BY": label("^BY3,2,100\n^FO45,28^BCN,100,Y,N,N^FDABC123^FS", 520, 210),
  "^CF": label("^CF0,54,44\n^FO30,55^FDDefault font^FS"),
  "^CI": label("^CI28\n^FO30,55^A0N,48,40^FDGrüße · ZPLr^FS"),
  "^FB": label("^FO35,25^A0N,34,28^FB360,3,38,L,0^FDField blocks wrap text across clean lines.^FS", 440, 180),
  "^FD": label("^FO35,55^A0N,52,44^FDSample field data^FS", 520, 180),
  "^FE": label([
    "^FO0,220^FN2^FDOrder^FS",
    "^FO0,220^FN3^FD1234^FS",
    "^FO35,55^A0N,48,40^FE#^FD#2#: #3#^FS",
  ].join("\n"), 520, 180),
  "^FH": label("^FO35,55^A0N,50,42^FH_^FDHex_20data_21^FS"),
  "^FN": label("^FO35,55^A0N,48,40^FN1\"Order ID\"^FDVariable #1^FS", 500, 180),
  "^FO": label("^FO45,55^A0N,48,40^FDOrigin (45,55)^FS", 500, 180),
  "^FP": label("^FO35,55^A0N,44,34^FPN,4^FDCharacter spacing^FS", 500, 180),
  "^FR": label([
    "^FO25,35^GB430,110,110,B,0^FS",
    "^FO65,62^A0N,48,40^FR^FDReverse print^FS",
  ].join("\n"), 480, 180),
  "^FS": label([
    "^FO30,55^A0N,44,36^FDFIRST^FS",
    "^FO260,55^A0N,44,36^FDSECOND^FS",
  ].join("\n"), 480, 180),
  "^FT": label("^FT35,115^A0N,48,40^FDBaseline at y=115^FS", 520, 180),
  "^FV": label("^FO35,55^A0N,52,44^FVVariable value^FS", 500, 180),
  "^FW": label("^FWR,0\n^FO55,25^A0,64,52^FDFW^FS", 180, 180),
  "^GB": label("^FO35,30^GB390,120,8,B,20^FS", 460, 180),
  "^GC": label("^FO160,25^GC130,8,B^FS", 460, 180),
  "^GD": label("^FO70,25^GD320,130,8,R,B^FS", 460, 180),
  "^GE": label("^FO70,25^GE320,130,8,B^FS", 460, 180),
  "^GF": label(`^FO110,10^GFA,32,32,2,${iconData}^FS`, 240, 180),
  "^GS": label("^FO160,25^GSN,120,120^FDA^FS", 440, 180),
  "^IL": `${storedIcon}\n${label("^ILR:ICON.GRF", 180, 180)}`,
  "^IM": `${storedIcon}\n${label("^FO70,70^IMR:ICON.GRF^FS", 180, 180)}`,
  "^LR": label([
    "^LRY",
    "^FO25,35^GB430,110,110,B,0^FS",
    "^FO65,62^A0N,48,40^FDLabel reverse^FS",
  ].join("\n"), 480, 180),
  "^PM": label("^PMY\n^FO35,55^A0N,52,44^FDMIRROR >^FS", 500, 180),
  "^PO": label("^POI\n^FO35,55^A0N,52,44^FDROTATED 180^FS", 500, 180),
  "^SN": label("^FO35,55^A0N,52,44^SN0001,1,Y^FS", 380, 180),
  "^TB": label("^FO30,20^A0N,32,26^TBN,360,130^FDText blocks keep copy inside a defined area.^FS", 420, 180),
  "^XG": `${storedIcon}\n${label("^FO45,45^XGR:ICON.GRF,5,5^FS", 180, 180)}`,
};

function isBarcodeVisual(command: string): boolean {
  return command.startsWith("^B") && command !== "^BY";
}

export function zplVisualSampleSupported(
  command: string,
  effect: CommandEffect,
): boolean {
  return effect === "raster" && (
    overviewPreviewOverrides[command] !== undefined ||
    isBarcodeVisual(command) ||
    command === "^FC"
  );
}

function withoutDocumentationCaption(source: string): string {
  return source.replace(/\^FO28,318\^A0N,22,22\^FD[^\r\n]*\^FS\r?\n?/g, "");
}

export function zplOverviewPreviewSource(
  command: string,
  effect: CommandEffect,
  generatedSource: string | undefined,
): string | undefined {
  if (!zplVisualSampleSupported(command, effect)) return undefined;
  const override = overviewPreviewOverrides[command];
  if (override) return override;
  if (isBarcodeVisual(command) || command === "^FC") {
    return generatedSource ? withoutDocumentationCaption(generatedSource) : undefined;
  }
  return undefined;
}
