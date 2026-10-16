<script setup lang="ts">
import { onMounted, onUnmounted, ref, watch } from 'vue';
import { ActionIcon, Avatar, Button, CopyButton, FileButton, Group, Modal, PasswordInput, SegmentedControl, Stack, Tabs, TabsList, TabsPanel, TabsTab, Text, Textarea, TextInput, Tooltip, useMantineColorScheme } from '@mantine-vue/core';
import { Check, Copy, ImagePlus, KeyRound, Plus, Save, Settings, Shield, Unplug } from 'lucide-vue-next';
import { api, imageUrl } from '../api';
import type { Account, Board, Token } from '../api';
import { reportError } from './feedback';
import AvatarCropModal from './AvatarCropModal.vue';
import TokenBoardAccess from './TokenBoardAccess.vue';
import { useLanguage } from './language';

const props = defineProps<{
    account: Account;
    onChange: ( account: Account ) => void;
    onClose: () => void;
}>();
const { t } = useLanguage();
const name = ref( props.account.name );
const theme = ref( props.account.theme );
const settings = ref( props.account.settings ?? '' );
const currentPassword = ref( '' );
const newPassword = ref( '' );
const tokens = ref<Token[]>( [] );
const boards = ref<Board[]>( [] );
const tokenName = ref( '' );
const secret = ref( '' );
const busy = ref( false );
const photo = ref<string | null>( null );
const photoReset = ref< ( () => void ) | null >( null );
const { setColorScheme } = useMantineColorScheme();
const mcpEndpoint = `${ window.location.origin }/mcp`;

function onPhotoSelected ( file: File | File[] | null )
{
    photo.value = file ? URL.createObjectURL( file as File ) : null;
    photoReset.value?.();
}

async function createMcpToken ()
{
    await action( async () =>
    {
        const result = await api<{ token: string }>( '/auth/tokens', 'POST', { name: tokenName.value } );
        secret.value = result.token;
        tokenName.value = '';
        tokens.value = await api<Token[]>( '/auth/tokens' );
    } );
}

async function changePassword ()
{
    await action( async () =>
    {
        await api( '/auth/password', 'PUT', { currentPassword: currentPassword.value, newPassword: newPassword.value } );
        window.dispatchEvent( new Event( 'soso-session-expired' ) );
        props.onClose();
    } );
}

async function saveCroppedPhoto ( cropped: File )
{
    const form = new FormData();
    form.append( 'file', cropped );
    props.onChange( await api<Account>( '/auth/avatar', 'POST', form ) );
    photo.value = null;
}

onMounted( () =>
{
    void api<Token[]>( '/auth/tokens' ).then( result => { tokens.value = result; } ).catch( reportError );
    void api<Board[]>( '/boards' ).then( result => { boards.value = result; } ).catch( reportError );
} );

watch( photo, ( _current, previous ) =>
{
    if ( previous )
    {
        URL.revokeObjectURL( previous );
    }
} );
onUnmounted( () =>
{
    if ( photo.value )
    {
        URL.revokeObjectURL( photo.value );
    }
} );

async function action ( work: () => Promise<void> )
{
    busy.value = true;
    try
    {
        await work();
    }
    catch ( error )
    {
        reportError( error );
    }
    finally
    {
        busy.value = false;
    }
}
</script>

