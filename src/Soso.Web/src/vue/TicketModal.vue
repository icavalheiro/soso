<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref, toRaw, useTemplateRef, watch } from 'vue';
import draggable from 'vuedraggable';
import { ActionIcon, Avatar, Button, CopyButton, FileButton, Group, Modal, MultiSelect, Progress, Select, Stack, Text, Textarea, TextInput, Tooltip } from '@mantine-vue/core';
import { Archive, ArchiveRestore, Check, CheckSquare, ChevronDown, Eye, ImagePlus, MessageSquare, Pencil, Play, Plus, Save, Send, Trash2, Video } from 'lucide-vue-next';
import { api, imageUrl, newId, ticketBody, ticketKey, tags, videoThumbnailUrl, videoUrl } from '../api';
import type { Account, BoardData, Subtask, Ticket } from '../api';
import { reportError } from './feedback';
import MarkdownText from './MarkdownText.vue';
import SubtaskRow, { type SubtaskNavKey } from './SubtaskRow.vue';
import { useLanguage } from './language';

const props = defineProps<{
    ticket: Ticket;
    data: BoardData;
    account: Account;
    focusDescription: boolean;
    onClose: () => void;
    onFocusSearch: () => void;
    onChange: ( ticket: Ticket ) => void;
    onDelete: ( id: string ) => void;
}>();
const { t } = useLanguage();

const draft = ref<Ticket>( { ...structuredClone( toRaw( props.ticket ) ), description: props.ticket.description ?? '' } );
const baseline = ref( JSON.stringify( ticketBody( props.ticket ) ) );
const comment = ref( '' );
const subtask = ref( '' );
const busy = ref( false );
const confirm = ref<'delete' | 'discard' | null>( null );
const preview = ref<string | null>( null );
const videoPreview = ref<string | null>( null );
const editingTitle = ref( false );
const editingDescription = ref( props.focusDescription );
const focusSearchOutsideModal = ref( false );
const grabbedSubtask = ref<string | null>( null );
const liveMessage = ref( '' );
const form = useTemplateRef<HTMLFormElement>( 'ticketForm' );
const descriptionBox = useTemplateRef<HTMLElement>( 'descriptionBox' );
let postingComment = false;
let savingDraft: Promise<Ticket> | null = null;
let autoSaveTimer = 0;
let autoSaveWork: () => void = () => {};
const path = `/boards/${ props.ticket.boardId }/tickets/${ props.ticket.id }`;

function focusDescriptionEditor ()
{
    descriptionBox.value?.querySelector( 'textarea' )?.focus();
}

watch( editingDescription, current =>
{
    if ( current )
    {
        void nextTick( focusDescriptionEditor );
    }
} );
onMounted( () =>
{
    if ( editingDescription.value )
    {
        // The modal focus trap steals the initial focus; retry after it settles.
        window.setTimeout( focusDescriptionEditor, 250 );
    }
} );

function activityText ( item: Ticket[ 'activity' ][ number ] ): string
{
    if ( item.action === 'created' )
    {
        return t( 'created this ticket' );
    }
    if ( item.action === 'comment_added' )
    {
        return t( 'added a comment' );
    }
    if ( item.action === 'comment_deleted' )
    {
        return t( 'deleted a comment' );
    }
    if ( item.action === 'image_added' )
    {
        return t( 'added an attachment' );
    }
    if ( item.action === 'image_removed' )
    {
        return t( 'removed an attachment' );
    }
    const field = item.field ?? '';
    const labels: Record<string, string> = { title: 'Title', description: 'Description', status: 'Status', priority: 'Priority', assignee: 'Assignee', due_date: 'Due date', tags: 'Tags', archived: 'Archive', subtasks: 'Subtasks' };
    return `${ t( 'changed' ) } ${ t( labels[ field ] ?? field ) }: ${ item.oldValue || t( 'empty' ) } → ${ item.newValue || t( 'empty' ) }`;
}

function apply ( result: Ticket )
{
    const normalized = { ...result, description: result.description ?? '' };
    draft.value = normalized;
    baseline.value = JSON.stringify( ticketBody( normalized ) );
    props.onChange( normalized );
}

