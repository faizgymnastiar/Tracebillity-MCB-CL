'use client';

import { useEffect, useRef } from 'react';
import type { Barcode2DType } from '@/types';
import { buildMatrixModules } from '@/lib/barcode';
import { cn } from '@/lib/cn';

interface MatrixCanvasProps {
  payload: string;
  mode: Barcode2DType;
  size?: number;
  className?: string;
}

export function MatrixCanvas({ payload, mode, size = 112, className }: MatrixCanvasProps) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    canvas.width = size;
    canvas.height = size;
    const context = canvas.getContext('2d');
    if (!context) return;

    context.fillStyle = '#FFFFFF';
    context.fillRect(0, 0, size, size);

    const { gridCount, quietZone, matrix } = buildMatrixModules(payload, mode);
    const totalModules = gridCount + quietZone * 2;
    const cellSize = size / totalModules;

    context.fillStyle = '#161B41';
    for (let row = 0; row < gridCount; row += 1) {
      for (let column = 0; column < gridCount; column += 1) {
        if (matrix[row][column] !== 1) continue;
        context.fillRect(
          Math.floor((column + quietZone) * cellSize),
          Math.floor((row + quietZone) * cellSize),
          Math.ceil(cellSize),
          Math.ceil(cellSize),
        );
      }
    }
  }, [payload, mode, size]);

  return (
    <canvas
      ref={canvasRef}
      width={size}
      height={size}
      className={cn('matrix-canvas', className)}
      role="img"
      aria-label={`Barcode 2D ${mode} untuk ${payload.slice(0, 40)}`}
    />
  );
}
