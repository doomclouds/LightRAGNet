// @vitest-environment happy-dom

import { act } from "react";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, test, vi } from "vitest";

import type { ChatMessage } from "../types/ragChat";
import { AssistantMessage } from "./AssistantMessage";

const { queryRagStream, getRagQueryData } = vi.hoisted(() => ({
  queryRagStream: vi.fn(),
  getRagQueryData: vi.fn()
}));

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

vi.mock("../api/ragChatApi", () => ({
  queryRagStream,
  getRagQueryData
}));

import { RagChatWorkbench } from "./RagChatWorkbench";

let host: HTMLDivElement;
let root: Root;

beforeEach(() => {
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
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
    expect(host.textContent).toContain("Query settings");
    expect(host.querySelector("[data-testid='rag-chat-composer']")).not.toBeNull();
    expect(getControl("Mode")).toBeInstanceOf(HTMLSelectElement);
    expect(getControl("References")).toBeInstanceOf(HTMLInputElement);
    expect(getControl("Debug output")).toBeInstanceOf(HTMLSelectElement);
  });

  test("renders preview references as new-tab links", async () => {
    await renderWorkbench({ initialAssistantReferenceUrl: "http://localhost/document-preview/1" });

    const link = host.querySelector<HTMLAnchorElement>("a[href='http://localhost/document-preview/1']");

    expect(link).not.toBeNull();
    expect(link?.getAttribute("target")).toBe("_blank");
    expect(link?.getAttribute("rel")).toContain("noopener");
    expect(link?.getAttribute("rel")).toContain("noreferrer");
  });

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

    await clickButton("x");

    expect(capturedSignal?.aborted).toBe(true);
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
});

async function renderWorkbench(props: Partial<React.ComponentProps<typeof RagChatWorkbench>> = {}) {
  await act(async () => {
    root.render(<RagChatWorkbench apiBase="http://localhost" {...props} />);
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
