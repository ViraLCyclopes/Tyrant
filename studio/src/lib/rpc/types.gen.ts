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
  referencesCapped?: boolean;
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
  textures?: string[] | null;
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

export interface AssetRefsParams {
  filter?: string | null;
  type?: string | null;
  group?: string | null;
  bundle?: string | null;
}

export interface AssetRefsResult {
  refs: string[];
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
  newBundles?: number;
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

export type FrameworkState = "missing" | "current" | "outdated";

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

export interface JobCurrentResult {
  job: JobInfo | null;
}

export interface JobDoneNotification {
  jobId: string;
  result: unknown | null;
}

export interface JobFailedNotification {
  jobId: string;
  error: RpcErrorObject;
}

export interface JobInfo {
  jobId: string;
  title: string;
  fraction: number;
  message: string;
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

export interface LogNotification {
  level: string;
  message: string;
}

export interface ModAddSkinParams {
  id: string;
  species: string;
  name: string;
  base?: string | null;
  male?: boolean;
  female?: boolean;
  maps?: boolean;
}

export interface ModCheckReport {
  errors: string[];
  warnings: string[];
  missingCutouts?: string[] | null;
}

export interface ModColorPreviewParams {
  id: string;
  skin: string;
  colors?: string | null;
  variant?: string;
  sex?: string;
  seed?: number;
  count?: number;
  size?: number;
}

export interface ModCreateParams {
  id: string;
  name?: string | null;
  author?: string | null;
}

export interface ModDetail {
  id: string;
  name: string;
  version: string;
  author: string | null;
  description: string | null;
  dir: string;
  revision: string;
  manifestJson: string;
  replace: ModReplacementDto[];
  skins: ModSkinDto[];
}

export interface ModEnableParams {
  id: string;
  enabled: boolean;
}

export interface ModForgetSkinsParams {
  keys: string[];
}

export interface ModIdParams {
  id: string;
}

export interface ModInstallResult {
  message: string;
  warnings: string[];
}

export interface ModPreviewFiles {
  files: string[];
}

export interface ModRemoveReplacementParams {
  id: string;
  revision: string;
  texture: string;
}

export interface ModRemoveSkinParams {
  id: string;
  revision: string;
  skin: string;
  deleteFiles?: boolean;
}

export interface ModRenameSkinParams {
  id: string;
  revision: string;
  skin: string;
  name: string;
}

export interface ModReplaceParams {
  id: string;
  texture: string;
  png?: string | null;
}

export interface ModReplacementDto {
  texture: string;
  key: string | null;
  guid: string | null;
  file: string;
}

export interface ModRestoreCutoutsResult {
  restored: string[];
  problems: string[];
}

export interface ModRow {
  id: string;
  name: string;
  version: string;
  author: string | null;
  replacements: number;
  skins: number;
  state: string;
  enabled: boolean | null;
  dir: string | null;
  error: string | null;
}

export interface ModSaveManifestParams {
  id: string;
  revision: string;
  manifest: string;
}

export interface ModSetColorsParams {
  id: string;
  revision: string;
  skin: string;
  colors?: string | null;
}

export interface ModSetDetailsParams {
  id: string;
  revision: string;
  name: string;
  version: string;
  author?: string | null;
  description?: string | null;
}

export interface ModSetSkinFileParams {
  id: string;
  revision: string;
  skin: string;
  sex: string;
  slot: string;
  png?: string | null;
}

export interface ModSetThumbnailParams {
  id: string;
  revision: string;
  skin: string;
  png?: string | null;
}

export interface ModSkinDto {
  id: string;
  key: string;
  species: string;
  name: string;
  base: string;
  thumbnail: string | null;
  male: Record<string, string> | null;
  female: Record<string, string> | null;
  colorsJson: string | null;
  baseMaleSlots: string[] | null;
  baseFemaleSlots: string[] | null;
}

export interface ModSpeciesResult {
  hasDump: boolean;
  species: SpeciesSkinsRow[];
}

export interface ModThumbnailParams {
  id: string;
  file: string;
  size?: number;
}

export interface ModThumbnailResult {
  file: string | null;
}

export interface ModsListResult {
  mods: ModRow[];
  frameworkInstalled: boolean;
}

export interface OrphanSkinRow {
  species: string;
  key: string;
  number: number;
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
  shader?: string | null;
  animal?: boolean;
  cutoff?: number | null;
  slots?: PreviewSlot[] | null;
}

export interface PreviewSkin {
  ref: string;
  name: string;
  current: boolean;
}

export interface PreviewSlot {
  name: string;
  texture: string;
  file: string | null;
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

export interface SkinSlotsResult {
  orphans: OrphanSkinRow[];
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

export interface SpeciesSkinsRow {
  speciesId: string;
  vivarium: boolean;
  skins: VanillaSkinRow[];
}

export interface VanillaSkinRow {
  index: number;
  name: string;
  male: boolean;
  female: boolean;
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
  framework?: FrameworkState;
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
  "assets.refs": { params: AssetRefsParams; result: AssetRefsResult };
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
  "job.current": { params: void; result: JobCurrentResult };
  "mods.addSkin": { params: ModAddSkinParams; result: ModsListResult };
  "mods.check": { params: ModIdParams; result: ModCheckReport };
  "mods.colorPreview": { params: ModColorPreviewParams; result: ModPreviewFiles };
  "mods.create": { params: ModCreateParams; result: ModsListResult };
  "mods.enable": { params: ModEnableParams; result: ModsListResult };
  "mods.forgetSkins": { params: ModForgetSkinsParams; result: SkinSlotsResult };
  "mods.get": { params: ModIdParams; result: ModDetail };
  "mods.install": { params: ModIdParams; result: JobStarted };
  "mods.list": { params: void; result: ModsListResult };
  "mods.remove": { params: ModIdParams; result: ModsListResult };
  "mods.removeReplacement": { params: ModRemoveReplacementParams; result: ModDetail };
  "mods.removeSkin": { params: ModRemoveSkinParams; result: ModDetail };
  "mods.renameSkin": { params: ModRenameSkinParams; result: ModDetail };
  "mods.replace": { params: ModReplaceParams; result: ModsListResult };
  "mods.restoreCutouts": { params: ModIdParams; result: ModRestoreCutoutsResult };
  "mods.saveManifest": { params: ModSaveManifestParams; result: ModDetail };
  "mods.setColors": { params: ModSetColorsParams; result: ModDetail };
  "mods.setDetails": { params: ModSetDetailsParams; result: ModDetail };
  "mods.setSkinFile": { params: ModSetSkinFileParams; result: ModDetail };
  "mods.setThumbnail": { params: ModSetThumbnailParams; result: ModDetail };
  "mods.skinSlots": { params: void; result: SkinSlotsResult };
  "mods.species": { params: void; result: ModSpeciesResult };
  "mods.thumbnail": { params: ModThumbnailParams; result: ModThumbnailResult };
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
  "mods.install": ModInstallResult;
  "species.pack": SpeciesPackRunResult;
  "workspace.refreshAll": RefreshAllResult;
}

export interface RpcNotifications {
  "job.progress": JobProgressNotification;
  "job.done": JobDoneNotification;
  "job.failed": JobFailedNotification;
  "log": LogNotification;
}
