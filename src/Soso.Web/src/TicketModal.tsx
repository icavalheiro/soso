import { useEffect, useRef, useState } from 'react';
import type { ClipboardEvent } from 'react';
import Markdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { DndContext, KeyboardSensor, PointerSensor, closestCenter, useSensor, useSensors } from '@dnd-kit/core';
import type { DragEndEvent } from '@dnd-kit/core';
import { SortableContext, arrayMove, sortableKeyboardCoordinates, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { ActionIcon, Avatar, Button, Checkbox, FileButton, Group, Modal, MultiSelect, Progress, Select, Stack, Text, Textarea, TextInput, Tooltip } from '@mantine/core';
import { Archive, ArchiveRestore, CheckSquare, ChevronDown, Eye, GripVertical, ImagePlus, MessageSquare, Pencil, Plus, Save, Send, Trash2 } from 'lucide-react';
import { api, imageUrl, newId, ticketBody, tags } from './api';
import type { Account, BoardData, Subtask, Ticket } from './api';
import { reportError } from './feedback';
import { useLanguage } from './useLanguage';

export function TicketModal ( { ticket, data, account, onClose, onFocusSearch, onChange, onDelete }: { ticket: Ticket; data: BoardData; account: Account; onClose: () => void; onFocusSearch: () => void; onChange: ( ticket: Ticket ) => void; onDelete: ( id: string ) => void; } )
{
    const { t } = useLanguage();
    const [ draft, setDraft ] = useState<Ticket>( () => ( { ...structuredClone( ticket ), description: ticket.description ?? '' } ) );
    const [ baseline, setBaseline ] = useState( JSON.stringify( ticketBody( ticket ) ) );
    const [ comment, setComment ] = useState( '' );
    const postingComment = useRef( false );
    const savingDraft = useRef<Promise<Ticket> | null>( null );
    const autoSaveTimer = useRef<number | null>( null);
    const autoSave = useRef( () => {} );
    const [ subtask, setSubtask ] = useState( '' );
    const [ busy, setBusy ] = useState( false );
    const [ confirm, setConfirm ] = useState<'delete' | 'discard' | null>( null );
    const [ preview, setPreview ] = useState<string | null>( null );
    const [ editingTitle, setEditingTitle ] = useState( false );
    const [ editingDescription, setEditingDescription ] = useState( false );
    const [ focusSearchOutsideModal, setFocusSearchOutsideModal ] = useState( false );
    const form = useRef<HTMLFormElement>( null );
    const subtaskSensors = useSensors( useSensor( PointerSensor, { activationConstraint: { distance: 6 } } ), useSensor( KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates } ) );
    const path = `/boards/${ ticket.boardId }/tickets/${ ticket.id }`;
    const done = draft.subtasks.filter( task => task.done ).length;

    function postComment ()
    {
        const text = comment.trim();
        if ( !text || busy || postingComment.current )
        {
            return;
        }
        postingComment.current = true;
        void action( async () => { await saveDraft(); apply( await api<Ticket>( `${ path }/comments`, 'POST', { text } ) ); setComment( '' ); } ).finally( () => { postingComment.current = false; } );
    }

    function activityText ( item: Ticket[ 'activity' ][ number ] )
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
        setDraft( normalized );
        setBaseline( JSON.stringify( ticketBody( normalized ) ) );
        onChange( normalized );
    }

    async function saveDraft ()
    {
        if ( autoSaveTimer.current !== null )
        {
            window.clearTimeout( autoSaveTimer.current );
            autoSaveTimer.current = null;
        }
        if ( savingDraft.current )
        {
            return savingDraft.current;
        }
        const dirty = JSON.stringify( ticketBody( draft ) ) !== baseline;
        if ( !dirty )
        {
            return draft;
        }
        const request = api<Ticket>( path, 'PUT', ticketBody( draft ) );
        savingDraft.current = request;
        try
        {
            const result = await request;
            apply( result );
            return result;
        }
        finally
        {
            if ( savingDraft.current === request )
            {
                savingDraft.current = null;
            }
        }
    }

    async function action ( work: () => Promise<void> )
    {
        setBusy( true );
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
            setBusy( false );
        }
    }

    autoSave.current = () => { void action( async () => { await saveDraft(); } ); };

    useEffect( () =>
    {
        if ( JSON.stringify( ticketBody( draft ) ) === baseline )
        {
            return;
        }
        autoSaveTimer.current = window.setTimeout( () =>
        {
            autoSaveTimer.current = null;
            autoSave.current();
        }, 10_000 );
        return () =>
        {
            if ( autoSaveTimer.current !== null )
            {
                window.clearTimeout( autoSaveTimer.current );
                autoSaveTimer.current = null;
            }
        };
    }, [ draft, baseline ] );

    useEffect( () =>
    {
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
                if ( confirm === null && preview === null && !busy )
                {
                    form.current?.requestSubmit();
                }
            }
            else if ( key === 'f' )
            {
                event.preventDefault();
                if ( confirm === null && preview === null )
                {
                    setFocusSearchOutsideModal( true );
                    window.requestAnimationFrame( onFocusSearch );
                }
            }
        }

        window.addEventListener( 'keydown', handleShortcut );
        return () => { window.removeEventListener( 'keydown', handleShortcut ); };
    }, [ busy, confirm, onFocusSearch, preview ] );

    function close ()
    {
        const dirty = JSON.stringify( ticketBody( draft ) ) !== baseline;
        if ( dirty )
        {
            setConfirm( 'discard' );
            return;
        }
        onClose();
    }

    function addSubtask ()
    {
        const title = subtask.trim();
        if ( !title )
        {
            return;
        }
        setDraft( { ...draft, subtasks: [ ...draft.subtasks, { id: newId(), title, done: false } ] } );
        setSubtask( '' );
    }

    function reorderSubtasks ( event: DragEndEvent )
    {
        const { active, over } = event;
        if ( over && active.id !== over.id )
        {
            const oldIndex = draft.subtasks.findIndex( task => task.id === active.id );
            const newIndex = draft.subtasks.findIndex( task => task.id === over.id );
            setDraft( current => ( { ...current, subtasks: arrayMove( current.subtasks, oldIndex, newIndex ) } ) );
        }
    }

    async function uploadImages ( files: File[] )
    {
        await saveDraft();
        const ids: string[] = [];
        for ( const file of files )
        {
            if ( draft.images.length + ids.length >= 6 )
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

    function handleImagePaste ( event: ClipboardEvent<HTMLTextAreaElement>, field: 'description' | 'comment' )
    {
        const files = Array.from( event.clipboardData.files ).filter( file => [ 'image/png', 'image/jpeg', 'image/webp' ].includes( file.type ) );
        if ( files.length === 0 )
        {
            return;
        }
        event.preventDefault();
        const textarea = event.currentTarget;
        const value = field === 'description' ? draft.description : comment;
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
            const prefix = value.slice( 0, start );
            const suffix = value.slice( end );
            const inserted = `${ prefix }${ prefix && !prefix.endsWith( '\n' ) ? '\n\n' : '' }${ reference }${ suffix && !suffix.startsWith( '\n' ) ? '\n\n' : '' }${ suffix }`;
            if ( field === 'description' )
            {
                setDraft( current => ( { ...current, description: inserted } ) );
            }
            else
            {
                setComment( inserted );
            }
        } );
    }

    function renderImageReference ( src: string | undefined, alt: string | undefined )
    {
        if ( !src?.startsWith( '/api/images/' ) )
        {
            return null;
        }
        const id = src.slice( '/api/images/'.length );
        return <button type="button" className="ticket-inline-image" aria-label={ t( 'View attached image' ) } onClick={ () => { setPreview( id ); } }><img src={ imageUrl( id ) } alt={ alt || t( 'Ticket attachment' ) } onError={ event => { event.currentTarget.hidden = true; } } /></button>;
    }

    return <Modal opened onClose={ close } title={ <span className="modal-ticket-label">{ t( 'Ticket' ).toUpperCase() } #{ ticket.id.slice( 0, 5 ).toUpperCase() }</span> } size={ 880 } centered closeOnClickOutside={ false } closeOnEscape={ !busy } withCloseButton={ !busy } trapFocus={ !focusSearchOutsideModal }>
        <form ref={ form } onSubmit={ event => { event.preventDefault(); void action( async () => { await saveDraft(); onClose(); } ); } }>
            <fieldset className="ticket-fieldset" disabled={ busy }>
                <Group className="ticket-title-row" justify="space-between" gap="xs" wrap="nowrap">
                    { editingTitle ?
                        <TextInput aria-label={ t( 'Ticket title' ) } className="ticket-title-input" style={ { flex: 1 } } required maxLength={ 160 } autoFocus value={ draft.title } onChange={ event => { setDraft( { ...draft, title: event.currentTarget.value } ); } } /> :
                        <Text className="ticket-title-text" size="xl" fw={ 600 }>{ draft.title }</Text> }
                    <Tooltip label={ t( editingTitle ? 'Preview title' : 'Edit title' ) }>
                        <ActionIcon type="button" variant="subtle" aria-label={ t( editingTitle ? 'Preview title' : 'Edit title' ) } disabled={ busy } onClick={ () => { setEditingTitle( !editingTitle ); } }>
                            { editingTitle ? <Eye size={ 16 } /> : <Pencil size={ 16 } /> }
                        </ActionIcon>
                    </Tooltip>
                </Group>
                <div className="ticket-editor-grid"><div className="ticket-editor-main"><Stack gap="lg">
                    <MultiSelect label={ t( 'Tags' ) } value={ draft.tags } onChange={ tags => { setDraft( { ...draft, tags } ); } } data={ tags.map( tag => ( { value: tag.value, label: tag.label } ) ) } searchable />
                    <section aria-label={ t( 'Description' ) }>
                        <Group justify="space-between" mb="sm">
                            <Text size="sm" fw={ 600 }>{ t( 'Description' ) }</Text>
                            <Tooltip label={ t( editingDescription ? 'Preview description' : 'Edit description' ) }>
                                <ActionIcon type="button" variant="subtle" aria-label={ t( editingDescription ? 'Preview description' : 'Edit description' ) } disabled={ busy } onClick={ () => { setEditingDescription( !editingDescription ); } }>
                                    { editingDescription ? <Eye size={ 16 } /> : <Pencil size={ 16 } /> }
                                </ActionIcon>
                            </Tooltip>
                        </Group>
                        { editingDescription ?
                            <Textarea aria-label={ t( 'Description' ) } placeholder={ t( 'Add a description' ) } minRows={ 4 } autosize maxRows={ 12 } maxLength={ 12000 } autoFocus value={ draft.description } onChange={ event => { setDraft( { ...draft, description: event.currentTarget.value } ); } } onPaste={ event => { handleImagePaste( event, 'description' ); } } /> :
                            <div className="ticket-description-markdown">
                                { ( draft.description ?? '' ).trim() ? <Markdown remarkPlugins={ [ remarkGfm ] } skipHtml components={ { img: ( { src, alt } ) => renderImageReference( src, alt ) } }>{ draft.description ?? '' }</Markdown> : <Text size="sm" c="dimmed">{ t( 'No description' ) }</Text> }
                            </div> }
                    </section>
                    <section><Group justify="space-between" mb="sm"><Text size="sm" fw={ 600 }><CheckSquare size={ 15 } className="inline-icon" /> { t( 'Subtasks' ) }</Text><Text c="dimmed" size="xs">{ done } / { draft.subtasks.length }</Text></Group>{ draft.subtasks.length > 0 && <Progress size={ 4 } value={ done / draft.subtasks.length * 100 } mb="md" /> }<DndContext sensors={ subtaskSensors } collisionDetection={ closestCenter } onDragEnd={ reorderSubtasks }><SortableContext items={ draft.subtasks.map( task => task.id ) } strategy={ verticalListSortingStrategy }><Stack gap={ 9 }>{ draft.subtasks.map( task => <SortableSubtask key={ task.id } task={ task } t={ t } onChange={ updated => { setDraft( current => ( { ...current, subtasks: current.subtasks.map( item => item.id === updated.id ? updated : item ) } ) ); } } onRemove={ id => { setDraft( current => ( { ...current, subtasks: current.subtasks.filter( item => item.id !== id ) } ) ); } } /> ) }</Stack></SortableContext></DndContext><Group gap="xs" mt="sm" wrap="nowrap"><TextInput aria-label={ t( 'New subtask' ) } placeholder={ t( 'Add a subtask' ) } maxLength={ 300 } value={ subtask } style={ { flex: 1 } } onChange={ event => { setSubtask( event.currentTarget.value ); } } onKeyDown={ event => { if ( event.key === 'Enter' ) { event.preventDefault(); addSubtask(); } } } /><Tooltip label={ t( 'Add subtask' ) }><ActionIcon aria-label={ t( 'Add subtask' ) } size="lg" variant="light" disabled={ draft.subtasks.length >= 100 || !subtask.trim() } onClick={ addSubtask }><Plus size={ 18 } /></ActionIcon></Tooltip></Group></section>
                    <section><Group justify="space-between" mb="sm"><Text size="sm" fw={ 600 }>{ t( 'Images' ) }</Text><FileButton accept="image/png,image/jpeg,image/webp" onChange={ file => { if ( !file ) { return; } void action( async () => { await uploadImages( [ file ] ); } ); } }>{ props => <Button { ...props } size="xs" variant="subtle" disabled={ busy || draft.images.length >= 6 } leftSection={ <ImagePlus size={ 15 } /> }>{ t( 'Add image' ) }</Button> }</FileButton></Group><div className="attachment-grid">{ draft.images.map( id => <div className="attachment" key={ id }><button type="button" aria-label={ t( 'View attached image' ) } onClick={ () => { setPreview( id ); } }><img src={ imageUrl( id ) } alt={ t( 'Ticket attachment' ) } onError={ event => { event.currentTarget.hidden = true; } } /></button><Tooltip label={ t( 'Remove image' ) }><ActionIcon className="attachment-remove" aria-label={ t( 'Remove image' ) } size="sm" color="red" variant="filled" onClick={ () => { void action( async () => { await saveDraft(); apply( await api<Ticket>( `${ path }/images/${ id }`, 'DELETE' ) ); } ); } }><Trash2 size={ 13 } /></ActionIcon></Tooltip></div> ) }</div></section>
                    <section><Text size="sm" fw={ 600 } mb="md"><MessageSquare size={ 15 } className="inline-icon" /> { t( 'Comments' ) }</Text><Stack gap="md">{ draft.comments.map( item =>
                    {
                        const author = data.members.find( member => member.id === item.authorId );
                        const canDelete = item.authorId === account.id || account.isAdmin;
                        return <div className="comment" key={ item.id }><Avatar size={ 28 } radius="xl" src={ imageUrl( author?.avatarId ) }>{ author?.name.slice( 0, 1 ) ?? '?' }</Avatar><div className="comment-body"><Group justify="space-between" gap="xs"><Text size="xs" fw={ 600 }>{ author?.name ?? t( 'Former member' ) }</Text><Text size="xs" c="dimmed">{ new Date( item.createdAt ).toLocaleString( undefined, { dateStyle: 'short', timeStyle: 'short' } ) }</Text></Group><div className="comment-markdown"><Markdown remarkPlugins={ [ remarkGfm ] } skipHtml components={ { img: ( { src, alt } ) => renderImageReference( src, alt ), a: ( { children } ) => <span>{ children }</span>, input: () => null } }>{ item.text }</Markdown></div></div>{ canDelete && <Tooltip label={ t( 'Delete comment' ) }><ActionIcon aria-label={ t( 'Delete comment' ) } variant="subtle" color="gray" size="sm" onClick={ () => { void action( async () => { await saveDraft(); apply( await api<Ticket>( `${ path }/comments/${ item.id }`, 'DELETE' ) ); } ); } }><Trash2 size={ 13 } /></ActionIcon></Tooltip> }</div>;
                    } ) }<Textarea aria-label={ t( 'New comment' ) } placeholder={ t( 'Write a comment' ) } description={ t( 'Press Ctrl + Enter to post' ) } minRows={ 2 } maxLength={ 4000 } value={ comment } disabled={ busy } onChange={ event => { setComment( event.currentTarget.value ); } } onPaste={ event => { handleImagePaste( event, 'comment' ); } } onKeyDown={ event => { if ( event.key === 'Enter' && ( event.ctrlKey || event.metaKey ) ) { event.preventDefault(); postComment(); } } } /><Group justify="flex-end"><Button size="xs" variant="light" leftSection={ <Send size={ 14 } /> } disabled={ busy || !comment.trim() } onClick={ postComment }>{ t( 'Post comment' ) }</Button></Group></Stack></section>
                    <details className="ticket-activity"><summary><Group gap="xs"><ChevronDown size={ 15 } /><Text size="sm" fw={ 600 }>{ t( 'Activity history' ) }</Text><Text size="xs" c="dimmed">{ draft.activity?.length ?? 0 }</Text></Group></summary><Stack gap="sm" mt="md">{ [ ...( draft.activity ?? [] ) ].reverse().map( item => <div className="activity-item" key={ item.id }><Avatar size={ 24 } radius="xl">{ item.actorName.slice( 0, 1 ).toUpperCase() }</Avatar><div className="activity-body"><Text size="xs"><strong>{ item.actorName }</strong> { activityText( item ) }</Text>{ item.action === 'comment_added' && item.newValue && <Text size="xs" c="dimmed" className="activity-detail">{ item.newValue }</Text> }<Text size="xs" c="dimmed">{ new Date( item.createdAt ).toLocaleString( undefined, { dateStyle: 'medium', timeStyle: 'short' } ) }</Text></div></div> ) }</Stack></details>
                </Stack></div><aside className="ticket-properties"><Stack><Select label={ t( 'Status' ) } value={ draft.columnId } allowDeselect={ false } data={ data.board.columns.map( column => ( { value: column.id, label: column.name } ) ) } onChange={ value => { setDraft( { ...draft, columnId: value ?? draft.columnId } ); } } /><Select label={ t( 'Priority' ) } value={ draft.priority } allowDeselect={ false } data={ [ 'low', 'normal', 'high', 'urgent' ].map( value => ( { value, label: t( value[ 0 ].toUpperCase() + value.slice( 1 ) ) } ) ) } onChange={ value => { setDraft( { ...draft, priority: value ?? 'normal' } ); } } /><Select label={ t( 'Assignee' ) } clearable searchable placeholder={ t( 'Unassigned' ) } value={ draft.assigneeId } data={ data.members.map( member => ( { value: member.id, label: member.name } ) ) } onChange={ value => { setDraft( { ...draft, assigneeId: value } ); } } /><TextInput label={ t( 'Due date' ) } type="date" value={ draft.dueDate?.slice( 0, 10 ) ?? '' } onChange={ event => { setDraft( { ...draft, dueDate: event.currentTarget.value ? `${ event.currentTarget.value }T23:59:59Z` : null } ); } } /></Stack></aside></div>
            </fieldset><Group className="modal-footer" justify="space-between"><Group gap="xs"><Tooltip label={ t( 'Delete ticket' ) }><ActionIcon color="red" variant="subtle" disabled={ busy } aria-label={ t( 'Delete ticket' ) } onClick={ () => { setConfirm( 'delete' ); } }><Trash2 size={ 17 } /></ActionIcon></Tooltip><Button variant="default" size="xs" disabled={ busy } leftSection={ draft.archived ? <ArchiveRestore size={ 15 } /> : <Archive size={ 15 } /> } onClick={ () => { void action( async () => { const saved = await saveDraft(); apply( await api<Ticket>( path, 'PUT', { ...ticketBody( saved ), archived: !saved.archived } ) ); onClose(); } ); } }>{ t( draft.archived ? 'Restore' : 'Archive' ) }</Button></Group><Button type="submit" loading={ busy } leftSection={ <Save size={ 15 } /> }>{ t( 'Save changes' ) }</Button></Group>
        </form>
        <Modal opened={ confirm !== null } onClose={ () => { setConfirm( null ); } } title={ t( confirm === 'delete' ? 'Delete ticket?' : 'Discard changes?' ) } centered><Stack><Text size="sm">{ t( confirm === 'delete' ? 'The ticket, comments and images will be permanently deleted.' : 'Your unsaved changes will be lost.' ) }</Text><Group justify="flex-end"><Button variant="default" onClick={ () => { setConfirm( null ); } }>{ t( 'Cancel' ) }</Button><Button color="red" loading={ busy } onClick={ () => { if ( confirm === 'discard' ) { onClose(); return; } void action( async () => { await api( path, 'DELETE' ); onDelete( ticket.id ); } ); } }>{ t( confirm === 'delete' ? 'Delete permanently' : 'Discard' ) }</Button></Group></Stack></Modal>
        <Modal opened={ preview !== null } onClose={ () => { setPreview( null ); } } title={ t( 'Attached image' ) } size="xl" centered>{ preview && <img className="image-preview" src={ imageUrl( preview ) } alt={ t( 'Ticket attachment' ) } /> }</Modal>
    </Modal>;
}

