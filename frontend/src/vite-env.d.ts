/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Backend SignalR hub URL. Defaults to the local dev backend when unset. */
  readonly VITE_HUB_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
