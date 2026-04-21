import test from "node:test";
import assert from "node:assert/strict";

import { shouldUseSecureCookies } from "../src/lib/cookie-policy.ts";

test("shouldUseSecureCookies disables Secure for plain HTTP when mode is auto", () => {
  assert.equal(
    shouldUseSecureCookies({
      protocol: "http:",
      forwardedProto: null,
      configuredMode: "auto",
    }),
    false,
  );
});

test("shouldUseSecureCookies enables Secure for forwarded HTTPS requests", () => {
  assert.equal(
    shouldUseSecureCookies({
      protocol: "http:",
      forwardedProto: "https",
      configuredMode: "auto",
    }),
    true,
  );
});

test("shouldUseSecureCookies respects explicit override modes", () => {
  assert.equal(
    shouldUseSecureCookies({
      protocol: "http:",
      forwardedProto: null,
      configuredMode: "always",
    }),
    true,
  );

  assert.equal(
    shouldUseSecureCookies({
      protocol: "https:",
      forwardedProto: "https",
      configuredMode: "never",
    }),
    false,
  );
});
