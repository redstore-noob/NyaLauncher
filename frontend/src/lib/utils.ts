import { clsx, type ClassValue } from 'clsx'
import { twMerge } from 'tailwind-merge'

// shadcn 标准工具：合并 className（Tailwind 冲突裁决）
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
