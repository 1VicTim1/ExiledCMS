import { getRequestConfig } from 'next-intl/server';
import { locales, defaultLocale, Locale } from './config';
import { cookies } from 'next/headers';

export default getRequestConfig(async () => {
  // Read from cookies, or fallback to default
  const cookieStore = await cookies();
  const localeCookie = cookieStore.get('NEXT_LOCALE')?.value;
  
  let locale: Locale = defaultLocale;
  if (localeCookie && (locales as readonly string[]).includes(localeCookie)) {
    locale = localeCookie as Locale;
  }

  return {
    locale,
    messages: (await import(`../../messages/${locale}.json`)).default
  };
});
