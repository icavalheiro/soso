import { BookOpen, Briefcase, Code, Columns3, GraduationCap, Heart, House, Plane, Rocket, Star, Target, Wallet } from 'lucide-react';
import { ActionIcon, Stack, Text, Tooltip } from '@mantine/core';

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

export function BoardIcon ( { icon, size = 18 }: { icon?: string; size?: number; } )
{
    const Icon = boardIcons.find( option => option.value === icon )?.Icon ?? Columns3;
    return <Icon size={ size } aria-hidden="true" />;
}

export function BoardIconPicker ( { value, onChange }: { value: string; onChange: ( value: string ) => void; } )
{
    return <Stack gap={ 6 }><Text size="sm" fw={ 500 } id="board-icon-label">Icon</Text><div className="board-icon-picker" role="group" aria-labelledby="board-icon-label">{ boardIcons.map( ( { value: icon, label, Icon } ) => <Tooltip key={ icon } label={ label }><ActionIcon type="button" aria-label={ `Board icon: ${ label }` } aria-pressed={ value === icon } variant={ value === icon ? 'filled' : 'default' } size={ 36 } onClick={ () => { onChange( icon ); } }><Icon size={ 19 } /></ActionIcon></Tooltip> ) }</div></Stack>;
}