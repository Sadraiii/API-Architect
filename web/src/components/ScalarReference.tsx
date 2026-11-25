import { useMemo, useSyncExternalStore } from "react";
import { ApiReferenceReact } from "@scalar/api-reference-react";
import "@scalar/api-reference-react/style.css";
import type { OpenApiDocument } from "../openapi/model";

/**
 * Sends a "Try it out" request from the reference and returns the target API's response.
 *
 * This is the hook for server-side execution. Scalar builds the request (URL, headers, auth, body) and hands it
 * to this function instead of calling fetch itself. The implementation should forward it to the API's request
 * executor, which applies SSRF protection, timeouts and size limits, and turn the executor's result back into
 * a Response. Until one is passed, the reference's send-request controls are hidden: sending straight from the
 * browser would bypass those protections and fail on CORS for most APIs anyway.
 */
export type SendRequest = (request: Request) => Promise<Response>;

const darkQuery = "(prefers-color-scheme: dark)";
const subscribeToColorScheme = (onChange: () => void) => {
  const media = window.matchMedia(darkQuery);
  media.addEventListener("change", onChange);
  return () => media.removeEventListener("change", onChange);
};
const prefersDark = () => window.matchMedia(darkQuery).matches;

// Scalar's own colour variables, mapped onto the app's palette (Tailwind zinc + indigo) so the reference
// reads as part of the app rather than an embedded third-party page.
const customCss = `
.scalar-app, .scalar-api-reference {
  --scalar-font: var(--font-sans);
  --scalar-font-code: var(--font-mono);
  --scalar-radius: 6px;
  --scalar-radius-lg: 8px;
  --scalar-radius-xl: 8px;
}
.light-mode {
  --scalar-background-1: #ffffff;
  --scalar-background-2: #fafafa;
  --scalar-background-3: #f4f4f5;
  --scalar-background-accent: #4f46e51a;
  --scalar-color-1: #18181b;
  --scalar-color-2: #52525b;
  --scalar-color-3: #71717a;
  --scalar-color-accent: #4f46e5;
  --scalar-border-color: #e4e4e7;
  --scalar-sidebar-background-1: #ffffff;
  --scalar-sidebar-item-active-background: #f4f4f5;
  --scalar-sidebar-color-active: #18181b;
  --scalar-button-1: #4f46e5;
  --scalar-button-1-hover: #6366f1;
  --scalar-button-1-color: #ffffff;
}
.dark-mode {
  --scalar-background-1: #09090b;
  --scalar-background-2: #18181b;
  --scalar-background-3: #27272a;
  --scalar-background-accent: #818cf81f;
  --scalar-color-1: #f4f4f5;
  --scalar-color-2: #a1a1aa;
  --scalar-color-3: #71717a;
  --scalar-color-accent: #818cf8;
  --scalar-border-color: #27272a;
  --scalar-sidebar-background-1: #09090b;
  --scalar-sidebar-item-active-background: #27272a;
  --scalar-sidebar-color-active: #f4f4f5;
  --scalar-button-1: #6366f1;
  --scalar-button-1-hover: #818cf8;
  --scalar-button-1-color: #ffffff;
}
`;

/**
 * The Scalar API reference for an imported document. Lazy-load this component: Scalar is a large bundle
 * and only this page needs it.
 */
export default function ScalarReference({ document, sendRequest }: { document: OpenApiDocument; sendRequest?: SendRequest }) {
  const dark = useSyncExternalStore(subscribeToColorScheme, prefersDark);

  const configuration = useMemo(
    () => ({
      content: document,
      theme: "none" as const,
      customCss,
      withDefaultFonts: false,
      forceDarkModeState: dark ? ("dark" as const) : ("light" as const),
      hideDarkModeToggle: true,
      // The page has its own export button that returns the document exactly as imported.
      documentDownloadType: "none" as const,
      // Leave the app's URL alone; the reference is one tab of a page, not the whole page.
      urlRouting: false,
      // Try it out goes through the server-side executor only (see SendRequest).
      hideTestRequestButton: !sendRequest,
      hideClientButton: !sendRequest,
      customFetch: sendRequest ? (input: RequestInfo | URL, init?: RequestInit) => sendRequest(new Request(input, init)) : undefined,
      // No AI features, no telemetry, no Scalar-hosted services.
      agent: { disabled: true },
      mcp: { disabled: true },
      telemetry: false,
      showDeveloperTools: "never" as const,
      persistAuth: false,
    }),
    [document, dark, sendRequest],
  );

  return <ApiReferenceReact configuration={configuration} />;
}
