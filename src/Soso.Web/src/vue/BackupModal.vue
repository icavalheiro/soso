<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ActionIcon, Alert, Button, CopyButton, Group, Modal, Stack, Text, TextInput, Tooltip } from '@mantine-vue/core';
import { AlertCircle, Check, Cloud, Copy, Unplug } from 'lucide-vue-next';
import { api } from '../api';
import { reportError } from './feedback';
import { useLanguage } from './language';

type BackupStatus = { connected: boolean; appKey: string; redirectUri: string; lastBackupAt: string | null; lastError: string | null; };

const props = defineProps<{ result: string | null; onClose: () => void }>();
const { t } = useLanguage();
const status = ref<BackupStatus | null>( null );
const appKey = ref( '' );
const busy = ref( false );
const failed = ref( props.result === 'cancelled' || props.result === 'error' );
const message = ref(
    props.result === 'connected' ? t( 'Dropbox connected successfully.' )
    : props.result === 'cancelled' ? t( 'Dropbox authorization was cancelled.' )
    : props.result === 'error' ? t( 'Dropbox connection failed.' ) : '',
);

function missingWriteScope (): boolean
{
    return [ message.value, status.value?.lastError ?? '' ].some( value => value.includes( 'files.content.write' ) || value.includes( 'missing_scope' ) );
}

async function refresh ()
{
    const next = await api<BackupStatus>( '/admin/backup' );
    status.value = next;
    appKey.value = next.appKey;
}

onMounted( () =>
{
    let active = true;
    api<BackupStatus>( '/admin/backup' ).then( next =>
    {
        if ( active )
        {
            status.value = next;
            appKey.value = next.appKey;
        }
    } ).catch( reportError );
    return;
} );

async function connect ()
{
    busy.value = true;
    message.value = '';
    failed.value = false;
    try
    {
        const key = appKey.value.trim();
        if ( !key )
        {
            throw new Error( t( 'Enter the Dropbox app key before connecting.' ) );
        }
        if ( status.value?.appKey !== key )
        {
            await api( '/admin/backup', 'PUT', { appKey: key } );
        }
        const authorization = await api<{ url: string; }>( '/admin/backup/dropbox/authorize' );
        window.location.assign( authorization.url );
    }
    catch ( error )
    {
        failed.value = true;
        message.value = error instanceof Error ? error.message : t( 'Dropbox connection failed.' );
        reportError( error );
        busy.value = false;
    }
}

async function disconnect ()
{
    busy.value = true;
    message.value = '';
    failed.value = false;
    try
    {
        await api( '/admin/backup', 'DELETE' );
        await refresh();
        message.value = t( 'Dropbox disconnected.' );
    }
    catch ( error )
    {
        failed.value = true;
        message.value = error instanceof Error ? error.message : t( 'Dropbox disconnection failed.' );
        reportError( error );
    }
    finally
    {
        busy.value = false;
    }
}

async function runBackup ()
{
    busy.value = true;
    message.value = '';
    failed.value = false;
    try
    {
        await api( '/admin/backup/run', 'POST' );
        await refresh();
        message.value = t( 'Backup completed.' );
    }
    catch ( error )
    {
        failed.value = true;
        message.value = error instanceof Error ? error.message : t( 'Backup failed. Check the Dropbox connection and try again.' );
        reportError( error );
        try
        {
            await refresh();
        }
        catch ( refreshError )
        {
            reportError( refreshError );
        }
    }
    finally
    {
        busy.value = false;
    }
}
</script>

