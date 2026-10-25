export function getAppObservationPresentation(status?: string) {
  switch (status?.trim().toLowerCase()) {
    case 'app closed':
      return {
        status: 'No matching processes at the action check',
        meaning: 'No matching app processes were observed then. This does not establish how much system memory was freed or whether the app was reopened later.',
      };
    case 'app still running in background':
      return {
        status: 'Processes were still running at the action check',
        meaning: 'Matching app processes were observed then. The app may have exited since this observation. RigMD did not force it to close.',
      };
    case 'app state not verified':
      return {
        status: 'App exit was not verified at the action check',
        meaning: 'RigMD could not verify whether the app had exited then. It did not force the app to close.',
      };
    default:
      return null;
  }
}
