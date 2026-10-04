<script setup lang="ts">
import { ChevronDownIcon, TriangleAlertIcon } from '@lucide/vue'
import { computed, useId } from 'vue'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { newRule } from '@/lib/security'
import {
  noPropertiesText,
  offeredProperties,
  ownedApplies,
  propertyAccess,
  rateLimitText,
  timeWindows,
} from '@/lib/securityAccess'
import type { Action, SecurityItem, SecurityRole, SecurityRule } from '@/lib/securityAccess'

// One cell of the rule grid: whether a role may call an action on the item, and when it may, the
// details of the rule (record scope, properties, rate limit), shown under the switch.
const props = defineProps<{
  item: SecurityItem
  role: SecurityRole
  action: Action
  rule: SecurityRule | undefined
  inheritance: 'anonymous' | 'authenticated' | undefined
  error?: string
  disabled?: boolean
}>()

const emit = defineEmits<{ change: [rule: SecurityRule | undefined] }>()

const expanded = defineModel<boolean>('expanded', { required: true })

const id = useId()
const actionLabel = computed(() => props.action.toUpperCase())
const offered = computed(() => offeredProperties(props.item, props.action))
const properties = computed(() => propertyAccess(props.item, props.action, props.rule?.Properties ?? []))

// A stored 'Owned' stays visible where it does nothing, so it can be seen and set back.
const showRecord = computed(() => ownedApplies(props.item, props.role.RoleID, props.action) || props.rule?.Record === 'Owned')

function toggle(on: boolean): void {
  emit('change', on ? newRule(props.item, props.role.RoleID, props.action) : undefined)
  expanded.value = on
}

function update(changes: Partial<SecurityRule>): void {
  if (props.rule) {
    emit('change', { ...props.rule, ...changes })
  }
}

function setProperty(name: string, on: boolean): void {
  const listed = new Set(props.rule?.Properties ?? [])

  if (on) {
    listed.add(name)
  } else {
    listed.delete(name)
  }

  // Kept in the order the item offers them.
  update({ Properties: offered.value.filter((property) => listed.has(property)) })
}

const rateWindow = computed({
  get: () => props.rule?.RateLimit?.TimeWindow ?? 'None',
  set: (value: string) =>
    update({ RateLimit: value === 'None' ? null : { MaxRequests: props.rule?.RateLimit?.MaxRequests ?? 1, TimeWindow: value } }),
})

const windowLabels: Record<string, string> = { Per_Second: 'Per second', Per_Minute: 'Per minute', Per_Hour: 'Per hour' }

const summary = computed(() => {
  const rule = props.rule

  if (!rule) {
    return []
  }

  const parts: string[] = []

  if (showRecord.value) {
    parts.push(rule.Record === 'Owned' ? 'Owned records only' : 'All records')
  }

  if (properties.value.kind === 'all') {
    parts.push('All properties')
  } else if (properties.value.kind === 'some') {
    parts.push(`${properties.value.included.length} of ${offered.value.length} properties`)
  }

  if (rule.RateLimit && rule.RateLimit.MaxRequests !== '') {
    parts.push(rateLimitText(rule.RateLimit))
  }

  return parts
})
</script>