<template>
  <Modal :opened="true" :title="t('Profile & settings')" size="lg" centered @close="onClose">
    <Tabs default-value="profile">
      <TabsList mb="lg">
        <TabsTab value="profile">
          <template #leftSection><Settings :size="15" /></template>
          {{ t('Profile') }}
        </TabsTab>
        <TabsTab value="security">
          <template #leftSection><Shield :size="15" /></template>
          {{ t('Security') }}
        </TabsTab>
        <TabsTab value="mcp">
          <template #leftSection><Unplug :size="15" /></template>
          {{ t('MCP') }}
        </TabsTab>
      </TabsList>
      <TabsPanel value="profile">
        <form @submit.prevent="action(async () => { const updated = await api<Account>('/auth/profile', 'PUT', { name: name, theme: theme, settings: settings }); onChange(updated); setColorScheme(theme); onClose(); })">
          <Stack>
            <Group>
              <Avatar size="64" radius="xl" :src="imageUrl(account.avatarId)">{{ account.name.slice(0, 2) }}</Avatar>
              <FileButton
                :reset-ref="photoReset" accept="image/png,image/jpeg,image/webp"
                @change="onPhotoSelected"
              >
                <template #default="{ onClick }">
                  <Button variant="light" size="xs" :disabled="busy" @click="onClick()">
                    <template #leftSection><ImagePlus :size="15" /></template>
                    {{ t('Change photo') }}
                  </Button>
                </template>
              </FileButton>
            </Group>
            <TextInput :label="t('Name')" v-model="name" required maxlength="80" />
            <TextInput :label="t('Email')" :model-value="account.email" readonly />
            <Text size="sm" :fw="500">{{ t('Appearance') }}</Text>
            <SegmentedControl v-model="theme" :data="[ { label: t('Light'), value: 'light' }, { label: t('Dark'), value: 'dark' } ]" />
            <Textarea :label="t('Custom settings')" v-model="settings" maxlength="4000" :min-rows="4" />
            <Button type="submit" :loading="busy">
              <template #leftSection><Save :size="15" /></template>
              {{ t('Save profile') }}
            </Button>
          </Stack>
        </form>
      </TabsPanel>
      <TabsPanel value="security">
        <form @submit.prevent="changePassword()">
          <Stack>
            <PasswordInput :label="t('Current password')" autocomplete="current-password" required maxlength="128" v-model="currentPassword" />
            <PasswordInput :label="t('New password')" autocomplete="new-password" :description="t('14 characters minimum')" required minlength="14" maxlength="128" v-model="newPassword" />
            <Text size="xs" c="dimmed">{{ t('Changing your password ends all sessions and revokes all MCP tokens.') }}</Text>
            <Button type="submit" :loading="busy">
              <template #leftSection><KeyRound :size="15" /></template>
              {{ t('Change password') }}
            </Button>
          </Stack>
        </form>
      </TabsPanel>
      <TabsPanel value="mcp">
        <Stack>
          <TextInput :label="t('MCP endpoint')" readonly :model-value="mcpEndpoint" />
          <form @submit.prevent="createMcpToken()">
            <Group align="end">
              <TextInput :label="t('Token name')" required maxlength="80" v-model="tokenName" :style="{ flex: 1, minWidth: 140 }" />
              <Button type="submit" :loading="busy">
                <template #leftSection><Plus :size="15" /></template>
                {{ t('Create token') }}
              </Button>
            </Group>
          </form>
          <Stack v-if="secret" gap="xs">
            <Text size="xs" c="dimmed">{{ t('Copy this token now. It will not be shown again. Send it as Authorization: Bearer <token>.') }}</Text>
            <TextInput :label="t('New token')" readonly :model-value="secret">
              <template #rightSection>
                <CopyButton :value="secret">
                  <template #default="{ copied, copy }">
                    <Tooltip :label="t(copied ? 'Copied' : 'Copy token')">
                      <ActionIcon :aria-label="t('Copy token')" variant="subtle" @click="copy()">
                        <Check v-if="copied" :size="16" />
                        <Copy v-else :size="16" />
                      </ActionIcon>
                    </Tooltip>
                  </template>
                </CopyButton>
              </template>
            </TextInput>
          </Stack>
          <TokenBoardAccess
            v-for="token in tokens" :key="`${token.id}:${token.boardIds.join(',')}`"
            :token="token" :boards="boards" :busy="busy"
            :on-save="boardIds => { action(async () => { await api<Token>(`/auth/tokens/${token.id}/boards`, 'PUT', { boardIds }); tokens = await api<Token[]>('/auth/tokens'); }); }"
            :on-revoke="() => { action(async () => { await api(`/auth/tokens/${token.id}`, 'DELETE'); tokens = tokens.filter(item => item.id !== token.id); secret = ''; }); }"
          />
        </Stack>
      </TabsPanel>
    </Tabs>
  </Modal>
  <AvatarCropModal
    v-if="photo" :source="photo" :on-close="() => { photo = null; }"
    :on-save="saveCroppedPhoto"
  />
</template>
