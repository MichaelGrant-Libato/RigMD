/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL: string;
  readonly VITE_AGENT_ID: string;
  readonly VITE_RIGMD_DOWNLOAD_URL?: string;
  readonly VITE_SUPABASE_URL?: string;
  readonly VITE_SUPABASE_ANON_KEY?: string;
  readonly VITE_SUPABASE_DOWNLOAD_BUCKET?: string;
  readonly VITE_SUPABASE_INSTALLER_PATH?: string;
  readonly VITE_RIGMD_INSTALLER_FILE_NAME?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
