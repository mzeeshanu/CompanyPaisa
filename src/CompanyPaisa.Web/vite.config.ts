import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// In development the React dev server runs on :5173 and forwards API calls to the ASP.NET Core API.
// `npm run build` writes the finished site into the API's wwwroot, so production is one app.
const apiTarget = process.env.COMPANYPAISA_API_URL ?? 'http://localhost:5104';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': { target: apiTarget, changeOrigin: true },
      '/health': { target: apiTarget, changeOrigin: true },
    },
  },
  build: {
    outDir: '../CompanyPaisa.Api/wwwroot',
    emptyOutDir: true,
  },
});
