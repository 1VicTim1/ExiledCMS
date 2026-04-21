import React from 'react';
import { ComponentSlot, Registry } from './index';

interface SlotProps {
  name: ComponentSlot;
  context?: Record<string, unknown>;
}

export function ExtensionSlot({ name, context }: SlotProps) {
  const components = Registry.getComponentsForSlot(name);

  if (components.length === 0) {
    return null;
  }

  return (
    <>
      {components.map((Component, index) => (
        <Component key={`${name}-${index}`} {...context} />
      ))}
    </>
  );
}