watch( () => props.ticket, incomingTicket =>
{
    const incoming = { ...incomingTicket, description: incomingTicket.description ?? '' };
    const current = draft.value;
    if ( incoming.revision <= current.revision )
    {
        return;
    }
    const previousBaseline = JSON.parse( baseline.value ) as ReturnType<typeof ticketBody>;
    const currentBody = ticketBody( current );
    const incomingBody = ticketBody( incoming );
    const rebased = { ...incoming };
    for ( const key of Object.keys( currentBody ) as Array<keyof typeof currentBody> )
    {
        if ( key !== 'revision' && JSON.stringify( currentBody[ key ] ) !== JSON.stringify( previousBaseline[ key ] ) )
        {
            Object.assign( rebased, { [ key ]: currentBody[ key ] } );
        }
    }
    draft.value = rebased;
    baseline.value = JSON.stringify( incomingBody );
} );

async function saveDraft (): Promise<Ticket>
{
    if ( autoSaveTimer !== 0 )
    {
        window.clearTimeout( autoSaveTimer );
        autoSaveTimer = 0;
    }
    if ( savingDraft )
    {
        return savingDraft;
    }
    const snapshot = JSON.stringify( ticketBody( draft.value ) );
    if ( snapshot === baseline.value )
    {
        return draft.value;
    }
    const request = api<Ticket>( path, 'PUT', ticketBody( draft.value ) );
    savingDraft = request;
    try
    {
        const result = await request;
        if ( result.revision < draft.value.revision )
        {
            return result;
        }
        if ( JSON.stringify( ticketBody( draft.value ) ) === snapshot )
        {
            apply( result );
        }
        else
        {
            draft.value = { ...draft.value, revision: result.revision };
                // Baseline tracks the saved snapshot (not the newer draft), so a
                // follow-up save is scheduled for edits made while saving.
                baseline.value = JSON.stringify( { ...( JSON.parse( snapshot ) as ReturnType<typeof ticketBody> ), revision: result.revision } );
        }
        return result;
    }
    finally
    {
        if ( savingDraft === request )
        {
            savingDraft = null;
        }
    }
}

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

autoSaveWork = () => { void saveDraft().catch( reportError ); };

watch( [ draft, baseline ], ( [ current, currentBaseline ], _previous, onCleanup ) =>
{
    if ( JSON.stringify( ticketBody( current ) ) === currentBaseline )
    {
        return;
    }
    autoSaveTimer = window.setTimeout( () =>
    {
        autoSaveTimer = 0;
        autoSaveWork();
    }, 10_000 );
    onCleanup( () =>
    {
        if ( autoSaveTimer !== 0 )
        {
            window.clearTimeout( autoSaveTimer );
            autoSaveTimer = 0;
        }
    } );
}, { deep: true } );

function handleShortcut ( event: KeyboardEvent )
{
    if ( !( event.ctrlKey || event.metaKey ) )
    {
        return;
    }
    const key = event.key.toLowerCase();
    if ( key === 's' )
    {
        event.preventDefault();
        if ( confirm.value === null && preview.value === null && !busy.value )
        {
            form.value?.requestSubmit();
        }
    }
    else if ( key === 'f' )
    {
        event.preventDefault();
        if ( confirm.value === null && preview.value === null )
        {
            focusSearchOutsideModal.value = true;
            window.requestAnimationFrame( props.onFocusSearch );
        }
    }
}

onMounted( () => { window.addEventListener( 'keydown', handleShortcut ); } );
onUnmounted( () =>
{
    window.removeEventListener( 'keydown', handleShortcut );
    if ( autoSaveTimer !== 0 )
    {
        window.clearTimeout( autoSaveTimer );
    }
} );

function close ()
{
    const dirty = JSON.stringify( ticketBody( draft.value ) ) !== baseline.value;
    if ( dirty )
    {
        confirm.value = 'discard';
        return;
    }
    props.onClose();
}

function addSubtask ()
{
    const title = subtask.value.trim();
    if ( !title )
    {
        return;
    }
    draft.value = { ...draft.value, subtasks: [ ...draft.value.subtasks, { id: newId(), title, done: false } ] };
    subtask.value = '';
}

