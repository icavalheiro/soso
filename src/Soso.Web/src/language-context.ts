import { createContext } from 'react';
import type { Language } from './translations';

export type { Language } from './translations';
export { isLanguage, resolveLanguage } from './translations';

export const LanguageContext = createContext<{ language: Language; setLanguage: ( language: Language ) => void; t: ( text: string ) => string; } | null>( null );
