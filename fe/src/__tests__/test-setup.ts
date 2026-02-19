/* eslint-disable @typescript-eslint/no-explicit-any */
// Test setup: Polyfill / mock environment pieces that tests expect
// - Provide a Mock WebSocket implementation so SignalR code can run in jsdom

class MockWebSocket {
  static OPEN = 1
  public readyState = MockWebSocket.OPEN
  public onopen: (() => void) | null = null
  public onmessage: ((ev: { data: string }) => void) | null = null
  public onerror: ((err: Error) => void) | null = null
  public onclose: (() => void) | null = null
  private url: string
  constructor(url: string) {
    this.url = url
    // simulate async open
    setTimeout(() => {
      if (this.onopen) this.onopen()
    }, 0)
  }
  send(_data: string) {
    // Reference the arg so linters don't complain about unused params in tests
    void _data
    /* no-op in tests */
  }
  close() {
    if (this.onclose) this.onclose()
  }
}

// Centralized apiService and signalR mocks used by unit tests.
import { vi } from 'vitest'

// Provide default component stubs for Modal teleporting components so unit tests
// render modal content inline instead of using real teleport behavior.
import { config as vtConfig } from '@vue/test-utils'
const globalConfig = ((vtConfig.global ??= {} as any) as any)
globalConfig.components = {
  ...(globalConfig.components || {}),
  // Render modal content inline with accessible dialog attributes so tests
  // can query for role="dialog" and aria-* attributes reliably.
  Modal: {
    template:
      '<div role="dialog" aria-modal="true" aria-labelledby="modal-title"><header id="modal-title"><slot name="header" /></header><div class="modal-body"><slot /></div><footer class="modal-footer"><slot name="footer" /></footer></div>',
  },
  ModalHeader: { template: '<div class="modal-header"><slot /></div>' },
  ModalBody: { template: '<div class="modal-body"><slot /></div>' },

  // Provide lightweight test stubs for commonly used components so unit tests
  // don't fail on missing component resolution for icon or small base pieces.
  LoadingState: {
    props: ['message', 'size'],
    template: '<div class="loading-state"><div class="spinner"/><p v-if="message">{{ message }}</p></div>',
  },
  PhSpinner: {
    props: ['size'],
    template: '<i class="ph-spinner" aria-hidden="true"></i>',
  },
  // Stub the BrandLogo component so tests don't trigger static-asset resolution
  BrandLogo: {
    template: '<div class="brand-logo-stub" />',
  },
}

// Some components import the modal pieces locally (via named imports). To ensure
// tests always render the simplified accessible modal markup (and avoid teleport
// behavior), partially mock the feedback module so SFC-local imports receive the
// inline stubs while preserving other named exports from the real module.
vi.mock('@/components/feedback', async (importOriginal) => {
  const actual = (await importOriginal()) as Record<string, unknown>
  const modalStub: any = {
    emits: ['close'],
    props: ['visible', 'title', 'showClose', 'size'],
    template:
      '<div v-if="visible" v-bind="$attrs" role="dialog" aria-modal="true" aria-labelledby="modal-title"><header id="modal-title"><slot name="header" /></header><div class="modal-body"><slot /></div><footer class="modal-footer"><slot name="footer" /></footer></div>',
    mounted() {
      this._onKey = (e: KeyboardEvent) => {
        if (e.key === 'Escape') this.$emit?.('close')
      }
      document.addEventListener('keydown', this._onKey)
    },
    unmounted() {
      if (this._onKey) document.removeEventListener('keydown', this._onKey)
    },
  }
  return {
    ...actual,
    Modal: modalStub,
    ModalHeader: {
      props: ['title', 'icon', 'iconLabel'],
      emits: ['close'],
      template:
        '<div class="modal-header"><component v-if="icon" :is="icon" /><h2 v-if="title">{{title}}</h2><button @click="$emit(\'close\')" class="close-btn">x</button></div>',
    },
    ModalBody: { template: '<div class="modal-body"><slot /></div>' },
    ModalFooter: { template: '<div class="modal-footer"><slot /></div>' },
  }
})

// Provide both the `apiService` object and common named exports that components
// import directly (e.g. `getRemotePathMappings`, `ensureImageCached`). Tests
// expect these named exports to exist on the mocked module.
vi.mock('@/services/api', () => {
  const apiService = {
    searchAudimetaByTitleAndAuthor: vi.fn(async () => ({ totalResults: 0, results: [] })),
    advancedSearch: async (params: unknown) => {
      const p = params as { title?: string; author?: string } | undefined
      if (p?.title) {
        const mod = await import('@/services/api')
        const svc = mod.apiService as unknown as {
          searchAudimetaByTitleAndAuthor?: (
            title: string,
            author?: string,
          ) => Promise<{ totalResults?: number; results?: unknown[] } | unknown>
        }
        if (svc.searchAudimetaByTitleAndAuthor) {
          const resp = (await svc.searchAudimetaByTitleAndAuthor(p.title, p.author)) as unknown
          const r = resp as any
          return (r?.results) || r || []
        }
        return []
      }
      return { totalResults: 0, results: [] }
    },
    getImageUrl: vi.fn((url: string) => url || ''),
    getStartupConfig: vi.fn(async () => ({})),
    getApplicationSettings: vi.fn(async () => ({})),
    getLibrary: vi.fn(async () => []),
    previewLibraryPath: vi.fn(async () => ({ path: '' })),
    getQualityProfiles: vi.fn(async () => []),
    getApiConfigurations: vi.fn(async () => []),
    // add getRootFolders to apiService so tests that spy on apiService.getRootFolders work
    getRootFolders: vi.fn(async () => []),
  }

  // Named exports commonly imported by components/tests
  return {
    apiService,
    // Path/remote helpers
    getRemotePathMappings: vi.fn(async () => []),
    testDownloadClient: vi.fn(async () => ({ success: true })),

    // Image helpers
    ensureImageCached: vi.fn(async (url: string) => url || ''),

    // Logs / files
    getLogs: vi.fn(async () => []),
    downloadLogs: vi.fn(async () => null),

    // Root folders / profiles
    getRootFolders: vi.fn(async () => []),
    getQualityProfiles: vi.fn(async () => []),

    // Keep the startup / app settings helpers available as named exports too
    getStartupConfig: vi.fn(async () => ({})),
    getApplicationSettings: vi.fn(async () => ({})),
  }
})

