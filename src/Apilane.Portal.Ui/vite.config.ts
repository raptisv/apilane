import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import vue from '@vitejs/plugin-vue'
import { defineConfig } from 'vite'

// Where the Portal (ASP.NET) runs while developing. Only the addresses under `portalPaths` are forwarded to it.
const portal = process.env.PORTAL_URL ?? 'http://localhost:5000'

// The first segment of every address the Portal answers itself. Keep it the same as _serverPaths
// in src/Apilane.Portal/Extensions/PortalApiDependencyInjection.cs and serverPaths in src/lib/returnUrl.ts.
const portalPaths = ['api', 'swagger', 'health', 'metrics']

export default defineConfig({
  // The UI is served from the site root, in development and in production alike.
  base: '/',
  plugins: [vue(), tailwindcss()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    strictPort: true,
    // The dev server answers every address of the UI itself and forwards only the Portal's own
    // addresses. The browser talks to one site, so the login cookie and the API work as in production.
    proxy: { [`^/(${portalPaths.join('|')})([/?]|$)`]: portal },
  },
  build: {
    // The Portal serves this folder at the site root. The Docker image copies it out of the Node stage.
    outDir: '../Apilane.Portal/wwwroot/ui',
    emptyOutDir: true,
  },
})
