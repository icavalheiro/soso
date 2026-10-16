import type { MantineThemeOverride } from '@mantine-vue/core';

// Wood palette, DM Sans and geometry. Single source for the Vue tree.
export const vueTheme: MantineThemeOverride = {
    fontFamily: 'DM Sans Variable, sans-serif',
    primaryColor: 'wood',
    primaryShade: 7,
    autoContrast: true,
    defaultRadius: 6,
    colors: {
        wood: [ '#faf4e8', '#f1e5cc', '#e6cea2', '#d8b773', '#c99a48', '#b58532', '#a37526', '#8c641f', '#735119', '#5c4014' ],
    },
    headings: { fontFamily: 'DM Sans Variable, sans-serif' },
};
