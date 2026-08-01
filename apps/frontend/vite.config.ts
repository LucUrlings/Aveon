import { fileURLToPath } from 'node:url'
import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'
import { generateSeoFiles, removeGeneratedSeoFiles, replacePublicUrlPlaceholders } from './config/seoFiles'

// https://vite.dev/config/
export default defineConfig(({ command, mode }) => {
  const workspaceRoot = fileURLToPath(new URL('../..', import.meta.url))
  const environment = loadEnv(mode, workspaceRoot, '')
  const configuredPublicUrl = environment.AVEON_PUBLIC_URL?.trim()
  const publicUrl = configuredPublicUrl || (command === 'serve' ? 'http://localhost:5173' : null)
  const developmentApiTarget = environment.VITE_DEV_API_TARGET?.trim() || 'http://localhost:5210'
  const publicDirectory = fileURLToPath(new URL('./public', import.meta.url))

  if (publicUrl) generateSeoFiles(publicDirectory, publicUrl)
  else removeGeneratedSeoFiles(publicDirectory)

  return {
    envDir: workspaceRoot,
    plugins: [
      vue(),
      {
        name: 'aveon-runtime-seo',
        transformIndexHtml: (html) => publicUrl ? replacePublicUrlPlaceholders(html, publicUrl) : html,
      },
    ],
    server: {
      proxy: {
        '/api': {
          target: developmentApiTarget,
          changeOrigin: true,
        },
      },
    },
  }
})
