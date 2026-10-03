import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';

export default defineConfig( {
  plugins: [ react() ],
  server: {
    host: '127.0.0.1',
    proxy: {
      '/api': { target: 'https://localhost:7240', secure: false },
      '/mcp': { target: 'https://localhost:7240', secure: false },
    },
  },
} );
