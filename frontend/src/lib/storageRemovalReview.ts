export interface StorageReviewApp {
  id?: string;
  name: string;
  publisher: string;
  version: string;
  scope: string;
  estimatedBytes: number | null;
}

export function storageReviewAppKey(app: StorageReviewApp) {
  if (app.id) return app.id;
  return JSON.stringify([app.scope, app.name, app.publisher, app.version]);
}

export function selectedStorageReviewApp<SelectedApp extends StorageReviewApp>(apps: SelectedApp[], key: string) {
  if (!key) return null;
  const matches = apps.filter(app => storageReviewAppKey(app) === key);
  return matches.length === 1 ? matches[0] : null;
}

export function filterStorageReviewApps<App extends StorageReviewApp & { canUninstall: boolean }>(apps: App[], filter: string, supportedOnly: boolean) {
  const query = filter.trim().toLocaleLowerCase();
  return apps.filter(app => (!supportedOnly || app.canUninstall) && `${app.name} ${app.publisher}`.toLocaleLowerCase().includes(query));
}
