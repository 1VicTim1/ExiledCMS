import "server-only";

export type PlatformModule = {
  id: string;
  name: string;
  kind: string;
  baseUrl?: string;
  healthUrl?: string;
  openApiUrl?: string;
  swaggerUiUrl?: string;
};

export type PlatformModuleAvailability = {
  available: boolean;
  moduleId: string;
  module?: PlatformModule;
  error?: string;
  requestedUrl: string;
};

function getPlatformCoreBaseUrl() {
  const configured = process.env.EXILEDCMS_PLATFORM_CORE_URL?.trim() || process.env.PLATFORM_CORE_URL?.trim();
  if (configured) {
    return configured.replace(/\/+$/, "");
  }

  return "http://localhost:8080";
}

export function getPlatformCorePublicUrl() {
  const configured =
    process.env.EXILEDCMS_PLATFORM_CORE_PUBLIC_URL?.trim() ||
    process.env.NEXT_PUBLIC_PLATFORM_CORE_PUBLIC_URL?.trim() ||
    process.env.EXILEDCMS_PLATFORM_CORE_URL?.trim() ||
    process.env.PLATFORM_CORE_URL?.trim();

  if (configured) {
    return configured.replace(/\/+$/, "");
  }

  return "http://localhost:8080";
}

export async function getModuleAvailability(moduleId: string): Promise<PlatformModuleAvailability> {
  const requestedUrl = `${getPlatformCoreBaseUrl()}/api/v1/platform/modules`;

  try {
    const response = await fetch(requestedUrl, {
      cache: "no-store",
    });

    if (!response.ok) {
      return {
        available: false,
        moduleId,
        requestedUrl,
        error: `platform-core returned HTTP ${response.status}`,
      };
    }

    const payload = (await response.json()) as { items?: PlatformModule[] };
    const registeredModule = payload.items?.find((item) => item.id === moduleId);

    return {
      available: Boolean(registeredModule),
      moduleId,
      module: registeredModule,
      requestedUrl,
      error: registeredModule ? undefined : "Module is not present in the platform-core registry.",
    };
  } catch (error) {
    return {
      available: false,
      moduleId,
      requestedUrl,
      error: error instanceof Error ? error.message : "Unknown platform-core error.",
    };
  }
}
