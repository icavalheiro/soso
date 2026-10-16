<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import { Avatar, AvatarGroup, Badge, Button, Group, Loader, Modal, Select, Stack, Text, Tooltip, useComputedColorScheme, useMantineColorScheme } from '@mantine-vue/core';
import { Archive, Columns3, LogOut, Moon, PanelLeftClose, PanelLeftOpen, Plus, Settings, SlidersHorizontal, Sun, Users, DatabaseBackup } from 'lucide-vue-next';
import { api, ApiError, refreshCsrf, imageUrl, ticketBody, ticketKey, tags, connectBoardEvents, boardPath, slugFromPath } from '../api';
import type { Account, Board, BoardData, Ticket } from '../api';
import { reportError } from './feedback';
import BoardIcon from './BoardIcon.vue';
import NewTicketModal from './NewTicketModal.vue';
import SearchInput from './SearchInput.vue';
import IconButton from './IconButton.vue';
import LanguageButton from './LanguageButton.vue';
import Login from './Login.vue';
import Kanban from './Kanban.vue';
import TicketModal from './TicketModal.vue';
import BoardModal from './BoardModal.vue';
import ProfileModal from './ProfileModal.vue';
import AdminModal from './AdminModal.vue';
import BackupModal from './BackupModal.vue';
import ArchiveModal from './ArchiveModal.vue';
import { useLanguage } from './language';

const account = ref<Account | null>( null );
const { t } = useLanguage();
const booting = ref( true );
const boards = ref<Board[]>( [] );
const activeId = ref( '' );
const loadedData = ref<BoardData | null>( null );
const data = computed( () => loadedData.value?.board.id === activeId.value ? loadedData.value : null );
const accountId = computed( () => account.value?.id );
const accountTheme = computed( () => account.value?.theme );
const collapsed = ref( ( () =>
{
    const stored = localStorage.getItem( 'soso-sidebar-collapsed' );
    return stored === null ? window.innerWidth < 768 : stored === 'true';
} )() );
const query = ref( '' );
const searchBox = ref<{ focus: () => void } | null>( null );
const priority = ref( 'all' );
const tagFilter = ref<string[]>( [] );
const assignee = ref( 'all' );
const filtersOpen = ref( false );
const archive = ref( false );
const selected = ref<Ticket | null>( null );
const createdTicketId = ref<string | null>( null );
const boardModal = ref<'create' | 'edit' | null>( null );
const profile = ref( false );
const admin = ref( false );
const backup = ref( new URLSearchParams( window.location.search ).has( 'dropbox' ) );
const backupResult = ref<string | null>( new URLSearchParams( window.location.search ).get( 'dropbox' ) );
const newColumn = ref<string | null>( null );
const boardLoading = ref( false );
let requestSync: ( boardId: string | null ) => void = () => {};
const { setColorScheme } = useMantineColorScheme();
const colorScheme = useComputedColorScheme( 'light', { getInitialValueInEffect: false } );

function publishQuery ( value: string )
{
    query.value = value;
}

let stopSessionEffect: () => void = () => {};
onMounted( () =>
{
    let alive = true;
    api<Account>( '/auth/me' ).then( async user =>
    {
        await refreshCsrf();
        if ( alive )
        {
            account.value = user;
        }
    } ).catch( error =>
    {
        const expected = error instanceof ApiError && error.status === 401;
        if ( !expected )
        {
            reportError( error );
        }
    } ).finally( () => { if ( alive ) { booting.value = false; } } );
    const expire = () =>
    {
        account.value = null;
        loadedData.value = null;
        boards.value = [];
        selected.value = null;
        profile.value = false;
        admin.value = false;
        backup.value = false;
        backupResult.value = null;
        boardModal.value = null;
    };
    window.addEventListener( 'soso-session-expired', expire );
    if ( new URLSearchParams( window.location.search ).has( 'dropbox' ) )
    {
        window.history.replaceState( null, '', window.location.pathname + window.location.hash );
    }
    stopSessionEffect = () =>
    {
        alive = false;
        window.removeEventListener( 'soso-session-expired', expire );
    };
} );
onUnmounted( () => { stopSessionEffect(); } );

