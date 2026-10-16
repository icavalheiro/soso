<script setup lang="ts">
import { MantineProvider } from '@mantine-vue/core';
import { Notifications } from '@mantine-vue/notifications';
import Workspace from './Workspace.vue';
import ColorSchemeSync from './ColorSchemeSync.vue';
import { vueTheme } from './theme';
</script>

<template>
  <MantineProvider :theme="vueTheme" default-color-scheme="auto">
    <ColorSchemeSync>
      <Notifications position="bottom-right" />
      <Workspace />
    </ColorSchemeSync>
  </MantineProvider>
</template>
