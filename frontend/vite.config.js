import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/api": {
        target: "http://172.20.10.6:5000", // C# backend / gateway
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ""),
      },
      "/coach": {
        target: "http://10.0.41.139:8000", // laptop where the Python coach runs
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/coach/, ""),
      },
    },
  },
})