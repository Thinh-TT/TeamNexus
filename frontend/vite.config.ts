import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  // Backend dev address. Override via VITE_DEV_API_TARGET (see .env.example).
  const apiTarget = env.VITE_DEV_API_TARGET ?? 'http://localhost:5000'

  return {
    plugins: [react()],
    server: {
      port: 5173,
      // Forward all /api/* calls to the ASP.NET Core backend during dev so the
      // browser only talks to this origin (no CORS, cookies work as same-origin).
      proxy: {
        '/api': {
          target: apiTarget,
          changeOrigin: true,
        },
      },
    },
  }
})
