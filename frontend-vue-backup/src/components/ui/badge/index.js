import { cva } from 'class-variance-authority';

export { default as Badge } from './Badge.vue';

export const badgeVariants = cva(
  'inline-flex items-center justify-center rounded-sm border px-2 py-0.5 text-[10px] font-bold w-fit whitespace-nowrap',
  {
    variants: {
      variant: {
        default: 'border-transparent bg-badge text-primary',
        secondary: 'border-transparent bg-secondary text-secondary-foreground',
        outline: 'text-foreground',
        destructive: 'border-transparent bg-destructive text-destructive-foreground',
      },
    },
    defaultVariants: { variant: 'default' },
  },
);
