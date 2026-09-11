import { cva } from 'class-variance-authority';

export { default as Button } from './Button.vue';

/**
 * shadcn Button 变体。默认附加原版动画语义：
 * - .btn-anim：hover scale 1.02 / 按压 0.97（base.css）
 * - 点击松开三段回弹 nya-bounce（Button.vue 内置）
 * 全局 hover 0.92 / 按压 0.82 透明度由 base.css 的 button 规则自动叠加。
 */
export const buttonVariants = cva(
  'inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md font-medium select-none ' +
  'outline-none focus-visible:ring-[2px] focus-visible:ring-ring/60 disabled:pointer-events-none disabled:opacity-50 ' +
  '[&_svg]:shrink-0',
  {
    variants: {
      variant: {
        default: 'bg-primary text-primary-foreground',
        secondary: 'bg-secondary text-secondary-foreground border border-border',
        outline: 'border border-border bg-transparent text-foreground hover:bg-accent',
        ghost: 'text-foreground hover:bg-accent',
        destructive: 'bg-destructive text-destructive-foreground',
        link: 'text-primary underline-offset-4 hover:underline',
      },
      size: {
        default: 'h-[34px] px-3.5 text-[13px]',
        sm: 'h-7 px-2.5 text-[12px]',
        lg: 'h-10 px-6 text-[15px]',
        icon: 'size-9',
        'icon-sm': 'size-7',
      },
    },
    defaultVariants: { variant: 'default', size: 'default' },
  },
);
