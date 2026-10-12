import { useDeferredValue, useEffect, useState } from 'react';
import type { RefObject } from 'react';
import { TextInput } from '@mantine/core';
import { Search } from 'lucide-react';
import { useLanguage } from './useLanguage';

export function SearchInput ( { inputRef, onQuery }: { inputRef: RefObject<HTMLInputElement | null>; onQuery: ( query: string ) => void } )
{
    const { t } = useLanguage();
    const [ value, setValue ] = useState( '' );
    const deferred = useDeferredValue( value );
    useEffect( () => { onQuery( deferred ); }, [ deferred, onQuery ] );
    return <TextInput ref={ inputRef } className="topbar-search" aria-label={ t( 'Search tickets' ) } placeholder={ t( 'Search cards...' ) } leftSection={ <Search size={ 14 } /> } size="xs" value={ value } onChange={ event => { setValue( event.currentTarget.value ); } } />;
}
