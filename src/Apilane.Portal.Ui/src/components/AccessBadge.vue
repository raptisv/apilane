<script setup lang="ts">
import { GlobeIcon, LockIcon, ShieldCheckIcon, TriangleAlertIcon, UsersIcon } from '@lucide/vue'
import { computed } from 'vue'
import type { RouteLocationRaw } from 'vue-router'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import type { AccessKind, AccessSummary } from '@/lib/accessSummary'

// An icon that says who may call an entity, a custom endpoint or a property, at a glance. The tooltip has the
// details (what each role may do), and the icon leads to the rules of the item in the security screen.
//
//   <AccessBadge :summary="access.item('Entity', entity.Name)" :to="securityRoute" subject="Orders" />
const props = defineProps<{
  /** Undefined while the rules are not known (or could not be read): nothing is shown. */
  summary: AccessSummary | undefined
  /** Where the icon leads: the security screen with the item selected. */
  to: RouteLocationRaw
  /** What the summary is about, for a screen reader: 'Orders'. */
  subject: string
}>()

const icons = { publicWrite: TriangleAlertIcon, public: GlobeIcon, signedIn: UsersIcon, roles: ShieldCheckIcon, closed: LockIcon }

const colours: Record<AccessKind, string> = {
  publicWrite: 'border-destructive/40 bg-destructive/10 text-destructive',
  public: 'border-warning/40 bg-warning/10 text-warning',
  signedIn: 'border-link/40 bg-link/10 text-link',
  roles: 'border-success/40 bg-success/10 text-success',
  closed: 'border-border bg-muted/40 text-muted-foreground',
}

const headColours: Record<AccessKind, string> = {
  publicWrite: 'text-red-400',
  public: 'text-amber-600',
  signedIn: 'text-blue-600',
  roles: 'text-emerald-600',
  closed: 'text-muted-foreground',
}

const label = computed(() => (props.summary ? `${props.subject}: ${props.summary.label}. ${props.summary.hint}` : ''))
</script>

<template>
  <Tooltip v-if="summary">
    <TooltipTrigger as-child>
      <RouterLink
        :to="to"
        class="inline-flex size-5 shrink-0 items-center justify-center rounded-full border align-middle focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
        :class="colours[summary.kind]"
        :aria-label="label"
        data-testid="access-badge"
        :data-kind="summary.kind"
      >
        <component :is="icons[summary.kind]" class="size-3" aria-hidden="true" />
      </RouterLink>
    </TooltipTrigger>

    <TooltipContent class="max-w-sm flex-col items-start gap-1 py-2.5 font-normal">
      <span class="flex items-center gap-1.5 text-sm font-semibold" :class="headColours[summary.kind]">
        <component :is="icons[summary.kind]" class="size-3.5" aria-hidden="true" />
        {{ summary.label }}
      </span>
      <span class="whitespace-normal opacity-80">{{ summary.hint }}</span>

      <dl v-if="summary.lines.length > 0" class="mt-1 grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
        <template v-for="line in summary.lines" :key="line.action">
          <dt class="font-mono text-[11px] uppercase opacity-70">{{ line.action }}</dt>
          <dd class="flex flex-wrap gap-1">
            <span v-if="line.who.length === 0" class="italic opacity-70">nobody</span>
            <span v-for="who in line.who" :key="who.name" class="rounded-sm bg-background/15 px-1.5 whitespace-normal">
              {{ who.name }}<span v-if="who.notes.length > 0" class="opacity-70"> · {{ who.notes.join(' · ') }}</span>
            </span>
          </dd>
        </template>
      </dl>

      <span class="mt-1 border-t border-background/20 pt-1.5 opacity-70">Click to edit these rules in Security</span>
    </TooltipContent>
  </Tooltip>
</template>
