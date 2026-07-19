export type ToastVariant = 'error' | 'info'

export interface Toast {
  id: number
  message: string
  variant: ToastVariant
}

export interface ToastState {
  toasts: Toast[]
  nextId: number
}

export type ToastAction = { type: 'add'; message: string; variant: ToastVariant } | { type: 'dismiss'; id: number }

export const MAX_TOASTS = 4
export const TOAST_DURATION_MS = 5000

export const initialToastState: ToastState = { toasts: [], nextId: 1 }

export function toastReducer(state: ToastState, action: ToastAction): ToastState {
  switch (action.type) {
    case 'add': {
      const toast: Toast = { id: state.nextId, message: action.message, variant: action.variant }
      const toasts = [...state.toasts, toast].slice(-MAX_TOASTS)
      return { toasts, nextId: state.nextId + 1 }
    }
    case 'dismiss':
      return { ...state, toasts: state.toasts.filter((toast) => toast.id !== action.id) }
  }
}

export function errorMessage(cause: unknown): string {
  return cause instanceof Error ? cause.message : String(cause)
}
