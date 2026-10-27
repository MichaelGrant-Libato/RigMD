const pages: Record<string, { title: string; description: string }> = {
  '/register': { title: 'Create Account | RigMD', description: 'Create a RigMD account to access the Windows diagnostic support installer.' },
  '/login': { title: 'Log In | RigMD', description: 'Log in to your verified RigMD account to access the Windows installer.' },
  '/verify-email': { title: 'Verify Email | RigMD', description: 'Verify your RigMD account email before accessing the Windows installer.' },
  '/desktop-connect': { title: 'Connect This PC | RigMD', description: 'Connect your verified RigMD website account to the desktop app on this PC.' },
  '/download': { title: 'Download | RigMD', description: 'Download RigMD for Windows to review supported device readings, diagnostic findings, and next steps.' },
};

export function getPublicPageMetadata(pathname: string) {
  return pages[pathname === '/' ? '/register' : pathname] ?? { title: 'Page Not Found | RigMD', description: 'This RigMD page could not be found. Return to account registration or log in.' };
}
