export type CookieSecurityInput = {
  protocol?: string | null;
  forwardedProto?: string | null;
  configuredMode?: string | null;
};

// Runtime auth cookies must stay usable both behind HTTPS reverse proxies and
// on plain HTTP test stands. "auto" enables Secure only when the original
// request was HTTPS.
export function shouldUseSecureCookies(input: CookieSecurityInput) {
  const configuredMode = input.configuredMode?.trim().toLowerCase();
  if (configuredMode === "always") {
    return true;
  }

  if (configuredMode === "never") {
    return false;
  }

  const forwardedProto = input.forwardedProto
    ?.split(",")[0]
    ?.trim()
    .toLowerCase();
  if (forwardedProto) {
    return forwardedProto === "https";
  }

  const protocol = input.protocol?.trim().toLowerCase();
  return protocol === "https" || protocol === "https:";
}
