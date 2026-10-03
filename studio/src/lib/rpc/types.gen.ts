// Generated from the C# DTOs in core/Tyrant.Rpc by `tyrant rpc --emit-ts`. Do not edit by hand:
// run `npm run gen:types` in studio/ (a test fails while this file is out of date).

export interface AppInfo {
  version: string;
  protocolVersion: number;
  runtime: string;
}

export interface AssetBundlesParams {
  group: string;
}

export interface AssetBundlesResult {
  bundles: AssetCount[];
}

export interface AssetCount {
  name: string;
  count: number;
}

export interface AssetDetails {
  asset: AssetRow;
  byteSize: number;
  references: AssetReferenceRow[];
  fields: unknown;
}

export interface AssetExportFailure {
  name: string;
  type: string;
  error: string;
}

export interface AssetExportParams {
  refs: string[];
}

export interface AssetExportRunResult {
  exported: number;
  failed: number;
  reportPath: string;
  failures: AssetExportFailure[];
  notes?: string[] | null;
}

export interface AssetIndexRunResult {
  assets: number;
  failures: number;
  missingBundles: number;
}

export interface AssetListParams {
  filter?: string | null;
  type?: string | null;
  group?: string | null;
  bundle?: string | null;
  page?: number;
  pageSize?: number;
}

export interface AssetListResult {
  rows: AssetRow[];
  total: number;
  page: number;
  pageSize: number;
}

export interface AssetPreview {
  kind: PreviewKind;
  files: string[];
  width?: number | null;
  height?: number | null;
  format?: string | null;
  mipCount?: number | null;
  vertices?: number | null;
  triangles?: number | null;
  skinned?: boolean | null;
  message?: string | null;
  materials?: PreviewMaterial[] | null;
  skins?: PreviewSkin[] | null;
}

export interface AssetRefParams {
  ref: string;
}

export interface AssetReferenceRow {
  field: string;
  ref: string | null;
  type: string | null;
  name: string | null;
  external: string | null;
}

export interface AssetRow {
  ref: string;
  bundle: string;
  pathId: number;
  type: string;
  name: string;
  containerPath: string | null;
  guid: string | null;
  script: string | null;
}

export interface AssetsSummary {
  assets: number;
  bundles: number;
  groups: AssetCount[];
  types: AssetCount[];
  warnings: string[];
  failures: number;
  missingBundles: number;
  stale: boolean;
}

export interface DataCompareParams {
  type: string;
  names: string[];
  onlyDifferences?: boolean;
}

export interface DataCompareResult {
  names: string[];
  fields: string[];
  values: string[][];
}

export interface DataExportParams {
  type: string;
  format?: string;
  path?: string | null;
}

export interface DataExportResult {
  path: string;
  count: number;
}

export interface DataObjectParams {
  type: string;
  name: string;
}

export interface DataObjectResult {
  type: string;
  name: string;
  json: unknown;
}

export interface DataObjectsParams {
  type: string;
  filter?: string | null;
}

export interface DataObjectsResult {
  names: string[];
}

export interface DataQueryParams {
  type: string;
  filter?: string | null;
  sort?: string | null;
  descending?: boolean;
  page?: number;
  pageSize?: number;
  columns?: string[] | null;
}

export interface DataQueryResult {
  type: string;
  allColumns: string[];
  columns: string[];
  rows: DataRow[];
  total: number;
  page: number;
  pageSize: number;
}

export interface DataRow {
  name: string;
  values: string[];
}

export interface DataTypeInfo {
  fullName: string;
  shortName: string;
  count: number;
}

export interface DataTypesResult {
  createdUtc: string;
  buildGuid: string;
  types: DataTypeInfo[];
  errors: string[];
}

export interface DecompileItem {
  assembly: string;
  success: boolean;
  error: string | null;
}

export interface DecompileParams {
  assemblies?: string[] | null;
}

export interface DecompileRunResult {
  assemblies: DecompileItem[];
}

export interface DiagnosticsResult {
  text: string;
}

export interface DumpRunParams {
  timeoutSeconds?: number;
}

export interface DumpRunResult {
  objects: number;
  types: number;
  languages: number;
  errors: string[];
}

export interface DumperInstallResult {
  installedLoader: boolean;
  message: string;
}

export interface DumperUninstallResult {
  removedLoader: boolean;
  message: string;
}

export interface EnvironmentList {
  grounds: EnvironmentOption[];
  skies: EnvironmentOption[];
}

export interface EnvironmentOption {
  id: string;
  label: string;
}

export interface EnvironmentParams {
  id: string;
}

export interface EnvironmentTexture {
  id: string;
  kind: string;
  files: string[];
}

export interface InstallDetectParams {
  gamePath?: string | null;
}

export interface InstallInfo {
  rootDir: string;
  steamAppId: string | null;
  buildGuid: string;
}

export type InstallState = "notInstalled" | "loaderOnly" | "installed" | "conflict";

export interface JobCancelParams {
  jobId: string;
}

export interface JobCancelResult {
  cancelled: boolean;
}

export interface JobDoneNotification {
  jobId: string;
  result: unknown | null;
}

