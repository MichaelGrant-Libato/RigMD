type SizedApp = { name: string; estimatedSizeBytes?: number | null };

export function sortInstalledApps<T extends SizedApp>(apps: T[], sort: 'name' | 'size'): T[] {
  function size(app: SizedApp) {
    const bytes = app.estimatedSizeBytes;
    return typeof bytes === 'number' && Number.isFinite(bytes) && bytes > 0 ? bytes : 0;
  }

  return [...apps].sort((a, b) => {
    if (sort === 'size') {
      const difference = size(b) - size(a);
      if (difference !== 0) return difference;
    }
    return a.name.localeCompare(b.name, undefined, { sensitivity: 'base', numeric: true });
  });
}
