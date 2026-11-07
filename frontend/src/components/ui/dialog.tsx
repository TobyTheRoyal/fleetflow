import * as DialogPrimitive from '@radix-ui/react-dialog'
import { X } from 'lucide-react'
import { useRef, type ReactNode, type RefObject } from 'react'
export function Dialog({
  open,
  onOpenChange,
  title,
  description,
  children,
  busy = false,
  wide = false,
  returnFocusTo,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: string
  children: ReactNode
  busy?: boolean
  wide?: boolean
  returnFocusTo?: RefObject<HTMLElement | null>
}) {
  const returnFocus = useRef<HTMLElement | null>(null)
  return (
    <DialogPrimitive.Root
      open={open}
      onOpenChange={(value) => {
        if (!busy) onOpenChange(value)
      }}
    >
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="dialog-overlay" />
        <DialogPrimitive.Content
          onOpenAutoFocus={() => {
            returnFocus.current =
              document.activeElement instanceof HTMLElement
                ? document.activeElement
                : null
          }}
          onCloseAutoFocus={(event) => {
            const explicit = returnFocusTo?.current
            const target = explicit?.isConnected
              ? explicit
              : returnFocus.current?.isConnected &&
                  returnFocus.current !== document.body
                ? returnFocus.current
                : document.getElementById('main-content')
            if (target?.isConnected) {
              event.preventDefault()
              target.focus()
            }
          }}
          className={`dialog-content ${wide ? 'dialog-wide' : ''}`}
          onInteractOutside={(event) => {
            if (busy) event.preventDefault()
          }}
          onEscapeKeyDown={(event) => {
            if (busy) event.preventDefault()
          }}
        >
          <div className="dialog-heading">
            <div>
              <DialogPrimitive.Title className="text-xl font-semibold tracking-tight">
                {title}
              </DialogPrimitive.Title>
              <DialogPrimitive.Description className="mt-2 text-sm text-slate-500">
                {description}
              </DialogPrimitive.Description>
            </div>
            <DialogPrimitive.Close
              className="icon-button shrink-0"
              disabled={busy}
              aria-label="Close dialog"
            >
              <X size={18} />
            </DialogPrimitive.Close>
          </div>
          {children}
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  )
}
