import { createRouter, createWebHistory } from 'vue-router'
import type { RouteLocationRaw } from 'vue-router'
import { loadedInstanceTitle } from '@/composables/useInstance'
import AppShell from '@/layouts/AppShell.vue'
import { isServerPath, safeReturnUrl } from '@/lib/returnUrl'
import { currentSession } from '@/lib/session'
import { bootstrapRequired } from '@/lib/bootstrap'

declare module 'vue-router' {
  interface RouteMeta {
    /** The browser tab title: '<title> · <instance title>'. */
    title?: string
    /** Only administrators may open the screen. Everyone else gets the Forbidden state (see AppShell). */
    requiresAdmin?: boolean
    /** A screen under /apps/:appToken that only the application's owner may open. Collaborators get the Forbidden state (see AppLayout). */
    requiresOwner?: boolean
    /** The screen works without a session (sign in, password reset). Any other screen sends a visitor to the login page. */
    public?: boolean
    /** A public screen that has no use once signed in (sign in, sign up): a signed-in user is sent on. */
    guestOnly?: boolean
    /** A side sheet over the screen it is opened from (the report editor): opening and closing it leaves the page where it is scrolled. */
    keepScroll?: boolean
  }
}

/**
 * Every screen is a lazy route: its code is downloaded the first time it is opened. Screens for a
 * signed-in user sit under AppShell. Screens that work without a session sit under AuthLayout and
 * carry meta.public.
 */
