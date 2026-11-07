import * as DialogPrimitive from '@radix-ui/react-dialog'
import { X } from 'lucide-react'
import { useRef, type ReactNode } from 'react'
export function Dialog({
  open,
  onOpenChange,
  title,
  description,
  children,
  busy = false,
  wide = false,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: string
  children: ReactNode
  busy?: boolean
  wide?: boolean
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
            if (returnFocus.current?.isConnected) {
              event.preventDefault()
              returnFocus.current.focus()
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
