import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath, URL } from 'node:url'

// https://vite.dev/config/
export default defineConfig({
  // Relative asset paths so the build works from any deploy location (subdirs, static hosts).
  base: './',
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    // The read-only viewer API (CodeReviewerAgent.Api) listens on 5180 in Development.
    proxy: {
      '/api': 'http://localhost:5180',
    },
  },
})