export const router = createRouter({
  // The UI is served from the site root. Paths match whatever their letter case (/Account/Login).
  history: createWebHistory(),
  // Back and Forward return to where the page was scrolled; a new screen or page of a list starts at
  // the top. Another change of the query alone (a dialog, ?item= of the security screen) stays put,
  // and so does opening or closing a sheet over a screen (meta.keepScroll).
  scrollBehavior: (to, from, saved) =>
    saved ??
    (to.meta.keepScroll || from.meta.keepScroll || (to.path === from.path && to.query.page === from.query.page) ? false : { top: 0 }),
  routes: [
    {
      path: '/',
      component: AppShell,
      children: [
        // The applications page is the home page.
        { path: '', redirect: { name: 'apps' } },
        {
          // ?info=<application token> opens the application info dialog.
          // ?compare=<application token> opens the compare dialog; &with=<application token> is the other side.
          path: 'apps',
          name: 'apps',
          component: () => import('@/pages/apps/ApplicationsPage.vue'),
          meta: { title: 'Applications' },
        },
        // Static segments come before any ':appToken' route, so 'new' and 'import' are never read as a token.
        {
          path: 'apps/new',
          name: 'app-create',
          component: () => import('@/pages/apps/CreateApplicationPage.vue'),
          meta: { title: 'New application' },
        },
        {
          path: 'apps/import',
          name: 'app-import',
          component: () => import('@/pages/apps/ImportApplicationPage.vue'),
          meta: { title: 'Import application' },
        },
        {
          // Every screen of one application. AppLayout loads the application and shows the
          // breadcrumb and the section navigation; a screen reads it with useApplication().
          path: 'apps/:appToken',
          component: () => import('@/layouts/AppLayout.vue'),
          children: [
            { path: '', redirect: (to) => ({ name: 'app-entities', params: to.params }) },
            {
              path: 'entities',
              name: 'app-entities',
              component: () => import('@/pages/application/EntitiesPage.vue'),
              meta: { title: 'Entities' },
            },
            {
              // Every screen of one entity. EntityLayout loads the entity and shows its name and
              // tabs; a screen reads it with useEntity(). Name the routes 'app-entity-<tab>'.
              path: 'entities/:entity',
              component: () => import('@/layouts/EntityLayout.vue'),
              children: [
                {
                  // The create, edit, rename and delete dialogs of a property open on this screen.
                  path: '',
                  name: 'app-entity-properties',
                  component: () => import('@/pages/application/PropertiesPage.vue'),
                  meta: { title: 'Properties' },
                },
                {
                  // System entities too: read-only unless the user is an administrator.
                  path: 'constraints',
                  name: 'app-entity-constraints',
                  component: () => import('@/pages/application/ConstraintsPage.vue'),
                  meta: { title: 'Constraints' },
                },
                {
                  path: 'sorting',
                  name: 'app-entity-sorting',
                  component: () => import('@/pages/application/SortingPage.vue'),
                  meta: { title: 'Default sorting' },
                },
              ],
            },
            {
              // The records of one entity: ?page=, ?pageSize= and ?sort=Name (-Name for descending).
              // Without an entity it opens the first custom one.
              path: 'data/:entity?',
              name: 'app-data',
              component: () => import('@/pages/application/DataPage.vue'),
              meta: { title: 'Data' },
            },
            {
              // Access settings and rules. ?item=Entity-<name> selects the item of the rule grid
              // (CustomEndpoint-<name>, Schema-Schema); ?view=tree and ?view=matrix open the read-only views.
              path: 'security',
              name: 'app-security',
              component: () => import('@/pages/application/SecurityPage.vue'),
              meta: { title: 'Security' },
            },
            {
              // The list, with the search dialog and the delete confirm.
              path: 'endpoints',
              name: 'app-endpoints',
              component: () => import('@/pages/application/CustomEndpointsPage.vue'),
              meta: { title: 'Custom endpoints' },
            },
            {
              // The editor, empty. 'new' comes before ':id', and ':id' takes digits only.
              path: 'endpoints/new',
              name: 'app-endpoint-create',
              component: () => import('@/pages/application/CustomEndpointEditorPage.vue'),
              meta: { title: 'New custom endpoint' },
            },
            {
              // The editor of one saved endpoint, by its numeric ID (a rename keeps the address).
              path: 'endpoints/:id(\\d+)',
              name: 'app-endpoint-edit',
              component: () => import('@/pages/application/CustomEndpointEditorPage.vue'),
              meta: { title: 'Edit custom endpoint' },
            },
            {
              // The reports dashboard. The report editor is a side sheet over it, so the next two
              // routes show the same page: 'new' comes before ':reportId', which takes digits only.
              path: 'reports',
              name: 'app-reports',
              component: () => import('@/pages/application/ReportsPage.vue'),
              meta: { title: 'Reports' },
            },
            {
              path: 'reports/new',
              name: 'app-report-create',
              component: () => import('@/pages/application/ReportsPage.vue'),
              meta: { title: 'New report', keepScroll: true },
            },
            {
              path: 'reports/:reportId(\\d+)/edit',
              name: 'app-report-edit',
              component: () => import('@/pages/application/ReportsPage.vue'),
              meta: { title: 'Edit report', keepScroll: true },
            },
            {
              // The users the application is shared with, with the share dialog and the remove confirm.
              path: 'sharing',
              name: 'app-sharing',
              component: () => import('@/pages/application/SharingPage.vue'),
              meta: { title: 'Sharing', requiresOwner: true },
            },
            {
              // Schema import: a JSON payload of entities, security rules and custom endpoints to add.
              // Not named 'app-import': that is the page that creates an application from an export.
              path: 'import',
              name: 'app-schema-import',
              component: () => import('@/pages/application/SchemaImportPage.vue'),
              meta: { title: 'Import schema' },
            },
            {
              // SMTP settings and landing page (Portal), e-mail templates (API server, in a dialog).
              path: 'email',
              name: 'app-email',
              component: () => import('@/pages/application/EmailPage.vue'),
              meta: { title: 'Email' },
            },
            {
              // The page number is in the query: ?page=2 (see AppPagination).
              path: 'audit-log',
              name: 'app-audit-log',
              component: () => import('@/pages/application/AuditLogPage.vue'),
              meta: { title: 'Audit log' },
            },
            {
              // The clone form. It has no tab: the card menu of the applications page opens it.
              path: 'clone',
              name: 'app-clone',
              component: () => import('@/pages/application/CloneApplicationPage.vue'),
              meta: { title: 'Clone' },
            },
            {
              // The progress of one clone, polled until it has completed or failed.
              path: 'clone/:operationId',
              name: 'app-clone-progress',
              component: () => import('@/pages/application/CloneProgressPage.vue'),
              meta: { title: 'Clone progress' },
            },
            {
              // General (name, connection string), status, and the danger zone (rebuild, delete).
              path: 'settings',
              name: 'app-settings',
              component: () => import('@/pages/application/SettingsPage.vue'),
              meta: { title: 'Settings' },
            },
          ],
        },
        {
          path: 'admin/servers',
          name: 'admin-servers',
          component: () => import('@/pages/admin/ServersPage.vue'),
          meta: { title: 'Servers', requiresAdmin: true },
        },
        {
          path: 'admin/users',
          name: 'admin-users',
          component: () => import('@/pages/admin/UsersPage.vue'),
          meta: { title: 'Users', requiresAdmin: true },
        },
        {
          path: 'admin/agents',
          name: 'admin-agents',
          component: () => import('@/pages/admin/AgentsPage.vue'),
          meta: { title: 'Agents', requiresAdmin: true },
        },
        {
          // Every application of the instance, whoever owns it.
          path: 'admin/applications',
          name: 'admin-applications',
          component: () => import('@/pages/admin/ApplicationsPage.vue'),
          meta: { title: 'Applications', requiresAdmin: true },
        },
        {
          // The records of any application: ?page=, ?pageSize= and ?sort=, as in 'app-data'. Without
          // an entity it opens the first custom one.
          path: 'admin/applications/:appToken/data/:entity?',
          name: 'admin-application-data',
          component: () => import('@/pages/admin/ApplicationDataPage.vue'),
          meta: { title: 'Data browser', requiresAdmin: true },
        },
        {
          path: 'admin/settings',
          name: 'admin-settings',
          component: () => import('@/pages/admin/SettingsPage.vue'),
          meta: { title: 'Settings', requiresAdmin: true },
        },
        {
          // The page number is in the query: ?page=2 (see AppPagination).
          path: 'admin/audit-log',
          name: 'admin-audit-log',
          component: () => import('@/pages/admin/AuditLogPage.vue'),
          meta: { title: 'Audit log', requiresAdmin: true },
        },
        {
          // The change-password dialog, shown over the applications page. The user menu opens the
          // same dialog in place; this address exists so the dialog can be linked to (see AppShell).
          path: 'account/password',
          name: 'change-password',
          component: () => import('@/pages/apps/ApplicationsPage.vue'),
          meta: { title: 'Change password' },
        },
        {
          path: ':pathMatch(.*)*',
          name: 'not-found',
          component: () => import('@/pages/NotFoundPage.vue'),
          meta: { title: 'Not found' },
        },
      ],
    },
    {
      path: '/account',
      component: () => import('@/layouts/AuthLayout.vue'),
      // /account on its own has no screen.
      redirect: { name: 'login' },
      meta: { public: true },
      children: [
        {
          path: 'setup',
          name: 'setup',
          component: () => import('@/pages/account/SetupPage.vue'),
          meta: { title: 'Administrator setup' },
        },
        {
          // ?returnUrl= is where to go after signing in (see afterSignIn).
          path: 'login',
          name: 'login',
          component: () => import('@/pages/account/LoginPage.vue'),
          meta: { title: 'Sign in', guestOnly: true },
        },
        {
          path: 'register',
          name: 'register',
          component: () => import('@/pages/account/RegisterPage.vue'),
          meta: { title: 'Sign up', guestOnly: true },
        },
        {
          path: 'forgot-password',
          name: 'forgot-password',
          component: () => import('@/pages/account/ForgotPasswordPage.vue'),
          meta: { title: 'Forgot password' },
        },
        {
          // ?code= comes from the link in the reset mail.
          path: 'reset-password',
          name: 'reset-password',
          component: () => import('@/pages/account/ResetPasswordPage.vue'),
          meta: { title: 'Reset password' },
        },
        {
          path: 'email-confirmed',
          name: 'email-confirmed',
          component: () => import('@/pages/account/EmailConfirmedPage.vue'),
          meta: { title: 'Email confirmed' },
        },
      ],
    },
  ],
})

