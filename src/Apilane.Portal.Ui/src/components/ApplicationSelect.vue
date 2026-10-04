<script setup lang="ts">
import { computed } from 'vue'
import { Select, SelectContent, SelectGroup, SelectItem, SelectLabel, SelectTrigger, SelectValue } from '@/components/ui/select'
import { groupByServer } from '@/lib/applications'
import type { Application } from '@/lib/applications'

// Picks one of the user's applications, grouped by server and ordered as on the applications page.
// Use it where a screen needs a second application (the source of a schema import, the other side
// of a comparison): pass the applications to choose from, without the one the screen is about.
//
//   <FormField v-slot="{ field }" label="Source application" :error="...">
//     <ApplicationSelect v-model="sourceToken" v-bind="field" :applications="others" />
//   </FormField>
defineOptions({ inheritAttrs: false })

const props = defineProps<{ applications: readonly Application[] }>()

/** The application's token; undefined until one is picked. */
const token = defineModel<string | undefined>({ required: true })

const groups = computed(() => groupByServer(props.applications))
</script>

<template>
  <Select v-model="token">
    <SelectTrigger v-bind="$attrs" class="w-full">
      <SelectValue placeholder="Select an application" />
    </SelectTrigger>
    <SelectContent>
      <SelectGroup v-for="group in groups" :key="group.server.ID">
        <SelectLabel>{{ group.server.Name }}</SelectLabel>
        <SelectItem v-for="application in group.applications" :key="application.Token" :value="application.Token">
          {{ application.Name }}
        </SelectItem>
      </SelectGroup>
    </SelectContent>
  </Select>
</template>
