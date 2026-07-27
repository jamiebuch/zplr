<template>
  <div ref="root" class="mini-preview" :class="{ compact, thumbnail, cropped: crop }" :aria-busy="state === 'loading'">
    <div v-if="state === 'idle' || state === 'loading'" class="mini-preview-state" role="status">
      <span class="preview-spinner" aria-hidden="true"></span>
      {{ state === "idle" ? (thumbnail ? "Sample queued" : "Preview queued") : (thumbnail ? "Rendering sample…" : "Rendering locally…") }}
    </div>

    <img
      v-else-if="imageUrl"
      :src="imageUrl"
      :alt="alt"
      class="mini-preview-image"
    />

    <div v-else class="mini-preview-state error" role="status">
      <IconAlertCircleOutline class="size-5" aria-hidden="true" />
      <span>{{ failure || "This example did not produce a label preview." }}</span>
    </div>

    <div v-if="state === 'ready'" class="preview-meta">
      <span>{{ dimensions }}</span>
      <span v-if="labelCount > 1">{{ labelCount }} labels · first shown</span>
      <span v-if="diagnosticCount">{{ diagnosticCount }} diagnostic{{ diagnosticCount === 1 ? "" : "s" }}</span>
      <span v-else>Rendered locally</span>
    </div>
  </div>
</template>

<script setup lang="ts">
import { IconAlertCircleOutline } from "@iconify-prerendered/vue-mdi";
import { previewInkBounds } from "../../web/zplPreviewThumbnail";

const props = defineProps<{
  source: string;
  alt: string;
  compact?: boolean;
  thumbnail?: boolean;
  crop?: boolean;
}>();

interface PreviewResult {
  imageUrl?: string;
  width?: number;
  height?: number;
  labelCount: number;
  diagnosticCount: number;
  failure?: string;
}

const previewCache = new Map<string, Promise<PreviewResult>>();
const root = ref<HTMLElement>();
const state = ref<"idle" | "loading" | "ready" | "error">("idle");
const imageUrl = ref<string>();
const failure = ref<string>();
const width = ref<number>();
const height = ref<number>();
const labelCount = ref(0);
const diagnosticCount = ref(0);
const dimensions = computed(() =>
  width.value && height.value
    ? `${width.value} × ${height.value} dots${props.crop ? " · cropped to ink" : ""}`
    : "Label preview",
);
let observer: IntersectionObserver | undefined;

function thumbnailUrl(canvas: HTMLCanvasElement): string {
  const context = canvas.getContext("2d", { willReadFrequently: true });
  if (!context) return canvas.toDataURL("image/png");
  const pixels = context.getImageData(0, 0, canvas.width, canvas.height);
  const crop = previewInkBounds(pixels.data, canvas.width, canvas.height);
  if (!crop) return canvas.toDataURL("image/png");

  const thumbnail = document.createElement("canvas");
  thumbnail.width = crop.width;
  thumbnail.height = crop.height;
  const thumbnailContext = thumbnail.getContext("2d");
  if (!thumbnailContext) return canvas.toDataURL("image/png");
  thumbnailContext.drawImage(
    canvas,
    crop.x,
    crop.y,
    crop.width,
    crop.height,
    0,
    0,
    crop.width,
    crop.height,
  );
  return thumbnail.toDataURL("image/png");
}

async function createPreview(source: string, crop: boolean): Promise<PreviewResult> {
  try {
    const { renderZpl } = await import("../../src/index.web");
    const result = await renderZpl(source, {
      printDensity: 8,
      strict: false,
      limits: {
        maxDimension: 1_200,
        maxPixels: 720_000,
        maxGraphicBytes: 1_000_000,
        maxSessionBytes: 2_000_000,
        maxTemplateDepth: 6,
        maxExpandedCommands: 5_000,
        maxLabels: 8,
      },
    });
    const label = result.labels[0];
    if (!label) {
      return {
        labelCount: 0,
        diagnosticCount: result.diagnostics.length,
        failure: result.diagnostics[0]?.message ?? "No printable label was produced.",
      };
    }
    return {
      imageUrl: crop ? thumbnailUrl(label.canvas) : label.canvas.toDataURL("image/png"),
      width: label.width,
      height: label.height,
      labelCount: result.labels.length,
      diagnosticCount: result.diagnostics.length,
    };
  } catch (error) {
    return {
      labelCount: 0,
      diagnosticCount: 0,
      failure: error instanceof Error ? error.message : "The local renderer failed.",
    };
  }
}