watch( accountId, id =>
{
    if ( !id )
    {
        return;
    }
    let alive = true;
    api<Board[]>( '/boards' ).then( result =>
    {
        if ( !alive )
        {
            return;
        }
        boards.value = result.map( board =>
        {
            const current = boards.value.find( item => item.id === board.id );
            return current && current.revision > board.revision ? current : board;
        } );
        const stored = localStorage.getItem( `soso-board-${ id }` );
        const slug = slugFromPath();
        const fromSlug = slug === null ? undefined : result.find( board => board.slug === slug );
        const selectedBoard = fromSlug ?? ( slug === null ? result.find( board => board.id === stored ) ?? result[ 0 ] : undefined );
        if ( !( activeId.value && result.some( board => board.id === activeId.value ) ) )
        {
            activeId.value = selectedBoard?.id ?? '';
        }
    } ).catch( reportError );
    return;
} );

watch( accountTheme, theme =>
{
    if ( theme )
    {
        setColorScheme( theme );
    }
} );

function handleShortcut ( event: KeyboardEvent )
{
    if ( ( event.ctrlKey || event.metaKey ) && event.key.toLowerCase() === 'f' )
    {
        event.preventDefault();
        if ( !selected.value )
        {
            searchBox.value?.focus();
        }
    }
}

onMounted( () => { window.addEventListener( 'keydown', handleShortcut ); } );
onUnmounted( () => { window.removeEventListener( 'keydown', handleShortcut ); } );

watch( [ activeId, accountId ], ( [ currentId, currentAccount ] ) =>
{
    if ( !currentId || !currentAccount )
    {
        return;
    }
    localStorage.setItem( `soso-board-${ currentAccount }`, currentId );
    requestSync( currentId );
} );

watch( accountId, ( id, _previous, onCleanup ) =>
{
    if ( !id )
    {
        return;
    }
    let alive = true;
    const pending = new Set<string>();
    let pendingAll = false;
    let running = false;
    let rerun = false;
    let retryTimer = 0;
    let retryAttempt = 0;
    let retryNeeded = false;
    const scheduleRetry = () =>
    {
        if ( !alive || retryTimer || retryAttempt >= 5 )
        {
            return;
        }
        const delay = Math.min( 1_000 * 2 ** retryAttempt++, 15_000 );
        retryTimer = window.setTimeout( () =>
        {
            retryTimer = 0;
            pendingAll = true;
            void drain();
        }, delay );
    };
    const mergeData = ( incoming: BoardData ): BoardData =>
    {
        const current = loadedData.value;
        const merged = current?.board.id !== incoming.board.id ? incoming : {
            ...incoming,
            board: current.board.revision > incoming.board.revision ? current.board : incoming.board,
            tickets: incoming.tickets.map( ticket =>
            {
                const existing = current.tickets.find( item => item.id === ticket.id );
                return existing && existing.revision > ticket.revision ? existing : ticket;
            } ),
        };
        loadedData.value = merged;
        return merged;
    };
    const drain = async (): Promise<void> =>
    {
        if ( running )
        {
            rerun = true;
            return;
        }
        running = true;
        try
        {
            do
            {
                rerun = false;
                const refreshAll = pendingAll;
                const boardIds = new Set( pending );
                pendingAll = false;
                pending.clear();
                if ( refreshAll || boardIds.size > 0 )
                {
                    try
                    {
                        const result = await api<Board[]>( '/boards' );
                        if ( alive )
                        {
                            boards.value = result.map( board =>
                            {
                                const existing = boards.value.find( item => item.id === board.id );
                                return existing && existing.revision > board.revision ? existing : board;
                            } );
                        }
                    }
                    catch ( error )
                    {
                        retryNeeded = true;
                        if ( alive )
                        {
                            reportError( error );
                        }
                    }
                }
                const activeBoardId = activeId.value;
                const shouldReadActive = activeBoardId && ( refreshAll || boardIds.has( activeBoardId ) );
                if ( alive && shouldReadActive )
                {
                    try
                    {
                        const result = await api<BoardData>( `/boards/${ activeBoardId }` );
                        if ( alive && activeId.value === activeBoardId )
                        {
                            const merged = mergeData( result );
                            if ( selected.value?.boardId === activeBoardId )
                            {
                                selected.value = merged.tickets.find( item => item.id === selected.value!.id ) ?? null;
                            }
                            boardLoading.value = false;
                        }
                    }
                    catch ( error )
                    {
                        if ( alive && error instanceof ApiError && error.status === 404 && activeId.value === activeBoardId )
                        {
                            boards.value = boards.value.filter( board => board.id !== activeBoardId );
                            if ( loadedData.value?.board.id === activeBoardId )
                            {
                                loadedData.value = null;
                            }
                            if ( activeId.value === activeBoardId )
                            {
                                activeId.value = '';
                            }
                            if ( selected.value?.boardId === activeBoardId )
                            {
                                selected.value = null;
                            }
                        }
                        else if ( alive )
                        {
                            retryNeeded = true;
                            reportError( error );
                        }
                    }
                }
                if ( retryNeeded )
                {
                    retryNeeded = false;
                    scheduleRetry();
                }
                else if ( refreshAll || shouldReadActive )
                {
                    retryAttempt = 0;
                    window.clearTimeout( retryTimer );
                    retryTimer = 0;
                }
            }
            while ( alive && ( pendingAll || pending.size > 0 || rerun ) );
        }
        finally
        {
            running = false;
            if ( alive && ( pendingAll || pending.size > 0 || rerun ) )
            {
                void drain();
            }
        }
    };
    requestSync = boardId =>
    {
        if ( boardId === null )
        {
            pendingAll = true;
        }
        else
        {
            pending.add( boardId );
        }
        void drain();
    };
    const stop = connectBoardEvents( event =>
    {
        requestSync( event.boardId );
    }, () =>
    {
        requestSync( null );
    } );
    requestSync( null );
    onCleanup( () =>
    {
        alive = false;
        window.clearTimeout( retryTimer );
        requestSync = () => {};
        stop();
    } );
} );

