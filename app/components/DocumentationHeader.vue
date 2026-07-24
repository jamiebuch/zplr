<template>
  <header class="docs-header sticky top-0 z-40 border-b border-zinc-200/80 bg-white/92 backdrop-blur-xl dark:border-white/10 dark:bg-zinc-950/90">
    <nav class="mx-auto flex h-16 max-w-[90rem] items-center gap-5 px-5 sm:px-7 lg:px-10" aria-label="Primary navigation">
      <NuxtLink to="/" class="flex shrink-0 items-center gap-2.5 rounded-md focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-zinc-500" aria-label="ZPLr home">
        <img :src="logoUrl" class="h-8 w-auto dark:invert" alt="" />
        <span class="text-sm font-black tracking-[-0.04em]">ZPLr</span>
      </NuxtLink>

      <div class="hidden items-center gap-1 sm:flex">
        <NuxtLink to="/zpl-commands" class="docs-nav-link" active-class="active">Commands</NuxtLink>
        <a href="https://github.com/le2ni/zplr" target="_blank" rel="noreferrer" class="docs-nav-link">GitHub</a>
      </div>

      <NuxtLink to="/editor" class="ml-auto inline-flex h-9 items-center rounded-lg bg-zinc-950 px-3.5 text-sm font-bold text-white transition hover:bg-zinc-700 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-zinc-500 dark:bg-white dark:text-zinc-950 dark:hover:bg-zinc-200">
        Open Editor
        <IconArrowRight class="ml-1.5 size-4" aria-hidden="true" />
      </NuxtLink>
    </nav>
  </header>
</template>

<script setup lang="ts">
import { IconArrowRight } from "@iconify-prerendered/vue-mdi";
import logoUrl from "../../web/assets/logo.svg";
</script>

<style scoped>
.docs-header {
  box-shadow: 0 1px 0 rgb(24 24 27 / 0.02);
}

.docs-nav-link {
  border-radius: 0.5rem;
  color: rgb(82 82 91);
  font-size: 0.8rem;
  font-weight: 700;
  padding: 0.55rem 0.75rem;
  transition: color 140ms ease, background-color 140ms ease;
}

.docs-nav-link:hover,
.docs-nav-link.active {
  background: rgb(244 244 245);
  color: rgb(9 9 11);
}

@media (prefers-color-scheme: dark) {
  .docs-nav-link {
    color: rgb(161 161 170);
  }

  .docs-nav-link:hover,
  .docs-nav-link.active {
    background: rgb(255 255 255 / 0.08);
    color: white;
  }
}
</style>
