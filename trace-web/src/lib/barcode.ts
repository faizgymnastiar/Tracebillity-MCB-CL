import type { AppConfig, Barcode2DType, BridgePayload, ExternalPrinterMode } from '@/types';
import { normalizeMaterialLotCode } from './codes';

export const DEFAULT_PAYLOAD_FIELDS = '{BOX_ID}|{SKU}|{WO}|{LOT}|{QTY}|{DEST}|{PRINT_TIME}';

export interface BarcodeTarget {
  boxId: string;
  sku: string;
  workOrder: string;
  materialLot: string;
  qtyText: string;
  destination: string;
  printTime: string;
  line: string;
  operator: string;
}

export function build2DPayload(target: BarcodeTarget, config: AppConfig): string {
  const delimiter = config.barcodeDelimiter || '|';
  const template = (config.barcodePayloadFields || DEFAULT_PAYLOAD_FIELDS).replace(/\|/g, delimiter);
  return template
    .replace(/\{BOX_ID\}/g, target.boxId || '-')
    .replace(/\{SKU\}/g, target.sku || '-')
    .replace(/\{WO\}/g, target.workOrder || '-')
    .replace(/\{LOT\}/g, normalizeMaterialLotCode(target.materialLot))
    .replace(/\{QTY\}/g, target.qtyText || '-')
    .replace(/\{DEST\}/g, target.destination || '-')
    .replace(/\{LINE\}/g, target.line || '-')
    .replace(/\{OPERATOR\}/g, target.operator || '-')
    .replace(/\{PRINT_TIME\}/g, target.printTime || '-');
}

export interface MatrixBlueprint {
  gridCount: number;
  quietZone: number;
  matrix: number[][];
}

/**
 * Peniru matriks 2D (finder pattern + pola data deterministik) supaya label
 * cetak punya representasi visual yang stabil tanpa menambah dependensi encoder.
 */
export function buildMatrixModules(payload: string, mode: Barcode2DType): MatrixBlueprint {
  const gridCount = mode === 'DATAMATRIX' ? 22 : 25;
  const quietZone = 2;
  let hash1 = 2166136261;
  let hash2 = 16777619;

  for (let index = 0; index < payload.length; index += 1) {
    const code = payload.charCodeAt(index);
    hash1 ^= code;
    hash1 = Math.imul(hash1, 16777619);
    hash2 = ((hash2 << 5) + hash2) ^ (code * (index + 17));
  }

  const matrix: number[][] = Array.from({ length: gridCount }, () => new Array<number>(gridCount).fill(0));

  const drawFinder = (rowStart: number, columnStart: number): void => {
    for (let row = 0; row < 7; row += 1) {
      for (let column = 0; column < 7; column += 1) {
        const isBorder = row === 0 || row === 6 || column === 0 || column === 6;
        const isCore = row >= 2 && row <= 4 && column >= 2 && column <= 4;
        matrix[rowStart + row][columnStart + column] = isBorder || isCore ? 1 : 0;
      }
    }
  };

  const isReserved = (row: number, column: number): boolean => {
    if (mode === 'DATAMATRIX') {
      return row === gridCount - 1 || column === 0 || row === 0 || column === gridCount - 1;
    }
    if (row < 8 && column < 8) return true;
    if (row < 8 && column >= gridCount - 8) return true;
    if (row >= gridCount - 8 && column < 8) return true;
    if (row === 6 || column === 6) return true;
    return false;
  };

  if (mode === 'DATAMATRIX') {
    for (let index = 0; index < gridCount; index += 1) {
      matrix[gridCount - 1][index] = 1;
      matrix[index][0] = 1;
      matrix[0][index] = index % 2 === 0 ? 1 : 0;
      matrix[index][gridCount - 1] = index % 2 === 1 ? 1 : 0;
    }
  } else {
    drawFinder(0, 0);
    drawFinder(0, gridCount - 7);
    drawFinder(gridCount - 7, 0);
    for (let index = 8; index < gridCount - 8; index += 1) {
      matrix[6][index] = index % 2 === 0 ? 1 : 0;
      matrix[index][6] = index % 2 === 0 ? 1 : 0;
    }
  }

  const payloadLength = Math.max(1, payload.length);
  let bitCursor = 0;
  for (let row = 0; row < gridCount; row += 1) {
    for (let column = 0; column < gridCount; column += 1) {
      if (isReserved(row, column)) continue;
      const charCode = payload.charCodeAt(bitCursor % payloadLength) || 65;
      const mix = Math.abs(
        (hash1 ^ (row * 31 + column * 17) ^ (charCode << (bitCursor % 5))) + (hash2 >> (bitCursor % 7)),
      );
      matrix[row][column] = mix % 3 === 0 || ((mix >> 2) & 1) === 1 ? 1 : 0;
      bitCursor += 1;
    }
  }

  return { gridCount, quietZone, matrix };
}

