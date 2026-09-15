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

// Defensive fallback: prevent ReferenceError when dangling microtasks/timers
// from third-party libraries (e.g. rc-component useDelayState) run after jsdom teardown.
if (typeof globalThis.window !== 'undefined' && typeof global !== 'undefined') {
  // @ts-expect-error polyfill global fallback
  global.window = globalThis.window
}
