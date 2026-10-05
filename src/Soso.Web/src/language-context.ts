import { createContext } from 'react';

export type Language = 'en' | 'pt-BR' | 'es-MX';

export const LanguageContext = createContext<{ language: Language; setLanguage: ( language: Language ) => void; t: ( text: string ) => string; } | null>( null );