watch( [ activeId, boards ], ( [ currentId, currentBoards ] ) =>
{
    if ( currentBoards.length === 0 )
    {
        return;
    }
    const board = currentBoards.find( item => item.id === currentId );
    const path = board?.slug ? boardPath( board.slug ) : '/';
    if ( window.location.pathname !== path )
    {
        window.history.replaceState( null, '', path );
    }
}, { deep: true } );

function selectBoard ( id: string, push = true )
{
    activeId.value = id;
    selected.value = null;
    query.value = '';
    assignee.value = 'all';
    tagFilter.value = [];
    archive.value = false;
    const slug = boards.value.find( board => board.id === id )?.slug;
    const path = slug ? boardPath( slug ) : '/';
    if ( window.location.pathname !== path )
    {
        if ( push )
        {
            window.history.pushState( null, '', path );
        }
        else
        {
            window.history.replaceState( null, '', path );
        }
    }
    if ( window.innerWidth < 768 )
    {
        collapsed.value = true;
        localStorage.setItem( 'soso-sidebar-collapsed', 'true' );
    }
}

function handlePopState ()
{
    const slug = slugFromPath();
    const board = slug === null ? undefined : boards.value.find( item => item.slug === slug );
    if ( board )
    {
        selectBoard( board.id, false );
    }
}

onMounted( () => { window.addEventListener( 'popstate', handlePopState ); } );
onUnmounted( () => { window.removeEventListener( 'popstate', handlePopState ); } );

function toggleSidebar ()
{
    collapsed.value = !collapsed.value;
    localStorage.setItem( 'soso-sidebar-collapsed', String( collapsed.value ) );
}

function changed ( ticket: Ticket )
{
    if ( activeId.value !== ticket.boardId )
    {
        requestSync( ticket.boardId );
        return;
    }
    if ( selected.value?.boardId === ticket.boardId && selected.value.id === ticket.id && selected.value.revision <= ticket.revision )
    {
        selected.value = ticket;
    }
    const previous = loadedData.value;
    if ( !previous || previous.board.id !== ticket.boardId )
    {
        requestSync( ticket.boardId );
        return;
    }
    const tickets = previous.tickets.some( item => item.id === ticket.id )
        ? previous.tickets.map( item => item.id === ticket.id && item.revision > ticket.revision ? item : item.id === ticket.id ? ticket : item )
        : [ ...previous.tickets, ticket ];
    loadedData.value = { ...previous, tickets };
    requestSync( ticket.boardId );
}

