<script setup lang="ts">
import {
  BracesIcon,
  ChartLineIcon,
  CloudDownloadIcon,
  CopyIcon,
  DatabaseIcon,
  EllipsisVerticalIcon,
  ExternalLinkIcon,
  GitCompareArrowsIcon,
  InfoIcon,
  Loader2Icon,
  LockIcon,
  MailIcon,
  PowerIcon,
  SettingsIcon,
  TablePropertiesIcon,
  Trash2Icon,
  UsersIcon,
  WrenchIcon,
} from '@lucide/vue'
import InlineAsync from '@/components/InlineAsync.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useAsync } from '@/composables/useAsync'
import { useMutation } from '@/composables/useMutation'
import { statusChange } from '@/lib/applicationActions'
import { apiServer, swaggerUrl } from '@/lib/apiServer'
import { databaseTypeLabel, formatStorage } from '@/lib/applications'
import type { Application } from '@/lib/applications'
import { saveBlob } from '@/lib/download'
import * as toast from '@/lib/toast'

// One application on the applications page. `info`, `compare`, `status`, `rebuild` and `delete` ask the page
// to open that dialog: the page holds them, so a deleted card can disappear while its dialog closes.
const props = defineProps<{ application: Application }>()
defineEmits<{ info: []; compare: []; status: []; rebuild: []; delete: [] }>()

function server() {
  return apiServer(props.application.Server.ServerUrl, props.application.Token)
}

// The size on disk comes straight from the API server, one call per card. A failure stays inside
// the card's footer.
const storage = useAsync(() => server().get<number>('/api/Application/GetStorageUsed'))

const exportZip = useMutation(async () => {
  saveBlob(await server().getBlob('/api/Application/Export'), `${props.application.Token}.zip`)
})

async function onExport(): Promise<void> {
  if (exportZip.pending.value) {
    return
  }

  if (!(await exportZip.run())) {
    toast.error(exportZip.error.value)
  }
}
</script>