function moveSubtask ( task: Subtask, key: SubtaskNavKey )
{
    if ( key === 'drop' || key === 'cancel' )
    {
        grabbedSubtask.value = null;
        liveMessage.value = key === 'drop' ? `${ task.title } was dropped.` : `${ task.title } was released without moving.`;
        return;
    }
    const list = [ ...draft.value.subtasks ];
    const index = list.findIndex( item => item.id === task.id );
    const target = index + ( key === 'up' ? -1 : 1 );
    if ( index < 0 || target < 0 || target >= list.length )
    {
        return;
    }
    const [ moved ] = list.splice( index, 1 );
    list.splice( target, 0, moved );
    draft.value = { ...draft.value, subtasks: list };
    liveMessage.value = `${ task.title } was moved over droppable area ${ target + 1 } of ${ list.length }.`;
}

function grabSubtask ( task: Subtask )
{
    grabbedSubtask.value = task.id;
    liveMessage.value = `${ task.title } was lifted. Use arrow keys to reorder, space to drop, escape to cancel.`;
}

function postComment ()
{
    const text = comment.value.trim();
    if ( !text || busy.value || postingComment )
    {
        return;
    }
    postingComment = true;
    void action( async () => { await saveDraft(); apply( await api<Ticket>( `${ path }/comments`, 'POST', { text } ) ); comment.value = ''; } )
        .finally( () => { postingComment = false; } );
}

async function uploadImages ( files: File[] ): Promise<string[]>
{
    await saveDraft();
    const ids: string[] = [];
    for ( const file of files )
    {
        if ( draft.value.images.length + ( draft.value.videos?.length ?? 0 ) + ids.length >= 6 )
        {
            break;
        }
        const form = new FormData();
        form.append( 'file', file );
        const result = await api<Ticket>( `${ path }/images`, 'POST', form );
        ids.push( result.images[ result.images.length - 1 ] );
        apply( result );
    }
    return ids;
}

async function createVideoThumbnail ( file: File ): Promise<File>
{
    const source = URL.createObjectURL( file );
    try
    {
        const video = document.createElement( 'video' );
        video.preload = 'metadata';
        video.muted = true;
        video.src = source;
        await new Promise<void>( ( resolve, reject ) =>
        {
            video.onloadeddata = () => { resolve(); };
            video.onerror = () => { reject( new Error( 'Unable to read this video.' ) ); };
        } );
        video.currentTime = Math.min( 1, ( video.duration || 1 ) / 2 );
        await new Promise<void>( ( resolve, reject ) =>
        {
            video.onseeked = () => { resolve(); };
            video.onerror = () => { reject( new Error( 'Unable to create a video thumbnail.' ) ); };
        } );
        const canvas = document.createElement( 'canvas' );
        const scale = Math.min( 1, 800 / Math.max( video.videoWidth, video.videoHeight ) );
        canvas.width = Math.max( 1, Math.round( video.videoWidth * scale ) );
        canvas.height = Math.max( 1, Math.round( video.videoHeight * scale ) );
        canvas.getContext( '2d' )?.drawImage( video, 0, 0, canvas.width, canvas.height );
        const blob = await new Promise<Blob>( ( resolve, reject ) => canvas.toBlob( value => value ? resolve( value ) : reject( new Error( 'Unable to create a video thumbnail.' ) ), 'image/png' ) );
        return new File( [ blob ], 'thumbnail.png', { type: 'image/png' } );
    }
    finally
    {
        URL.revokeObjectURL( source );
    }
}

async function uploadVideos ( files: File[] ): Promise<string[]>
{
    await saveDraft();
    const ids: string[] = [];
    for ( const file of files )
    {
        if ( draft.value.images.length + ( draft.value.videos?.length ?? 0 ) + ids.length >= 6 )
        {
            break;
        }
        const form = new FormData();
        form.append( 'file', file );
        form.append( 'thumbnail', await createVideoThumbnail( file ) );
        const result = await api<Ticket>( `${ path }/videos`, 'POST', form );
        ids.push( result.videos[ result.videos.length - 1 ] );
        apply( result );
    }
    return ids;
}

