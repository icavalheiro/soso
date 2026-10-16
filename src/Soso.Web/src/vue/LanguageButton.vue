<script setup lang="ts">
import { ref } from 'vue';
import { Button, Popover, PopoverDropdown, PopoverTarget, Stack } from '@mantine-vue/core';
import { Languages } from 'lucide-vue-next';
import IconButton from './IconButton.vue';
import { useLanguage } from './language';

const { language, setLanguage, t } = useLanguage();
const opened = ref( false );
</script>

<template>
  <Popover v-model:opened="opened" position="bottom-end" with-arrow shadow="md">
    <PopoverTarget>
      <div :style="{ display: 'flex', alignItems: 'center' }">
        <IconButton :label="t('Choose language')" :expanded="opened" @click="opened = !opened">
          <Languages :size="15" />
        </IconButton>
      </div>
    </PopoverTarget>
    <PopoverDropdown :aria-label="t('Choose language')" p="xs">
      <Stack gap="4">
        <Button :variant="language === 'en' ? 'light' : 'subtle'" justify="flex-start" size="xs" @click="setLanguage('en'); opened = false;"><span aria-hidden="true">🇺🇸</span>&nbsp; {{ t('English') }}</Button>
        <Button :variant="language === 'pt-BR' ? 'light' : 'subtle'" justify="flex-start" size="xs" @click="setLanguage('pt-BR'); opened = false;"><span aria-hidden="true">🇧🇷</span>&nbsp; {{ t('Português (Brasil)') }}</Button>
        <Button :variant="language === 'es-MX' ? 'light' : 'subtle'" justify="flex-start" size="xs" @click="setLanguage('es-MX'); opened = false;"><span aria-hidden="true">🇲🇽</span>&nbsp; {{ t('Español (México)') }}</Button>
      </Stack>
    </PopoverDropdown>
  </Popover>
</template>
