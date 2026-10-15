import { inject, ref, type InjectionKey, type Ref } from 'vue';
import { resolveLanguage, spanishTranslations, translations, type Language } from '../translations';

export type LanguageState = {
    language: Ref<Language>;
    setLanguage: ( language: Language ) => void;
    t: ( text: string ) => string;
};

export const languageKey: InjectionKey<LanguageState> = Symbol( 'soso-language' );

// Mirrors the React LanguageProvider (src/language.tsx): same storage key,
// same navigator-based initial resolution, same documentElement.lang side effect,
// same t() fallback semantics. Provided once by the Vue entry (vue/main.ts).
export function createLanguageState (): LanguageState
{
    const saved = localStorage.getItem( 'soso-language' );
    const preferredLanguages = navigator.languages.length > 0 ? [ ...navigator.languages ] : [ navigator.language ];
    const language = ref<Language>( resolveLanguage( saved, preferredLanguages ) );
    document.documentElement.lang = language.value;

    function setLanguage ( next: Language )
    {
        localStorage.setItem( 'soso-language', next );
        language.value = next;
        document.documentElement.lang = next;
    }

    const t = ( text: string ) => language.value === 'pt-BR'
        ? translations[ text ] ?? text
        : language.value === 'es-MX'
            ? spanishTranslations[ text ] ?? text
            : text;

    return { language, setLanguage, t };
}

export function useLanguage (): LanguageState
{
    const state = inject( languageKey );
    if ( !state )
    {
        throw new Error( 'useLanguage must be used within a provided language state' );
    }
    return state;
}
