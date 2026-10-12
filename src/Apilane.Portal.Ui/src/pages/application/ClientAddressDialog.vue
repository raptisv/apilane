<script setup lang="ts">
import { computed } from 'vue'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { settingExamples } from '@/lib/clientAddress'
import type { Assessment, ClientAddressInfo } from '@/lib/clientAddress'

// The long explanation behind the IP access card: where the address of a caller comes from, what the
// TrustedProxies setting of the API server changes, what works and what does not, and what to write
// for each setup. The first part is live: it shows what the API server saw for this browser.
const props = defineProps<{
  info: ClientAddressInfo | undefined
  assessment: Assessment | undefined
}>()

const open = defineModel<boolean>('open', { required: true })

const examples = computed(() => settingExamples(props.info))

const modeText = computed(() => {
  switch (props.info?.Mode) {
    case 'TrustedProxies':
      return `TrustedProxies is set: ${props.info.TrustedProxies.join(', ')}`
    case 'NoProxies':
      return 'TrustedProxies is none: the header is never read'
    default:
      return 'TrustedProxies is not set: the header of any caller is believed'
  }
})

const sourceText = computed(() => {
  switch (props.info?.Source) {
    case 'ForwardedFor':
      return 'the X-Forwarded-For header'
    case 'RemoteAddrHeader':
      return 'the REMOTE_ADDR header'
    case 'None':
      return 'nothing: no address was found'
    default:
      return 'the connection'
  }
})

const codeClass = 'overflow-x-auto rounded-md border bg-muted/40 p-3 font-mono text-xs leading-relaxed whitespace-pre'
const headingClass = 'text-sm font-semibold'
const cellClass = 'border-b px-3 py-2 align-top'
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="flex max-h-[calc(100dvh-2rem)] flex-col sm:max-w-3xl">
      <DialogHeader class="pr-8">
        <DialogTitle>Where the address of a caller comes from</DialogTitle>
        <DialogDescription>
          The IP list of an application is looked up with the address of each caller. This explains how the API server
          finds that address, why a header can be forged, and what to set so that the list can be trusted.
        </DialogDescription>
      </DialogHeader>

      <div class="-mx-4 grid min-h-0 flex-1 content-start gap-6 overflow-y-auto px-4 text-sm">
        <section class="grid gap-2" aria-labelledby="ca-now">
          <h3 id="ca-now" :class="headingClass">What the API server sees for this browser</h3>

          <template v-if="info">
            <dl class="grid gap-x-4 gap-y-1.5 rounded-md border p-3 sm:grid-cols-[14rem_1fr]">
              <dt class="text-muted-foreground">Connection came from</dt>
              <dd class="font-mono">{{ info.ConnectionAddress || 'unknown' }}</dd>
              <dt class="text-muted-foreground">X-Forwarded-For header</dt>
              <dd class="font-mono wrap-anywhere">{{ info.ForwardedFor ?? 'not sent' }}</dd>
              <dt class="text-muted-foreground">The IP list is looked up with</dt>
              <dd class="font-mono">{{ info.ClientAddress || 'nothing' }}</dd>
              <dt class="text-muted-foreground">Taken from</dt>
              <dd>{{ sourceText }}</dd>
              <dt class="text-muted-foreground">Setting of the API server</dt>
              <dd>{{ modeText }}</dd>
            </dl>

            <p v-if="assessment" class="font-medium">{{ assessment.title }}.</p>
            <p v-if="assessment" class="text-muted-foreground">{{ assessment.detail }}</p>
            <ol v-if="assessment && assessment.steps.length" class="list-decimal space-y-1 pl-5">
              <li v-for="step in assessment.steps" :key="step">{{ step }}</li>
            </ol>
          </template>
          <p v-else class="text-muted-foreground">
            The API server could not be asked. The explanation below holds for every setup.
          </p>
        </section>

        <section class="grid gap-2" aria-labelledby="ca-why">
          <h3 id="ca-why" :class="headingClass">Why this matters</h3>
          <p>
            The API server sees the address of the connection that reached it. Behind a reverse proxy, a load balancer or a CDN that
            is the address of the proxy, so the proxy adds the real address of the caller in the <code>X-Forwarded-For</code> header.
            But a header is only text: a caller who reaches the API server can send
            <code>X-Forwarded-For: 10.0.0.5</code> and be taken for <code>10.0.0.5</code>. With an Allow list that opens the application
            to anyone who knows an allowed address. The same address is written in the log.
          </p>
          <p>
            <strong>TrustedProxies</strong> tells the API server which connections are your own proxies. The header is read only when
            the connection comes from one of them, and then from its right end, where your proxy wrote what it saw. Anything a caller
            wrote earlier in the header is ignored.
          </p>
        </section>

        <section class="grid gap-2" aria-labelledby="ca-setups">
          <h3 id="ca-setups" :class="headingClass">What to set, for each setup</h3>
          <div class="overflow-x-auto rounded-md border">
            <table class="w-full text-left">
              <thead class="bg-muted/50 text-xs text-muted-foreground">
                <tr>
                  <th :class="cellClass">Your setup</th>
                  <th :class="cellClass">TrustedProxies</th>
                  <th :class="cellClass">Result</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td :class="cellClass">Callers reach the API server directly</td>
                  <td :class="cellClass"><code>none</code></td>
                  <td :class="cellClass">The header is ignored. The address is the connection's. Cannot be forged.</td>
                </tr>
                <tr>
                  <td :class="cellClass">Your own reverse proxy (nginx, Caddy, Traefik, HAProxy, the ingress of Kubernetes) is the only way in</td>
                  <td :class="cellClass">The address or network of the proxy, for example <code>172.18.0.2</code> or <code>172.18.0.0/16</code></td>
                  <td :class="cellClass">The caller is the address the proxy saw. A forged header is not believed.</td>
                </tr>
                <tr>
                  <td :class="cellClass">A CDN or a cloud load balancer in front</td>
                  <td :class="cellClass">The networks the provider publishes for its edge, together with your own proxy behind it</td>
                  <td :class="cellClass">The caller is the first address that is not one of those networks.</td>
                </tr>
                <tr>
                  <td :class="cellClass">Not set</td>
                  <td :class="cellClass">(nothing)</td>
                  <td :class="cellClass">The first entry of the header, from anyone. <strong>Can be forged.</strong> The API server logs a warning at start.</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p class="text-muted-foreground">
            The setting is a setting of the API server, not of the application, so it applies to every application on that server. It is
            read at start: restart the API server after changing it. Reload this screen to see the result.
          </p>
        </section>

        <section class="grid gap-2" aria-labelledby="ca-examples">
          <h3 id="ca-examples" :class="headingClass">Examples</h3>

          <p class="font-medium">appsettings.json (or appsettings.Production.json) of the API server</p>
          <pre :class="codeClass">{{ examples.json }}</pre>

          <p class="font-medium">Environment variable</p>
          <pre :class="codeClass">{{ examples.env }}
