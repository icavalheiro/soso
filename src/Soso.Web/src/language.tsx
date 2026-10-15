import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { LanguageContext, resolveLanguage, type Language } from './language-context';

import { spanishTranslations, translations } from './translations';

export function LanguageProvider ( { children }: { children: ReactNode; } )
{
    const [ language, setLanguageState ] = useState<Language>( () =>
    {
        const saved = localStorage.getItem( 'soso-language' );
        const preferredLanguages = navigator.languages.length > 0 ? navigator.languages : [ navigator.language ];
        return resolveLanguage( saved, preferredLanguages );
    } );
    useEffect( () => { document.documentElement.lang = language; }, [ language ] );
    function setLanguage ( next: Language )
    {
        localStorage.setItem( 'soso-language', next );
        setLanguageState( next );
        document.documentElement.lang = next;
    }
    const t = ( text: string ) => language === 'pt-BR' ? translations[ text ] ?? text : language === 'es-MX' ? spanishTranslations[ text ] ?? text : text;
    return <LanguageContext.Provider value={ { language, setLanguage, t } }>{ children }</LanguageContext.Provider>;
}
