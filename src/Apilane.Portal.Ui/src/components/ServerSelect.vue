<script setup lang="ts">
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { ApplicationServer } from '@/lib/applications'

// The API server an application is put on, picked from GET /api/v1/servers. Use it inside
// FormField wherever an application is created (new, import, clone):
//
//   <FormField v-slot="{ field }" label="Server" :error="errors.fields.ServerID">
//     <ServerSelect v-model="form.ServerID" v-bind="field" :servers="servers" />
//   </FormField>
//
// When there is no server at all, show NoServersState instead of the form.
defineOptions({ inheritAttrs: false })

defineProps<{ servers: readonly ApplicationServer[] }>()

/** The server's ID; undefined until one is picked. */
const serverId = defineModel<number | undefined>({ required: true })
</script>

<template>
  <Select v-model="serverId">
    <SelectTrigger v-bind="$attrs" class="w-full">
      <SelectValue placeholder="Select a server" />
    </SelectTrigger>
    <SelectContent>
      <SelectItem v-for="server in servers" :key="server.ID" :value="server.ID">{{ server.Name }}</SelectItem>
    </SelectContent>
  </Select>
</template>
