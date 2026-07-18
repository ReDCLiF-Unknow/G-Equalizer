// @ts-check
import { defineConfig } from 'astro/config';

import tailwindcss from '@tailwindcss/vite';

// https://astro.build/config
export default defineConfig({
  site: 'https://redclif-unknow.github.io',
  base: '/G-Equalizer/',
  vite: {
    plugins: [tailwindcss()]
  }
});