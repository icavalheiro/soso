import { useEffect, useState } from 'react';
import { ActionIcon, Button, Checkbox, Group, Modal, MultiSelect, Stack, Text, Textarea, TextInput, Tooltip } from '@mantine/core';
import { ArrowDown, ArrowUp, Plus, Save, Trash2 } from 'lucide-react';
import { api, newId } from './api';
import type { Board, Column, Person } from './api';
import { reportError } from './feedback';
import { BoardColorPicker, BoardIconPicker } from './BoardIcon';

export function BoardModal ( { board, onClose, onSave, onDelete }: { board?: Board; onClose: () => void; onSave: ( board: Board ) => void; onDelete: ( id: string ) => void; } )
{
    const [ name, setName ] = useState( board?.name ?? '' );
    const [ description, setDescription ] = useState( board?.description ?? '' );
    const [ icon, setIcon ] = useState( board?.icon ?? 'columns' );
    const [ color, setColor ] = useState( board?.color ?? 'teal' );
    const [ members, setMembers ] = useState<string[]>( board?.members ?? [] );
    const [ columns, setColumns ] = useState<Column[]>( board?.columns ?? [] );
    const [ people, setPeople ] = useState<Person[]>( [] );
    const [ busy, setBusy ] = useState( false );
    const [ confirmDelete, setConfirmDelete ] = useState( false );
    useEffect( () => { void api<Person[]>( '/people' ).then( setPeople ).catch( reportError ); }, [] );
    async function save ( event: React.FormEvent )
    {
        event.preventDefault();
        setBusy( true );
        try
        {
            const result = board ? await api<Board>( `/boards/${ board.id }`, 'PUT', { name, description, icon, color, members, columns, revision: board.revision } ) : await api<Board>( '/boards', 'POST', { name, description, icon, color } );
            onSave( result );
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
    function reorder ( index: number, direction: number )
    {
        const next = [ ...columns ];
        [ next[ index ], next[ index + direction ] ] = [ next[ index + direction ], next[ index ] ];
        setColumns( next );
    }
    return <Modal opened onClose={ onClose } title={ board ? 'Board settings' : 'Create board' } centered size="md"><form onSubmit={ event => { void save( event ); } }><Stack>
        <TextInput label="Name" required maxLength={ 80 } autoFocus value={ name } onChange={ event => { setName( event.currentTarget.value ); } } />
        <BoardIconPicker value={ icon } color={ color } onChange={ setIcon } />
        <BoardColorPicker value={ color } onChange={ setColor } />
        <Textarea label="Description" maxLength={ 2000 } value={ description } onChange={ event => { setDescription( event.currentTarget.value ); } } />
        { board && <><MultiSelect label="Members" searchable value={ members } onChange={ setMembers } data={ people.filter( person => person.id !== board.ownerId ).map( person => ( { value: person.id, label: person.name } ) ) } /><Text fw={ 600 } size="sm">Columns</Text>{ columns.map( ( column, index ) => <div key={ column.id }><Group gap="xs" wrap="nowrap"><TextInput aria-label={ `Column ${ index + 1 } name` } value={ column.name } required maxLength={ 60 } style={ { flex: 1 } } onChange={ event => { const name = event.currentTarget.value; setColumns( previous => previous.map( item => item.id === column.id ? { ...item, name } : item ) ); } } /><Tooltip label="Move up"><ActionIcon aria-label="Move column up" variant="subtle" disabled={ index === 0 } onClick={ () => { reorder( index, -1 ); } }><ArrowUp size={ 16 } /></ActionIcon></Tooltip><Tooltip label="Move down"><ActionIcon aria-label="Move column down" variant="subtle" disabled={ index === columns.length - 1 } onClick={ () => { reorder( index, 1 ); } }><ArrowDown size={ 16 } /></ActionIcon></Tooltip><Tooltip label="Remove column"><ActionIcon aria-label="Remove column" variant="subtle" color="red" disabled={ columns.length === 1 } onClick={ () => { setColumns( columns.filter( item => item.id !== column.id ) ); } }><Trash2 size={ 16 } /></ActionIcon></Tooltip></Group><Checkbox mt={ 7 } size="xs" label="Completed column" checked={ column.isDone } onChange={ event => { const isDone = event.currentTarget.checked; setColumns( previous => previous.map( item => item.id === column.id ? { ...item, isDone } : item ) ); } } /></div> ) }<Button variant="subtle" size="xs" leftSection={ <Plus size={ 15 } /> } disabled={ columns.length >= 20 } onClick={ () => { setColumns( [ ...columns, { id: newId(), name: 'New column', isDone: false } ] ); } }>Add column</Button></> }
        <Group justify="space-between">{ board ? <Button color="red" variant="subtle" leftSection={ <Trash2 size={ 15 } /> } onClick={ () => { setConfirmDelete( true ); } }>Delete board</Button> : <span /> }<Button type="submit" loading={ busy } leftSection={ <Save size={ 15 } /> }>Save board</Button></Group>
    </Stack></form><Modal opened={ confirmDelete } onClose={ () => { setConfirmDelete( false ); } } title="Delete board?" centered><Stack><Text size="sm">All tickets, comments and images in this board will be permanently deleted.</Text><Button color="red" loading={ busy } onClick={ () => { setBusy( true ); void api( `/boards/${ board!.id }`, 'DELETE' ).then( () => { onDelete( board!.id ); } ).catch( reportError ).finally( () => { setBusy( false ); } ); } }>Delete permanently</Button></Stack></Modal></Modal>;
}