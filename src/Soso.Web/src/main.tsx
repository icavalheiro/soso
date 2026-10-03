import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { MantineProvider, createTheme } from '@mantine/core';
import { Notifications } from '@mantine/notifications';
import '@mantine/core/styles.css';
import '@mantine/notifications/styles.css';
import '@fontsource-variable/dm-sans';
import './styles.css';
import App from './Workspace.tsx';

const theme = createTheme( {
    fontFamily: 'DM Sans Variable, sans-serif', primaryColor: 'teal', defaultRadius: 6,
    headings: { fontFamily: 'DM Sans Variable, sans-serif' },
} );

createRoot( document.getElementById( 'root' )! ).render(
    <StrictMode>
        <MantineProvider theme={ theme } defaultColorScheme="light">
            <Notifications position="bottom-right" />
            <App />
        </MantineProvider>
    </StrictMode>,
);
