<script setup lang="ts">
import { computed } from 'vue'

// The placeholder a screen shows while its first load runs: what is being loaded, an indeterminate
// bar, and rows with a light sweeping over them. Pair it with useAsync:
//   <LoadingState v-if="loading" label="Loading servers" />
const props = withDefaults(
  defineProps<{
    /** What is loading. Shown, and read to screen-reader users. */
    label: string
    rows?: number
  }>(),
  { rows: 3 },
)

// "Loading servers" reads as "Loading servers…"; a label that already ends in a mark is left as it is.
const text = computed(() => (/[.…!?]$/.test(props.label.trim()) ? props.label : `${props.label}…`))

// The rows are not all as long, and the sweep reaches them one after the other.
const widths = ['w-full', 'w-[94%]', 'w-[88%]', 'w-[96%]']
</script>

<template>
  <div role="status" class="grid gap-3">
    <div class="flex items-center gap-3">
      <span class="loading-dots" aria-hidden="true"><i /><i /><i /></span>
      <span class="text-sm text-muted-foreground">{{ text }}</span>
    </div>

    <div class="loading-bar" aria-hidden="true"><span /></div>

    <div class="grid gap-2" aria-hidden="true">
      <div
        v-for="n in rows"
        :key="n"
        class="loading-row h-11 rounded-md bg-muted"
        :class="widths[(n - 1) % widths.length]"
        :style="{ animationDelay: `${(n - 1) * 140}ms` }"
      />
    </div>
  </div>
</template>

<style scoped>
.loading-dots {
  display: inline-flex;
  gap: 4px;
}

.loading-dots i {
  animation: loading-dot 1.2s ease-in-out infinite;
  background: var(--color-primary, #2563eb);
  border-radius: 999px;
  height: 6px;
  width: 6px;
}

.loading-dots i:nth-child(2) {
  animation-delay: 0.15s;
}

.loading-dots i:nth-child(3) {
  animation-delay: 0.3s;
}

.loading-bar {
  background: color-mix(in oklab, var(--color-primary, #2563eb) 14%, transparent);
  border-radius: 999px;
  height: 2px;
  overflow: hidden;
}

.loading-bar > span {
  animation: loading-bar 1.3s cubic-bezier(0.4, 0, 0.2, 1) infinite;
  background: var(--color-primary, #2563eb);
  border-radius: 999px;
  display: block;
  height: 100%;
  width: 40%;
}

/* A soft light travels over each row, one row after the other. */
.loading-row {
  animation: loading-sweep 1.6s ease-in-out infinite;
  background-image: linear-gradient(
    100deg,
    transparent 30%,
    color-mix(in oklab, var(--color-foreground, #fff) 7%, transparent) 50%,
    transparent 70%
  );
  background-repeat: no-repeat;
  background-size: 250% 100%;
}

@keyframes loading-dot {
  0%,
  80%,
  100% {
    opacity: 0.3;
    transform: scale(0.8);
  }
  40% {
    opacity: 1;
    transform: scale(1);
  }
}

@keyframes loading-bar {
  from {
    transform: translateX(-110%);
  }
  to {
    transform: translateX(260%);
  }
}

@keyframes loading-sweep {
  from {
    background-position: 120% 0;
  }
  to {
    background-position: -120% 0;
  }
}

@media (prefers-reduced-motion: reduce) {
  .loading-dots i,
  .loading-bar > span,
  .loading-row {
    animation: none;
  }

  .loading-bar > span {
    width: 100%;
  }
}
</style>
