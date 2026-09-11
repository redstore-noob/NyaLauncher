/*
 * 底部左侧警示滑条（NyaAlertHost.axaml：左下滑入，强调条颜色随级别）。
 * React 等价 Vue transition：可见时挂 .nya-alert-enter / 关闭时 .nya-alert-leave（CSS animation，参数与原版一致）。
 */
import Icon from './Icon'
import { useOverlayState, mapSeverity, hideAlertNow } from './state'

export default function NyaAlert() {
  const state = useOverlayState()
  const show = state.alertVisible && !!state.alert
  const strip = mapSeverity(state.alert?.severity)

  return (
    <div
      className={[
        'fixed left-5 bottom-[46px] z-[940] flex max-w-[440px] items-stretch gap-0',
        'rounded-xl border border-input bg-popover py-[9px] shadow-lg',
        show ? 'nya-alert-enter' : 'nya-alert-leave',
      ].join(' ')}
      style={{ visibility: show ? 'visible' : 'hidden' }}
      aria-hidden={!show}
    >
      <span className="mx-0 my-px w-1 shrink-0 rounded-full" style={{ background: strip.color }} />
      <span className="ml-[11px] flex flex-none items-center" style={{ color: strip.color }}>
        <Icon name={strip.icon} size={18} />
      </span>
      <span className="mx-2.5 self-center text-xs leading-[17px] text-body-text break-words select-text">
        {state.alert?.message}
      </span>
      <button
        className="mr-2 h-[26px] w-[26px] flex-none self-center inline-flex items-center justify-center rounded-md text-hint-text transition-colors hover:bg-muted hover:text-foreground"
        title="关闭"
        onClick={hideAlertNow}
      >
        <Icon name="close" size={12} />
      </button>
    </div>
  )
}