async function renderPreview(): Promise<void> {
  if (state.value !== "idle") return;
  state.value = "loading";
  const crop = Boolean(props.thumbnail || props.crop);
  const cacheKey = `${crop ? "cropped" : "full"}:${props.source}`;
  let request = previewCache.get(cacheKey);
  if (!request) {
    request = createPreview(props.source, crop);
    previewCache.set(cacheKey, request);
  }
  const result = await request;
  imageUrl.value = result.imageUrl;
  failure.value = result.failure;
  width.value = result.width;
  height.value = result.height;
  labelCount.value = result.labelCount;
  diagnosticCount.value = result.diagnosticCount;
  state.value = result.imageUrl ? "ready" : "error";
}

onMounted(() => {
  const element = root.value;
  if (!element) {
    void renderPreview();
    return;
  }

  const bounds = element.getBoundingClientRect();
  if (bounds.bottom >= -280 && bounds.top <= window.innerHeight + 280) {
    void renderPreview();
    return;
  }

  if (!("IntersectionObserver" in window)) {
    void renderPreview();
    return;
  }

  observer = new IntersectionObserver((entries) => {
    if (!entries.some(({ isIntersecting }) => isIntersecting)) return;
    observer?.disconnect();
    void renderPreview();
  }, { rootMargin: "280px 0px" });
  observer.observe(element);
});

onBeforeUnmount(() => observer?.disconnect());
</script>

<style scoped>
.mini-preview {
  display: flex;
  min-height: 18rem;
  min-width: 0;
  flex-direction: column;
  background:
    linear-gradient(45deg, rgb(244 244 245) 25%, transparent 25%),
    linear-gradient(-45deg, rgb(244 244 245) 25%, transparent 25%),
    linear-gradient(45deg, transparent 75%, rgb(244 244 245) 75%),
    linear-gradient(-45deg, transparent 75%, rgb(244 244 245) 75%);
  background-position: 0 0, 0 8px, 8px -8px, -8px 0;
  background-size: 16px 16px;
}

.mini-preview-image {
  margin: auto;
  max-height: 24rem;
  max-width: 100%;
  object-fit: contain;
  padding: 1.25rem;
  image-rendering: pixelated;
}

.mini-preview.compact {
  min-height: 14rem;
}

.mini-preview.compact .mini-preview-image {
  max-height: 18rem;
  padding: 1rem;
}

.mini-preview.cropped .mini-preview-image {
  width: 100%;
}

.mini-preview.thumbnail {
  height: 4.5rem;
  min-height: 4.5rem;
  background: rgb(250 250 250);
}

.mini-preview.thumbnail .mini-preview-image {
  width: 100%;
  height: 100%;
  max-height: none;
  padding: 0.375rem 0.6rem;
}

.mini-preview.thumbnail .mini-preview-state {
  padding: 1rem;
  font-size: 0.65rem;
}

.mini-preview.thumbnail .preview-meta {
  display: none;
}

.mini-preview-state {
  margin: auto;
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 2rem;
  color: rgb(113 113 122);
  font-size: 0.75rem;
  font-weight: 650;
  text-align: center;
}

.mini-preview-state.error {
  max-width: 26rem;
  color: rgb(190 24 93);
}

.preview-spinner {
  width: 0.85rem;
  height: 0.85rem;
  border: 2px solid rgb(212 212 216);
  border-top-color: rgb(63 63 70);
  border-radius: 999px;
  animation: spin 0.8s linear infinite;
}

.preview-meta {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  gap: 0.4rem 1rem;
  border-top: 1px solid rgb(228 228 231);
  background: rgb(255 255 255 / 0.92);
  padding: 0.45rem 0.7rem;
  color: rgb(113 113 122);
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  font-size: 0.62rem;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

@media (prefers-color-scheme: dark) {
  .mini-preview {
    background:
      linear-gradient(45deg, rgb(39 39 42) 25%, transparent 25%),
      linear-gradient(-45deg, rgb(39 39 42) 25%, transparent 25%),
      linear-gradient(45deg, transparent 75%, rgb(39 39 42) 75%),
      linear-gradient(-45deg, transparent 75%, rgb(39 39 42) 75%);
    background-color: rgb(24 24 27);
    background-position: 0 0, 0 8px, 8px -8px, -8px 0;
    background-size: 16px 16px;
  }

  .mini-preview.thumbnail {
    background: rgb(24 24 27);
  }

  .preview-meta {
    border-color: rgb(255 255 255 / 0.1);
    background: rgb(9 9 11 / 0.94);
  }

  .preview-spinner {
    border-color: rgb(63 63 70);
    border-top-color: rgb(212 212 216);
  }

  .mini-preview-state.error {
    color: rgb(251 113 133);
  }
}
</style>
