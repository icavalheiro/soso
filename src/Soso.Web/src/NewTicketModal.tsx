import { useState } from 'react';
import { Button, Modal, Select, Stack, TextInput } from '@mantine/core';
import type { Column } from './api';
import { useLanguage } from './useLanguage';

export function NewTicketModal ( { column, columns, onClose, onCreate }: { column: string; columns: Column[]; onClose: () => void; onCreate: ( title: string, columnId: string ) => Promise<void>; } )
{
    const { t } = useLanguage();
    const [ title, setTitle ] = useState( '' );
    const [ selectedColumn, setSelectedColumn ] = useState( column );
    const [ busy, setBusy ] = useState( false );
    async function submit ( event: React.FormEvent )
    {
        event.preventDefault();
        if ( busy || !title.trim() )
        {
            return;
        }
        setBusy( true );
        try
        {
            await onCreate( title, selectedColumn );
        }
        finally
        {
            setBusy( false );
        }
    }
    return <Modal opened onClose={ onClose } title={ t( 'New ticket' ) } centered><form onSubmit={ event => { void submit( event ); } }><Stack><TextInput label={ t( 'Title' ) } placeholder={ t( 'What needs to happen?' ) } required maxLength={ 160 } value={ title } autoFocus onChange={ event => { setTitle( event.currentTarget.value ); } } /><Select label={ t( 'Column' ) } value={ selectedColumn } onChange={ value => { if ( value ) { setSelectedColumn( value ); } } } data={ columns.map( column => ( { value: column.id, label: column.name } ) ) } allowDeselect={ false } /><Button type="submit" loading={ busy } disabled={ !title.trim() }>{ t( 'Create ticket' ) }</Button></Stack></form></Modal>;
}