export interface JobFailedNotification {
  jobId: string;
  error: RpcErrorObject;
}

export interface JobProgressNotification {
  jobId: string;
  fraction: number;
  message: string;
}

export interface JobStarted {
  jobId: string;
}

export interface LanguageInfo {
  code: string;
  name: string;
  termCount: number;
}

export interface LanguagesResult {
  languages: LanguageInfo[];
}

export interface LocalizationQueryParams {
  filter?: string | null;
  languages?: string[] | null;
  page?: number;
  pageSize?: number;
}

export interface LocalizationQueryResult {
  languages: string[];
  rows: LocalizationRow[];
  total: number;
  page: number;
  pageSize: number;
}

export interface LocalizationRow {
  term: string;
  values: string[];
}

export interface OutputStatus {
  name: string;
  createdUtc: string;
  stale: boolean;
}

export type PreviewKind = "texture" | "model" | "none";

export interface PreviewMaterial {
  name: string;
  baseColor: string | null;
  normal: string | null;
  skinnable: boolean;
}

export interface PreviewSkin {
  ref: string;
  name: string;
  current: boolean;
}

export interface RefreshAllResult {
  steps: RefreshStep[];
}

export interface RefreshStep {
  name: string;
  status: RefreshStepStatus;
  message: string;
}

export type RefreshStepStatus = "ok" | "failed" | "skipped";

export interface RpcErrorData {
  code: string;
  fix: string | null;
}

export interface RpcErrorObject {
  code: number;
  message: string;
  data: RpcErrorData | null;
}

export interface SpeciesListResult {
  species: SpeciesRow[];
}

export interface SpeciesPackParams {
  key: string;
}

export interface SpeciesPackRunResult {
  directory: string;
  models: number;
  textures: number;
  failed: number;
  targetsPath: string;
  notes?: string[] | null;
}

export interface SpeciesRow {
  key: string;
  displayName: string;
  vivarium: boolean;
  group: string;
  prefabRef: string;
  textures: number;
}

export interface WorkspaceOpenParams {
  dir: string;
  gamePath?: string | null;
}

export interface WorkspaceStatus {
  dir: string;
  gameRoot: string;
  steamAppId: string | null;
  buildGuid: string;
  stale: boolean;
  outputs: OutputStatus[];
  dumper: InstallState;
  hasData: boolean;
  hasAssetIndex: boolean;
  hasSource: boolean;
}

export interface RpcMethods {
  "app.diagnostics": { params: void; result: DiagnosticsResult };
  "app.info": { params: void; result: AppInfo };
  "assets.bundles": { params: AssetBundlesParams; result: AssetBundlesResult };
  "assets.environment": { params: EnvironmentParams; result: EnvironmentTexture };
  "assets.environments": { params: void; result: EnvironmentList };
  "assets.export": { params: AssetExportParams; result: JobStarted };
  "assets.get": { params: AssetRefParams; result: AssetDetails };
  "assets.index": { params: void; result: JobStarted };
  "assets.list": { params: AssetListParams; result: AssetListResult };
  "assets.preview": { params: AssetRefParams; result: AssetPreview };
  "assets.summary": { params: void; result: AssetsSummary };
  "data.compare": { params: DataCompareParams; result: DataCompareResult };
  "data.export": { params: DataExportParams; result: DataExportResult };
  "data.languages": { params: void; result: LanguagesResult };
  "data.localization": { params: LocalizationQueryParams; result: LocalizationQueryResult };
  "data.object": { params: DataObjectParams; result: DataObjectResult };
  "data.objects": { params: DataObjectsParams; result: DataObjectsResult };
  "data.query": { params: DataQueryParams; result: DataQueryResult };
  "data.types": { params: void; result: DataTypesResult };
  "decompile.run": { params: DecompileParams; result: JobStarted };
  "dump.install": { params: void; result: JobStarted };
  "dump.run": { params: DumpRunParams; result: JobStarted };
  "dump.uninstall": { params: void; result: DumperUninstallResult };
  "install.detect": { params: InstallDetectParams; result: InstallInfo };
  "job.cancel": { params: JobCancelParams; result: JobCancelResult };
  "species.list": { params: void; result: SpeciesListResult };
  "species.pack": { params: SpeciesPackParams; result: JobStarted };
  "workspace.create": { params: WorkspaceOpenParams; result: WorkspaceStatus };
  "workspace.open": { params: WorkspaceOpenParams; result: WorkspaceStatus };
  "workspace.refreshAll": { params: void; result: JobStarted };
  "workspace.status": { params: void; result: WorkspaceStatus };
}

/** What job.done delivers for each job method. */
export interface RpcJobs {
  "assets.export": AssetExportRunResult;
  "assets.index": AssetIndexRunResult;
  "decompile.run": DecompileRunResult;
  "dump.install": DumperInstallResult;
  "dump.run": DumpRunResult;
  "species.pack": SpeciesPackRunResult;
  "workspace.refreshAll": RefreshAllResult;
}

export interface RpcNotifications {
  "job.progress": JobProgressNotification;
  "job.done": JobDoneNotification;
  "job.failed": JobFailedNotification;
}