function handleImagePaste ( event: ClipboardEvent, field: 'description' | 'comment' )
{
    const files = Array.from( event.clipboardData?.files ?? [] ).filter( file => [ 'image/png', 'image/jpeg', 'image/webp' ].includes( file.type ) );
    if ( files.length === 0 )
    {
        return;
    }
    event.preventDefault();
    const textarea = event.target as HTMLTextAreaElement;
    const value = field === 'description' ? draft.value.description : comment.value;
    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    void action( async () =>
    {
        const ids = await uploadImages( files );
        const reference = ids.map( id => `![${ t( 'Attached image' ) }](/api/images/${ id })` ).join( '\n\n' );
        if ( !reference )
        {
            return;
        }
        const prefix = value.slice( 0, start ?? value.length );
        const suffix = value.slice( end ?? value.length );
        const inserted = `${ prefix }${ prefix && !prefix.endsWith( '\n' ) ? '\n\n' : '' }${ reference }${ suffix && !suffix.startsWith( '\n' ) ? '\n\n' : '' }${ suffix }`;
        if ( field === 'description' )
        {
            draft.value = { ...draft.value, description: inserted };
        }
        else
        {
            comment.value = inserted;
        }
    } );
}

const doneCount = () => draft.value.subtasks.filter( task => task.done ).length;
const removedColumn = () => props.data.board.removedColumns?.find( item => item.column.id === draft.value.columnId )?.column;
const statusColumns = () => removedColumn() ? [ ...props.data.board.columns, removedColumn()! ] : props.data.board.columns;
</script>

