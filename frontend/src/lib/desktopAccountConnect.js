export const DESKTOP_CONNECT_CALLBACK = 'http://127.0.0.1:5273/desktop-account-connect';
export function readDesktopConnectRequest(hash) {
  if (typeof hash !== 'string' || hash.length > 2048) return null;
  const params = new URLSearchParams(hash.replace(/^#/, ''));
  const state = params.get('state'), publicKey = params.get('publicKey');
  if ([...params.keys()].some(key => !['state', 'publicKey'].includes(key)) ||
    params.getAll('state').length !== 1 || params.getAll('publicKey').length !== 1 ||
    !/^[A-Za-z0-9_-]{43}$/.test(state ?? '') || !/^[A-Za-z0-9_-]{400,1000}$/.test(publicKey ?? '')) return null;
  return { state, publicKey, code: state.slice(0, 8).toUpperCase() };
}
