import { fileURLToPath, URL } from 'node:url'
import type { ServerResponse } from 'node:http'

import { defineConfig } from 'vite'
import type { PluginOption } from 'vite'
import vue from '@vitejs/plugin-vue'
// Visualizer for bundle analysis. We cast to any when injecting to avoid
// TypeScript plugin signature mismatches between rollup and vite types.
import { visualizer } from 'rollup-plugin-visualizer'

// https://vite.dev/config/
export default defineConfig(({ mode }) => ({
  plugins: [
    vue(),
    // Generate a static treemap report after build
    // cast to any to satisfy TypeScript when mixing rollup plugin types with Vite
  // cast plugin to any to avoid Vite/TS signature issues
  // Visualizer returns a Rollup plugin. Cast via unknown -> Plugin to avoid explicit `any`.
  (visualizer({ filename: 'dist/stats.html', title: 'Listenarr bundle analysis', open: false }) as unknown as PluginOption),
  ],
  build: {
    // Generate sourcemaps for bundle analysis tools (source-map-explorer)
    sourcemap: true,
    ...(mode === 'production' ? { minify: 'esbuild' as const } : {}),
  },
  ...(mode === 'production'
    ? {
        esbuild: {
          // Remove console.log and debugger statements from production builds
          drop: ['console', 'debugger'],
        },
      }
    : {}),
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    },
  },
  server: {
    // Use a fixed port so local development URLs remain stable
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:4545',
        changeOrigin: true,
        // Rewrite cookie domains coming from the backend so the browser will
        // accept Set-Cookie when the backend sets cookies for its own host.
        // Use the object form which is more explicit and reliable across
        // environments. Also rewrite path to '/' to ensure cookie applies.
        cookieDomainRewrite: { '*': '' },
        cookiePathRewrite: '/'
        ,
        // Ensure the original Cookie header from the browser is forwarded to
        // the backend. Some proxy environments do not forward cookies by
        // default; adding this configure hook forces the header through.
        configure: (proxy) => {
          if (proxy && typeof proxy.on === 'function') {
            proxy.on('error', (err, _req, res) => {
              if ((err as NodeJS.ErrnoException).code === 'ECONNREFUSED') {
                // API not ready yet — return 503 silently instead of logging to console
                try {
                  if (res && typeof (res as ServerResponse).writeHead === 'function') {
                    const httpRes = res as ServerResponse
                    if (!httpRes.headersSent) {
                      httpRes.writeHead(503, { 'Content-Type': 'application/json' })
                      httpRes.end(JSON.stringify({ message: 'API is starting, please retry.' }))
                    }
                  }
                } catch { /* ignore */ }
                return
              }
            })
            proxy.on('proxyReq', (proxyReq, req) => {
              try {
                const origCookie = req && req.headers && (req.headers['cookie'] || req.headers.cookie)
                if (origCookie) {
                  proxyReq.setHeader('cookie', origCookie)
                }
              } catch {}
            })
          }
        }
      },
      '/hubs': {
        target: 'http://localhost:4545',
        changeOrigin: true,
        ws: true,
        configure: (proxy) => {
          if (proxy && typeof proxy.on === 'function') {
            proxy.on('error', (err) => {
              if ((err as NodeJS.ErrnoException).code !== 'ECONNREFUSED') {
                console.error('[vite proxy /hubs]', err)
              }
              // ECONNREFUSED during startup — ignore silently
            })
          }
        }
      }
    }
  }
}))
