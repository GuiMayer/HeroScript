import { defineConfig } from 'vite'

const engine = { target: process.env.HEROSCRIPT_EDITOR_API ?? 'http://127.0.0.1:5260' }

export default defineConfig({
  base: '/editor/',
  server: {
    host: '127.0.0.1',
    port: 5174,
    strictPort: true,
    proxy: {
      '/api': engine,
      '/docs/api': engine,
      '/openapi': engine,
    },
  },
})
