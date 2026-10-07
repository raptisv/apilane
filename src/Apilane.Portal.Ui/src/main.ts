import { createApp } from 'vue'
import App from './App.vue'
import { ApiError } from './lib/api'
import { bootstrapRequired, loadBootstrap } from './lib/bootstrap'
import { tryLoadSession } from './lib/session'
import { router } from './router'
import './style.css'

// A page restored from the back/forward cache comes back frozen as it was left (for example
// mid-redirect to the login page), so start fresh instead.
window.addEventListener('pageshow', (event) => {
  if (event.persisted) {
    location.reload()
  }
})

// The session comes first, so the router knows whether somebody is signed in before it opens the
// first screen: without a session it shows the login page, and with one every screen inside
// AppShell can rely on useSession() being set.
async function start(): Promise<void> {
  // Fail closed even on a public screen if the setup state cannot be read.
  await loadBootstrap()

  if (!bootstrapRequired()) {
    try {
      await tryLoadSession()
    } catch (error) {
      // Public screens need no session after the instance has completed setup.
      if (!startsOnPublicScreen()) {
        throw error
      }
    }
  }

  mount()
}

start().catch(showStartupError)

function startsOnPublicScreen(): boolean {
  return router.resolve(location.pathname).meta.public === true
}

function mount(): void {
  createApp(App).use(router).mount('#app')
}

// Shown instead of the app when the session could not be loaded. Built with DOM calls because
// the app, and with it every component, is not running.
function showStartupError(error: unknown): void {
  const root = document.getElementById('app')

  if (!root) {
    return
  }

  const message = document.createElement('p')
  message.textContent = error instanceof ApiError ? error.message : 'The Portal could not be reached.'

  const retry = document.createElement('button')
  retry.type = 'button'
  retry.textContent = 'Try again'
  retry.className = 'rounded-md border border-input px-2.5 py-1 text-foreground hover:bg-muted'
  retry.addEventListener('click', () => location.reload())

  const actions = document.createElement('p')
  actions.className = 'mt-4'
  actions.append(retry)

  root.className = 'p-6 text-sm text-muted-foreground'
  root.replaceChildren(message, actions)
}
