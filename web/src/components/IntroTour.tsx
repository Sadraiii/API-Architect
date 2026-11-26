import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState, type CSSProperties, type ReactNode } from "react";
import { Button } from "./ui";

interface TourStep {
  title: string;
  body: ReactNode;
  /** The `data-tour` value of the element to highlight. Without one, or when it isn't on screen, the card is centred. */
  target?: string;
  /** Shown when the target isn't on the current page, so the user knows where to look. */
  whereToFind?: string;
}

const steps: TourStep[] = [
  {
    title: "Welcome to API Architect",
    body: (
      <>
        This is where you keep, read and test your own APIs. The next few steps show the usual order of work. You can skip
        now and replay the tour later from <b>Introduction</b> in the sidebar.
      </>
    ),
  },
  {
    title: "1. Create a project",
    body: "A project holds one API: its specifications, designs, saved requests and environments. Click New project, give it a name and save.",
    target: "new-project",
    whereToFind: "Go to Projects and click New project.",
  },
  {
    title: "2. Import or design your API",
    body: (
      <>
        Inside a project, click <b>Import OpenAPI</b> and pick or paste a JSON document. For an ASP.NET Core API that is
        usually <code className="font-mono text-xs">/openapi/v1.json</code>. Starting from nothing? Use <b>New design</b>{" "}
        to build models and endpoints, then generate the spec and a .NET project from it.
      </>
    ),
    target: "import-spec",
    whereToFind: "Open a project to see Import OpenAPI and New design.",
  },
  {
    title: "3. Browse the docs",
    body: "Open a specification to read its API reference, list endpoints and look at schemas. Try it out in the reference sends through the same runner as below.",
    target: "specifications",
    whereToFind: "Imported specifications are listed on the project page.",
  },
  {
    title: "4. Send requests",
    body: "Send a request builds a call with method, URL, headers, auth and body. It runs from the app's server, so APIs on localhost work. You get status, headers, body and timing back.",
    target: "send-request",
    whereToFind: "Open a project and click Send a request.",
  },
  {
    title: "5. Save work in collections",
    body: (
      <>
        Collections keep requests in folders so you can send them again. Environments hold variables like{" "}
        <code className="font-mono text-xs">{"{{baseUrl}}"}</code> and secrets, so the same request works against local and
        staging.
      </>
    ),
    target: "project-workspace",
    whereToFind: "Open a project and use the Collections and Environments links.",
  },
  {
    title: "That's it",
    body: "Start by creating a project. Click Introduction here any time to see this tour again.",
    target: "help",
  },
];

const CARD_WIDTH = 340;
const GAP = 12;
const MARGIN = 16;

/**
 * A short step-by-step tour that highlights parts of the page. The parent decides when it's open
 * and what closing means; this component only walks through the steps.
 */
export function IntroTour({ onClose }: { onClose: () => void }) {
  const [index, setIndex] = useState(0);
  const [rect, setRect] = useState<DOMRect | null>(null);
  const cardRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const step = steps[index]!; // index stays within 0..steps.length - 1
  const isLast = index === steps.length - 1;

  const next = useCallback(() => (isLast ? onClose() : setIndex((i) => i + 1)), [isLast, onClose]);
  const back = useCallback(() => setIndex((i) => Math.max(0, i - 1)), []);

  // Find the target and bring it into view when the step changes.
  useLayoutEffect(() => {
    const element = step.target ? document.querySelector(`[data-tour="${step.target}"]`) : null;
    element?.scrollIntoView({ block: "center", behavior: "instant" });
    setRect(element?.getBoundingClientRect() ?? null);
    cardRef.current?.focus();
  }, [step]);

  // Keep the highlight on the target while the page loads data, scrolls or resizes.
  // Polling is simpler than wiring resize, scroll and mutation observers, and is cheap at this rate.
  useEffect(() => {
    if (!step.target) return;
    const timer = window.setInterval(() => {
      const element = document.querySelector(`[data-tour="${step.target}"]`);
      const latest = element?.getBoundingClientRect() ?? null;
      setRect((current) => (sameRect(current, latest) ? current : latest));
    }, 200);
    return () => window.clearInterval(timer);
  }, [step]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
      else if (event.key === "ArrowRight") next();
      else if (event.key === "ArrowLeft") back();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose, next, back]);

  return (
    <div className="fixed inset-0 z-50">
      {rect ? (
        // The huge shadow dims everything outside the highlighted box.
        <div
          aria-hidden
          className="pointer-events-none fixed rounded-lg ring-2 ring-indigo-500 transition-all duration-200"
          style={{
            top: rect.top - 6,
            left: rect.left - 6,
            width: rect.width + 12,
            height: rect.height + 12,
            boxShadow: "0 0 0 9999px rgb(0 0 0 / 0.5)",
          }}
        />
      ) : (
        <div aria-hidden className="fixed inset-0 bg-black/50" />
      )}

      <div
        ref={cardRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        className="fixed rounded-lg border border-zinc-200 bg-white p-5 shadow-xl outline-none dark:border-zinc-800 dark:bg-zinc-900"
        style={cardPosition(rect)}
      >
        <p className="text-xs text-zinc-500">
          {index + 1} of {steps.length}
        </p>
        <h2 id={titleId} className="mt-1 text-base font-semibold">
          {step.title}
        </h2>
        <div className="mt-2 text-sm leading-relaxed text-zinc-700 dark:text-zinc-300">{step.body}</div>
        {!rect && step.whereToFind && <p className="mt-3 text-sm text-indigo-700 dark:text-indigo-300">{step.whereToFind}</p>}

        <div className="mt-5 flex items-center justify-between">
          {isLast ? (
            <span />
          ) : (
            <button className="text-sm text-zinc-500 hover:underline" onClick={onClose}>
              Skip tour
            </button>
          )}
          <div className="flex gap-2">
            {index > 0 && (
              <Button variant="secondary" onClick={back}>
                Back
              </Button>
            )}
            <Button onClick={next}>{isLast ? "Done" : "Next"}</Button>
          </div>
        </div>
      </div>
    </div>
  );
}

/** Below the target when there's room, otherwise above it. Centred when there's no target. */
function cardPosition(rect: DOMRect | null): CSSProperties {
  const width = Math.min(CARD_WIDTH, window.innerWidth - MARGIN * 2);
  if (!rect) {
    return { width, top: "50%", left: "50%", transform: "translate(-50%, -50%)" };
  }

  const left = Math.min(Math.max(rect.left, MARGIN), window.innerWidth - width - MARGIN);
  const roomBelow = window.innerHeight - rect.bottom;
  return roomBelow > 260
    ? { width, left, top: rect.bottom + GAP }
    : { width, left, bottom: window.innerHeight - rect.top + GAP };
}

function sameRect(a: DOMRect | null, b: DOMRect | null) {
  if (!a || !b) return a === b;
  return a.top === b.top && a.left === b.left && a.width === b.width && a.height === b.height;
}
