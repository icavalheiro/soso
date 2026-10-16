import { BookOpen, Briefcase, CalendarDays, Code, Columns3, Coffee, Dumbbell, GraduationCap, Heart, House, Leaf, Lightbulb, Music, Plane, Rocket, ShoppingCart, Star, Target, Users, Wallet } from 'lucide-vue-next';
import { useComputedColorScheme, useMantineTheme } from '@mantine-vue/core';
import { computed, toValue, type Component, type ComputedRef, type MaybeRefOrGetter } from 'vue';

export const boardColors = [
    { value: 'teal', label: 'Teal' },
    { value: 'blue', label: 'Blue' },
    { value: 'cyan', label: 'Cyan' },
    { value: 'green', label: 'Green' },
    { value: 'grape', label: 'Purple' },
    { value: 'pink', label: 'Pink' },
    { value: 'orange', label: 'Orange' },
    { value: 'gray', label: 'Gray' },
];

export const boardIcons: Array<{ value: string; label: string; Icon: Component }> = [
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
    { value: 'calendar', label: 'Calendar', Icon: CalendarDays },
    { value: 'shopping', label: 'Shopping', Icon: ShoppingCart },
    { value: 'music', label: 'Music', Icon: Music },
    { value: 'fitness', label: 'Fitness', Icon: Dumbbell },
    { value: 'team', label: 'Team', Icon: Users },
    { value: 'ideas', label: 'Ideas', Icon: Lightbulb },
    { value: 'nature', label: 'Nature', Icon: Leaf },
    { value: 'coffee', label: 'Coffee', Icon: Coffee },
];

export function useBoardColor ( color: MaybeRefOrGetter<string> ): ComputedRef<string>
{
    const theme = useMantineTheme();
    const scheme = useComputedColorScheme( 'light' );
    return computed( () =>
    {
        const name = boardColors.find( option => option.value === toValue( color ) )?.value ?? 'teal';
        return theme.value.colors[ name ][ scheme.value === 'dark' ? 4 : 7 ];
    } );
}
