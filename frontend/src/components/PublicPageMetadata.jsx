import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { getPublicPageMetadata } from '../lib/publicPageMetadata';

export default function PublicPageMetadata() {
  const { pathname } = useLocation();
  useEffect(() => {
    const page = getPublicPageMetadata(pathname);
    document.title = page.title;
    const description = document.querySelector('meta[name="description"]');
    if (description) description.content = page.description;
  }, [pathname]);
  return null;
}
