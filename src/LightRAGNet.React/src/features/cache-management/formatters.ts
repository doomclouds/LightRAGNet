const numberFormatter = new Intl.NumberFormat("en-US");

export function formatNumber(value: number): string {
  return numberFormatter.format(value);
}

export function formatHitRate(value: number | null): string {
  if (value === null) {
    return "N/A";
  }

  return `${(value * 100).toFixed(1)}%`;
}

export function formatLatencySaved(valueMs: number | null): string {
  if (valueMs === null) {
    return "N/A";
  }

  if (valueMs < 1000) {
    return `${formatNumber(valueMs)} ms`;
  }

  if (valueMs < 60_000) {
    return `${(valueMs / 1000).toFixed(1)} sec`;
  }

  if (valueMs < 3_600_000) {
    return `${Math.round(valueMs / 60_000)} min`;
  }

  return `${(valueMs / 3_600_000).toFixed(1)} hr`;
}

export function formatDateTime(value: string | null): string {
  if (!value) {
    return "No hits";
  }

  const date = new Date(value);
  if (Number.isNaN(date.valueOf())) {
    return value;
  }

  return date.toLocaleString(undefined, {
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit"
  });
}

export function normalizeTone(value: string | null | undefined): "good" | "info" | "warn" | "bad" | "neutral" {
  const normalized = value?.toLowerCase() ?? "";

  if (normalized.includes("high") || normalized.includes("good") || normalized.includes("healthy")) {
    return "good";
  }

  if (normalized.includes("medium") || normalized.includes("watch") || normalized.includes("warn")) {
    return "warn";
  }

  if (normalized.includes("low") || normalized.includes("measured")) {
    return "info";
  }

  if (normalized.includes("critical") || normalized.includes("bad") || normalized.includes("danger")) {
    return "bad";
  }

  return "neutral";
}

export function getValueTone(value: string | null | undefined): "good" | "info" | "warn" | "bad" | "neutral" {
  const normalized = value?.toLowerCase() ?? "";

  if (normalized.includes("very high") || normalized.includes("high") || normalized.includes("good")) {
    return "good";
  }

  if (normalized.includes("medium")) {
    return "warn";
  }

  if (normalized.includes("low")) {
    return "info";
  }

  return normalizeTone(value);
}

export function getRiskTone(value: string | null | undefined): "good" | "info" | "warn" | "bad" | "neutral" {
  const normalized = value?.toLowerCase() ?? "";

  if (normalized.includes("high") || normalized.includes("critical") || normalized.includes("danger")) {
    return "bad";
  }

  if (normalized.includes("medium") || normalized.includes("watch") || normalized.includes("warn")) {
    return "warn";
  }

  if (normalized.includes("low") || normalized.includes("safe") || normalized.includes("current")) {
    return "good";
  }

  return normalizeTone(value);
}
