'use client';

import { useLocale } from 'next-intl';
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu';
import { Globe } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { locales } from '@/i18n/config';

export function LanguageSwitcher() {
  const locale = useLocale();
  const router = useRouter();

  const handleLanguageChange = (newLocale: string) => {
    if (typeof window !== 'undefined') {
      // eslint-disable-next-line react-hooks/immutability
      window.document.cookie = `NEXT_LOCALE=${newLocale}; path=/; max-age=31536000`;
    }
    router.refresh();
  };

  const labels: Record<string, string> = {
    ru: 'Русский',
    en: 'English'
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger className="group/button inline-flex shrink-0 items-center justify-center rounded-[min(var(--radius-md),10px)] size-8 border border-transparent bg-clip-padding text-sm font-medium whitespace-nowrap transition-all outline-none select-none hover:bg-muted hover:text-foreground">
        <Globe className="h-[1.2rem] w-[1.2rem]" />
        <span className="sr-only">Switch language</span>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {locales.map((loc) => (
          <DropdownMenuItem
            key={loc}
            onClick={() => handleLanguageChange(loc)}
            className={locale === loc ? "bg-muted" : ""}
          >
            {labels[loc]}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
