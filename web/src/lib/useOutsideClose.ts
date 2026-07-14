import { useEffect, type RefObject } from 'react'

/** Closes an open popover on an outside click or Escape. Shared by DropdownMenu and
 *  any other click-away popover (e.g. the check-in comment popover in ProjectView). */
export function useOutsideClose(ref: RefObject<HTMLElement | null>, open: boolean, close: () => void) {
  useEffect(() => {
    if (!open) return
    function onPointerDown(event: MouseEvent) {
      if (ref.current !== null && !ref.current.contains(event.target as Node)) close()
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') close()
    }
    document.addEventListener('mousedown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('mousedown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open, ref, close])
}
