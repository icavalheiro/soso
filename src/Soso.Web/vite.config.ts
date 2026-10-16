import vue from '@vitejs/plugin-vue';
import { defineConfig } from 'vite';

export default defineConfig( {
  plugins: [ vue() ],
  server: {
    host: '127.0.0.1',
    proxy: {
      '/api': { target: 'https://localhost:7240', secure: false, ws: true, rewriteWsOrigin: true },
      '/mcp': { target: 'https://localhost:7240', secure: false },
    },
  },
} );
