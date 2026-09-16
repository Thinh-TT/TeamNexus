import '@testing-library/jest-dom'

// Mock matchMedia for Ant Design components in jsdom
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }),
})

// Mock ResizeObserver
globalThis.ResizeObserver = class ResizeObserver {
  observe() {}
  unobserve() {}
  disconnect() {}
}

if (typeof global !== 'undefined' && typeof global.window !== 'undefined') {
  // @ts-expect-error fallback
  global.window = globalThis.window
}

// Defensive handler: Suppress dangling microtask/timer ReferenceErrors ("window is not defined")
// that fire from third-party UI component libraries (e.g., rc-component useDelayState)
// after Vitest has completed jsdom environment teardown for a test file.
if (typeof process !== 'undefined' && typeof process.on === 'function') {
  process.on('uncaughtException', (err) => {
    if (
      err &&
      (err.name === 'ReferenceError' || err instanceof ReferenceError) &&
      err.message?.includes('window is not defined')
    ) {
      // Ignore post-teardown dangling timer ReferenceError
      return
    }
  })
}



