import { BookOpen, Briefcase, Check, Code, Columns3, GraduationCap, Heart, House, Plane, Rocket, Star, Target, Wallet } from 'lucide-react';
import { ActionIcon, ColorSwatch, Group, Stack, Text, Tooltip, useComputedColorScheme, useMantineTheme } from '@mantine/core';

const boardColors = [
    { value: 'teal', label: 'Teal' },
    { value: 'blue', label: 'Blue' },
    { value: 'cyan', label: 'Cyan' },
    { value: 'green', label: 'Green' },
    { value: 'grape', label: 'Purple' },
    { value: 'pink', label: 'Pink' },
    { value: 'orange', label: 'Orange' },
    { value: 'gray', label: 'Gray' },
];

const boardIcons = [
    { value: 'columns', label: 'Columns', Icon: Columns3 },
    { value: 'briefcase', label: 'Work', Icon: Briefcase },
    { value: 'house', label: 'Home', Icon: House },
    { value: 'heart', label: 'Health', Icon: Heart },
    { value: 'star', label: 'Favorites', Icon: Star },
    { value: 'rocket', label: 'Projects', Icon: Rocket },
    { value: 'code', label: 'Code', Icon: Code },
    { value: 'book', label: 'Reading', Icon: BookOpen },
    { value: 'graduation-cap', label: 'Studies', Icon: GraduationCap },
    { value: 'plane', label: 'Travel', Icon: Plane },
    { value: 'wallet', label: 'Finances', Icon: Wallet },
    { value: 'target', label: 'Goals', Icon: Target },
];

function useBoardColor ( color: string )
{
    const theme = useMantineTheme();
    const scheme = useComputedColorScheme( 'light' );
    const name = boardColors.find( option => option.value === color )?.value ?? 'teal';
    return theme.colors[ name ][ scheme === 'dark' ? 4 : 7 ];
}

export function BoardIcon ( { icon, color = 'teal', size = 18 }: { icon?: string; color?: string; size?: number; } )
{
    const Icon = boardIcons.find( option => option.value === icon )?.Icon ?? Columns3;
    const iconColor = useBoardColor( color );
    return <Icon size={ size } color={ iconColor } aria-hidden="true" />;
}

export function BoardIconPicker ( { value, color = 'teal', onChange }: { value: string; color?: string; onChange: ( value: string ) => void; } )
{
    const iconColor = useBoardColor( color );
    return <Stack gap={ 6 }><Group gap="xs"><Text size="sm" fw={ 500 } id="board-icon-label">Icon</Text><BoardIcon icon={ value } color={ color } size={ 20 } /></Group><div className="board-icon-picker" role="group" aria-labelledby="board-icon-label">{ boardIcons.map( ( { value: icon, label, Icon } ) => <Tooltip key={ icon } label={ label }><ActionIcon type="button" aria-label={ `Board icon: ${ label }` } aria-pressed={ value === icon } color={ color } variant={ value === icon ? 'light' : 'default' } size={ 36 } onClick={ () => { onChange( icon ); } }><Icon size={ 19 } color={ iconColor } /></ActionIcon></Tooltip> ) }</div></Stack>;
}

export function BoardColorPicker ( { value, onChange }: { value: string; onChange: ( value: string ) => void; } )
{
    const theme = useMantineTheme();
    const scheme = useComputedColorScheme( 'light' );
    const shade = scheme === 'dark' ? 4 : 7;
    return <Stack gap={ 6 }><Text size="sm" fw={ 500 } id="board-color-label">Color</Text><div className="board-color-picker" role="group" aria-labelledby="board-color-label">{ boardColors.map( ( { value: color, label } ) => <Tooltip key={ color } label={ label }><ActionIcon type="button" aria-label={ `Board color: ${ label }` } aria-pressed={ value === color } variant="default" size={ 32 } onClick={ () => { onChange( color ); } }><ColorSwatch color={ theme.colors[ color ][ shade ] } size={ 24 }>{ value === color && <Check size={ 15 } color={ scheme === 'dark' ? theme.black : theme.white } /> }</ColorSwatch></ActionIcon></Tooltip> ) }</div></Stack>;
}