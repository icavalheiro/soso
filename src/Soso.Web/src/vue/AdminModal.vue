<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { ActionIcon, Avatar, Badge, Button, Checkbox, Group, Modal, MultiSelect, PasswordInput, Stack, Tabs, TabsList, TabsPanel, TabsTab, Text, TextInput, Tooltip } from '@mantine-vue/core';
import { Plus, Save, Settings } from 'lucide-vue-next';
import { api, imageUrl } from '../api';
import type { Account, Board } from '../api';
import { reportError } from './feedback';
import { useLanguage } from './language';

const props = defineProps<{ onClose: () => void }>();
const { t } = useLanguage();
const accounts = ref<Account[]>( [] );
const boards = ref<Board[]>( [] );
const name = ref( '' );
const email = ref( '' );
const password = ref( '' );
const isAdmin = ref( false );
const busy = ref( false );
const selected = ref<Account | null>( null );
const disabled = ref( false );
const boardIds = ref<string[]>( [] );
const resetPassword = ref( '' );

onMounted( () =>
{
    void api<Account[]>( '/admin/accounts' ).then( result => { accounts.value = result; } ).catch( reportError );
    void api<Board[]>( '/boards' ).then( result => { boards.value = result; } ).catch( reportError );
} );

async function create ( event: Event )
{
    event.preventDefault();
    busy.value = true;
    try
    {
        const result = await api<Account>( '/admin/accounts', 'POST', { name: name.value, email: email.value, password: password.value, isAdmin: isAdmin.value } );
        accounts.value = [ ...accounts.value, result ];
        name.value = '';
        email.value = '';
        password.value = '';
        isAdmin.value = false;
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

async function update ( event: Event )
{
    event.preventDefault();
    if ( !selected.value )
    {
        return;
    }
    busy.value = true;
    try
    {
        const updated = await api<Account>( `/admin/accounts/${ selected.value.id }`, 'PUT', { disabled: disabled.value, password: resetPassword.value || null, boardIds: boardIds.value } );
        accounts.value = accounts.value.map( account => account.id === updated.id ? updated : account );
        selected.value = null;
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

function editAccount ( account: Account )
{
    selected.value = account;
    disabled.value = account.disabled;
    boardIds.value = account.boardIds ?? boards.value.filter( board => board.ownerId === account.id || board.members.includes( account.id ) ).map( board => board.id );
    resetPassword.value = '';
}
</script>

<template>
  <Modal :opened="true" :title="t('Account management')" size="lg" centered @close="onClose">
    <Tabs default-value="accounts">
      <TabsList mb="lg">
        <TabsTab value="accounts">{{ t('Accounts') }}</TabsTab>
        <TabsTab value="create">
          <template #leftSection><Plus :size="15" /></template>
          {{ t('New account') }}
        </TabsTab>
      </TabsList>
      <TabsPanel value="accounts">
        <Stack gap="0">
          <Group v-for="account in accounts" :key="account.id" justify="space-between" class="account-row" wrap="nowrap">
            <Group gap="sm" wrap="nowrap" :style="{ minWidth: 0 }">
              <Avatar :src="imageUrl(account.avatarId)" radius="xl" size="32px">{{ account.name.slice(0, 1) }}</Avatar>
              <div class="account-info">
                <Text size="sm" :fw="500" truncate>{{ account.name }}</Text>
                <Text size="xs" c="dimmed" truncate>{{ account.email }}</Text>
              </div>
            </Group>
            <Group gap="xs" wrap="nowrap">
              <Badge size="xs" :color="account.disabled ? 'red' : account.isAdmin ? 'teal' : 'gray'" variant="light">
                {{ account.disabled ? t('Disabled') : account.isAdmin ? t('Admin') : t('Member') }}
              </Badge>
              <Tooltip :label="t('Manage account')">
                <ActionIcon :aria-label="`${t('Manage account')}: ${account.name}`" variant="subtle" @click="editAccount(account)">
                  <Settings :size="16" />
                </ActionIcon>
              </Tooltip>
            </Group>
          </Group>
        </Stack>
      </TabsPanel>
      <TabsPanel value="create">
        <form @submit="create">
          <Stack>
            <TextInput :label="t('Name')" required maxlength="80" v-model="name" />
            <TextInput :label="t('Email')" type="email" required maxlength="254" v-model="email" />
            <PasswordInput :label="t('Initial password')" autocomplete="new-password" :description="t('14 characters minimum')" required minlength="14" maxlength="128" v-model="password" />
            <Checkbox :label="t('System administrator')" v-model="isAdmin" />
            <Button type="submit" :loading="busy">
              <template #leftSection><Plus :size="15" /></template>
              {{ t('Create account') }}
            </Button>
          </Stack>
        </form>
      </TabsPanel>
    </Tabs>
    <Modal v-if="selected" :opened="true" :title="`${t('Manage account')}: ${selected?.name ?? ''}`" centered @close="selected = null">
      <form @submit="update">
        <Stack>
          <MultiSelect
            :label="t('Boards this user can access')" :aria-label="t('Boards this user can access')"
            :placeholder="t('No boards selected')" searchable clearable
            v-model="boardIds" :data="boards.map(board => ({ value: board.id, label: board.name }))"
          />
          <Checkbox :label="t('Disabled account')" v-model="disabled" />
          <PasswordInput :label="t('Reset password')" autocomplete="new-password" :description="t('Leave blank to keep the current password')" minlength="14" maxlength="128" v-model="resetPassword" />
          <Group justify="flex-end">
            <Button variant="default" @click="selected = null">{{ t('Cancel') }}</Button>
            <Button type="submit" :loading="busy">
              <template #leftSection><Save :size="15" /></template>
              {{ t('Save account') }}
            </Button>
          </Group>
        </Stack>
      </form>
    </Modal>
  </Modal>
</template>