function SortableSubtask ( { task, t, onChange, onRemove }: { task: Subtask; t: ( text: string ) => string; onChange: ( task: Subtask ) => void; onRemove: ( id: string ) => void; } )
{
    const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable( { id: task.id } );
    return <Group ref={ setNodeRef } gap="xs" wrap="nowrap" className="subtask-row" style={ { transform: CSS.Transform.toString( transform ), transition, opacity: isDragging ? 0.45 : 1 } }>
        <Tooltip label={ t( 'Move subtask' ) }><ActionIcon aria-label={ `${ t( 'Move subtask' ) }: ${ task.title }` } className="subtask-drag-handle" color="gray" variant="subtle" { ...attributes } { ...listeners }><GripVertical size={ 15 } /></ActionIcon></Tooltip>
        <Checkbox aria-label={ `${ t( 'Complete' ) } ${ task.title }` } checked={ task.done } onChange={ event => { onChange( { ...task, done: event.currentTarget.checked } ); } } />
        <TextInput aria-label={ t( 'Subtask title' ) } variant="unstyled" maxLength={ 300 } required value={ task.title } className={ task.done ? 'completed-task' : '' } style={ { flex: 1 } } onChange={ event => { onChange( { ...task, title: event.currentTarget.value } ); } } />
        <Tooltip label={ t( 'Remove subtask' ) }><ActionIcon aria-label={ t( 'Remove subtask' ) } color="gray" variant="subtle" onClick={ () => { onRemove( task.id ); } }><Trash2 size={ 14 } /></ActionIcon></Tooltip>
    </Group>;
}
