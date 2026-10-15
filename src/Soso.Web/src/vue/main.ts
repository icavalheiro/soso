import { createApp } from 'vue';
import '@mantine-vue/core/styles.css';
import '@mantine-vue/notifications/styles.css';
import '@fontsource-variable/dm-sans';
import '../styles.css';
import App from './App.vue';
import { createLanguageState, languageKey } from './language';

const app = createApp( App );
app.provide( languageKey, createLanguageState() );
app.mount( '#app' );
