import { useEffect, useState } from 'react';
import type { Device } from './api';
const calc = (): Device => (window.innerWidth < 640 ? 'mobile' : window.innerWidth < 1024 ? 'tablet' : 'desktop');
export function useDevice(): Device {
  const [d, setD] = useState<Device>(calc);
  useEffect(() => { const f = () => setD(calc()); window.addEventListener('resize', f); return () => window.removeEventListener('resize', f); }, []);
  return d;
}
