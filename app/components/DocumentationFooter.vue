<template>
  <footer class="border-t border-zinc-200 bg-zinc-50 py-10 text-zinc-600 dark:border-white/10 dark:bg-zinc-950 dark:text-zinc-400">
    <div class="mx-auto flex max-w-[90rem] flex-col gap-6 px-5 sm:px-7 lg:flex-row lg:items-end lg:justify-between lg:px-10">
      <div>
        <p class="text-sm font-bold text-zinc-900 dark:text-white">ZPLr Command Reference</p>
        <p class="mt-2 max-w-2xl text-xs/5">
          Independent documentation for the pinned ZPL II 2025 renderer profile. Examples run locally in your browser.
        </p>
      </div>
      <nav class="flex flex-wrap gap-x-5 gap-y-2 text-xs font-semibold" aria-label="Documentation footer">
        <NuxtLink to="/zpl-commands" class="hover:text-zinc-950 dark:hover:text-white">All commands</NuxtLink>
        <NuxtLink to="/editor" class="hover:text-zinc-950 dark:hover:text-white">Editor</NuxtLink>
        <a href="https://github.com/le2ni/zplr" target="_blank" rel="noreferrer" class="hover:text-zinc-950 dark:hover:text-white">Source</a>
      </nav>
    </div>
    <div class="mx-auto mt-7 max-w-[90rem] border-t border-zinc-200 px-5 pt-5 text-[11px]/5 sm:px-7 lg:px-10 dark:border-white/10">
      ZPL, ZPL II, Zebra, and Zebra Programming Language are trademarks or registered trademarks of Zebra Technologies Corp. ZPLr is independent and is not affiliated with or endorsed by Zebra Technologies Corp.
    </div>
  </footer>
</template>
