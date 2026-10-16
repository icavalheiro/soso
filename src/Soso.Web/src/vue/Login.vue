<script setup lang="ts">
import { ref } from 'vue';
import { Button, Group, PasswordInput, Stack, Text, TextInput } from '@mantine-vue/core';
import { Moon, Sun } from 'lucide-vue-next';
import { useComputedColorScheme, useMantineColorScheme } from '@mantine-vue/core';
import { api, refreshCsrf } from '../api';
import type { Account } from '../api';
import IconButton from './IconButton.vue';
import LanguageButton from './LanguageButton.vue';
import { useLanguage } from './language';

const props = defineProps<{ onLogin: ( account: Account ) => void }>();
const { t } = useLanguage();
const { setColorScheme } = useMantineColorScheme();
const colorScheme = useComputedColorScheme( 'light', { getInitialValueInEffect: false } );
const email = ref( '' );
const password = ref( '' );
const busy = ref( false );
const error = ref( '' );

async function submit ( event: Event )
{
    event.preventDefault();
    busy.value = true;
    error.value = '';
    try
    {
        await refreshCsrf();
        const account = await api<Account>( '/auth/login', 'POST', { email: email.value, password: password.value } );
        await refreshCsrf();
        props.onLogin( account );
    }
    catch ( failure )
    {
        error.value = failure instanceof Error ? failure.message : t( 'Unable to sign in.' );
    }
    finally
    {
        busy.value = false;
    }
}
</script>

<template>
  <main class="login-page">
    <div class="login-brand">
      <img src="/logo.png" alt="Sosô" />
      <h1>Sosô</h1>
      <Group gap="4" wrap="nowrap">
        <LanguageButton />
        <IconButton :label="t(colorScheme === 'dark' ? 'Light theme' : 'Dark theme')" @click="setColorScheme(colorScheme === 'dark' ? 'light' : 'dark')">
          <Sun v-if="colorScheme === 'dark'" :size="15" />
          <Moon v-else :size="15" />
        </IconButton>
      </Group>
    </div>
    <form class="login-form" @submit="submit">
      <h2>{{ t('Sign in') }}</h2>
      <Stack gap="md">
        <TextInput :label="t('Email')" type="email" autocomplete="username" required maxlength="254" v-model="email" />
        <PasswordInput :label="t('Password')" autocomplete="current-password" required maxlength="128" v-model="password" />
        <Text v-if="error" role="alert" c="red" size="sm">{{ error }}</Text>
        <Button type="submit" :loading="busy">{{ t('Sign in') }}</Button>
      </Stack>
    </form>
  </main>
</template>