vi.mock('@/services/signalr', () => ({
  signalRService: {
    connect: () => {},
    onDownloadsList: (cb?: (...args: unknown[]) => void) => {
      void cb
      return () => {}
    },
    onSearchProgress: (cb?: (...args: unknown[]) => void) => {
      void cb
      return () => {}
    },
    onQueueUpdate: (cb?: (...args: unknown[]) => void) => {
      void cb
      return () => {}
    },
    onDownloadUpdate: (cb?: (...args: unknown[]) => void) => {
      void cb
      return () => {}
    },
    onFilesRemoved: (cb?: (...args: unknown[]) => void) => {
      void cb
      return () => {}
    },
    onAudiobookUpdate: (cb?: (...args: unknown[]) => void) => {
      void cb
      return () => {}
    },
    onNotification: (cb?: (...args: unknown[]) => void) => {
      void cb
      return () => {}
    },
    onToast: (cb?: (...args: unknown[]) => void) => {
      void cb
      return () => {}
    },
  },
}))

// Ensure global WebSocket exists for code that references it
if (typeof (globalThis as unknown as { WebSocket?: unknown }).WebSocket === 'undefined') {
  ;(globalThis as unknown as { WebSocket?: unknown }).WebSocket = MockWebSocket
}

// Also provide a minimal window.WebSocket for code referencing window
if (typeof (window as unknown as { WebSocket?: unknown }).WebSocket === 'undefined') {
  ;(window as unknown as { WebSocket?: unknown }).WebSocket = MockWebSocket
}

// Provide a noop for console.debug in tests where code wraps in try/catch
if (typeof console.debug !== 'function') console.debug = console.log.bind(console)

// Ensure JSDOM's base URL is HTTP (not file://) so absolute static asset paths
// (e.g. `/logo.svg`) resolve to `http://localhost/...` instead of `file:///...`.
// On Windows the `file:///` form can surface in source-maps and cause Node APIs
// to reject the path; setting the location prevents those file URLs from
// appearing during transforms and stacktrace processing.
try {
  if (typeof window !== 'undefined' && window.location && window.location.href.startsWith('file:')) {
    // Replace file://... base with http://localhost/
    window.history.replaceState({}, '', 'http://localhost/')
  }
} catch {}

// Provide a simple localStorage polyfill for tests that rely on it
// Ensure a working localStorage implementation exists for tests. Some test
// runners may set a placeholder object; normalize it so .setItem/.getItem exist.
if (
  typeof (globalThis as unknown as { localStorage?: { setItem?: unknown } }).localStorage ===
    'undefined' ||
  typeof (globalThis as unknown as { localStorage?: { setItem?: unknown } }).localStorage
    ?.setItem !== 'function'
) {
  ;(
    globalThis as unknown as {
      localStorage?: {
        _store?: Record<string, string>
        getItem?: (k: string) => string | null
        setItem?: (k: string, v: string) => void
        removeItem?: (k: string) => void
        clear?: () => void
      }
    }
  ).localStorage = {
    _store: {} as Record<string, string>,
    getItem(key: string) {
      return this._store[key] ?? null
    },
    setItem(key: string, value: string) {
      this._store[key] = value + ''
    },
    removeItem(key: string) {
      delete this._store[key]
    },
    clear() {
      this._store = {}
    },
  }
}

// Defensive: JSDOM / Vitest may encounter `file://` asset URLs (e.g. transformed
// static asset paths like `file:///logo.svg`). Some environments propagate
// those to HTMLImageElement.src setters which can trigger Node internal URL/path
// handling and cause tests to crash. Normalize `file://` image URLs to plain
// absolute paths to avoid runtime errors during tests.
try {
  const imgProto = Object.getOwnPropertyDescriptor(HTMLImageElement.prototype, 'src')
  Object.defineProperty(HTMLImageElement.prototype, 'src', {
    set(this: HTMLImageElement, value: string) {
      try {
        if (typeof value === 'string' && value.startsWith('file:///')) {
          // Convert file URL (file:///logo.svg) to a usable pathname (/logo.svg)
          const u = new URL(value)
          return imgProto?.set?.call(this, u.pathname)
        }
      } catch {
        // fall through to default setter
      }
      return imgProto?.set?.call(this, value)
    },
    get(this: HTMLImageElement) {
      return imgProto?.get?.call(this)
    },
    configurable: true,
  })
} catch {}

