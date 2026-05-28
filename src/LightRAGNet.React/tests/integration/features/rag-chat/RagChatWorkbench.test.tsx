import { StrictMode, act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

import type { ChatMessage } from "@/types/ragChat";
import { AssistantMessage } from "@/features/rag-chat/AssistantMessage";

const { queryRagStream, getRagQueryData } = vi.hoisted(() => ({
  queryRagStream: vi.fn(),
  getRagQueryData: vi.fn()
}));

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

vi.mock("@/api/ragChatApi", () => ({
  queryRagStream,
  getRagQueryData
}));

import { RagChatWorkbench } from "@/features/rag-chat/RagChatWorkbench";

let host: HTMLDivElement;
let root: Root;

beforeEach(() => {
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
  window.history.pushState({}, "", "/");
  queryRagStream.mockReset();
  getRagQueryData.mockReset();
});

afterEach(async () => {
  await act(async () => {
    root.unmount();
  });
  host.remove();
  document.body.innerHTML = "";
});

describe("RagChatWorkbench", () => {
  test("renders chat pane, composer, settings, mode, references and debug controls", async () => {
    await renderWorkbench();

    expect(host.textContent).toContain("RAG Chat");
    expect(host.textContent).toContain("Query Settings");
    expect(host.textContent).toContain("New conversation");
    expect(host.textContent).toContain("Auto");
    expect(host.textContent).toContain("Reset to defaults");
    expect(host.querySelector(".rag-chat__workbench")).not.toBeNull();
    expect(host.querySelector(".rag-chat__settings")).not.toBeNull();
    expect(host.querySelector(".rag-chat__layout--fixed")).not.toBeNull();
    expect(host.querySelector(".rag-chat__layout--empty")).not.toBeNull();
    expect(host.querySelector(".rag-chat__chat--empty")).not.toBeNull();
    expect(host.querySelector(".rag-chat__messages")).toHaveAttribute("data-scroll-surface", "messages");
    expect(host.querySelector("[data-testid='rag-chat-composer']")).not.toBeNull();
    expect(getControl("Mode")).toBeInstanceOf(HTMLSelectElement);
    expect(getControl("Mode")).toHaveClass("lrn-select");
    expect(getControl("References")).toBeInstanceOf(HTMLInputElement);
    expect(getControl("Debug output")).toBeInstanceOf(HTMLSelectElement);
    expect(host.querySelector(".rag-chat__mode-segment")).toBeNull();
    expect(host.querySelectorAll(".rag-chat__setting-row")).toHaveLength(7);
    expect(host.textContent).toContain("Retrieval route and graph blend.");
    expect(host.textContent).toContain("Shape the answer before it is rendered.");
    expect(host.textContent).toContain("Stream the answer as tokens arrive.");
    expect(host.textContent).toContain("Surface source previews when metadata is available.");
    expect(host.textContent).toContain("Use the reranker to sharpen retrieved context.");
    expect(host.textContent).toContain("Number of chunks to retrieve.");
    expect(host.textContent).toContain("Chunks per document.");
    expect(host.textContent).toContain("Bias retrieval toward important terms.");
    expect(host.textContent).toContain("Filter out noisy concepts.");
    expect(host.textContent).toContain("Choose answer, context, or prompt inspection.");
    expect(host.querySelector(".rag-chat__settings-body > .rag-chat__reset-action")).not.toBeNull();
  });

  test("renders only real current state chips in the workbench heading", async () => {
    await renderWorkbench();

    const heading = [...host.querySelectorAll("h1")].find((item) => item.textContent === "RAG Chat");
    const pageHeader = heading?.closest(".rag-chat__topline");

    expect(pageHeader).not.toBeNull();
    expect(pageHeader?.textContent).toContain("Mix");
    expect(pageHeader?.textContent).toContain("Streaming");
    expect(pageHeader?.textContent).not.toContain("References");
    expect(pageHeader?.textContent).not.toContain("Message diagnostics");

    await act(async () => {
      const modeSelect = getControl("Mode") as HTMLSelectElement;
      modeSelect.value = "Bypass";
      modeSelect.dispatchEvent(new Event("change", { bubbles: true }));

      const streamToggle = getControl("Streaming") as HTMLInputElement;
      streamToggle.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    });

    expect(pageHeader?.textContent).toContain("Bypass");
    expect(pageHeader?.textContent).toContain("Non-stream");
  });

  test("keeps all query settings visible", async () => {
    await renderWorkbench();

    for (const label of [
      "Mode",
      "Response",
      "Streaming",
      "References",
      "Rerank",
      "TopK",
      "ChunkTopK",
      "High keywords",
      "Low keywords",
      "Debug output"
    ]) {
      expect(getControl(label)).toBeTruthy();
    }

    expect(selectOptions("Mode")).toEqual(["Mix", "Naive", "Bypass", "Local", "Global", "Hybrid"]);
    expect(selectOptions("Response")).toEqual([
      "Multiple Paragraphs",
      "Single Paragraph",
      "Bullet Points",
      "Concise"
    ]);
    expect(selectOptions("Debug output")).toEqual(["Answer", "ContextOnly", "PromptOnly"]);
  });

  test("renders recognizable document preview references as frontend new-tab links", async () => {
    await renderWorkbench({ initialAssistantReferenceUrl: "http://localhost/document-preview/1" });

    const link = host.querySelector<HTMLAnchorElement>("a[href='/document-preview/1']");

    expect(link).not.toBeNull();
    expect(link?.getAttribute("target")).toBe("_blank");
    expect(link?.getAttribute("rel")).toContain("noopener");
    expect(link?.getAttribute("rel")).toContain("noreferrer");
  });

  test("keeps current path base when normalizing document preview references", async () => {
    window.history.pushState({}, "", "/app/chat");

    await renderWorkbench({ initialAssistantReferenceUrl: "http://localhost/app/document-preview/1" });

    const link = host.querySelector<HTMLAnchorElement>("a[href='/app/document-preview/1']");

    expect(link).not.toBeNull();
    expect(link?.getAttribute("target")).toBe("_blank");
  });

  test.each(["ConvertedMarkdown", "OriginalArtifact"] as const)(
    "normalizes safe document preview urls for %s references",
    async (openKind) => {
      window.history.pushState({}, "", "/app/chat");
      const message = createAssistantMessage({
        metadata: {
          type: "metadata",
          mode: "Mix",
          stream: false,
          includeReferences: true,
          responseType: "Multiple Paragraphs",
          cachePolicy: "Cacheable request",
          references: [
            {
              referenceId: openKind,
              filePath: "/uploads/doc.md",
              fileName: `${openKind}.md`,
              previewUrl: "http://localhost/app/document-preview/12",
              openKind
            }
          ],
          highLevelKeywords: [],
          lowLevelKeywords: [],
          diagnostics: {}
        }
      });

      await act(async () => {
        root.render(<AssistantMessage message={message} onOpenDetails={() => undefined} />);
      });

      const link = host.querySelector<HTMLAnchorElement>("a[href='/app/document-preview/12']");

      expect(link).not.toBeNull();
      expect(link?.textContent).toContain(openKind);
      expect(link?.getAttribute("target")).toBe("_blank");
    }
  );

  test("renders unresolved references as plain text labels", async () => {
    const message = createAssistantMessage({
      metadata: {
        type: "metadata",
        mode: "Mix",
        stream: false,
        includeReferences: true,
        responseType: "Multiple Paragraphs",
        cachePolicy: "Cacheable request",
        references: [
          {
            referenceId: "missing-preview",
            filePath: "/uploads/unresolved.md",
            fileName: "unresolved.md",
            previewUrl: null,
            openKind: "ExternalOrUnresolved"
          }
        ],
        highLevelKeywords: [],
        lowLevelKeywords: [],
        diagnostics: {}
      }
    });

    await act(async () => {
      root.render(<AssistantMessage message={message} onOpenDetails={() => undefined} />);
    });

    const references = host.querySelector("[aria-label='References']");

    expect(references).not.toBeNull();
    expect(references?.textContent).toContain("unresolved.md");
    expect(references?.querySelector("a")).toBeNull();
  });

  test("does not create document preview links from file paths", async () => {
    const message = createAssistantMessage({
      metadata: {
        type: "metadata",
        mode: "Mix",
        stream: false,
        includeReferences: true,
        responseType: "Multiple Paragraphs",
        cachePolicy: "Cacheable request",
        references: [
          {
            referenceId: "file-path-only",
            filePath: "/document-preview/99",
            fileName: "path-only.md",
            previewUrl: null,
            openKind: "DocumentPreview"
          }
        ],
        highLevelKeywords: [],
        lowLevelKeywords: [],
        diagnostics: {}
      }
    });

    await act(async () => {
      root.render(<AssistantMessage message={message} onOpenDetails={() => undefined} />);
    });

    const references = host.querySelector("[aria-label='References']");

    expect(references?.textContent).toContain("path-only.md");
    expect(references?.querySelector("a")).toBeNull();
  });

  test("opens message details in a portal and auto-loads tabbed retrieval data", async () => {
    let resolveRetrievalData: (value: unknown) => void = () => undefined;
    getRagQueryData.mockReturnValue(
      new Promise((resolve) => {
        resolveRetrievalData = resolve;
      })
    );
    await renderWorkbench({ initialAssistantReferenceUrl: "http://localhost/document-preview/1" });

    await clickButton("View query details");

    expect(host.querySelector("[role='dialog']")).toBeNull();
    expect(document.body.querySelector(".rag-chat__dialog")?.classList.contains("lrn-modal")).toBe(true);
    expect(document.body.querySelector(".rag-chat__dialog")?.classList.contains("lrn-dialog")).toBe(false);
    expect(document.body.textContent).toContain("Query details");
    expect(document.body.textContent).toContain("Entities");
    expect(document.body.textContent).toContain("Relationships");
    expect(document.body.textContent).toContain("Chunks");
    expect(document.body.textContent).toContain("References");
    expect(document.body.textContent).toContain("Metadata");
    expect(document.body.textContent).toContain("Diagnostics");
    expect(document.body.textContent).toContain("Raw JSON");
    expect(document.body.textContent).toContain("Loading retrieval data");
    expect((getRagQueryData.mock.calls[0]?.[2] as { signal?: AbortSignal } | undefined)?.signal).toBeInstanceOf(AbortSignal);

    await act(async () => {
      resolveRetrievalData({
        status: "ok",
        message: "loaded",
        data: {
          entities: [{ entity: "entity-1" }],
          relationships: [{ relation: "relation-1" }],
          chunks: [{ id: "chunk-1" }],
          references: [{ fileName: "reference-1.md" }]
        },
        metadata: { elapsedMs: 12 }
      });
    });

    expect(document.body.textContent).toContain("entity-1");
    expect(document.body.querySelector(".rag-chat__detail-table")).not.toBeNull();
    await clickButton("Chunks");
    expect(document.body.textContent).toContain("chunk-1");
    await clickButton("Diagnostics");
    expect(document.body.textContent).toContain("source");
    expect(document.body.textContent).toContain("test-preview");
    expect(getRagQueryData).toHaveBeenCalledTimes(1);
  });

  test("loads retrieval data after StrictMode remounts query details effects", async () => {
    let resolveRetrievalData: (value: unknown) => void = () => undefined;
    getRagQueryData.mockReturnValue(
      new Promise((resolve) => {
        resolveRetrievalData = resolve;
      })
    );
    await renderWorkbench({ initialAssistantReferenceUrl: "http://localhost/document-preview/1" }, { strictMode: true });

    await clickButton("View query details");

    await act(async () => {
      resolveRetrievalData({
        status: "ok",
        message: "loaded",
        data: {
          entities: [{ entity: "strict-entity" }],
          relationships: [],
          chunks: [],
          references: []
        },
        metadata: { elapsedMs: 8 }
      });
    });

    expect(document.body.textContent).toContain("strict-entity");
    expect(document.body.textContent).toContain("Retrieval data loaded");
  });

  test("aborts retrieval data loading when details dialog closes", async () => {
    let capturedSignal: AbortSignal | undefined;
    getRagQueryData.mockImplementation(
      (_apiBase: string, _request: unknown, options: { signal?: AbortSignal }) => {
        capturedSignal = options.signal;
        return new Promise(() => undefined);
      }
    );
    await renderWorkbench({ initialAssistantReferenceUrl: "http://localhost/document-preview/1" });

    await clickButton("View query details");
    expect(capturedSignal?.aborted).toBe(false);

    await clickButton("Close query details");

    expect(capturedSignal?.aborted).toBe(true);
  });

  test("retries retrieval data loading after closing a pending details dialog", async () => {
    const capturedSignals: AbortSignal[] = [];
    getRagQueryData.mockImplementation(
      (_apiBase: string, _request: unknown, options: { signal?: AbortSignal }) => {
        if (options.signal) {
          capturedSignals.push(options.signal);
        }

        return new Promise(() => undefined);
      }
    );
    await renderWorkbench({ initialAssistantReferenceUrl: "http://localhost/document-preview/1" });

    await clickButton("View query details");

    expect(getRagQueryData).toHaveBeenCalledTimes(1);
    expect(document.body.textContent).toContain("Loading retrieval data");

    await clickButton("Close query details");

    expect(capturedSignals[0]?.aborted).toBe(true);

    await clickButton("View query details");

    expect(getRagQueryData).toHaveBeenCalledTimes(2);
    expect(capturedSignals[1]?.aborted).toBe(false);
  });

  test("keeps retrieval data errors inside the details dialog", async () => {
    getRagQueryData.mockRejectedValue(new Error("retrieval exploded"));
    await renderWorkbench({ initialAssistantReferenceUrl: "http://localhost/document-preview/1" });

    await clickButton("View query details");

    expect(document.body.textContent).toContain("retrieval exploded");

    await clickButton("x");

    expect(host.querySelector(".rag-chat__message .rag-chat__error")).toBeNull();
  });

  test("disables retrieval controls in bypass mode", async () => {
    await renderWorkbench();

    await act(async () => {
      const modeSelect = getControl("Mode") as HTMLSelectElement;
      modeSelect.value = "Bypass";
      modeSelect.dispatchEvent(new Event("change", { bubbles: true }));
    });

    expect((getControl("References") as HTMLInputElement).disabled).toBe(true);
    expect((getControl("Rerank") as HTMLInputElement).disabled).toBe(true);
    expect((getControl("TopK") as HTMLInputElement).disabled).toBe(true);
    expect((getControl("ChunkTopK") as HTMLInputElement).disabled).toBe(true);
    expect((getControl("High keywords") as HTMLInputElement).disabled).toBe(true);
    expect((getControl("Low keywords") as HTMLInputElement).disabled).toBe(true);
  });

  test("aborts the active stream when the workbench unmounts", async () => {
    let capturedSignal: AbortSignal | undefined;
    queryRagStream.mockImplementation((_apiBase: string, _request: unknown, options: { signal?: AbortSignal }) => {
      capturedSignal = options.signal;
      return new Promise(() => undefined);
    });
    await renderWorkbench();

    await act(async () => {
      const input = getControl("Message") as HTMLTextAreaElement;
      setNativeValue(input, "Explain cache hit rate");
      input.dispatchEvent(new InputEvent("input", { bubbles: true, data: "Explain cache hit rate" }));
    });
    await clickButton("Send");

    expect(capturedSignal?.aborted).toBe(false);

    await act(async () => {
      root.unmount();
    });
    root = createRoot(host);

    expect(capturedSignal?.aborted).toBe(true);
  });

  test("applies stream updates and ends running state after StrictMode remounts effects", async () => {
    queryRagStream.mockImplementation(
      async (_apiBase: string, _request: unknown, options: { onChunk?: (chunk: string) => void }) => {
        options.onChunk?.("Strict answer");
      }
    );
    await renderWorkbench({}, { strictMode: true });

    await act(async () => {
      const input = getControl("Message") as HTMLTextAreaElement;
      setNativeValue(input, "Explain strict mode");
      input.dispatchEvent(new InputEvent("input", { bubbles: true, data: "Explain strict mode" }));
    });
    await clickButton("Send");

    expect(host.textContent).toContain("Strict answer");

    await act(async () => {
      const input = getControl("Message") as HTMLTextAreaElement;
      setNativeValue(input, "Next question");
      input.dispatchEvent(new InputEvent("input", { bubbles: true, data: "Next question" }));
    });

    expect(getSendButton()).not.toBeDisabled();
  });
});

async function renderWorkbench(
  props: Partial<React.ComponentProps<typeof RagChatWorkbench>> = {},
  options: { strictMode?: boolean } = {}
) {
  await act(async () => {
    const workbench = <RagChatWorkbench apiBase="http://localhost" {...props} />;
    root.render(options.strictMode ? <StrictMode>{workbench}</StrictMode> : workbench);
  });
}

function createAssistantMessage(overrides: Partial<ChatMessage>): ChatMessage {
  return {
    id: "assistant-message",
    role: "Assistant",
    text: "Assistant answer",
    isComplete: true,
    isStreaming: false,
    isLoadingRetrievalData: false,
    ...overrides
  };
}

function setNativeValue(element: HTMLTextAreaElement, value: string): void {
  const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, "value")?.set;
  setter?.call(element, value);
}

function getControl(label: string): Element {
  const control = host.querySelector(`[aria-label='${label}']`);

  if (!control) {
    throw new Error(`Missing control: ${label}`);
  }

  return control;
}

function selectOptions(label: string): string[] {
  const select = getControl(label) as HTMLSelectElement;

  return [...select.options].map((option) => option.textContent ?? "");
}

async function clickButton(label: string): Promise<void> {
  const button = [...document.body.querySelectorAll("button")].find(
    (item) => item.textContent?.includes(label) || item.getAttribute("aria-label")?.includes(label)
  );

  if (!button) {
    throw new Error(`Missing button: ${label}`);
  }

  await act(async () => {
    button.dispatchEvent(new MouseEvent("click", { bubbles: true }));
  });
}

function getSendButton(): HTMLButtonElement {
  const button = [...host.querySelectorAll("button")].find((item) => item.textContent?.includes("Send"));

  if (!button) {
    throw new Error("Missing Send button");
  }

  return button;
}
