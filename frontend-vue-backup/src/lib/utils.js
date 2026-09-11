import { clsx } from 'clsx';
import { twMerge } from 'tailwind-merge';

/** 合并 Tailwind 类名（shadcn 约定的 cn 工具） */
export function cn(...inputs) {
  return twMerge(clsx(inputs));
}
