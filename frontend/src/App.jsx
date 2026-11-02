import { lazy, Suspense, useEffect, useRef, useState } from 'react';
import { AnimatePresence } from 'motion/react';
import {
  BrowserRouter,
  Navigate,
  Route,
  Routes,
} from 'react-router-dom';

import SplashScreen from './components/SplashScreen';
import DisplayPreferencesProvider from './components/DisplayPreferencesProvider';
import { afterScreenPaint, notifyDesktopDashboardReady } from './lib/performanceRecording';
import HardwareDashboard from './pages/HardwareDashboard';
import PublicPageMetadata from './components/PublicPageMetadata';

const isPublicDownloadSite =
  import.meta.env.VITE_PUBLIC_DOWNLOAD_SITE === 'true';

const ProtectedDownloadRoute = lazy(() => import('./components/ProtectedDownloadRoute'));
const DownloadLandingPage = lazy(() => import('./pages/DownloadLandingPage'));
const LoginPage = lazy(() => import('./pages/LoginPage'));
const RegisterPage = lazy(() => import('./pages/RegisterPage'));
const VerifyEmailPage = lazy(() => import('./pages/VerifyEmailPage'));
const DesktopConnectPage = lazy(() => import('./pages/DesktopConnectPage'));
const NotFoundPage = lazy(() => import('./pages/NotFoundPage'));

function DashboardApp() {
  const [showSplash, setShowSplash] = useState(true);
  const [startupReady, setStartupReady] = useState(null);
  const [splashExited, setSplashExited] = useState(false);
  const hiddenDuringStartup = useRef(document.visibilityState === 'hidden');
  useEffect(() => {
    const track = () => { hiddenDuringStartup.current ||= document.visibilityState === 'hidden'; };
    document.addEventListener('visibilitychange', track);
    return () => document.removeEventListener('visibilitychange', track);
  }, []);
  useEffect(() => {
    if (!splashExited || startupReady === null) return;
    return afterScreenPaint(() => notifyDesktopDashboardReady(startupReady && !hiddenDuringStartup.current));
  }, [splashExited, startupReady]);

  useEffect(() => {
    const splashTimer = window.setTimeout(() => {
      setShowSplash(false);
    }, 1100);

    return () => window.clearTimeout(splashTimer);
  }, []);

  return (
    <>
      <HardwareDashboard onStartupReady={setStartupReady} />

      <AnimatePresence onExitComplete={() => setSplashExited(true)}>
        {showSplash && <SplashScreen />}
      </AnimatePresence>
    </>
  );
}

function PublicDownloadRoutes() {
  return (
    <>
    <PublicPageMetadata />
    <Suspense fallback={<SplashScreen />}>
      <Routes>
        <Route path="/" element={<Navigate to="/register" replace />} />
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/desktop-connect" element={<DesktopConnectPage />} />
        <Route path="/login" element={<LoginPage />} />
        <Route path="/verify-email" element={<VerifyEmailPage />} />
        <Route
          path="/download"
          element={
            <ProtectedDownloadRoute>
              <DownloadLandingPage />
            </ProtectedDownloadRoute>
          }
        />
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </Suspense>
    </>
  );
}

function DesktopAppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<DisplayPreferencesProvider><DashboardApp /></DisplayPreferencesProvider>} />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

function App() {
  return (
    <BrowserRouter>
      {isPublicDownloadSite ? (
        <PublicDownloadRoutes />
      ) : (
        <DesktopAppRoutes />
      )}
    </BrowserRouter>
  );
}

export default App;