<template>
  <Modal :opened="true" :title="t('Backup')" size="md" centered @close="onClose">
    <Stack>
      <div class="dropbox-panel">
        <span class="dropbox-mark">
          <svg width="22" height="22" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M6 1.807L0 5.629l6 3.822 6.001-3.822L6 1.807zM18 1.807l-6 3.822 6 3.822 6-3.822-6-3.822zM0 13.274l6 3.822 6.001-3.822L6 9.452l-6 3.822zM18 9.452l-6 3.822 6 3.822 6-3.822-6-3.822zM6 18.371l6.001 3.822 6-3.822-6-3.822L6 18.371z" /></svg>
        </span>
        <div>
          <Text :fw="600">Dropbox</Text>
          <Text size="sm" c="dimmed">{{ status?.connected ? t('Dropbox connected') : t('Dropbox not connected') }}</Text>
        </div>
      </div>
      <Text size="sm" c="dimmed">{{ t('Connect a Dropbox app to store automatic database backups. Backups run 10 minutes after changes stop. If changes continue, a backup runs after 1 hour without a saved backup.') }}</Text>
      <Text size="sm" c="dimmed">{{ t('Create an app in the Dropbox App Console, add the redirect URI below under OAuth redirect URIs, and enable files.content.write.') }}</Text>
      <Alert v-if="missingWriteScope()" color="red" :title="t('Dropbox write permission required')" role="alert">
        <template #icon><AlertCircle :size="18" /></template>
        <Text size="sm">{{ t('Dropbox rejected this connection because the app does not include files.content.write. In the Dropbox App Console, enable files.content.write, then reconnect Dropbox.') }}</Text>
      </Alert>
      <Text v-else size="sm" c="dimmed">{{ t('The Dropbox app needs the files.content.write permission. Enable it in the Dropbox App Console before connecting, or reconnect after changing it.') }}</Text>
      <TextInput :label="t('Dropbox app key')" required maxlength="200" v-model="appKey" />
      <TextInput v-if="status" :label="t('Dropbox redirect URI')" readonly :model-value="status.redirectUri">
        <template #rightSection>
          <CopyButton :value="status.redirectUri" :timeout="1500">
            <template #default="{ copied, copy }">
              <Tooltip :label="t(copied ? 'Copied' : 'Copy redirect URI')">
                <ActionIcon :aria-label="t('Copy redirect URI')" variant="subtle" color="gray" @click="copy()">
                  <Check v-if="copied" :size="15" />
                  <Copy v-else :size="15" />
                </ActionIcon>
              </Tooltip>
            </template>
          </CopyButton>
        </template>
      </TextInput>
      <Text v-if="status?.lastBackupAt" size="sm">{{ t('Last backup') }}: {{ new Date(status.lastBackupAt).toLocaleString() }}</Text>
      <Text v-if="status?.lastError && !missingWriteScope()" size="sm" c="red" role="alert">{{ t('Last backup error') }}: {{ status.lastError }}</Text>
      <Text v-if="message" size="sm" :c="failed ? 'red' : 'teal'" :role="failed ? 'alert' : 'status'">{{ message }}</Text>
      <Group justify="space-between" wrap="wrap">
        <Group>
          <Button v-if="status?.connected" type="button" variant="light" color="red" :disabled="busy" @click="disconnect()">
            <template #leftSection><Unplug :size="15" /></template>
            {{ t('Disconnect') }}
          </Button>
          <Button v-if="status?.connected" type="button" variant="light" :loading="busy" @click="runBackup()">
            <template #leftSection><Cloud :size="15" /></template>
            {{ t('Back up now') }}
          </Button>
        </Group>
        <Button type="button" class="dropbox-connect" :loading="busy" @click="connect()">
          <template #leftSection>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M6 1.807L0 5.629l6 3.822 6.001-3.822L6 1.807zM18 1.807l-6 3.822 6 3.822 6-3.822-6-3.822zM0 13.274l6 3.822 6.001-3.822L6 9.452l-6 3.822zM18 9.452l-6 3.822 6 3.822 6-3.822-6-3.822zM6 18.371l6.001 3.822 6-3.822-6-3.822L6 18.371z" /></svg>
          </template>
          {{ status?.connected ? t('Reconnect Dropbox') : t('Connect Dropbox') }}
        </Button>
      </Group>
    </Stack>
  </Modal>
</template>
