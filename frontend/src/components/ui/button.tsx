import * as React from 'react'
import { Slot } from '@radix-ui/react-slot'
import { cva, type VariantProps } from 'class-variance-authority'
import { cn } from '@/lib/utils'

// Button：语义与 Vue 版 src/components/ui/button 一致——
// 默认带 .btn-anim（hover 1.02 / 按压 0.97）与点击三段回弹（.btn-bounce，nya-bounce 300ms）。
const buttonVariants = cva(
  "btn-anim inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md text-[13px] font-medium transition-[color,box-shadow] disabled:pointer-events-none disabled:opacity-50 [&_svg]:pointer-events-none [&_svg:not([class*='size-'])]:size-4 shrink-0 [&_svg]:shrink-0 outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50",
  {
    variants: {
      variant: {
        default: 'bg-primary text-primary-foreground shadow-xs hover:bg-primary/90',
        secondary: 'bg-secondary text-secondary-foreground border border-border hover:bg-secondary/80',
        outline: 'border border-input bg-background hover:bg-accent hover:text-accent-foreground',
        ghost: 'hover:bg-accent hover:text-accent-foreground',
        destructive: 'bg-destructive text-white shadow-xs hover:bg-destructive/90',
        link: 'text-primary underline-offset-4 hover:underline',
      },
      size: {
        default: 'h-9 px-4 py-2 has-[>svg]:px-3',
        sm: 'h-8 rounded-md gap-1.5 px-3 has-[>svg]:px-2.5',
        lg: 'h-10 rounded-md px-6 has-[>svg]:px-4',
        icon: 'size-9',
      },
    },
    defaultVariants: {
      variant: 'default',
      size: 'default',
    },
  }
)

// 点击三段回弹：pointerup 时追加 btn-bounce 类（nya-bounce 0.97→1.06→0.98→1，300ms）
function useBounce() {
  const [bouncing, setBouncing] = React.useState(false)
  const timer = React.useRef<number | undefined>(undefined)
  React.useEffect(() => () => window.clearTimeout(timer.current), [])
  const onPointerUp = React.useCallback(() => {
    setBouncing(false)
    window.clearTimeout(timer.current)
    // 下一帧再挂类，确保动画从 0.97 关键帧重播
    requestAnimationFrame(() => {
      setBouncing(true)
      timer.current = window.setTimeout(() => setBouncing(false), 300)
    })
  }, [])
  return { bouncing, onPointerUp }
}

function Button({
  className,
  variant,
  size,
  asChild = false,
  disableBounce = false,
  onPointerUp,
  ...props
}: React.ComponentProps<'button'> &
  VariantProps<typeof buttonVariants> & {
    asChild?: boolean
    /** 关闭点击三段回弹（保留 hover/按压缩放） */
    disableBounce?: boolean
  }) {
  const bounce = useBounce()
  const Comp: React.ElementType = asChild ? Slot : 'button'
  return (
    <Comp
      data-slot="button"
      className={cn(buttonVariants({ variant, size, className }), bounce.bouncing && 'btn-bounce')}
      onPointerUp={(e: React.PointerEvent<HTMLButtonElement>) => {
        if (!disableBounce && !e.defaultPrevented) bounce.onPointerUp()
        onPointerUp?.(e)
      }}
      {...props}
    />
  )
}

export { Button, buttonVariants }
