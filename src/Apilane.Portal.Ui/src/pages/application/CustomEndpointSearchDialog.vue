<script setup lang="ts">
import { PencilIcon, SearchIcon } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import type { Schemas } from '@/lib/api'
import { highlightMatches, searchEndpoints } from '@/lib/customEndpoints'

type Endpoint = Schemas['CustomEndpointResponse']

// Finds custom endpoints by any text of their name, description or SQL, and shows the whole SQL of
// each match with the found text marked. Edit opens the editor in a new tab, so the search stays.
const props = defineProps<{
  endpoints: readonly Endpoint[]
  appToken: string
}>()

const open = defineModel<boolean>('open', { required: true })
const text = ref('')

// Every opening starts with an empty box.
watch(open, (isOpen) => {
  if (isOpen) {
    text.value = ''
  }
})

const matches = computed(() => searchEndpoints(props.endpoints, text.value))

const status = computed(() => {
  if (text.value === '') {
    return ''
  }

  return matches.value.length === 1 ? '1 match' : `${matches.value.length} matches`
})

const markClass = 'rounded-sm bg-warning/30 text-foreground'
</script>

<template>
  <Dialog v-model:open="open">
    <!-- At the top of the window, so the box does not move while the results grow and shrink. -->
    <DialogContent class="top-4 flex max-h-[calc(100dvh-2rem)] translate-y-0 flex-col sm:top-12 sm:max-h-[calc(100dvh-6rem)] sm:max-w-2xl">
      <DialogHeader class="pr-8">
        <DialogTitle>Search custom endpoints</DialogTitle>
        <DialogDescription>Finds the text in the names, descriptions and the whole SQL, in any letter case.</DialogDescription>
      </DialogHeader>

      <div class="relative">
        <SearchIcon class="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
        <Input
          v-model="text"
          type="search"
          aria-label="Text to find"
          placeholder="Name, description or SQL"
          autocomplete="off"
          spellcheck="false"
          class="pl-8"
        />
      </div>

      <p class="sr-only" role="status">{{ status }}</p>

      <div class="-mx-4 min-h-0 flex-1 overflow-y-auto px-4">
        <p v-if="text === ''" class="py-6 text-center text-sm text-muted-foreground">Type to find endpoints.</p>

        <p v-else-if="matches.length === 0" class="py-6 text-center text-sm text-muted-foreground">No matches.</p>

        <ul v-else class="grid gap-2" aria-label="Matches">
          <li v-for="endpoint in matches" :key="endpoint.ID" class="rounded-lg border bg-card px-3 py-2">
            <div class="flex items-start justify-between gap-3">
              <div class="min-w-0">
                <p class="text-sm font-medium wrap-anywhere">
                  <template v-for="(part, index) in highlightMatches(endpoint.Name, text)" :key="index">
                    <mark v-if="part.match" :class="markClass">{{ part.text }}</mark>
                    <template v-else>{{ part.text }}</template>
                  </template>
                </p>
                <p v-if="endpoint.Description" class="text-xs text-muted-foreground wrap-anywhere">
                  <template v-for="(part, index) in highlightMatches(endpoint.Description, text)" :key="index">
                    <mark v-if="part.match" :class="markClass">{{ part.text }}</mark>
                    <template v-else>{{ part.text }}</template>
                  </template>
                </p>
              </div>
              <Button as-child variant="outline" size="icon-sm" class="shrink-0">
                <RouterLink
                  :to="{ name: 'app-endpoint-edit', params: { appToken, id: endpoint.ID } }"
                  target="_blank"
                  title="Edit in a new tab"
                >
                  <PencilIcon />
                  <span class="sr-only">Edit {{ endpoint.Name }} (opens in a new tab)</span>
                </RouterLink>
              </Button>
            </div>
            <pre class="mt-1.5 font-mono text-xs break-all whitespace-pre-wrap text-muted-foreground"><template
                v-for="(part, index) in highlightMatches(endpoint.Query, text)"
                :key="index"
              ><mark v-if="part.match" :class="markClass">{{ part.text }}</mark><template v-else>{{ part.text }}</template></template></pre>
          </li>
        </ul>
      </div>

      <DialogFooter>
        <Button type="button" variant="outline" @click="open = false">Close</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
