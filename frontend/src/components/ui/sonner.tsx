import type * as React from 'react'
import { Toaster as Sonner, type ToasterProps } from 'sonner'

// Toaster：主题跟随 body 的 data-theme / data-mode（shadcn.css 已配 --normal-bg 等变量映射）
function Toaster(props: ToasterProps) {
  return (
    <Sonner
      theme="system"
      className="toaster group"
      style={
        {
          '--normal-bg': 'var(--dialog-bg)',
          '--normal-text': 'var(--primary-text)',
          '--normal-border': 'var(--medium-border)',
          '--border-radius': 'var(--radius-lg)',
        } as React.CSSProperties
      }
      {...props}
    />
  )
}

export { Toaster }
export { toast } from 'sonner'