/**
 * Where a user goes once signed in: to `returnUrl` when it is a path on this site (see
 * safeReturnUrl), otherwise to the applications page. Returns the route to open. For an address
 * the Portal answers itself (/swagger) it starts a full page load instead and returns false.
 */
export function afterSignIn(returnUrl: unknown): RouteLocationRaw | false {
  const target = safeReturnUrl(returnUrl)

  if (!target) {
    return { name: 'apps' }
  }

  if (!isServerPath(target)) {
    return target
  }

  location.assign(target)
  return false
}

// The one place that decides which screens open without a session.
router.beforeEach((to) => {
  if (bootstrapRequired()) {
    return to.name === 'setup' ? true : { name: 'setup' }
  }

  if (to.name === 'setup') {
    return { name: currentSession() ? 'apps' : 'login' }
  }

  const signedIn = currentSession() !== undefined

  if (to.meta.public) {
    return to.meta.guestOnly && signedIn ? afterSignIn(to.query.returnUrl) : true
  }

  if (signedIn) {
    return true
  }

  // Sign in first, then come back to this screen. The plain applications page is where signing in
  // leads anyway.
  return { name: 'login', query: to.fullPath === '/apps' ? {} : { returnUrl: to.fullPath } }
})

/**
 * Sets the browser tab title. Call it again after the instance name changed (the settings screen
 * does) or became known (AuthLayout does, on a public screen).
 */
export function setTitle(title: string | undefined): void {
  const instance = currentSession()?.InstanceTitle ?? loadedInstanceTitle() ?? 'Apilane'
  document.title = title ? `${title} · ${instance}` : instance
}

// A navigation that was stopped (a leave guard said no) keeps the title of the screen that stayed.
router.afterEach((to, _from, failure) => {
  if (!failure) {
    setTitle(to.meta.title)
  }
})

// A tab opened before a deploy asks for chunks that no longer exist: load the target page afresh.
// The second condition stops a reload loop when the chunk is still missing afterwards.
router.onError((error, to) => {
  const current = location.pathname + location.search + location.hash

  if (/dynamically imported module|module script failed/i.test(String(error)) && to.fullPath !== current) {
    location.assign(to.fullPath)
  }
})
