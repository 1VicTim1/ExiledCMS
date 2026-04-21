import React from 'react';

export type ComponentSlot = 
  | "admin_dashboard_widget"
  | "admin_sidebar_menu"
  | "public_home_widget"
  | "public_header_action"
  | "public_footer_link";

export interface PluginRoute {
  path: string;
  component: React.ComponentType<Record<string, unknown>>;
  layout?: "admin" | "public" | "none";
  roles?: string[]; // permissions
}

export interface PluginRegistration {
  id: string;
  name: string;
  version: string;
  routes?: PluginRoute[];
  components?: Partial<Record<ComponentSlot, React.ComponentType<Record<string, unknown>>[]>>;
  onInit?: () => void;
}

class ExiledRegistry {
  private plugins = new Map<string, PluginRegistration>();
  private slots = new Map<ComponentSlot, React.ComponentType<Record<string, unknown>>[]>();

  public registerPlugin(plugin: PluginRegistration) {
    if (this.plugins.has(plugin.id)) {
      console.warn(`[Registry] Plugin ${plugin.id} is already registered.`);
      return;
    }

    this.plugins.set(plugin.id, plugin);

    // Register slots
    if (plugin.components) {
      Object.entries(plugin.components).forEach(([slot, components]) => {
        const slotKey = slot as ComponentSlot;
        const existing = this.slots.get(slotKey) || [];
        this.slots.set(slotKey, [...existing, ...(components as React.ComponentType<Record<string, unknown>>[])]);
      });
    }

    // Call init hook
    if (plugin.onInit) {
      plugin.onInit();
    }

    console.log(`[Registry] Plugin registered: ${plugin.name} v${plugin.version}`);
  }

  public getComponentsForSlot(slot: ComponentSlot): React.ComponentType<Record<string, unknown>>[] {
    return this.slots.get(slot) || [];
  }

  public getPlugins(): PluginRegistration[] {
    return Array.from(this.plugins.values());
  }
}

export const Registry = new ExiledRegistry();