# several, comma separated
TrustedProxies=172.18.0.0/16,10.0.0.0/8
# callers reach the API server directly
TrustedProxies=none</pre>

          <p class="font-medium">docker-compose</p>
          <pre :class="codeClass">{{ examples.compose }}</pre>

          <p class="font-medium">The proxy has to send the header</p>
          <pre :class="codeClass"># nginx: replace the header by the address nginx saw
proxy_set_header X-Forwarded-For $remote_addr;
# nginx: or add it to what the caller sent (also fine with TrustedProxies)
proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;

# Caddy and Traefik add it by default

# HAProxy
option forwardfor</pre>
        </section>

        <section class="grid gap-2" aria-labelledby="ca-works">
          <h3 id="ca-works" :class="headingClass">What works and what does not</h3>
          <ul class="list-disc space-y-1 pl-5">
            <li>An <strong>Allow</strong> list works only when the address is the real caller's: set TrustedProxies first, then fill the list.</li>
            <li>
              A <strong>Block</strong> list only stops callers who do not try: anyone can come from another address. Use it for accidents,
              not for attackers.
            </li>
            <li>
              When the API server sits behind a proxy and TrustedProxies lacks it, every caller looks like the proxy: an Allow list refuses
              everyone, a Block list refuses everyone if the proxy's address is on it.
            </li>
            <li>IPv6 and IPv4 callers both work. An IPv4 caller shown as <code>::ffff:10.0.0.5</code> is <code>10.0.0.5</code>.</li>
            <li>People who manage the application in the Portal are not subject to the list.</li>
            <li>
              The Portal has its own address handling (rate limits and sign-in log of the Portal); this setting is for the API server
              and the lists of the applications.
            </li>
          </ul>
        </section>
      </div>

      <DialogFooter>
        <Button @click="open = false">Close</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