<template>
  <Modal
    :opened="true" :size="880" centered :close-on-click-outside="!busy" :close-on-escape="!busy"
    :with-close-button="!busy" :trap-focus="!focusSearchOutsideModal" @close="close()"
  >
    <template #title>
      <span class="modal-ticket-label">
        {{ t('Ticket').toUpperCase() }}
        <CopyButton :value="ticketKey(data.board, ticket)" :timeout="1500">
          <template #default="{ copied, copy }">
            <Tooltip :label="copied ? t('Copied') : t('Copy ticket id')">
              <button type="button" class="modal-ticket-id" :aria-label="t('Copy ticket id')" @click="copy()">
                <Check v-if="copied" :size="12" />
                {{ ticketKey(data.board, ticket) }}
              </button>
            </Tooltip>
          </template>
        </CopyButton>
      </span>
    </template>
    <form ref="ticketForm" @submit.prevent="action(async () => { await saveDraft(); onClose(); })">
      <fieldset class="ticket-fieldset" :disabled="busy">
        <Group class="ticket-title-row" justify="space-between" gap="xs" wrap="nowrap">
          <TextInput
            v-if="editingTitle" :aria-label="t('Ticket title')" class="ticket-title-input" :style="{ flex: 1 }"
            required maxlength="160" v-model="draft.title"
          />
          <Text v-else class="ticket-title-text" size="xl" :fw="600">{{ draft.title }}</Text>
          <Tooltip :label="t(editingTitle ? 'Preview title' : 'Edit title')">
            <ActionIcon
              type="button" variant="subtle" :aria-label="t(editingTitle ? 'Preview title' : 'Edit title')"
              :disabled="busy" @click="editingTitle = !editingTitle"
            >
              <Eye v-if="editingTitle" :size="16" />
              <Pencil v-else :size="16" />
            </ActionIcon>
          </Tooltip>
        </Group>
        <div class="ticket-editor-grid">
          <div class="ticket-editor-main">
            <Stack gap="lg">
              <MultiSelect
                :label="t('Tags')" v-model="draft.tags"
                :data="tags.map(tag => ({ value: tag.value, label: tag.label }))" searchable
              />
              <section :aria-label="t('Description')">
                <Group justify="space-between" mb="sm">
                  <Text size="sm" :fw="600">{{ t('Description') }}</Text>
                  <Tooltip :label="t(editingDescription ? 'Preview description' : 'Edit description')">
                    <ActionIcon
                      type="button" variant="subtle"
                      :aria-label="t(editingDescription ? 'Preview description' : 'Edit description')"
                      :disabled="busy" @click="editingDescription = !editingDescription"
                    >
                      <Eye v-if="editingDescription" :size="16" />
                      <Pencil v-else :size="16" />
                    </ActionIcon>
                  </Tooltip>
                </Group>
                <div ref="descriptionBox">
                  <Textarea
                    v-if="editingDescription" :aria-label="t('Description')" :placeholder="t('Add a description')"
                    :min-rows="4" autosize :max-rows="12" maxlength="12000" v-model="draft.description"
                    @paste="handleImagePaste($event, 'description')"
                  />
                  <div v-else class="ticket-description-markdown">
                    <MarkdownText
                      v-if="(draft.description ?? '').trim()" :source="draft.description ?? ''" mode="full"
                      @preview-image="id => { preview = id; }"
                    />
                    <Text v-else size="sm" c="dimmed">{{ t('No description') }}</Text>
                  </div>
                </div>
              </section>
              <section>
                <Group justify="space-between" mb="sm">
                  <Text size="sm" :fw="600"><CheckSquare :size="15" class="inline-icon" /> {{ t('Subtasks') }}</Text>
                  <Text c="dimmed" size="xs">{{ doneCount() }} / {{ draft.subtasks.length }}</Text>
                </Group>
                <Progress v-if="draft.subtasks.length > 0" size="4px" :value="doneCount() / draft.subtasks.length * 100" mb="md" />
                <draggable
                  :list="draft.subtasks" item-key="id" handle=".subtask-drag-handle" draggable=".subtask-row" :animation="180"
                  @change="liveMessage = 'Subtask order updated.'"
                >
                  <template #item="{ element }">
                    <SubtaskRow
                      :task="element" :grabbed="grabbedSubtask === element.id"
                      :on-change="updated => { draft = { ...draft, subtasks: draft.subtasks.map(item => item.id === updated.id ? updated : item) }; }"
                      :on-remove="id => { draft = { ...draft, subtasks: draft.subtasks.filter(item => item.id !== id) }; }"
                      :on-grab="grabSubtask" :on-navigate="moveSubtask"
                    />
                  </template>
                </draggable>
                <Group gap="xs" mt="sm" wrap="nowrap">
                  <TextInput
                    :aria-label="t('New subtask')" :placeholder="t('Add a subtask')" maxlength="300"
                    v-model="subtask" :style="{ flex: 1 }"
                    @keydown="(event: KeyboardEvent) => { if (event.key === 'Enter') { event.preventDefault(); addSubtask(); } }"
                  />
                  <Tooltip :label="t('Add subtask')">
                    <ActionIcon :aria-label="t('Add subtask')" size="lg" variant="light" :disabled="draft.subtasks.length >= 100 || !subtask.trim()" @click="addSubtask()">
                      <Plus :size="18" />
                    </ActionIcon>
                  </Tooltip>
                </Group>
              </section>
              <section>
                <Group justify="space-between" mb="sm">
                  <Text size="sm" :fw="600">{{ t('Attachments') }}</Text>
                  <Group gap="4px">
                    <FileButton accept="image/png,image/jpeg,image/webp" @change="file => { if (file) { void action(async () => { await uploadImages([file as File]); }); } }">
                      <template #default="{ onClick }">
                        <Button size="xs" variant="subtle" :disabled="busy || draft.images.length + (draft.videos?.length ?? 0) >= 6" @click="onClick()">
                          <template #leftSection><ImagePlus :size="15" /></template>
                          {{ t('Add image') }}
                        </Button>
                      </template>
                    </FileButton>
                    <FileButton accept="video/mp4,video/webm,video/ogg,.mp4,.webm,.ogv" @change="file => { if (file) { void action(async () => { await uploadVideos([file as File]); }); } }">
                      <template #default="{ onClick }">
                        <Button size="xs" variant="subtle" :disabled="busy || draft.images.length + (draft.videos?.length ?? 0) >= 6" @click="onClick()">
                          <template #leftSection><Video :size="15" /></template>
                          {{ t('Add video') }}
                        </Button>
                      </template>
                    </FileButton>
                  </Group>
                </Group>
                <div class="attachment-grid">
                  <div v-for="id in draft.images" :key="id" class="attachment">
                    <button type="button" :aria-label="t('View attached image')" @click="preview = id">
                      <img :src="imageUrl(id)" :alt="t('Ticket attachment')" @error="(event: Event) => { ((event.target as HTMLImageElement).hidden = true); }" />
                    </button>
                    <Tooltip :label="t('Remove attachment')">
                      <ActionIcon
                        class="attachment-remove" :aria-label="t('Remove attachment')" size="sm" color="red"
                        variant="filled" @click="action(async () => { await saveDraft(); apply(await api<Ticket>(`${path}/images/${id}`, 'DELETE')); })"
                      >
                        <Trash2 :size="13" />
                      </ActionIcon>
                    </Tooltip>
                  </div>
                  <div v-for="id in (draft.videos ?? [])" :key="id" class="attachment">
                    <button type="button" class="video-thumbnail" :aria-label="t('View attached video')" @click="videoPreview = id">
                      <img :src="videoThumbnailUrl(id)" :alt="t('Attached video')" /><span><Play :size="22" fill="currentColor" /></span>
                    </button>
                    <Tooltip :label="t('Remove attachment')">
                      <ActionIcon
                        class="attachment-remove" :aria-label="t('Remove attachment')" size="sm" color="red"
                        variant="filled" @click="action(async () => { await saveDraft(); apply(await api<Ticket>(`${path}/images/${id}`, 'DELETE')); })"
                      >
                        <Trash2 :size="13" />
                      </ActionIcon>
                    </Tooltip>
                  </div>
                </div>
              </section>
              <section>
                <Text size="sm" :fw="600" mb="md"><MessageSquare :size="15" class="inline-icon" /> {{ t('Comments') }}</Text>
                <Stack gap="md">
                  <div v-for="item in draft.comments" :key="item.id" class="comment">
                    <Avatar size="28" radius="xl" :src="imageUrl(data.members.find(member => member.id === item.authorId)?.avatarId)">
                      {{ data.members.find(member => member.id === item.authorId)?.name.slice(0, 1) ?? '?' }}
                    </Avatar>
                    <div class="comment-body">
                      <Group justify="space-between" gap="xs">
                        <Text size="xs" :fw="600">{{ data.members.find(member => member.id === item.authorId)?.name ?? t('Former member') }}</Text>
                        <Text size="xs" c="dimmed">{{ new Date(item.createdAt).toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' }) }}</Text>
                      </Group>
                      <MarkdownText class="comment-markdown" :source="item.text" mode="comment" @preview-image="id => { preview = id; }" />
                    </div>
                    <Tooltip v-if="item.authorId === account.id || account.isAdmin" :label="t('Delete comment')">
                      <ActionIcon
                        :aria-label="t('Delete comment')" variant="subtle" color="gray" size="sm"
                        @click="action(async () => { await saveDraft(); apply(await api<Ticket>(`${path}/comments/${item.id}`, 'DELETE')); })"
                      >
                        <Trash2 :size="13" />
                      </ActionIcon>
                    </Tooltip>
                  </div>
                  <Textarea
                    :aria-label="t('New comment')" :placeholder="t('Write a comment')" :description="t('Press Ctrl + Enter to post')"
                    :min-rows="2" maxlength="4000" v-model="comment" :disabled="busy"
                    @paste="handleImagePaste($event, 'comment')"
                    @keydown="(event: KeyboardEvent) => { if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) { event.preventDefault(); postComment(); } }"
                  />
                  <Group justify="flex-end">
                    <Button size="xs" variant="light" :disabled="busy || !comment.trim()" @click="postComment()">
                      <template #leftSection><Send :size="14" /></template>
                      {{ t('Post comment') }}
                    </Button>
                  </Group>
                </Stack>
              </section>
              <details class="ticket-activity">
                <summary>
                  <Group gap="xs">
                    <ChevronDown :size="15" />
                    <Text size="sm" :fw="600">{{ t('Activity history') }}</Text>
                    <Text size="xs" c="dimmed">{{ draft.activity?.length ?? 0 }}</Text>
                  </Group>
                </summary>
                <Stack gap="sm" mt="md">
                  <div v-for="item in [...(draft.activity ?? [])].reverse()" :key="item.id" class="activity-item">
                    <Avatar size="24" radius="xl">{{ item.actorName.slice(0, 1).toUpperCase() }}</Avatar>
                    <div class="activity-body">
                      <Text size="xs"><strong>{{ item.actorName }}</strong> {{ activityText(item) }}</Text>
                      <Text v-if="item.action === 'comment_added' && item.newValue" size="xs" c="dimmed" class="activity-detail">{{ item.newValue }}</Text>
                      <Text size="xs" c="dimmed">{{ new Date(item.createdAt).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) }}</Text>
                    </div>
                  </div>
                </Stack>
              </details>
            </Stack>
          </div>
          <aside class="ticket-properties">
            <Stack>
              <Select
                :label="t('Status')" v-model="draft.columnId" :allow-deselect="false"
                :data="statusColumns().map(column => ({ value: column.id, label: column.name }))"
              />
              <Select
                :label="t('Priority')" v-model="draft.priority" :allow-deselect="false"
                :data="['low', 'normal', 'high', 'urgent'].map(value => ({ value, label: t(value[0].toUpperCase() + value.slice(1)) }))"
              />
              <Select
                :label="t('Assignee')" v-model="draft.assigneeId" clearable searchable :placeholder="t('Unassigned')"
                :data="data.members.map(member => ({ value: member.id, label: member.name }))"
              />
              <TextInput
                :label="t('Due date')" type="date" :model-value="draft.dueDate?.slice(0, 10) ?? ''"
                @update:model-value="value => { draft = { ...draft, dueDate: value ? `${value}T23:59:59Z` : null }; }"
              />
            </Stack>
          </aside>
        </div>
      </fieldset>
      <Group class="modal-footer" justify="space-between">
        <Group gap="xs">
          <Tooltip :label="t('Delete ticket')">
            <ActionIcon color="red" variant="subtle" :disabled="busy" :aria-label="t('Delete ticket')" @click="confirm = 'delete'">
              <Trash2 :size="17" />
            </ActionIcon>
          </Tooltip>
          <Button
            variant="default" size="xs" :disabled="busy"
            @click="action(async () => { const saved = await saveDraft(); apply(await api<Ticket>(path, 'PUT', { ...ticketBody(saved), archived: !saved.archived })); onClose(); })"
          >
            <template #leftSection><ArchiveRestore v-if="draft.archived" :size="15" /><Archive v-else :size="15" /></template>
            {{ t(draft.archived ? 'Restore' : 'Archive') }}
          </Button>
        </Group>
        <Button type="submit" :loading="busy">
          <template #leftSection><Save :size="15" /></template>
          {{ t('Save changes') }}
        </Button>
      </Group>
    </form>
    <Modal :opened="confirm !== null" :title="t(confirm === 'delete' ? 'Delete ticket?' : 'Discard changes?')" centered @close="confirm = null">
      <Stack>
        <Text size="sm">{{ t(confirm === 'delete' ? 'The ticket, comments and images will be permanently deleted.' : 'Your unsaved changes will be lost.') }}</Text>
        <Group justify="flex-end">
          <Button variant="default" @click="confirm = null">{{ t('Cancel') }}</Button>
          <Button
            color="red" :loading="busy"
            @click="confirm === 'discard' ? onClose() : action(async () => { await api(path, 'DELETE'); onDelete(ticket.id); })"
          >
            {{ t(confirm === 'delete' ? 'Delete permanently' : 'Discard') }}
          </Button>
        </Group>
      </Stack>
    </Modal>
    <Modal :opened="preview !== null" :title="t('Attached image')" size="xl" centered @close="preview = null">
      <img v-if="preview" class="image-preview" :src="imageUrl(preview)" :alt="t('Ticket attachment')" />
    </Modal>
    <Modal :opened="videoPreview !== null" :title="t('Attached video')" size="xl" centered @close="videoPreview = null">
      <video v-if="videoPreview" class="video-preview" :src="videoUrl(videoPreview)" controls autoplay playsinline />
    </Modal>
    <div class="sr-only" role="status" aria-live="polite">{{ liveMessage }}</div>
  </Modal>
</template>
