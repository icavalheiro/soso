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
  fontFamily: 'DM Sans Variable, sans-serif', primaryColor: 'wood', primaryShade: 7, autoContrast: true, defaultRadius: 6,
  colors: {
    wood: [ '#faf4e8', '#f1e5cc', '#e6cea2', '#d8b773', '#c99a48', '#b58532', '#a37526', '#8c641f', '#735119', '#5c4014' ],
  },
  headings: { fontFamily: 'DM Sans Variable, sans-serif' },
} );

createRoot( document.getElementById( 'root' )! ).render(
  <StrictMode>
    <MantineProvider theme={ theme } defaultColorScheme="auto">
      <Notifications position="bottom-right" />
      <App />
    </MantineProvider>
  </StrictMode>,
);
