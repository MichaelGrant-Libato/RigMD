export function removalAvailability(app: { canUninstall: boolean; uninstallReason: string }) {
  return app.canUninstall ? 'Uninstall option available — review first' : /protected|shared system/i.test(app.uninstallReason)
    ? 'Protected by RigMD' : 'Removal not available here yet';
}

export function installationStatusText(status: string) {
  const labels: Record<string, string> = {
    installed: 'Windows lists this app as installed.',
    absent: 'Windows no longer lists this installation.',
    unknown: 'The app’s installed status could not be confirmed.',
    unsupported: 'RigMD cannot check removal for this app yet.',
  };
  return labels[status] ?? labels.unknown;
}
