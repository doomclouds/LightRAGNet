export type JsonValue =
  | string
  | number
  | boolean
  | null
  | JsonValue[]
  | { [key: string]: JsonValue };

export type GraphNodeProperties = Record<string, JsonValue>;

export type GraphNodeDto = {
  id: string;
  label: string;
  size: number;
  color: string;
  type?: string | null;
  properties: GraphNodeProperties;
};

export type GraphEdgeDto = {
  id: string;
  source: string;
  target: string;
  type?: string | null;
  size: number;
  color: string;
  properties?: GraphNodeProperties;
};

export type GraphViewDto = {
  nodes: GraphNodeDto[];
  edges: GraphEdgeDto[];
  isTruncated: boolean;
};

export type GraphEntityExistsResponse = {
  exists: boolean;
};

export type GraphEntityEditDto = {
  updatedData: GraphNodeProperties;
  allowRename: boolean;
  allowMerge: boolean;
};

export type GraphRelationEditDto = {
  sourceEntity: string;
  targetEntity: string;
  updatedData: GraphNodeProperties;
};

export type GraphCurationSummaryDto = {
  merged: boolean;
  mergeStatus: string;
  mergeError?: string | null;
  operationStatus: string;
  targetEntity?: string | null;
  finalEntity: string;
  renamed: boolean;
};

export type GraphCurationResponse = {
  succeeded: boolean;
  status: string;
  message: string;
  data?: GraphNodeProperties | null;
  operationSummary?: GraphCurationSummaryDto | null;
  failureStage?: string | null;
};
