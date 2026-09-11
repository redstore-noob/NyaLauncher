/*
 * NyaCheckbox —— 自绘三态勾选框（自 Vue 版 ModpackView scoped .nya-checkbox 提升）：
 * checked=true 勾、=false 空、='indeterminate' 半选（横线）。
 */
import Icon from '@/components/overlay/Icon'

export interface NyaCheckboxProps {
  checked: boolean | 'indeterminate'
  title?: string
  onChange?: () => void
}

export default function NyaCheckbox({ checked, title, onChange }: NyaCheckboxProps) {
  return (
    <button
      type="button"
      title={title}
      className={`nya-checkbox ${checked === true ? 'is-checked' : ''} ${checked === 'indeterminate' ? 'is-indeterminate' : ''}`}
      onClick={(e) => { e.preventDefault(); onChange?.() }}
    >
      {checked === true ? <Icon name="success" size={12} /> : null}
      {checked === 'indeterminate' ? <span className="nya-checkbox-dash" /> : null}
    </button>
  )
}
