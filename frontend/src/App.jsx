import { useEffect, useState } from 'react';
import { AnimatePresence } from 'motion/react';
import {
  BrowserRouter,
  Navigate,
  Route,
  Routes,
} from 'react-router-dom';

import SplashScreen from './components/SplashScreen';
import ProtectedDownloadRoute from './components/ProtectedDownloadRoute';
import HardwareDashboard from './pages/HardwareDashboard';
import DownloadLandingPage from './pages/DownloadLandingPage';
import LoginPage from './pages/LoginPage';
import RegisterPage from './pages/RegisterPage';
import VerifyEmailPage from './pages/VerifyEmailPage';

const isPublicDownloadSite =
  import.meta.env.VITE_PUBLIC_DOWNLOAD_SITE === 'true';

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
  );
}

function DesktopAppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<DashboardApp />} />
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