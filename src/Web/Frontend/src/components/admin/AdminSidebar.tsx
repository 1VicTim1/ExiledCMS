import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { LayoutDashboard, FileText, Palette, Puzzle, Settings, LogOut } from 'lucide-react';
import { LanguageSwitcher } from '../lang/LanguageSwitcher';
import { ExtensionSlot } from '@/core/registry/ExtensionSlot';

export function AdminSidebar() {
  const t = useTranslations('Admin');

  return (
    <aside className="w-64 h-screen border-r border-border bg-card/50 backdrop-blur-sm flex flex-col fixed left-0 top-0">
      <div className="h-16 flex items-center px-6 border-b border-border gap-3">
        <div className="w-8 h-8 rounded-lg bg-primary flex items-center justify-center text-primary-foreground font-bold">
          EP
        </div>
        <span className="font-semibold text-lg">Admin Panel</span>
      </div>

      <nav className="flex-1 overflow-y-auto py-4 px-3 space-y-1">
        <Link href="/admin" className="flex items-center gap-3 px-3 py-2.5 rounded-lg bg-primary/10 text-primary font-medium">
          <LayoutDashboard className="w-5 h-5" />
          {t('dashboard')}
        </Link>
        <Link href="/admin/pages" className="flex items-center gap-3 px-3 py-2.5 rounded-lg hover:bg-muted text-muted-foreground hover:text-foreground transition-colors">
          <FileText className="w-5 h-5" />
          {t('pages')}
        </Link>
        <Link href="/admin/themes" className="flex items-center gap-3 px-3 py-2.5 rounded-lg hover:bg-muted text-muted-foreground hover:text-foreground transition-colors">
          <Palette className="w-5 h-5" />
          {t('themes')}
        </Link>
        <Link href="/admin/plugins" className="flex items-center gap-3 px-3 py-2.5 rounded-lg hover:bg-muted text-muted-foreground hover:text-foreground transition-colors">
          <Puzzle className="w-5 h-5" />
          {t('plugins')}
        </Link>
        <Link href="/admin/settings" className="flex items-center gap-3 px-3 py-2.5 rounded-lg hover:bg-muted text-muted-foreground hover:text-foreground transition-colors">
          <Settings className="w-5 h-5" />
          {t('settings')}
        </Link>
        
        {/* Slot for plugins to add their own sidebar items */}
        <ExtensionSlot name="admin_sidebar_menu" />
      </nav>

      <div className="p-4 border-t border-border flex items-center justify-between">
        <button className="flex items-center gap-3 px-3 py-2 rounded-lg hover:bg-muted text-muted-foreground hover:text-foreground transition-colors w-full">
          <LogOut className="w-5 h-5" />
          <span>{t('logout')}</span>
        </button>
        <LanguageSwitcher />
      </div>
    </aside>
  );
}
