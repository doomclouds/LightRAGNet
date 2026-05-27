import { describe, expect, it } from 'vitest';
import type { SystemHealthResponse } from '@/api/systemStatusApi';
import {
  formatDurationMs,
  formatEvidenceValue,
  formatGeneratedAt,
  formatHealthJson,
  getStatusIconName,
  getStatusTone,
  summarizeEvidence
} from '@/features/system-status/systemStatusPresentation';

describe('system status presentation helpers', () => {
  it('maps health statuses to StatusPill tones', () => {
    expect(getStatusTone('Healthy')).toBe('success');
    expect(getStatusTone('Degraded')).toBe('warning');
    expect(getStatusTone('Unhealthy')).toBe('danger');
    expect(getStatusTone('NotMeasured')).toBe('neutral');
  });

  it('maps health statuses to semantic lucide icon names', () => {
    expect(getStatusIconName('Healthy')).toBe('CircleCheck');
    expect(getStatusIconName('Degraded')).toBe('TriangleAlert');
    expect(getStatusIconName('Unhealthy')).toBe('CircleX');
    expect(getStatusIconName('NotMeasured')).toBe('CircleDashed');
  });

  it('formats missing and millisecond durations compactly', () => {
    expect(formatDurationMs(null)).toBe('Not measured');
    expect(formatDurationMs(undefined)).toBe('Not measured');
    expect(formatDurationMs(845)).toBe('845 ms');
  });

  it('formats durations of at least one second with one decimal place', () => {
    expect(formatDurationMs(1000)).toBe('1.0 s');
    expect(formatDurationMs(1420)).toBe('1.4 s');
  });

  it('formats generated timestamps with stable date and time fields', () => {
    const formatted = formatGeneratedAt('2026-05-27T08:09:10.000Z');

    expect(formatted).toContain('2026-05-27');
    expect(formatted).toMatch(/\d{2}:\d{2}:\d{2}/);
  });

  it('returns Unknown for invalid generated timestamps', () => {
    expect(formatGeneratedAt('not-a-date')).toBe('Unknown');
    expect(formatGeneratedAt('')).toBe('Unknown');
  });

  it('summarizes empty evidence without leaking object placeholders', () => {
    expect(summarizeEvidence(null)).toBe('No evidence');
    expect(summarizeEvidence(undefined)).toBe('No evidence');
    expect(summarizeEvidence({})).toBe('No evidence');
  });

  it('summarizes evidence as compact key value pairs', () => {
    const summary = summarizeEvidence({
      provider: 'qdrant',
      latencyMs: 42,
      connected: true
    });

    expect(summary).toBe('provider=qdrant, latencyMs=42, connected=true');
  });

  it('summarizes arrays and objects readably with bounded length', () => {
    const summary = summarizeEvidence({
      endpoints: ['qdrant', 'neo4j', 'sqlite'],
      details: { host: 'localhost', port: 6333, path: '/collections/default/documents' }
    });

    expect(summary).toContain('endpoints=["qdrant","neo4j","sqlite"]');
    expect(summary).toContain('details={"host":"localhost","port":6333');
    expect(summary).not.toContain('[object Object]');
    expect(summary.length).toBeLessThanOrEqual(180);
  });

  it('summarizes circular evidence without falling back to object placeholders', () => {
    const circular: Record<string, unknown> = { provider: 'neo4j' };
    circular.self = circular;

    const summary = summarizeEvidence({ details: circular });

    expect(summary).toContain('"self":"[Circular]"');
    expect(summary).not.toContain('[object Object]');
  });

  it('formats nested evidence values with bounded length and circular guards', () => {
    const circular: Record<string, unknown> = { provider: 'neo4j' };
    circular.self = circular;

    const circularFormatted = formatEvidenceValue(circular);
    const boundedFormatted = formatEvidenceValue({
      endpoints: ['qdrant', 'neo4j', 'sqlite'],
      message: 'x'.repeat(120)
    });

    expect(circularFormatted).toContain('"self":"[Circular]"');
    expect(circularFormatted).not.toContain('[object Object]');
    expect(boundedFormatted).not.toContain('[object Object]');
    expect(boundedFormatted.length).toBeLessThanOrEqual(80);
  });

  it('formats the full health response as pretty JSON', () => {
    const response: SystemHealthResponse = {
      status: 'Healthy',
      generatedAt: '2026-05-27T08:09:10.000Z',
      durationMs: 1420,
      summary: {
        healthy: 1,
        degraded: 0,
        unhealthy: 0,
        notMeasured: 0
      },
      checks: [],
      fixFirst: [],
      featureImpacts: []
    };

    expect(formatHealthJson(response)).toBe(JSON.stringify(response, null, 2));
  });
});
