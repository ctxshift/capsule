import React from 'react';
import { Box, Image, type Props } from './index';

export interface PanelOrnament {
  asset: string;
  anchor: 'top' | 'bottom' | 'left' | 'right';
  width: number;
  height: number;
}
export interface PanelProps extends Props {
  width: number;
  height: number;
  background?: string;
  frame: string;
  ornaments?: readonly PanelOrnament[];
}

/** Artwork recipes own tiling/slicing; layout owns size and ornament anchors. */
export function Panel({
  width,
  height,
  background,
  frame,
  ornaments = [],
  children,
  ...props
}: PanelProps) {
  return (
    <Box {...props} width={width} height={height}>
      {background && (
        <Image
          position="absolute"
          width={width}
          height={height}
          asset={background}
        />
      )}
      {children}
      <Image position="absolute" width={width} height={height} asset={frame} />
      {ornaments.map((ornament, index) => (
        <Image
          key={index}
          position="absolute"
          x={
            ornament.anchor === 'left'
              ? 0
              : ornament.anchor === 'right'
                ? width - ornament.width
                : (width - ornament.width) / 2
          }
          y={
            ornament.anchor === 'top'
              ? 0
              : ornament.anchor === 'bottom'
                ? height - ornament.height
                : (height - ornament.height) / 2
          }
          width={ornament.width}
          height={ornament.height}
          asset={ornament.asset}
        />
      ))}
    </Box>
  );
}
