import type { ReactNode } from 'react';

export interface UserRbacProfile {
  username: string;
  fullName: string;
  department: string;
  roles: string[];
  permissions: string[];
}

export interface AuthProfile extends UserRbacProfile {
  authenticated: boolean;
}

// ---------------------------------------------------------------------------
// Fondasi Fase 1 — kontrak sesi login, produksi, dan master dari trace-api
// ---------------------------------------------------------------------------

export interface WorkOrderInfo {
  id: string;
  number: string;
  client: string;
  shippingReference: string;
  targetQty: number;
}

export interface ProductionContext {
  productionSessionId: string;
  operatorName: string;
  shiftCode: string;
  shiftName: string;
  lineCode: string;
  lineName: string;
  skuCode: string;
  skuName: string;
  familyCode: string;
  activeWorkOrderId: string | null;
  activeWorkOrderNumber: string | null;
  workOrderClient: string | null;
  shippingReference: string | null;
  targetQty: number | null;
  contextVersion: number;
  startedAtUtc: string;
  workOrders: WorkOrderInfo[];
}

export interface AuthProfileDto {
  accountId: string;
  email: string;
  fullName: string;
  status: string;
  mustChangePassword: boolean;
  permissions: string[];
  production: ProductionContext | null;
}

export interface LoginResultDto {
  token: string;
  expiresAtUtc: string;
  profile: AuthProfileDto;
}

export interface AccountInfo {
  id: string;
  email: string;
  fullName: string;
  status: string;
  mustChangePassword: boolean;
  permissions: string[];
  createdAtUtc: string;
}

export interface PermissionOption {
  key: string;
  label: string;
}

export interface AuthSessionInfo {
  id: string;
  accountId: string;
  email: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  lastSeenAtUtc: string;
  revoked: boolean;
}

export interface MasterItem {
  id: string;
  code: string;
  name: string;
  status: string;
  familyId?: string | null;
  stageId?: string | null;
  groupCode?: string | null;
  seq?: number;
  defects?: MasterItem[];
}

export type MasterEntity = 'family' | 'sku' | 'shift' | 'line' | 'stage' | 'defect';

export interface MasterCatalog {
  families: MasterItem[];
  skus: MasterItem[];
  shifts: MasterItem[];
  lines: MasterItem[];
  stages: MasterItem[];
}

export interface PagedResponse<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
  lastSyncAt?: string | null;
}

export interface DataTableColumn<T> {
  key: string;
  label: string;
  align?: 'left' | 'right' | 'center';
  defaultWidth?: number;
  optional?: boolean;
  sortable?: boolean;
  origin?: 'erp' | 'calculated' | 'postgres';
  render?: (row: T, rowIndex: number) => ReactNode;
  exportValue?: (row: T) => string | number | null | undefined;
}

export interface GridLayoutConfig {
  order: string[];
  hidden: string[];
  widths: Record<string, number>;
  wrap: boolean;
}

export interface DataTableQuery {
  page: number;
  pageSize: number;
  sortBy: string;
  sortAsc: boolean;
}

export interface NavItemConfig {
  slug: string;
  label: string;
  href: string;
  icon?: string;
  requiredPermission: string;
  apiEndpoint: string;
  description: string;
}

export interface NavGroupConfig {
  id: string;
  label: string;
  defaultOpen?: boolean;
  items: NavItemConfig[];
}

export interface NgStation {
  id: string;
  label: string;
  defects: string[];
}

export type ScanMode = 'OK' | 'NG' | 'REWORK';
export type RecordCategory = 'OK' | 'NG' | 'REWORK';
export type TraceStatusFilter = 'ALL' | RecordCategory;
export type ExternalPrinterMode = 'BARTENDER_CSV' | 'JSON_API' | 'ZPL_RAW' | 'TSPL_RAW';
export type Barcode2DType = 'QR_MATRIX' | 'DATAMATRIX';
export type MasterListKey =
  | 'skus'
  | 'materials'
  | 'operators'
  | 'lines'
  | 'shifts'
  | 'destinations'
  | 'reworks';

export interface ExportTemplate {
  name: string;
  columns: string[];
  orientation: string;
}