<template>
  <div class="grid gap-2" :class="error && 'rounded-md ring-1 ring-destructive/60 ring-offset-4 ring-offset-card'">
    <div class="flex items-center gap-2">
      <Switch
        :id="`${id}-on`"
        :model-value="rule !== undefined"
        :disabled="disabled"
        :aria-describedby="error ? `${id}-error` : undefined"
        @update:model-value="toggle"
      />
      <Label :for="`${id}-on`" class="sr-only">{{ role.DisplayName }} may {{ actionLabel }} {{ item.Name }}</Label>
      <span v-if="inheritance === 'anonymous'" class="text-xs text-warning">Inherits from Anonymous</span>
      <span v-else-if="inheritance === 'authenticated'" class="text-xs text-muted-foreground">Inherits from Authenticated</span>
    </div>

    <template v-if="rule">
      <p v-if="properties.kind === 'none'" class="flex gap-1.5 text-xs text-warning">
        <TriangleAlertIcon class="mt-px size-3.5 shrink-0" aria-hidden="true" />
        {{ noPropertiesText(action) }}
      </p>
      <p v-if="summary.length > 0" class="text-xs text-muted-foreground">{{ summary.join(' · ') }}</p>

      <Button
        variant="ghost"
        size="sm"
        class="-ml-2 w-fit text-muted-foreground"
        :aria-expanded="expanded"
        :aria-controls="`${id}-details`"
        @click="expanded = !expanded"
      >
        <ChevronDownIcon class="transition-transform" :class="expanded && 'rotate-180'" />
        Details
        <span class="sr-only">of {{ role.DisplayName }} {{ actionLabel }}</span>
      </Button>

      <div v-show="expanded" :id="`${id}-details`" class="grid gap-3 border-l-2 pl-3">
        <div v-if="showRecord" class="grid gap-1.5">
          <Label :for="`${id}-record`" class="text-xs">Records</Label>
          <Select :model-value="rule.Record" :disabled="disabled" @update:model-value="update({ Record: String($event) })">
            <SelectTrigger :id="`${id}-record`" size="sm" class="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="All">All records</SelectItem>
              <SelectItem value="Owned">Owned records only</SelectItem>
            </SelectContent>
          </Select>
          <p v-if="!ownedApplies(item, role.RoleID, action)" class="text-xs text-muted-foreground">
            Owned records only has no effect here.
          </p>
        </div>

        <fieldset v-if="offered.length > 0" class="grid gap-1.5">
          <legend class="mb-1.5 flex w-full flex-wrap items-center justify-between gap-1.5 text-xs font-medium">
            Properties
            <span class="flex gap-1">
              <Button variant="outline" size="xs" :disabled="disabled" @click="update({ Properties: [...offered] })">
                Select all<span class="sr-only"> properties</span>
              </Button>
              <Button variant="outline" size="xs" :disabled="disabled" @click="update({ Properties: [] })">
                Deselect all<span class="sr-only"> properties</span>
              </Button>
            </span>
          </legend>
          <ul class="grid max-h-56 gap-1.5 overflow-y-auto pr-1">
            <li v-for="name in offered" :key="name" class="flex items-center gap-2">
              <Checkbox
                :id="`${id}-p-${name}`"
                :model-value="properties.included.includes(name)"
                :disabled="disabled"
                @update:model-value="setProperty(name, $event === true)"
              />
              <Label :for="`${id}-p-${name}`" class="font-mono text-xs font-normal break-all">
                {{ name }}
                <span v-if="name === item.DifferentiationProperty" class="text-muted-foreground" title="Differentiation property">
                  (DP)
                </span>
              </Label>
            </li>
          </ul>
        </fieldset>

        <div class="grid gap-1.5">
          <Label :for="`${id}-rate`" class="text-xs">Rate limit</Label>
          <div class="flex gap-1.5">
            <Input
              v-if="rule.RateLimit"
              :model-value="rule.RateLimit.MaxRequests"
              type="number"
              min="1"
              step="1"
              class="h-7 w-20"
              :aria-label="`Maximum requests ${windowLabels[rule.RateLimit.TimeWindow]?.toLowerCase() ?? ''}`"
              :disabled="disabled"
              @update:model-value="update({ RateLimit: { TimeWindow: rule.RateLimit.TimeWindow, MaxRequests: $event === '' ? '' : Number($event) } })"
            />
            <Select v-model="rateWindow" :disabled="disabled">
              <SelectTrigger :id="`${id}-rate`" size="sm" class="min-w-0 flex-1">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="None">No limit</SelectItem>
                <SelectItem v-for="window in timeWindows" :key="window" :value="window">{{ windowLabels[window] }}</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>
      </div>
    </template>

    <p v-if="error" :id="`${id}-error`" role="alert" class="text-xs text-destructive">{{ error }}</p>
  </div>
</template>
