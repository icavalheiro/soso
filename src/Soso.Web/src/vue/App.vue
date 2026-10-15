<script setup lang="ts">
import { MantineProvider } from '@mantine-vue/core';
import { Notifications } from '@mantine-vue/notifications';
import { vueTheme } from './theme';

// Placeholder shell: providers and theme are final, but views are not ported
// yet. The Workspace port (ticket subtask 5) replaces this content, and the
// cutover (subtask 9) points index.html at vue/main.ts.
</script>

<template>
  <MantineProvider :theme="vueTheme" default-color-scheme="auto">
    <Notifications position="bottom-right" />
    <p>Vue foundation scaffold — views land here.</p>
  </MantineProvider>
</template>
