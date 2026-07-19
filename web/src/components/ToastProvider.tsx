import { useCallback, useReducer, type ReactNode } from 'react'
import { errorMessage, initialToastState, toastReducer, type ToastVariant } from '../lib/toast'
import { ToastContext } from './toastContext'
import { ToastHost } from './ToastHost'

export function ToastProvider({ children }: { children: ReactNode }) {
  const [state, dispatch] = useReducer(toastReducer, initialToastState)

  const showToast = useCallback((message: string, variant: ToastVariant = 'info') => {
    dispatch({ type: 'add', message, variant })
  }, [])
  const showError = useCallback((cause: unknown) => showToast(errorMessage(cause), 'error'), [showToast])

  return (
    <ToastContext.Provider value={{ showToast, showError }}>
      {children}
      <ToastHost toasts={state.toasts} onDismiss={(id) => dispatch({ type: 'dismiss', id })} />
    </ToastContext.Provider>
  )
}