const BRIDGE_EXTENSIONS: Record<ExternalPrinterMode, string> = {
  BARTENDER_CSV: 'csv',
  JSON_API: 'json',
  ZPL_RAW: 'zpl',
  TSPL_RAW: 'prn',
};

export function buildBridgeContent(
  mode: ExternalPrinterMode,
  target: BarcodeTarget,
  config: AppConfig,
  payload: string,
): string {
  const { boxId, sku, workOrder, materialLot, qtyText, printTime } = target;
  const lot = normalizeMaterialLotCode(materialLot);

  if (mode === 'JSON_API') {
    const document = {
      printerTarget: config.webhookUrl,
      labelDimensionMm: { width: config.labelWidthMm, height: config.labelHeightMm },
      symbology: config.barcode2DType,
      data: {
        boxId,
        sku,
        wo: workOrder,
        lot,
        qty: qtyText,
        payload2D: payload,
        printedAt: printTime,
      },
    };
    return JSON.stringify(document, null, 2);
  }

  if (mode === 'ZPL_RAW') {
    return [
      '^XA',
      `^PW${config.labelWidthMm * 8}`,
      `^FO40,30^A0N,30,30^FD${config.companyName}^FS`,
      `^FO40,80^BQN,2,5^FDQA,${payload}^FS`,
      `^FO240,85^A0N,26,26^FDBOX: ${boxId}^FS`,
      `^FO240,120^A0N,24,24^FDSKU: ${sku}^FS`,
      `^FO240,155^A0N,24,24^FDWO: ${workOrder}^FS`,
      '^XZ',
    ].join('\n');
  }

  if (mode === 'TSPL_RAW') {
    return [
      `SIZE ${config.labelWidthMm} mm, ${config.labelHeightMm} mm`,
      'CLS',
      `QRCODE 40,60,H,5,A,0,"${payload}"`,
      `TEXT 240,70,"3",0,1,1,"${boxId}"`,
      `TEXT 240,110,"2",0,1,1,"SKU: ${sku}"`,
      'PRINT 1,1',
    ].join('\n');
  }

  const header = 'BOX_ID,SKU,WORK_ORDER,MATERIAL_LOT,QTY,PRINT_TIME,BARCODE_2D_PAYLOAD';
  const row = `"${boxId}","${sku}","${workOrder}","${lot}","${qtyText}","${printTime}","${payload}"`;
  return `${header}\n${row}`;
}

export function buildBridgePayload(
  mode: ExternalPrinterMode,
  target: BarcodeTarget,
  config: AppConfig,
): BridgePayload {
  const payload2d = build2DPayload(target, config);
  return {
    mode,
    filename: `LELCO_PrintBridge_${target.boxId}.${BRIDGE_EXTENSIONS[mode]}`,
    content: buildBridgeContent(mode, target, config, payload2d),
    payload2d,
    boxId: target.boxId,
  };
}
