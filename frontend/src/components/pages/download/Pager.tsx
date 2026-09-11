/*
 * 分页条（DownloadPage.axaml 上一页/下一页：ControlBg 容器 + PanelBg 按钮）。
 * 自 Vue 版 Pager.vue 平移。
 */
import { Button } from '@/components/ui/button'

interface Props {
  page: number
  totalPages: number
  onPrev: () => void
  onNext: () => void
}

export default function Pager({ page, totalPages, onPrev, onNext }: Props) {
  return (
    <div className="flex items-center justify-between rounded-sm bg-muted px-4 py-2">
      <Button variant="secondary" size="sm" disabled={page <= 1} onClick={onPrev}>◀ 上一页</Button>
      <span className="text-[13px] text-hint-text">第 {page} 页 / 共 {totalPages} 页</span>
      <Button variant="secondary" size="sm" disabled={page >= totalPages} onClick={onNext}>下一页</Button>
    </div>
  )
}
