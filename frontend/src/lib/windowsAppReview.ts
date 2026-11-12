export interface WindowsAppReviewResult {
  appId: string;
  appName: string;
  nameCopied: boolean;
  summary: string;
}

export async function reviewSelectedAppInWindows(
  app: { id: string; name: string },
  copyName: (name: string) => Promise<void>,
  openSettings: () => Promise<{ success: boolean; summary?: string }>,
): Promise<WindowsAppReviewResult> {
  // Start copying during the button's user gesture. Clipboard failure must not block Settings.
  let copied: Promise<boolean>;
  try { copied = copyName(app.name).then(() => true, () => false); }
  catch { copied = Promise.resolve(false); }
  const opened = await openSettings();
  if (opened.success !== true) throw new Error(opened.summary || 'Windows Installed apps could not be opened.');
  let timer: ReturnType<typeof setTimeout> | undefined;
  const nameCopied = await Promise.race([copied, new Promise<boolean>(resolve => { timer = setTimeout(() => resolve(false), 2000); })]);
  clearTimeout(timer);
  return { appId: app.id, appName: app.name, nameCopied, summary: opened.summary || 'Windows Installed apps opened.' };
}