<template>
  <article class="flex min-w-0 flex-col rounded-lg border bg-card">
    <div class="flex items-start gap-3 p-4">
      <span
        class="flex size-9 shrink-0 items-center justify-center rounded-md bg-primary/15 text-sm font-semibold uppercase text-link"
        aria-hidden="true"
      >
        {{ application.Name.charAt(0) }}
      </span>

      <div class="min-w-0 flex-1">
        <h3 class="text-sm font-medium">
          <RouterLink
            :to="{ name: 'app-entities', params: { appToken: application.Token } }"
            class="rounded-sm wrap-anywhere underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
          >
            {{ application.Name }}
          </RouterLink>
        </h3>
        <p class="mt-0.5 text-xs text-muted-foreground">{{ databaseTypeLabel(application.DatabaseType) }}</p>
      </div>

      <DropdownMenu>
        <DropdownMenuTrigger as-child>
          <Button variant="ghost" size="icon-sm" class="-mt-1 -mr-1.5">
            <EllipsisVerticalIcon />
            <span class="sr-only">Actions for {{ application.Name }}</span>
          </Button>
        </DropdownMenuTrigger>

        <DropdownMenuContent align="end" class="w-52">
          <DropdownMenuItem as-child>
            <RouterLink :to="{ name: 'app-entities', params: { appToken: application.Token } }">
              <TablePropertiesIcon />Entities
            </RouterLink>
          </DropdownMenuItem>
          <DropdownMenuItem as-child>
            <RouterLink :to="{ name: 'app-data', params: { appToken: application.Token } }"><DatabaseIcon />Data</RouterLink>
          </DropdownMenuItem>
          <DropdownMenuItem as-child>
            <RouterLink :to="{ name: 'app-security', params: { appToken: application.Token } }"><LockIcon />Security</RouterLink>
          </DropdownMenuItem>
          <DropdownMenuItem as-child>
            <RouterLink :to="{ name: 'app-endpoints', params: { appToken: application.Token } }">
              <BracesIcon />Custom endpoints
            </RouterLink>
          </DropdownMenuItem>
          <DropdownMenuItem as-child>
            <RouterLink :to="{ name: 'app-email', params: { appToken: application.Token } }"><MailIcon />Email</RouterLink>
          </DropdownMenuItem>
          <DropdownMenuItem as-child>
            <RouterLink :to="{ name: 'app-reports', params: { appToken: application.Token } }"><ChartLineIcon />Reports</RouterLink>
          </DropdownMenuItem>
          <DropdownMenuItem v-if="application.IsOwner" as-child>
            <RouterLink :to="{ name: 'app-sharing', params: { appToken: application.Token } }"><UsersIcon />Sharing</RouterLink>
          </DropdownMenuItem>

          <DropdownMenuSeparator />

          <DropdownMenuItem @select="$emit('info')">
            <InfoIcon />
            Info
          </DropdownMenuItem>
          <DropdownMenuItem as-child>
            <a
              :href="swaggerUrl(application.Server.ServerUrl, application.Token, application.Name)"
              target="_blank"
              rel="noopener"
            >
              <ExternalLinkIcon />
              API
              <span class="sr-only">(Swagger, opens in a new tab)</span>
            </a>
          </DropdownMenuItem>
          <!-- The menu stays open while the export runs, so the busy state is visible. -->
          <DropdownMenuItem :aria-busy="exportZip.pending.value" @select.prevent="onExport">
            <Loader2Icon v-if="exportZip.pending.value" class="animate-spin" />
            <CloudDownloadIcon v-else />
            {{ exportZip.pending.value ? 'Exporting…' : 'Export' }}
          </DropdownMenuItem>
          <DropdownMenuItem @select="$emit('compare')">
            <GitCompareArrowsIcon />
            Compare with…
          </DropdownMenuItem>

          <DropdownMenuSeparator />

          <DropdownMenuItem as-child>
            <RouterLink :to="{ name: 'app-clone', params: { appToken: application.Token } }"><CopyIcon />Clone</RouterLink>
          </DropdownMenuItem>
          <DropdownMenuItem as-child>
            <RouterLink :to="{ name: 'app-settings', params: { appToken: application.Token } }">
              <SettingsIcon />Settings
            </RouterLink>
          </DropdownMenuItem>
          <DropdownMenuItem @select="$emit('status')">
            <PowerIcon />
            {{ statusChange(application.Name, application.Online).label }}
          </DropdownMenuItem>

          <DropdownMenuSeparator />

          <DropdownMenuItem variant="destructive" @select="$emit('rebuild')"><WrenchIcon />Rebuild</DropdownMenuItem>
          <DropdownMenuItem variant="destructive" @select="$emit('delete')"><Trash2Icon />Delete</DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
    </div>

    <div class="flex flex-1 flex-col gap-2 px-4 pb-4 text-sm">
      <div>
        <Badge v-if="application.Online" variant="outline" class="border-success/40 text-success">Online</Badge>
        <Badge v-else variant="destructive">Offline</Badge>
      </div>
      <p class="text-muted-foreground">
        Differentiation
        <span class="wrap-anywhere text-foreground">{{ application.DifferentiationEntity ?? 'none' }}</span>
      </p>
      <p class="wrap-anywhere text-muted-foreground">
        <template v-if="application.IsOwner">Shared with {{ application.CollaboratorCount }}</template>
        <template v-else>Shared by {{ application.OwnerEmail }}</template>
      </p>
    </div>

    <div class="flex min-h-11 items-center justify-between gap-3 border-t px-4 py-2 text-xs text-muted-foreground">
      <span class="shrink-0">Size on disk</span>

      <InlineAsync
        :loading="storage.loading.value"
        :error="storage.error.value"
        :label="`the size on disk of ${application.Name}`"
        @retry="storage.reload()"
      >
        <span v-if="typeof storage.data.value === 'number'" class="font-mono text-foreground">
          {{ formatStorage(storage.data.value) }}
        </span>
      </InlineAsync>
    </div>
  </article>
</template>