async function move ( ticket: Ticket, columnId: string, position: number )
{
    try
    {
        changed( await api<Ticket>( `/boards/${ ticket.boardId }/tickets/${ ticket.id }`, 'PUT', { ...ticketBody( ticket ), columnId, position } ) );
    }
    catch ( error )
    {
        reportError( error );
        requestSync( ticket.boardId );
    }
}

async function createTicket ( title: string, columnId: string )
{
    const boardId = activeId.value;
    try
    {
        const ticket = await api<Ticket>( `/boards/${ boardId }/tickets`, 'POST', { title, columnId, assigneeId: account.value?.id } );
        if ( activeId.value !== boardId )
        {
            requestSync( boardId );
            return;
        }
        const previous = loadedData.value;
        if ( previous )
        {
            const tickets = previous.tickets.some( item => item.id === ticket.id )
                ? previous.tickets.map( item => item.id === ticket.id && item.revision > ticket.revision ? item : item.id === ticket.id ? ticket : item )
                : [ ...previous.tickets, ticket ];
            loadedData.value = { ...previous, tickets };
        }
        requestSync( boardId );
        newColumn.value = null;
        createdTicketId.value = ticket.id;
        if ( !( selected.value?.boardId === ticket.boardId && selected.value.id === ticket.id && selected.value.revision > ticket.revision ) )
        {
            selected.value = ticket;
        }
    }
    catch ( error )
    {
        reportError( error );
    }
}

async function toggleTheme ()
{
    const next = colorScheme.value === 'dark' ? 'light' : 'dark';
    setColorScheme( next );
    if ( !account.value )
    {
        return;
    }
    try
    {
        account.value = await api<Account>( '/auth/profile', 'PUT', { name: account.value.name, settings: account.value.settings ?? '', theme: next } );
    }
    catch ( error )
    {
        setColorScheme( account.value.theme );
        reportError( error );
    }
}

const filtered = computed( () => data.value?.tickets.filter( ticket =>{
    const matchesQuery = `${ ticket.id } ${ ticketKey( data.value!.board, ticket ) } ${ ticket.title } ${ ticket.description }`.toLowerCase().includes( query.value.toLowerCase() );
    const matchesPriority = priority.value === 'all' || ticket.priority === priority.value;
    const matchesTags = tagFilter.value.length === 0 || tagFilter.value.some( tag => ticket.tags.includes( tag ) );
    const matchesAssignee = assignee.value === 'all' || ( assignee.value === 'unassigned' ? ticket.assigneeId === null : ticket.assigneeId === assignee.value );
    return !ticket.archived && matchesQuery && matchesPriority && matchesTags && matchesAssignee;
} ) ?? [] );

const canManage = computed( () => data.value?.board.ownerId === account.value?.id || account.value?.isAdmin === true );
const activeFilters = computed( () => tagFilter.value.length + Number( priority.value !== 'all' ) + Number( assignee.value !== 'all' ) );

function deleteTicket ( id: string, boardId: string )
{
    const previous = loadedData.value;
    if ( previous?.board.id === boardId )
    {
        loadedData.value = { ...previous, tickets: previous.tickets.filter( ticket => ticket.id !== id ) };
    }
    requestSync( boardId );
    selected.value = null;
    createdTicketId.value = null;
}
</script>

