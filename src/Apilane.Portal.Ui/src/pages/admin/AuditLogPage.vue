<script setup lang="ts">
import { HistoryIcon } from '@lucide/vue'
import { watch } from 'vue'
import AppPagination from '@/components/AppPagination.vue'
import AuditLogTable from '@/components/AuditLogTable.vue'
import ErrorState from '@/components/ErrorState.vue'
import LoadingState from '@/components/LoadingState.vue'
import PageHeader from '@/components/PageHeader.vue'
import StateMessage from '@/components/StateMessage.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useAsync } from '@/composables/useAsync'
import { usePageQuery } from '@/composables/usePageQuery'
import { api, unwrap } from '@/lib/api'

const pageSize = 50
const page = usePageQuery()

const { data, error, loading, reload } = useAsync(() =>
  unwrap(api.GET('/api/v1/admin/audit-log', { params: { query: { Page: page.value, PageSize: pageSize } } })),
)

// Not the `watch` option of useAsync: that drops the data, which unmounts the pagination and loses keyboard focus.
watch(page, () => void reload())
</script>

<template>
  <PageHeader
    title="Audit log"
    description="Who changed what on this instance: servers, settings, user roles, new applications and database backups."
  >
    <Badge v-if="data" variant="secondary">
      {{ data.Total }}
      <span class="sr-only">entries</span>
    </Badge>
  </PageHeader>

  <LoadingState v-if="loading" label="Loading the audit log" :rows="8" />

  <ErrorState v-else-if="error" :error="error" @retry="reload" />

  <template v-else-if="data">
    <!-- An empty page past the last one (an old link, a typed page number) offers the way back. -->
    <StateMessage v-if="data.Data.length === 0" :icon="HistoryIcon" title="No audit log entries found">
      <Button v-if="data.Total > 0" as-child variant="outline">
        <RouterLink :to="{ query: { page: '1' } }" aria-current-value="false">Go to the first page</RouterLink>
      </Button>
    </StateMessage>

    <AuditLogTable v-else :entries="data.Data" />

    <AppPagination :total="data.Total" :page-size="pageSize" label="Audit log pages" />
  </template>
</template>
