<script setup lang="ts">
import { onMounted, watch } from 'vue';
import { useMantineColorScheme } from '@mantine-vue/core';

// Mantine color-scheme persistence: the explicit choice
// survives reloads, while 'auto' keeps following the OS preference.
const STORAGE_KEY = 'mantine-color-scheme';
const { colorScheme, setColorScheme } = useMantineColorScheme();

onMounted( () =>
{
    const stored = localStorage.getItem( STORAGE_KEY );
    if ( stored === 'light' || stored === 'dark' || stored === 'auto' )
    {
        setColorScheme( stored );
    }
} );

watch( colorScheme, scheme =>
{
    localStorage.setItem( STORAGE_KEY, scheme );
} );
</script>

<template>
  <slot />
</template>
