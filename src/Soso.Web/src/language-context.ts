import { createContext } from 'react';

export type Language = 'en' | 'pt-BR' | 'es-MX';

const supportedLanguages: readonly Language[] = [ 'en', 'pt-BR', 'es-MX' ];

export function isLanguage ( value: string | null ): value is Language
{
    return value !== null && supportedLanguages.includes( value as Language );
}

export function resolveLanguage ( savedLanguage: string | null, preferredLanguages: readonly string[] ): Language
{
    if ( isLanguage( savedLanguage ) )
    {
        return savedLanguage;
    }

    for ( const preferredLanguage of preferredLanguages )
    {
        const baseLanguage = preferredLanguage.trim().toLowerCase().split( /[-_]/, 1 )[ 0 ];
        if ( baseLanguage === 'pt' )
        {
            return 'pt-BR';
        }
        if ( baseLanguage === 'es' )
        {
            return 'es-MX';
        }
        if ( baseLanguage === 'en' )
        {
            return 'en';
        }
    }

    return 'en';
}

export const LanguageContext = createContext<{ language: Language; setLanguage: ( language: Language ) => void; t: ( text: string ) => string; } | null>( null );
