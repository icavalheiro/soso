<script setup lang="ts">
import { computed } from 'vue';
import { ActionIcon, Stack, Text, Tooltip, ColorSwatch, useMantineTheme, useComputedColorScheme } from '@mantine-vue/core';
import { Check } from 'lucide-vue-next';
import { boardColors } from './board-icon-data';

const props = defineProps<{ value: string; onChange: ( value: string ) => void }>();
const theme = useMantineTheme();
const scheme = useComputedColorScheme( 'light' );
const shade = computed( () => scheme.value === 'dark' ? 4 : 7 );
</script>

<template>
  <Stack gap="6">
    <Text size="sm" :fw="500" id="board-color-label">Color</Text>
    <div class="board-color-picker" role="group" aria-labelledby="board-color-label">
      <Tooltip v-for="{ value: color, label } in boardColors" :key="color" :label="label">
        <ActionIcon
          type="button" :aria-label="`Board color: ${label}`" :aria-pressed="value === color"
          variant="default" :size="32" @click="onChange(color)"
        >
          <ColorSwatch :color="theme.colors[color][shade]" :size="24">
            <Check v-if="value === color" :size="15" :color="scheme === 'dark' ? theme.black : theme.white" />
          </ColorSwatch>
        </ActionIcon>
      </Tooltip>
    </div>
  </Stack>
</template>