<template>
  <div v-if="booting" class="loading-screen"><Loader /></div>
  <Login v-else-if="!account" :on-login="value => { account = value; }" />
  <div v-else :class="`app-shell ${collapsed ? 'is-collapsed' : ''}`">
    <aside class="sidebar" id="workspace-sidebar" :aria-label="t('Workspace navigation')">
      <div class="brand"><img src="/logo.png" alt="Sosô" /><strong>Sosô<span>{{ t('Organizing your life :D') }}</span></strong></div>
      <div class="sidebar-heading">
        <span>{{ t('BOARDS') }}</span>
        <IconButton :label="t('Create board')" @click="boardModal = 'create'"><Plus :size="17" /></IconButton>
      </div>
      <nav class="board-nav">
        <button
          v-for="board in boards" :key="board.id" :class="`board-link ${board.id === activeId ? 'active' : ''}`"
          :aria-current="board.id === activeId ? 'page' : undefined" @click="selectBoard(board.id)"
        >
          <BoardIcon :icon="board.icon" :color="board.color" /><span>{{ board.name }}</span>
        </button>
      </nav>
      <div class="sidebar-bottom">
        <button v-if="account.isAdmin" class="board-link" :aria-label="t('Accounts')" @click="admin = true"><Users :size="18" /><span>{{ t('Accounts') }}</span></button>
        <button v-if="account.isAdmin" class="board-link" :aria-label="t('Backup')" @click="backup = true"><DatabaseBackup :size="18" /><span>{{ t('Backup') }}</span></button>
        <button class="board-link" :aria-label="t('Profile & settings')" @click="profile = true"><Settings :size="18" /><span>{{ t('Profile & settings') }}</span></button>
        <button class="account-button" :aria-label="t('Open profile')" @click="profile = true">
          <Avatar :src="imageUrl(account.avatarId)" size="32px" radius="xl">{{ account.name.slice(0, 2).toUpperCase() }}</Avatar>
          <span><strong>{{ account.name }}</strong><small>{{ t(account.isAdmin ? 'Administrator' : 'Member') }}</small></span>
        </button>
      </div>
    </aside>
    <main class="workspace">
      <header class="topbar">
        <Group gap="6px" wrap="nowrap" class="topbar-title">
          <IconButton
            :label="t(collapsed ? 'Expand sidebar' : 'Collapse sidebar')" :expanded="!collapsed"
            controls="workspace-sidebar" @click="toggleSidebar()"
          >
            <PanelLeftOpen v-if="collapsed" :size="16" />
            <PanelLeftClose v-else :size="16" />
          </IconButton>
          <span class="workspace-label">Sosô</span><span class="separator">/</span>
          <BoardIcon v-if="data" :icon="data.board.icon" :color="data.board.color" :size="14" />
          <Text class="topbar-board-name" size="xs" :fw="600" truncate>{{ data?.board.name ?? t('Boards') }}</Text>
          <Badge v-if="data" class="board-ticket-count" variant="light" color="gray" size="sm">{{ filtered.length }} {{ t(filtered.length === 1 ? 'ticket' : 'tickets') }}</Badge>
        </Group>
        <Group gap="4px" wrap="nowrap" class="topbar-actions">
          <SearchInput v-if="data" :key="data.board.id" ref="searchBox" :on-query="publishQuery" />
          <Group gap="4px" wrap="nowrap">
            <LanguageButton />
            <IconButton :label="t(colorScheme === 'dark' ? 'Light theme' : 'Dark theme')" @click="toggleTheme()">
              <Sun v-if="colorScheme === 'dark'" :size="15" />
              <Moon v-else :size="15" />
            </IconButton>
          </Group>
          <IconButton :label="t('Sign out')" @click="api('/auth/logout', 'POST').then(() => { account = null; loadedData = null; boards = []; }).catch(reportError)"><LogOut :size="15" /></IconButton>
        </Group>
      </header>
      <template v-if="data">
        <section class="board-heading">
          <div class="board-title"><h1>{{ data.board.name }}</h1><p v-if="data.board.description">{{ data.board.description }}</p></div>
          <Group class="board-actions" gap="6px">
            <AvatarGroup class="board-avatars">
              <Tooltip v-for="member in data.members.slice(0, 4)" :key="member.id" :label="member.name">
                <Avatar size="24px" radius="xl" :src="imageUrl(member.avatarId)">{{ member.name.slice(0, 1) }}</Avatar>
              </Tooltip>
            </AvatarGroup>
            <IconButton v-if="canManage" :label="t('Board settings')" @click="boardModal = 'edit'"><Settings :size="16" /></IconButton>
            <Button size="xs" :variant="activeFilters > 0 ? 'light' : 'default'" aria-haspopup="dialog" @click="filtersOpen = true">
              <template #leftSection><SlidersHorizontal :size="14" /></template>
              {{ t('Filters') }}{{ activeFilters > 0 ? ` (${activeFilters})` : '' }}
            </Button>
            <Button size="xs" variant="default" @click="archive = true">
              <template #leftSection><Archive :size="14" /></template>
              {{ t('Archive') }}
            </Button>
            <Button size="xs" @click="newColumn = data.board.columns[0].id">
              <template #leftSection><Plus :size="14" /></template>
              {{ t('New ticket') }}
            </Button>
          </Group>
        </section>
        <Kanban
          :data="data" :tickets="filtered"
          :on-open="ticket => { createdTicketId = null; selected = ticket; }"
          :on-create="column => { newColumn = column; }" :on-move="move"
        />
      </template>
      <div v-else class="empty-workspace">
        <Loader v-if="boardLoading || activeId" />
        <template v-else>
          <Columns3 :size="42" :stroke-width="1.2" />
          <h1>{{ t('Your workspace, ready.') }}</h1>
          <Button @click="boardModal = 'create'">
            <template #leftSection><Plus :size="17" /></template>
            {{ t('Create a board') }}
          </Button>
        </template>
      </div>
    </main>
    <Modal :opened="filtersOpen && data !== null" :title="t('Filters')" centered size="sm" @close="filtersOpen = false">
      <Stack>
        <div>
          <Text size="sm" :fw="500" mb="8px">{{ t('Tags') }}</Text>
          <div class="tag-filters">
            <button
              v-for="tag in tags" :key="tag.value" :class="`tag-filter ${tagFilter.includes(tag.value) ? 'selected' : ''}`"
              :aria-pressed="tagFilter.includes(tag.value)"
              @click="tagFilter = tagFilter.includes(tag.value) ? tagFilter.filter(value => value !== tag.value) : [...tagFilter, tag.value]"
            >
              <i :style="{ background: tag.color }" />{{ t(tag.label) }}
            </button>
          </div>
        </div>
        <Select
          :label="t('Assignee')" :aria-label="t('Filter by assignee')" v-model="assignee"
          :data="[{ label: t('All assignees'), value: 'all' }, { label: t('Unassigned'), value: 'unassigned' }, ...(data?.members ?? []).map(member => ({ value: member.id, label: member.name }))]"
          :allow-deselect="false"
        />
        <Select
          :label="t('Priority')" :aria-label="t('Filter by priority')" v-model="priority"
          :data="[{ label: t('All priorities'), value: 'all' }, ...['urgent', 'high', 'normal', 'low'].map(value => ({ value, label: t(value[0].toUpperCase() + value.slice(1)) }))]"
          :allow-deselect="false"
        />
        <Group justify="space-between">
          <Button variant="subtle" :disabled="activeFilters === 0" @click="priority = 'all'; assignee = 'all'; tagFilter = [];">{{ t('Clear filters') }}</Button>
          <Button @click="filtersOpen = false">{{ t('Done') }}</Button>
        </Group>
      </Stack>
    </Modal>
    <TicketModal
      v-if="selected && data" :key="selected.id" :ticket="selected" :data="data" :account="account"
      :focus-description="createdTicketId === selected.id" :on-close="() => { selected = null; createdTicketId = null; }"
      :on-focus-search="() => { searchBox?.focus(); }"
      :on-change="changed" :on-delete="id => { if (selected) deleteTicket(id, selected.boardId); }"
    />
    <BoardModal
      v-if="boardModal" :board="boardModal === 'edit' ? data?.board : undefined" :on-close="() => { boardModal = null; }"
      :on-save="board => { boards = [...boards.filter(item => item.id !== board.id), board]; selectBoard(board.id); boardModal = null; requestSync(board.id); }"
      :on-delete="id => { const remaining = boards.filter(board => board.id !== id); boards = remaining; selectBoard(remaining[0]?.id ?? ''); loadedData = null; boardModal = null; requestSync(null); }"
    />
    <ProfileModal v-if="profile" :account="account" :on-change="value => { account = value; }" :on-close="() => { profile = false; }" />
    <AdminModal v-if="admin" :on-close="() => { admin = false; }" />
    <BackupModal v-if="backup" :result="backupResult" :on-close="() => { backup = false; backupResult = null; }" />
    <ArchiveModal
      v-if="archive && data" :data="data" :on-close="() => { archive = false; }"
      :on-open="ticket => { createdTicketId = null; selected = ticket; }" :on-change="changed"
    />
    <NewTicketModal v-if="newColumn !== null" :column="newColumn" :columns="data?.board.columns ?? []" :on-close="() => { newColumn = null; }" :on-create="createTicket" />
  </div>
</template>