export interface AppConfig {
  adminPin: string;
  browserTitle: string;
  companyName: string;
  subtitle: string;
  heroTitle: string;
  btnLoginText: string;
  btnDbText: string;
  loginTitle: string;
  loginSubmitText: string;
  footerTopText: string;
  footerBottomText: string;
  logoDataUrl: string;
  accentColor: string;
  uiScanOkTitle: string;
  uiScanStatusBadge: string;
  uiScanNgTitle: string;
  uiScanRwTitle: string;
  uiDbHeaderTitle: string;
  uiDbSearchTitle: string;
  uiDbTableTitle: string;
  uiReportTitle: string;
  innerCodeFormat: string;
  outerCodeFormat: string;
  reworkCodeFormat: string;
  seqPadding: number;
  snPattern: string;
  barcode2DType: Barcode2DType;
  barcode2DSize: number;
  labelWidthMm: number;
  labelHeightMm: number;
  barcodeDelimiter: string;
  barcodePayloadFields: string;
  externalPrinterMode: ExternalPrinterMode;
  webhookUrl: string;
  autoBridgePrint: boolean;
  innerCap: number;
  outerCap: number;
  maxReworkCycles: number;
  skus: string[];
  materials: string[];
  operators: string[];
  lines: string[];
  shifts: string[];
  destinations: string[];
  reworks: string[];
  faviconEmoji: string;
  colors: { ok: string; ng: string; rework: string; navy: string };
  labelInnerTitle: string;
  labelOuterTitle: string;
  dailyReset: boolean;
  ngStations: NgStation[];
  columnLabels: Record<string, string>;
  exportTemplates: ExportTemplate[];
  selectedColumns: string[];
}

export type PublicConfig = Omit<AppConfig, 'adminPin'>;

export interface OperatorContext {
  operator: string;
  workOrder: string;
  materialLot: string;
  line: string;
  shift: string;
  sku: string;
  destination: string;
  // Fase 1 — pengayaan dari sesi produksi (opsional untuk kompatibilitas lama).
  workOrderClient?: string;
  shippingReference?: string;
  workOrderTarget?: number;
  productionSessionId?: string;
  contextVersion?: number;
}

export interface BatchItem {
  sn: string;
  dayName: string;
  isoDate: string;
  time: string;
  operator: string;
  workOrder: string;
  materialLot: string;
  line: string;
  shift: string;
  sku: string;
  reworked: boolean;
  reworkCycle: number;
  reworkTicket: string;
}

export interface InnerBox {
  id: string;
  dayName: string;
  isoDate: string;
  time: string;
  operator: string;
  workOrder: string;
  materialLot: string;
  line: string;
  shift: string;
  sku: string;
  destination: string;
  partial?: boolean;
  items: BatchItem[];
}

export interface OuterBox {
  id: string;
  dayName: string;
  isoDate: string;
  time: string;
  operator: string;
  workOrder: string;
  materialLot: string;
  sku: string;
  destination: string;
  partial?: boolean;
  inners: string[];
  totalPcs: number;
}

export interface NgItem {
  sn: string;
  stationId: string;
  stationLabel: string;
  reason: string;
  disposition: string;
  reworkCycle: number;
  reworkFails?: number;
  dayName: string;
  isoDate: string;
  time: string;
  operator: string;
  workOrder: string;
  materialLot: string;
  line: string;
  shift: string;
  sku: string;
  destination: string;
}

export interface ReworkItem {
  ticketId: string;
  sn: string;
  cycle: number;
  stationLabel: string;
  prevDefect: string;
  action: string;
  dayName: string;
  isoDate: string;
  time: string;
  operator: string;
  workOrder: string;
  materialLot: string;
  line: string;
  shift: string;
  sku: string;
  destination: string;
}

export interface TraceabilityRecord {
  id: string;
  category: RecordCategory;
  dayName: string;
  isoDate: string;
  waktu: string;
  sn: string;
  status: string;
  station: string;
  sku: string;
  wo: string;
  lot: string;
  innerBox: string;
  outerBox: string;
  reworkTicket: string;
  line: string;
  shift: string;
  destination: string;
  operator: string;
  keterangan: string;
}

export interface DailySummaryRow {
  isoDate: string;
  dayName: string;
  total: number;
  directOk: number;
  afterRework: number;
  ngCount: number;
  innerBoxes: number;
  outerBoxes: number;
}

export interface RecentBox {
  id: string;
  type: 'INNER' | 'OUTER';
  sku: string;
  qty: number;
}

export interface TraceFocus {
  kind: 'unit' | 'inner' | 'outer';
  id: string;
  title: string;
  subtitle: string;
  status: string;
}

export interface TraceRecordListResponse extends PagedResponse<TraceabilityRecord> {
  counts: { ok: number; rework: number; ng: number };
  recentBoxes: RecentBox[];
  focus: TraceFocus | null;
}

export interface TimelineStep {
  label: string;
  detail: string;
  time: string | null;
  tone: 'ok' | 'ng' | 'rework' | 'neutral';
}

export interface TraceDetailResponse {
  query: string;
  kind: 'unit' | 'inner' | 'outer';
  unit: TraceabilityRecord | null;
  innerBox: InnerBox | null;
  outerBox: OuterBox | null;
  timeline: TimelineStep[];
  relatedUnits: { sn: string; status: string; href: string }[];
  relatedBoxes: { id: string; type: 'INNER' | 'OUTER'; label: string; href: string }[];
}

