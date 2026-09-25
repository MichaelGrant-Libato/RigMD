import { lazy, Suspense, useEffect, useState } from 'react';
import { AnimatePresence } from 'motion/react';
import {
  BrowserRouter,
  Navigate,
  Route,
  Routes,
} from 'react-router-dom';

import SplashScreen from './components/SplashScreen';
import HardwareDashboard from './pages/HardwareDashboard';

const isPublicDownloadSite =
  import.meta.env.VITE_PUBLIC_DOWNLOAD_SITE === 'true';

const ProtectedDownloadRoute = lazy(() => import('./components/ProtectedDownloadRoute'));
const DownloadLandingPage = lazy(() => import('./pages/DownloadLandingPage'));
const LoginPage = lazy(() => import('./pages/LoginPage'));
const RegisterPage = lazy(() => import('./pages/RegisterPage'));
const VerifyEmailPage = lazy(() => import('./pages/VerifyEmailPage'));

function DashboardApp() {
  const [showSplash, setShowSplash] = useState(true);

  useEffect(() => {
    const splashTimer = window.setTimeout(() => {
      setShowSplash(false);
    }, 1100);

    return () => window.clearTimeout(splashTimer);
  }, []);

  return (
    <>
      <HardwareDashboard />

      <AnimatePresence>
        {showSplash && <SplashScreen />}
      </AnimatePresence>
    </>
  );
}

function PublicDownloadRoutes() {
  return (
    <Suspense fallback={<SplashScreen />}>
      <Routes>
        <Route path="/" element={<Navigate to="/register" replace />} />
        <Route path="/register" element={<RegisterPage />} />
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
        <Route path="*" element={<Navigate to="/register" replace />} />
      </Routes>
    </Suspense>
  );
}

function DesktopAppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<DashboardApp />} />
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