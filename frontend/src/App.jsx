import { useEffect, useState } from 'react';
import { AnimatePresence } from 'motion/react';

import SplashScreen from './components/SplashScreen';
import HardwareDashboard from './pages/HardwareDashboard';

function App() {
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

export default App;