export interface DailySummaryResponse {
  items: DailySummaryRow[];
  totalDays: number;
  lastSyncAt: string;
}

export interface LabelData {
  code: string;
  sku: string;
  workOrder: string;
  materialLot: string;
  destination: string;
  qtyText: string;
  printedAt: string;
  payload: string;
}

export interface BarcodeHistoryEntry {
  type: 'INNER' | 'OUTER';
  id: string;
  sku: string;
  destination: string;
  qty: number;
  event: 'FULL' | 'PRINT';
  time: string;
  isoDate: string;
}

export interface ScanSnapshot {
  activeDate: string;
  activeDayName: string;
  context: OperatorContext;
  activeInnerId: string;
  activeOuterId: string;
  nextReworkTicket: string;
  innerCap: number;
  outerCap: number;
  activeInnerQty: number;
  unassignedInnerQty: number;
  maxReworkCycles: number;
  currentBatch: BatchItem[];
  completedInners: InnerBox[];
  completedOuters: OuterBox[];
  ngToday: NgItem[];
  reworkToday: ReworkItem[];
  history: BarcodeHistoryEntry[];
  kpis: { ok: number; ng: number; rework: number };
  labels: { inner: LabelData; outer: LabelData };
  ngStations: NgStation[];
  reworks: string[];
  ui: {
    scanOkTitle: string;
    scanNgTitle: string;
    scanRwTitle: string;
    dbHeaderTitle: string;
    dbSearchTitle: string;
    dbTableTitle: string;
    reportTitle: string;
    scanStatusBadge: string;
  };
  brand: {
    companyName: string;
    subtitle: string;
    logoDataUrl: string;
    labelInnerTitle: string;
    labelOuterTitle: string;
    accentColor: string;
  };
  barcode: {
    type: Barcode2DType;
    size: number;
    labelWidthMm: number;
    labelHeightMm: number;
  };
  bridgeMode: ExternalPrinterMode;
  lastSyncAt: string;
  // Fase 1 — kontrak fondasi: konteks sesi produksi aktif & hak pelaku.
  production?: ProductionContext | null;
  authEnforced?: boolean;
  permissions?: string[];
}

export interface ScanResult {
  ok: boolean;
  message: string;
  tone: 'ok' | 'error' | 'rework';
  snapshot: ScanSnapshot | null;
  autoBridge: BridgePayload | null;
}

export interface HealthStatus {
  status: string;
  dataSource: string;
  latestSyncIso: string | null;
  rowsProcessed: number | null;
}

export interface BridgePayload {
  mode: ExternalPrinterMode;
  filename: string;
  content: string;
  payload2d: string;
  boxId: string;
}

export interface HomeStats {
  activeDate: string;
  totalOutput: number;
  directOk: number;
  ngCount: number;
  innerBoxes: number;
  outerBoxes: number;
}

export interface ColumnCatalogItem {
  key: string;
  label: string;
  defaultOn: boolean;
}

// ---------------------------------------------------------------------------
// Fase 2 — stok label QR internal NG, job cetak, dan sequence penerbitan.
// ---------------------------------------------------------------------------

export type LabelStatus = 'AVAILABLE' | 'BOUND' | 'VOID';
export type PrintJobKind = 'FIRST' | 'REPRINT';
export type PrintChannel = 'NICELABEL_CSV' | 'DIRECT_PRINTER';
export type PrintJobStatus = 'EXPORTED' | 'CONFIRMED' | 'UNKNOWN' | 'FAILED';

export interface NgLabelInfo {
  id: string;
  seq: number;
  namespace: string;
  /** Isi QR final; tidak pernah berubah sehingga cetak ulang menghasilkan QR yang sama. */
  payload: string;
  batchId: string;
  status: LabelStatus;
  boundSerial?: string | null;
  boundAtUtc?: string | null;
  printedCount: number;
  lastPrintedAtUtc?: string | null;
  createdAtUtc: string;
}

export interface LabelBatchInfo {
  id: string;
  namespace: string;
  quantity: number;
  firstSeq: number;
  lastSeq: number;
  note: string;
  createdByAccountId: string;
  createdByName: string;
  createdAtUtc: string;
  availableCount: number;
  boundCount: number;
  printJobCount: number;
  confirmedPrintCount: number;
  lastPrintedAtUtc?: string | null;
}

export interface PrintJobInfo {
  id: string;
  batchId: string;
  kind: PrintJobKind;
  channel: PrintChannel;
  status: PrintJobStatus;
  quantity: number;
  requestedByAccountId: string;
  requestedByName: string;
  requestedAtUtc: string;
  confirmedAtUtc?: string | null;
  confirmedCount?: number | null;
  exportFilename?: string | null;
  note: string;
  labelIds: string[];
}

