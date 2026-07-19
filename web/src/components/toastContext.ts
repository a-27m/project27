import { createContext, useContext } from 'react'
import type { ToastVariant } from '../lib/toast'

export interface ToastApi {
  showToast: (message: string, variant?: ToastVariant) => void
  showError: (cause: unknown) => void
}

export const ToastContext = createContext<ToastApi | null>(null)

export function useToast(): ToastApi {
  const context = useContext(ToastContext)
  if (context === null) throw new Error('useToast must be used within a ToastProvider')
  return context
}
