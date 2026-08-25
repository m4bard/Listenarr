/// <reference types="vite/client" />

// Provide a module declaration for Vue SFCs so TypeScript can import `.vue` files.
// This avoids "Cannot find module './Foo.vue'" errors during `tsc` checks.
declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent
  export default component
}

interface Window {
  /**
   * URL sub-path Listenarr is served under, injected into index.html by the backend before the
   * entry module runs. Absent when serving from the site root. Read it through
   * `@/utils/urlBase`, which normalizes the value, rather than touching it directly.
   */
  __listenarrUrlBase?: string
}