export interface LabelBatchDetail {
  batch: LabelBatchInfo;
  labels: NgLabelInfo[];
  printJobs: PrintJobInfo[];
}

export interface LabelSequenceState {
  name: string;
  nextValue: number;
  maxLabelSeq: number;
  ledgerMaxSeq: number;
  highWaterSeq: number;
  /** False bila restore tertinggal; generate ditolak sampai rekonsiliasi. */
  consistent: boolean;
  ledgerPath: string;
  ledgerError?: string | null;
}

export interface LabelStockSummary {
  usbPrintEnabled?: boolean;
  totalLabels: number;
  availableStock: number;
  boundStock: number;
  totalBatches: number;
  totalPrintJobs: number;
  pendingConfirmationJobs: number;
  sequence: LabelSequenceState;
}

export interface GenerateBatchResult {
  batch: LabelBatchInfo;
  labels: NgLabelInfo[];
  /** True bila permintaan ulang mengembalikan batch lama (key + isi sama). */
  reused: boolean;
  sequence: LabelSequenceState;
  maxPerRequest: number;
}

export interface PrintJobResult {
  job: PrintJobInfo;
  reused: boolean;
  exportCsv?: string | null;
  exportFilename?: string | null;
}

// ---------------------------------------------------------------------------
// Fase 3 — karantina NG, rework selesai, dan scan OK akhir.
// ---------------------------------------------------------------------------

export type NgUnitStatus = 'NG_QUARANTINE' | 'REWORK_RETEST' | 'OK_FINAL' | 'QC_HOLD';
export type NgScanMode = 'SERIAL' | 'REWORK';
export type NgDisposition = 'REWORK_REQUIRED' | 'QC_HOLD';

export interface NgDefectOption {
  id: string;
  groupCode: string;
  name: string;
}

export interface NgStationInfo {
  id: string;
  code: string;
  name: string;
  seq: number;
  defects: NgDefectOption[];
}

export interface NgUnitInfo {
  id: string;
  serial: string | null;
  labelId: string | null;
  labelPayload: string | null;
  status: NgUnitStatus;
  statusLabel: string;
  attempts: number;
  maxAttempts: number;
  attemptsRemaining: number;
  familyCode: string;
  skuCode: string;
  stageCode: string;
  originWorkOrderNumber: string | null;
  targetWorkOrderNumber: string | null;
  firstNgAtUtc: string;
  lastEventAtUtc: string;
  okAtUtc: string | null;
  okActorName: string | null;
}

export interface NgEventInfo {
  id: string;
  seq: number;
  labelId: string;
  stageId: string;
  stageCode: string;
  stageName: string;
  defectId: string;
  defectGroup: string;
  defectName: string;
  attemptNo: number;
  disposition: NgDisposition;
  workOrderOriginNumber: string | null;
  actorName: string;
  shiftCode: string;
  lineCode: string;
  note: string;
  occurredAtUtc: string;
}

export interface NgReworkEventInfo {
  id: string;
  attemptNo: number;
  ngEventId: string;
  action: string;
  actorName: string;
  note: string;
  performedAtUtc: string;
}

export interface NgOkScanInfo {
  serial: string;
  labelId: string | null;
  mode: 'SERIAL_ONLY' | 'REWORK_PAIR';
  workOrderTargetNumber: string | null;
  client: string | null;
  actorName: string;
  occurredAtUtc: string;
}

export interface NgUnitDetail {
  unit: NgUnitInfo;
  ngEvents: NgEventInfo[];
  reworkEvents: NgReworkEventInfo[];
  okScan: NgOkScanInfo | null;
}

export interface NgWorkspaceCounts {
  quarantine: number;
  retest: number;
  qcHold: number;
  okFinalToday: number;
  ngEventsToday: number;
  reworkToday: number;
}

export interface NgWorkspaceSummary {
  counts: NgWorkspaceCounts;
  maxAttempts: number;
  /** Hak backend pelaku; tab yang tidak diizinkan tidak dibuka. */
  permissions: string[];
  quarantine: NgUnitInfo[];
  retest: NgUnitInfo[];
  qcHold: NgUnitInfo[];
}

export interface RecordNgResult {
  unit: NgUnitInfo;
  event: NgEventInfo;
  labelPayload: string;
  /** True bila retry mengembalikan kejadian lama tanpa record baru. */
  reused: boolean;
  message: string;
}

export interface RecordReworkResult {
  unit: NgUnitInfo;
  reworkEvent: NgReworkEventInfo;
  reused: boolean;
  message: string;
}

export interface OkScanResult {
  unit: NgUnitInfo;
  okScan: NgOkScanInfo;
  reused: boolean;
  /** Instruksi melepas stiker hanya berlaku setelah backend mengonfirmasi sukses. */
  removeLabel: boolean;
  message: string;
}

