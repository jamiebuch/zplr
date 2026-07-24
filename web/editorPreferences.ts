export type VisualEditorSnapSize = 1 | 5 | 10 | 20;

export interface VisualEditorPreferences {
  snapSize: VisualEditorSnapSize;
  gridVisible: boolean;
  objectSnapEnabled: boolean;
  rulersVisible: boolean;
}

export const defaultVisualEditorPreferences: Readonly<VisualEditorPreferences> = {
  snapSize: 10,
  gridVisible: false,
  objectSnapEnabled: true,
  rulersVisible: true,
};

export function normalizeVisualEditorPreferences(value: unknown): VisualEditorPreferences {
  if (!value || typeof value !== "object") return { ...defaultVisualEditorPreferences };
  const candidate = value as Partial<VisualEditorPreferences>;
  const snapSize = candidate.snapSize;
  return {
    snapSize: snapSize === 1 || snapSize === 5 || snapSize === 10 || snapSize === 20
      ? snapSize
      : defaultVisualEditorPreferences.snapSize,
    gridVisible: typeof candidate.gridVisible === "boolean"
      ? candidate.gridVisible
      : defaultVisualEditorPreferences.gridVisible,
    objectSnapEnabled: typeof candidate.objectSnapEnabled === "boolean"
      ? candidate.objectSnapEnabled
      : defaultVisualEditorPreferences.objectSnapEnabled,
    rulersVisible: typeof candidate.rulersVisible === "boolean"
      ? candidate.rulersVisible
      : defaultVisualEditorPreferences.rulersVisible,
  };
}
