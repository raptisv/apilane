import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

// The Portal (ASP.NET): it serves the API, /swagger and, until they are removed, the classic Razor pages.
const portal = process.env.PORTAL_URL ?? 'http://localhost:5000'

export default defineConfig({
  // The UI lives under /ui/ in the Portal, in development and in production alike.
  base: '/ui/',
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    strictPort: true,
    // Everything that is not the UI itself goes to the Portal. The browser only ever talks to
    // this dev server, so the login cookie and the API work as in production.
    proxy: { '^/(?!ui(/|$)).*': portal },
  },
  build: {
    // The Portal serves its wwwroot. The Docker image copies this folder out of the Node stage.
    outDir: '../Apilane.Portal/wwwroot/ui',
    emptyOutDir: true,
  },
})
