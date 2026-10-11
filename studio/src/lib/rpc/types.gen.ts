// Generated from the C# DTOs in core/Tyrant.Rpc by `tyrant rpc --emit-ts`. Do not edit by hand:
// run `npm run gen:types` in studio/ (a test fails while this file is out of date).

export interface AnimationInfo {
  id: string;
  name: string;
  length: number;
  frameRate: number;
  loops: boolean;
  travels: boolean;
}

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
  format?: string | null;
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

export interface BlenderOpenDto {
  projectFile: string;
  how: string;
  gameChanged: boolean;
}

export interface BlenderOpenParams {
  species?: string;
  skin?: string | null;
  mod?: string | null;
  fresh?: boolean;
  lods?: boolean;
  sex?: string;
  prefabRef?: string | null;
  ik?: boolean;
  animations?: string[] | null;
}

export interface BlenderSetPathParams {
  path: string | null;
}

export interface BlenderStatusDto {
  found: boolean;
  exe: string | null;
  version: string | null;
  supported: boolean;
  missingConfigured: string | null;
  addon: string;
  addonInstalled: string | null;
  addonBundled: string | null;
  problem: string | null;
}

export interface CustomSpeciesRow {
  speciesId: string;
  name: string;
  modId: string;
  modName: string;
  own: boolean;
  inWorkspace: boolean;
  installed: boolean;
  enabled: boolean;
  skins: VanillaSkinRow[];
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

export interface GamePackageFrameworkParams {
  out?: string | null;
}

export interface GamePackageFrameworkResult {
  path: string;
}

export interface IkChainRow {
  name: string;
  kind: string;
  joints: string[];
  poleFrom: string | null;
  influence: number;
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
  group?: string | null;
}

export interface ModAddSpeciesParams {
  id: string;
  revision: string;
  speciesId: string;
  template: string;
  name: string;
  genus: string;
  epithet: string;
  cost?: number | null;
  stats?: Record<string, number> | null;
  startingSkin?: number;
}

export interface ModAnimalDatabase {
  animals: ModAnimalDto[];
  fields: ModStatDto[];
}

export interface ModAnimalDto {
  id: string;
  key: string;
  base: string;
  name: string;
  genus: string;
  epithet: string;
  cost: number | null;
  stats: Record<string, number>;
  inheritedCost: number | null;
  inheritedStats: Record<string, number>;
  dataFields: ModAnimalFieldDto[];
  localization: Record<string, string>;
  icon: string | null;
  prefab: SpeciesPrefab | null;
  startingSkin?: number;
  startingSkinName?: string | null;
  swatch?: string | null;
  portraits?: Record<string, string> | null;
}

export interface ModAnimalFieldDto {
  path: string;
  kind: string;
  value: string;
  templateValue: string;
  editable: boolean;
}

export interface ModArrangeSkinsParams {
  id: string;
  revision: string;
  order: ModSkinPlace[];
}

export interface ModCheckReport {
  errors: string[];
  warnings: string[];
  missingCutouts?: string[] | null;
}

export interface ModClearRigParams {
  id: string;
  revision: string;
  target: string;
  skin?: string | null;
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

export interface ModCopyBaseFileParams {
  id: string;
  revision: string;
  skin: string;
  sex: string;
  slot: string;
}

export interface ModCreateParams {
  id: string;
  name?: string | null;
  author?: string | null;
}

export interface ModCustomSpeciesParams {
  id?: string | null;
}

export interface ModCustomSpeciesResult {
  species: CustomSpeciesRow[];
}

export interface ModDeleteParams {
  id: string;
  removeFromGame?: boolean;
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
  models: ModModelDto[];
  sounds: ModSoundDto[];
  filesStamp?: string;
  prefabFiles?: Record<string, string> | null;
  prefabTargets?: ModPrefabTargetDto[] | null;
  usesSpeciesFrom?: ModSpeciesUseDto[] | null;
}

export interface ModEnableParams {
  id: string;
  enabled: boolean;
}

export interface ModExportParams {
  id: string;
  out?: string | null;
}

export interface ModExportResult {
  path: string;
  warnings: string[];
}

export interface ModForgetSkinsParams {
  keys: string[];
}

export interface ModIdParams {
  id: string;
}

export interface ModImportParams {
  file: string;
  replace?: boolean;
}

export interface ModImportResult {
  id: string;
  problems: string[];
  mods: ModsListResult;
}

export interface ModInstallResult {
  message: string;
  warnings: string[];
}

export interface ModModelDto {
  target: string;
  skin: string | null;
  file: string;
  lods: ModModelLodDto[];
  errors: string[];
  warnings: string[];
  stale: boolean;
  origin: string | null;
  originChanged: boolean;
  rig?: ModRigBone[] | null;
}

export interface ModModelLodDto {
  file: string;
  vertices: number;
  index32: boolean;
  vanilla: number;
}

export interface ModModelPreview {
  prefabRef: string;
  lods: ModModelPreviewLod[];
  materials: PreviewMaterial[];
}

export interface ModModelPreviewLod {
  file: string;
  vertices: number;
}

export interface ModModelPreviewParams {
  id: string;
  target: string;
  skin?: string | null;
}

export interface ModPortraitDto {
  slot: string;
  file: string | null;
  own: boolean;
  rendered: boolean;
  width: number;
  height: number;
  inherited?: boolean;
}

export interface ModPortraitSourceDto {
  png?: string | null;
  data?: string | null;
  rendered?: boolean;
}

export interface ModPortraitsParams {
  id: string;
  skin?: string | null;
  speciesId?: string | null;
}

export interface ModPortraitsResult {
  portraits: ModPortraitDto[];
}

export interface ModPrefabFieldDto {
  object: string;
  component: string;
  field: string;
  kind: string;
  gameValue: string;
  value: string | null;
  editable: boolean;
  enumNames: string[] | null;
  enumValues: number[] | null;
}

export interface ModPrefabFields {
  target: string;
  fields: ModPrefabFieldDto[];
}

export interface ModPrefabFieldsParams {
  id: string;
  target: string;
}

export interface ModPrefabTargetDto {
  target: string;
  file: string;
  fields: number;
}

export interface ModPreviewFiles {
  files: string[];
}

export interface ModRebuildModelsParams {
  id: string;
  revision: string;
}

export interface ModRemoveModelParams {
  id: string;
  revision: string;
  target: string;
  skin?: string | null;
}

export interface ModRemovePrefabTargetParams {
  id: string;
  revision: string;
  target: string;
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

export interface ModRemoveSoundParams {
  id: string;
  revision: string;
  event: string;
  species?: string | null;
  skin?: string | null;
}

export interface ModRenameSkinParams {
  id: string;
  revision: string;
  skin: string;
  name: string;
}

export interface ModReplaceModelParams {
  id: string;
  file: string;
  revision?: string | null;
  target?: string | null;
  prefabRef?: string | null;
  skin?: string | null;
}

export interface ModReplaceParams {
  id: string;
  texture: string;
  png?: string | null;
}

export interface ModReplaceSoundParams {
  id: string;
  event: string;
  files: string[];
  revision?: string | null;
  species?: string | null;
  skin?: string | null;
  volume?: number | null;
  agePitch?: number | null;
  chance?: number | null;
  likeGame?: boolean;
}

export interface ModReplaceSoundsParams {
  id: string;
  revision: string;
  sounds: ModSoundFilesDto[];
  species?: string | null;
  skin?: string | null;
}

export interface ModReplacementDto {
  texture: string;
  key: string | null;
  guid: string | null;
  file: string;
}

export interface ModResetPrefabFieldParams {
  id: string;
  revision: string;
  target: string;
  object: string;
  component: string;
  field: string;
}

export interface ModRestoreCutoutsResult {
  restored: string[];
  problems: string[];
}

export interface ModRestoreSnapshotParams {
  id: string;
  revision: string;
  manifest: string;
  prefabs: Record<string, string>;
}

export interface ModRigBone {
  bone: string;
  move: number[];
  rotate: number[];
  scale: number[];
}

export interface ModRigParams {
  id: string;
  target: string;
  skin?: string | null;
}

export interface ModRigView {
  bones: ModRigBone[];
  hasModel: boolean;
  errors: string[];
  warnings: string[];
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
  clashes?: string[] | null;
  usesSpeciesFrom?: ModSpeciesUseDto[] | null;
  cooked?: string | null;
}

export interface ModSampleColorsParams {
  id: string;
  skin: string;
  colors?: string | null;
  variant?: string;
  seed?: number;
  sex?: string;
}

export interface ModSampledColors {
  a: string | null;
  b: string | null;
  secondary: string | null;
  eye: string | null;
  strength: number;
  softness: number;
  hue: number;
  saturation: number;
  value: number;
}

export interface ModSetColorsParams {
  id: string;
  revision: string;
  skin: string;
  colors?: string | null;
}

export interface ModSetCompressionParams {
  id: string;
  revision: string;
  file: string | null;
  mode: string | null;
}

export interface ModSetDetailsParams {
  id: string;
  revision: string;
  name: string;
  version: string;
  author?: string | null;
  description?: string | null;
}

export interface ModSetPortraitsParams {
  id: string;
  revision: string;
  skin: string;
  set: Record<string, ModPortraitSourceDto | null>;
}

export interface ModSetPrefabFieldParams {
  id: string;
  revision: string;
  target: string;
  object: string;
  component: string;
  field: string;
  value: unknown;
}

export interface ModSetSkinFileParams {
  id: string;
  revision: string;
  skin: string;
  sex: string;
  slot: string;
  png?: string | null;
}

export interface ModSetSoundParams {
  id: string;
  revision: string;
  event: string;
  species?: string | null;
  skin?: string | null;
  volume?: number | null;
  agePitch?: number | null;
  newSpecies?: string | null;
  newSkin?: string | null;
  forEveryone?: boolean | null;
  chance?: number | null;
  likeGame?: boolean;
}

export interface ModSetSpeciesIconParams {
  id: string;
  revision: string;
  speciesId: string;
  png?: string | null;
}

export interface ModSetSpeciesParams {
  id: string;
  revision: string;
  speciesId: string;
  name: string;
  genus: string;
  epithet: string;
  cost: number | null;
  stats: Record<string, number>;
  dataFields?: Record<string, string> | null;
  localization?: Record<string, string> | null;
}

export interface ModSetSpeciesPortraitsParams {
  id: string;
  revision: string;
  speciesId: string;
  set: Record<string, ModPortraitSourceDto | null>;
}

export interface ModSetSpeciesPrefabParams {
  id: string;
  revision: string;
  speciesId: string;
  bundle?: string | null;
  asset?: string | null;
}

export interface ModSetSpeciesStartingSkinParams {
  id: string;
  revision: string;
  speciesId: string;
  startingSkin: number;
  name?: string | null;
}

export interface ModSetSpeciesSwatchParams {
  id: string;
  revision: string;
  speciesId: string;
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
  model?: string | null;
  rig?: ModRigBone[] | null;
  group?: string | null;
  portraits?: Record<string, string> | null;
}

export interface ModSkinModel {
  prefabRef: string;
  maps: Record<string, string>;
  ownModel?: ModSkinOwnModel | null;
}

export interface ModSkinModelParams {
  id: string;
  skin: string;
  sex?: string;
}

export interface ModSkinOwnModel {
  file: string;
  materials: PreviewMaterial[];
}

export interface ModSkinPlace {
  id: string;
  group?: string | null;
}

export interface ModSoundDto {
  event: string;
  name: string;
  group: string;
  species: string | null;
  skin: string | null;
  files: string[];
  volume: number;
  agePitch: number;
  chance: number | null;
}

export interface ModSoundFilesDto {
  event: string;
  files: string[];
}

export interface ModSpeciesModelParams {
  id: string;
  speciesId: string;
  sex?: string;
}

export interface ModSpeciesResult {
  hasDump: boolean;
  species: SpeciesSkinsRow[];
}

export interface ModSpeciesTemplateDto {
  template: string;
  found: boolean;
  cost: number | null;
  stats: Record<string, number>;
}

export interface ModSpeciesTemplateParams {
  template: string;
}

export interface ModSpeciesTexturesParams {
  species: string;
}

export interface ModSpeciesTexturesResult {
  speciesId: string | null;
  textures: SpeciesTextureDto[];
}

export interface ModSpeciesUseDto {
  modId: string;
  modName: string | null;
  skins: number;
  inWorkspace: boolean;
  installed: boolean;
  enabled: boolean;
}

export interface ModStatDto {
  id: string;
  label: string;
  unit: string;
  min: number;
  max: number;
}

export interface ModTextureDto {
  file: string;
  kind: string;
  mode: string;
  override: boolean;
  format: string;
  width: number;
  height: number;
  bytes: number;
}

export interface ModTexturesResult {
  compression: string;
  textures: ModTextureDto[];
  totalBytes: number;
}

export interface ModThumbnailParams {
  id: string;
  file: string;
  size?: number;
}

export interface ModThumbnailResult {
  file: string | null;
}

export interface ModUnityProjectDto {
  directory: string;
  readme: string;
}

export interface ModsListResult {
  mods: ModRow[];
  frameworkInstalled: boolean;
  frameworkOutdated?: boolean;
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

export interface SoundDto {
  event: string;
  name: string;
  group: string;
  species: string[];
  lengthMs: number | null;
  oneShot: boolean | null;
  perAnimal: boolean;
}

export interface SoundFolderGroupDto {
  name: string;
  files: string[];
  sounds: SoundDto[];
}

export interface SoundsForSpeciesParams {
  species: string;
}

export interface SoundsForSpeciesResult {
  speciesId: string | null;
  sounds: SoundDto[];
  hasEventList: boolean;
}

export interface SoundsMatchFolderParams {
  folder: string;
  species?: string | null;
}

export interface SoundsMatchFolderResult {
  groups: SoundFolderGroupDto[];
  unmatched: string[];
}

export interface SoundsSearchParams {
  text?: string | null;
  limit?: number;
}

export interface SoundsSearchResult {
  sounds: SoundDto[];
  hasEventList: boolean;
}

export interface SpeciesAnimationsParams {
  species: string;
}

export interface SpeciesAnimationsResult {
  species: string;
  animations: AnimationInfo[];
}

export interface SpeciesExportAnimationsParams {
  species: string;
  ids?: string[] | null;
  all?: boolean;
  format?: string | null;
  singleFile?: boolean;
  out?: string | null;
}

export interface SpeciesExportAnimationsResult {
  directory: string;
  files: string[];
  notes: string[];
}

export interface SpeciesIkParams {
  key: string;
}

export interface SpeciesIkResult {
  chains: IkChainRow[];
}

export interface SpeciesListResult {
  species: SpeciesRow[];
}

export interface SpeciesPackParams {
  key: string;
  format?: string | null;
  animations?: string | null;
}

export interface SpeciesPackRunResult {
  directory: string;
  models: number;
  textures: number;
  failed: number;
  targetsPath: string;
  notes?: string[] | null;
}

export interface SpeciesPrefab {
  bundle: string;
  asset: string;
}

export interface SpeciesRigInfoParams {
  species: string;
}

export interface SpeciesRigInfoResult {
  species: string;
  bones: number;
  clipMoved: string[];
  growthMoved: string[];
  growthScaled: string[];
  growthSupported: boolean;
  failures: string[];
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
  prefabGuid?: string | null;
}

export interface SpeciesTextureDto {
  texture: string;
  slot: string;
  skins: string[];
  sharedWith: string[];
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
  frameworkInGame?: string | null;
  frameworkBundled?: string;
  loaderInGame?: string | null;
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
  "blender.installAddon": { params: void; result: BlenderStatusDto };
  "blender.open": { params: BlenderOpenParams; result: BlenderOpenDto };
  "blender.setPath": { params: BlenderSetPathParams; result: BlenderStatusDto };
  "blender.status": { params: void; result: BlenderStatusDto };
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
  "game.packageFramework": { params: GamePackageFrameworkParams; result: GamePackageFrameworkResult };
  "install.detect": { params: InstallDetectParams; result: InstallInfo };
  "job.cancel": { params: JobCancelParams; result: JobCancelResult };
  "job.current": { params: void; result: JobCurrentResult };
  "mods.addSkin": { params: ModAddSkinParams; result: ModsListResult };
  "mods.addSpecies": { params: ModAddSpeciesParams; result: ModDetail };
  "mods.animalDatabase": { params: ModIdParams; result: ModAnimalDatabase };
  "mods.arrangeSkins": { params: ModArrangeSkinsParams; result: ModDetail };
  "mods.check": { params: ModIdParams; result: ModCheckReport };
  "mods.clearRig": { params: ModClearRigParams; result: ModDetail };
  "mods.colorPreview": { params: ModColorPreviewParams; result: ModPreviewFiles };
  "mods.copyBaseFile": { params: ModCopyBaseFileParams; result: ModDetail };
  "mods.create": { params: ModCreateParams; result: ModsListResult };
  "mods.createUnityProject": { params: ModIdParams; result: ModUnityProjectDto };
  "mods.customSpecies": { params: ModCustomSpeciesParams; result: ModCustomSpeciesResult };
  "mods.delete": { params: ModDeleteParams; result: ModsListResult };
  "mods.enable": { params: ModEnableParams; result: ModsListResult };
  "mods.export": { params: ModExportParams; result: JobStarted };
  "mods.forgetSkins": { params: ModForgetSkinsParams; result: SkinSlotsResult };
  "mods.get": { params: ModIdParams; result: ModDetail };
  "mods.import": { params: ModImportParams; result: ModImportResult };
  "mods.install": { params: ModIdParams; result: JobStarted };
  "mods.list": { params: void; result: ModsListResult };
  "mods.modelPreview": { params: ModModelPreviewParams; result: ModModelPreview };
  "mods.portraits": { params: ModPortraitsParams; result: ModPortraitsResult };
  "mods.prefabFields": { params: ModPrefabFieldsParams; result: ModPrefabFields };
  "mods.rebuildModels": { params: ModRebuildModelsParams; result: ModDetail };
  "mods.remove": { params: ModIdParams; result: ModsListResult };
  "mods.removeModel": { params: ModRemoveModelParams; result: ModDetail };
  "mods.removePrefabTarget": { params: ModRemovePrefabTargetParams; result: ModDetail };
  "mods.removeReplacement": { params: ModRemoveReplacementParams; result: ModDetail };
  "mods.removeSkin": { params: ModRemoveSkinParams; result: ModDetail };
  "mods.removeSound": { params: ModRemoveSoundParams; result: ModDetail };
  "mods.renameSkin": { params: ModRenameSkinParams; result: ModDetail };
  "mods.replace": { params: ModReplaceParams; result: ModsListResult };
  "mods.replaceModel": { params: ModReplaceModelParams; result: ModDetail };
  "mods.replaceSound": { params: ModReplaceSoundParams; result: ModDetail };
  "mods.replaceSounds": { params: ModReplaceSoundsParams; result: ModDetail };
  "mods.resetPrefabField": { params: ModResetPrefabFieldParams; result: ModDetail };
  "mods.restoreCutouts": { params: ModIdParams; result: ModRestoreCutoutsResult };
  "mods.restoreSnapshot": { params: ModRestoreSnapshotParams; result: ModDetail };
  "mods.rig": { params: ModRigParams; result: ModRigView };
  "mods.sampleColors": { params: ModSampleColorsParams; result: ModSampledColors };
  "mods.setColors": { params: ModSetColorsParams; result: ModDetail };
  "mods.setCompression": { params: ModSetCompressionParams; result: ModDetail };
  "mods.setDetails": { params: ModSetDetailsParams; result: ModDetail };
  "mods.setPortraits": { params: ModSetPortraitsParams; result: ModDetail };
  "mods.setPrefabField": { params: ModSetPrefabFieldParams; result: ModDetail };
  "mods.setSkinFile": { params: ModSetSkinFileParams; result: ModDetail };
  "mods.setSound": { params: ModSetSoundParams; result: ModDetail };
  "mods.setSpecies": { params: ModSetSpeciesParams; result: ModDetail };
  "mods.setSpeciesIcon": { params: ModSetSpeciesIconParams; result: ModDetail };
  "mods.setSpeciesPortraits": { params: ModSetSpeciesPortraitsParams; result: ModDetail };
  "mods.setSpeciesPrefab": { params: ModSetSpeciesPrefabParams; result: ModDetail };
  "mods.setSpeciesStartingSkin": { params: ModSetSpeciesStartingSkinParams; result: ModDetail };
  "mods.setSpeciesSwatch": { params: ModSetSpeciesSwatchParams; result: ModDetail };
  "mods.setThumbnail": { params: ModSetThumbnailParams; result: ModDetail };
  "mods.skinModel": { params: ModSkinModelParams; result: ModSkinModel };
  "mods.skinSlots": { params: void; result: SkinSlotsResult };
  "mods.species": { params: void; result: ModSpeciesResult };
  "mods.speciesModel": { params: ModSpeciesModelParams; result: ModSkinModel };
  "mods.speciesTemplate": { params: ModSpeciesTemplateParams; result: ModSpeciesTemplateDto };
  "mods.speciesTextures": { params: ModSpeciesTexturesParams; result: ModSpeciesTexturesResult };
  "mods.textures": { params: ModIdParams; result: ModTexturesResult };
  "mods.thumbnail": { params: ModThumbnailParams; result: ModThumbnailResult };
  "sounds.forSpecies": { params: SoundsForSpeciesParams; result: SoundsForSpeciesResult };
  "sounds.matchFolder": { params: SoundsMatchFolderParams; result: SoundsMatchFolderResult };
  "sounds.search": { params: SoundsSearchParams; result: SoundsSearchResult };
  "species.animations": { params: SpeciesAnimationsParams; result: SpeciesAnimationsResult };
  "species.exportAnimations": { params: SpeciesExportAnimationsParams; result: JobStarted };
  "species.ik": { params: SpeciesIkParams; result: SpeciesIkResult };
  "species.list": { params: void; result: SpeciesListResult };
  "species.pack": { params: SpeciesPackParams; result: JobStarted };
  "species.rigInfo": { params: SpeciesRigInfoParams; result: SpeciesRigInfoResult };
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
  "mods.export": ModExportResult;
  "mods.install": ModInstallResult;
  "species.exportAnimations": SpeciesExportAnimationsResult;
  "species.pack": SpeciesPackRunResult;
  "workspace.refreshAll": RefreshAllResult;
}

export interface RpcNotifications {
  "job.progress": JobProgressNotification;
  "job.done": JobDoneNotification;
  "job.failed": JobFailedNotification;
  "log": LogNotification;
}
