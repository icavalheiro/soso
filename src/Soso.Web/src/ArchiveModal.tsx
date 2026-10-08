import { useState } from 'react';
import { ActionIcon, Group, Modal, SegmentedControl, Stack, Text, TextInput, Tooltip } from '@mantine/core';
import { Archive, ArchiveRestore, Search } from 'lucide-react';
import { api, ticketBody } from './api';
import type { BoardData, Ticket } from './api';
import { reportError } from './feedback';
import { useLanguage } from './useLanguage';

export function ArchiveModal ( { data, onClose, onOpen, onChange }: { data: BoardData; onClose: () => void; onOpen: ( ticket: Ticket ) => void; onChange: ( ticket: Ticket ) => void; } )
{
    const { t } = useLanguage();
    const [ mode, setMode ] = useState( 'done' );
    const [ search, setSearch ] = useState( '' );
    const [ busy, setBusy ] = useState( false );
    const doneColumns = data.board.columns.filter( column => column.isDone ).map( column => column.id );
    const archiveColumns = [ ...data.board.columns, ...( data.board.removedColumns?.map( item => item.column ) ?? [] ) ];
    const tickets = data.tickets.filter( ticket =>
    {
        const matchesMode = mode === 'archived' ? ticket.archived : !ticket.archived && doneColumns.includes( ticket.columnId );
        return matchesMode && ticket.title.toLowerCase().includes( search.toLowerCase() );
    } );
    async function toggle ( ticket: Ticket )
    {
        setBusy( true );
        try
        {
            onChange( await api<Ticket>( `/boards/${ ticket.boardId }/tickets/${ ticket.id }`, 'PUT', { ...ticketBody( ticket ), archived: !ticket.archived } ) );
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
    return <Modal opened onClose={ onClose } title={ t( 'Archive list' ) } centered size="lg"><Stack><SegmentedControl value={ mode } onChange={ setMode } data={ [ { value: 'done', label: t( 'Done' ) }, { value: 'archived', label: t( 'Archived' ) } ] } /><TextInput aria-label={ t( 'Search archive' ) } placeholder={ t( 'Search tickets' ) } leftSection={ <Search size={ 16 } /> } value={ search } onChange={ event => { setSearch( event.currentTarget.value ); } } />{ tickets.length === 0 && <Text size="sm" c="dimmed" ta="center" py="xl">{ t( mode === 'done' ? 'No completed tickets' : 'No archived tickets' ) }</Text> }{ tickets.map( ticket => <Group key={ ticket.id } className="archive-row" wrap="nowrap"><button className="archive-title" onClick={ () => { onClose(); onOpen( ticket ); } }>{ ticket.title }<small>{ archiveColumns.find( column => column.id === ticket.columnId )?.name }</small></button><Tooltip label={ t( ticket.archived ? 'Restore ticket' : 'Archive ticket' ) }><ActionIcon aria-label={ t( ticket.archived ? 'Restore ticket' : 'Archive ticket' ) } variant="subtle" disabled={ busy } onClick={ () => { void toggle( ticket ); } }>{ ticket.archived ? <ArchiveRestore size={ 18 } /> : <Archive size={ 18 } /> }</ActionIcon></Tooltip></Group> ) }</Stack></Modal>;
}
