import { useState } from 'react';
import Markdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { ActionIcon, Avatar, Button, Checkbox, FileButton, Group, Modal, MultiSelect, Progress, Select, Stack, Text, Textarea, TextInput, Tooltip } from '@mantine/core';
import { Archive, ArchiveRestore, CheckSquare, Eye, ImagePlus, MessageSquare, Pencil, Plus, Save, Send, Trash2 } from 'lucide-react';
import { api, imageUrl, newId, ticketBody, tags } from './api';
import type { Account, BoardData, Ticket } from './api';
import { reportError } from './feedback';

export function TicketModal ( { ticket, data, account, onClose, onChange, onDelete }: { ticket: Ticket; data: BoardData; account: Account; onClose: () => void; onChange: ( ticket: Ticket ) => void; onDelete: ( id: string ) => void; } )
{
    const [ draft, setDraft ] = useState<Ticket>( structuredClone( ticket ) );
    const [ baseline, setBaseline ] = useState( JSON.stringify( ticketBody( ticket ) ) );
    const [ comment, setComment ] = useState( '' );
    const [ subtask, setSubtask ] = useState( '' );
    const [ busy, setBusy ] = useState( false );
    const [ confirm, setConfirm ] = useState<'delete' | 'discard' | null>( null );
    const [ preview, setPreview ] = useState<string | null>( null );
    const [ editingDescription, setEditingDescription ] = useState( false );
    const path = `/boards/${ ticket.boardId }/tickets/${ ticket.id }`;
    const done = draft.subtasks.filter( task => task.done ).length;

    function apply ( result: Ticket )
    {
        setDraft( result );
        setBaseline( JSON.stringify( ticketBody( result ) ) );
        onChange( result );
    }

    async function saveDraft ()
    {
        const dirty = JSON.stringify( ticketBody( draft ) ) !== baseline;
        if ( !dirty )
        {
            return draft;
        }
        const result = await api<Ticket>( path, 'PUT', ticketBody( draft ) );
        apply( result );
        return result;
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

    return <Modal opened onClose={ close } title={ <span className="modal-ticket-label">TICKET #{ ticket.id.slice( 0, 5 ).toUpperCase() }</span> } size={ 880 } centered closeOnClickOutside={ false } closeOnEscape={ !busy } withCloseButton={ !busy }>
        <form onSubmit={ event => { event.preventDefault(); void action( async () => { await saveDraft(); onClose(); } ); } }>
            <fieldset className="ticket-fieldset" disabled={ busy }>
                <TextInput aria-label="Ticket title" className="ticket-title-input" required maxLength={ 160 } value={ draft.title } onChange={ event => { setDraft( { ...draft, title: event.currentTarget.value } ); } } />
                <div className="ticket-editor-grid"><div className="ticket-editor-main"><Stack gap="lg">
                    <MultiSelect label="Tags" value={ draft.tags } onChange={ tags => { setDraft( { ...draft, tags } ); } } data={ tags } searchable />
                    <section aria-label="Description">
                        <Group justify="space-between" mb="sm">
                            <Text size="sm" fw={ 600 }>Description</Text>
                            <Tooltip label={ editingDescription ? 'Preview description' : 'Edit description' }>
                                <ActionIcon type="button" variant="subtle" aria-label={ editingDescription ? 'Preview description' : 'Edit description' } disabled={ busy } onClick={ () => { setEditingDescription( !editingDescription ); } }>
                                    { editingDescription ? <Eye size={ 16 } /> : <Pencil size={ 16 } /> }
                                </ActionIcon>
                            </Tooltip>
                        </Group>
                        { editingDescription ?
                            <Textarea aria-label="Description" placeholder="Add a description" minRows={ 4 } autosize maxRows={ 12 } maxLength={ 12000 } autoFocus value={ draft.description } onChange={ event => { setDraft( { ...draft, description: event.currentTarget.value } ); } } /> :
                            <div className="ticket-description-markdown">
                                { draft.description.trim() ? <Markdown remarkPlugins={ [ remarkGfm ] } skipHtml>{ draft.description }</Markdown> : <Text size="sm" c="dimmed">No description</Text> }
                            </div> }
                    </section>
                    <section><Group justify="space-between" mb="sm"><Text size="sm" fw={ 600 }><CheckSquare size={ 15 } className="inline-icon" /> Subtasks</Text><Text c="dimmed" size="xs">{ done } / { draft.subtasks.length }</Text></Group>{ draft.subtasks.length > 0 && <Progress size={ 4 } value={ done / draft.subtasks.length * 100 } mb="md" /> }<Stack gap={ 9 }>{ draft.subtasks.map( task => <Group key={ task.id } gap="xs" wrap="nowrap"><Checkbox aria-label={ `Complete ${ task.title }` } checked={ task.done } onChange={ event => { const checked = event.currentTarget.checked; setDraft( { ...draft, subtasks: draft.subtasks.map( item => item.id === task.id ? { ...item, done: checked } : item ) } ); } } /><TextInput aria-label="Subtask title" variant="unstyled" maxLength={ 300 } required value={ task.title } className={ task.done ? 'completed-task' : '' } style={ { flex: 1 } } onChange={ event => { const title = event.currentTarget.value; setDraft( { ...draft, subtasks: draft.subtasks.map( item => item.id === task.id ? { ...item, title } : item ) } ); } } /><Tooltip label="Remove subtask"><ActionIcon aria-label="Remove subtask" color="gray" variant="subtle" onClick={ () => { setDraft( { ...draft, subtasks: draft.subtasks.filter( item => item.id !== task.id ) } ); } }><Trash2 size={ 14 } /></ActionIcon></Tooltip></Group> ) }</Stack><Group gap="xs" mt="sm" wrap="nowrap"><TextInput aria-label="New subtask" placeholder="Add a subtask" maxLength={ 300 } value={ subtask } style={ { flex: 1 } } onChange={ event => { setSubtask( event.currentTarget.value ); } } onKeyDown={ event => { if ( event.key === 'Enter' ) { event.preventDefault(); addSubtask(); } } } /><Tooltip label="Add subtask"><ActionIcon aria-label="Add subtask" size="lg" variant="light" disabled={ draft.subtasks.length >= 100 || !subtask.trim() } onClick={ addSubtask }><Plus size={ 18 } /></ActionIcon></Tooltip></Group></section>
                    <section><Group justify="space-between" mb="sm"><Text size="sm" fw={ 600 }>Images</Text><FileButton accept="image/png,image/jpeg,image/webp" onChange={ file => { if ( !file ) { return; } void action( async () => { await saveDraft(); const form = new FormData(); form.append( 'file', file ); apply( await api<Ticket>( `${ path }/images`, 'POST', form ) ); } ); } }>{ props => <Button { ...props } size="xs" variant="subtle" disabled={ busy || draft.images.length >= 6 } leftSection={ <ImagePlus size={ 15 } /> }>Add image</Button> }</FileButton></Group><div className="attachment-grid">{ draft.images.map( id => <div className="attachment" key={ id }><button type="button" aria-label="View attached image" onClick={ () => { setPreview( id ); } }><img src={ imageUrl( id ) } alt="Ticket attachment" /></button><Tooltip label="Remove image"><ActionIcon className="attachment-remove" aria-label="Remove image" size="sm" color="red" variant="filled" onClick={ () => { void action( async () => { await saveDraft(); apply( await api<Ticket>( `${ path }/images/${ id }`, 'DELETE' ) ); } ); } }><Trash2 size={ 13 } /></ActionIcon></Tooltip></div> ) }</div></section>
                    <section><Text size="sm" fw={ 600 } mb="md"><MessageSquare size={ 15 } className="inline-icon" /> Comments</Text><Stack gap="md">{ draft.comments.map( item =>
                    {
                        const author = data.members.find( member => member.id === item.authorId );
                        const canDelete = item.authorId === account.id || account.isAdmin;
                        return <div className="comment" key={ item.id }><Avatar size={ 28 } radius="xl" src={ imageUrl( author?.avatarId ) }>{ author?.name.slice( 0, 1 ) ?? '?' }</Avatar><div className="comment-body"><Group justify="space-between" gap="xs"><Text size="xs" fw={ 600 }>{ author?.name ?? 'Former member' }</Text><Text size="xs" c="dimmed">{ new Date( item.createdAt ).toLocaleString() }</Text></Group><p>{ item.text }</p></div>{ canDelete && <Tooltip label="Delete comment"><ActionIcon aria-label="Delete comment" variant="subtle" color="gray" size="sm" onClick={ () => { void action( async () => { await saveDraft(); apply( await api<Ticket>( `${ path }/comments/${ item.id }`, 'DELETE' ) ); } ); } }><Trash2 size={ 13 } /></ActionIcon></Tooltip> }</div>;
                    } ) }<Textarea aria-label="New comment" placeholder="Write a comment" minRows={ 2 } maxLength={ 4000 } value={ comment } onChange={ event => { setComment( event.currentTarget.value ); } } /><Group justify="flex-end"><Button size="xs" variant="light" leftSection={ <Send size={ 14 } /> } disabled={ !comment.trim() } onClick={ () => { void action( async () => { await saveDraft(); apply( await api<Ticket>( `${ path }/comments`, 'POST', { text: comment } ) ); setComment( '' ); } ); } }>Post comment</Button></Group></Stack></section>
                </Stack></div><aside className="ticket-properties"><Stack><Select label="Status" value={ draft.columnId } allowDeselect={ false } data={ data.board.columns.map( column => ( { value: column.id, label: column.name } ) ) } onChange={ value => { setDraft( { ...draft, columnId: value ?? draft.columnId } ); } } /><Select label="Priority" value={ draft.priority } allowDeselect={ false } data={ [ 'low', 'normal', 'high', 'urgent' ].map( value => ( { value, label: value[ 0 ].toUpperCase() + value.slice( 1 ) } ) ) } onChange={ value => { setDraft( { ...draft, priority: value ?? 'normal' } ); } } /><Select label="Assignee" clearable searchable placeholder="Unassigned" value={ draft.assigneeId } data={ data.members.map( member => ( { value: member.id, label: member.name } ) ) } onChange={ value => { setDraft( { ...draft, assigneeId: value } ); } } /><TextInput label="Due date" type="date" value={ draft.dueDate?.slice( 0, 10 ) ?? '' } onChange={ event => { setDraft( { ...draft, dueDate: event.currentTarget.value ? `${ event.currentTarget.value }T23:59:59Z` : null } ); } } /></Stack></aside></div>
            </fieldset><Group className="modal-footer" justify="space-between"><Group gap="xs"><Tooltip label="Delete ticket"><ActionIcon color="red" variant="subtle" disabled={ busy } aria-label="Delete ticket" onClick={ () => { setConfirm( 'delete' ); } }><Trash2 size={ 17 } /></ActionIcon></Tooltip><Button variant="default" size="xs" disabled={ busy } leftSection={ draft.archived ? <ArchiveRestore size={ 15 } /> : <Archive size={ 15 } /> } onClick={ () => { void action( async () => { const saved = await saveDraft(); apply( await api<Ticket>( path, 'PUT', { ...ticketBody( saved ), archived: !saved.archived } ) ); onClose(); } ); } }>{ draft.archived ? 'Restore' : 'Archive' }</Button></Group><Button type="submit" loading={ busy } leftSection={ <Save size={ 15 } /> }>Save changes</Button></Group>
        </form>
        <Modal opened={ confirm !== null } onClose={ () => { setConfirm( null ); } } title={ confirm === 'delete' ? 'Delete ticket?' : 'Discard changes?' } centered><Stack><Text size="sm">{ confirm === 'delete' ? 'The ticket, comments and images will be permanently deleted.' : 'Your unsaved changes will be lost.' }</Text><Group justify="flex-end"><Button variant="default" onClick={ () => { setConfirm( null ); } }>Cancel</Button><Button color="red" loading={ busy } onClick={ () => { if ( confirm === 'discard' ) { onClose(); return; } void action( async () => { await api( path, 'DELETE' ); onDelete( ticket.id ); } ); } }>{ confirm === 'delete' ? 'Delete permanently' : 'Discard' }</Button></Group></Stack></Modal>
        <Modal opened={ preview !== null } onClose={ () => { setPreview( null ); } } title="Attached image" size="xl" centered>{ preview && <img className="image-preview" src={ imageUrl( preview ) } alt="Ticket attachment" /> }</Modal>
    </Modal>;
}