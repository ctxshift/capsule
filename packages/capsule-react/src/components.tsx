import React, { useRef, type ReactNode } from 'react';
import {
  Box,
  Button as NativeButton,
  Range,
  Select as NativeSelect,
  Text,
  Window as NativeWindow,
  type PointerEvent,
  type ActionEvent,
} from './index';

export interface ComponentTheme {
  button: string;
  buttonLabel: string;
  fieldLabel: string;
  slider: string;
  select: string;
  paragraph: string;
  windowInset: number;
  windowContentTop: number;
  windowBottom: number;
  titleTop: number;
  titleHeight: number;
  titleRightInset: number;
  buttonHeight: number;
  buttonPadding: number;
  fieldGap: number;
}
// Theme-owned native skins provide exact art; Yoga owns content geometry.
export function createComponents(theme: ComponentTheme) {
  function Button({
    label,
    onActivate,
    height = theme.buttonHeight,
  }: {
    label: string;
    onActivate: () => void;
    height?: number;
  }) {
    return (
      <NativeButton
        style={theme.button}
        height={height}
        align="center"
        justify="center"
        paddingLeft={theme.buttonPadding}
        paddingRight={theme.buttonPadding}
        onActivate={onActivate}
      >
        <Text style={theme.buttonLabel}>{label}</Text>
      </NativeButton>
    );
  }
  function Field({ label, children }: { label: string; children: ReactNode }) {
    return (
      <Box gap={theme.fieldGap}>
        <Text style={theme.fieldLabel}>{label}</Text>
        {children}
      </Box>
    );
  }
  function Slider({
    value,
    onChange,
  }: {
    value: number;
    onChange: (value: number) => void;
  }) {
    return (
      <Box padding={2}>
        <Range
          height={16}
          style={theme.slider}
          value={value}
          step={1 / 40}
          onValueChange={onChange}
        />
      </Box>
    );
  }
  function Select({
    options,
    value,
    onChange,
  }: {
    options: readonly string[];
    value: number;
    onChange: (value: number) => void;
  }) {
    return (
      <NativeSelect
        height={25}
        style={theme.select}
        options={options}
        selectedIndex={value}
        onSelectionChange={onChange}
      />
    );
  }
  function Paragraph({ children }: { children: string }) {
    return <Text style={theme.paragraph}>{children}</Text>;
  }
  function Window({
    title,
    titleStyle,
    width,
    height,
    direction,
    gap,
    position,
    dragExtent,
    viewport,
    onMove,
    onFocus,
    onClose,
    children,
  }: {
    title: string;
    titleStyle?: string;
    width: number;
    height?: number;
    direction?: 'row' | 'column';
    gap?: number;
    position: { x: number; y: number };
    dragExtent: { height: number };
    viewport: { width: number; height: number };
    onMove: (x: number, y: number) => void;
    onFocus: () => void;
    onClose: (event: ActionEvent) => void;
    children: ReactNode;
  }) {
    const drag = useRef<{
      pointer: number;
      x: number;
      y: number;
      originX: number;
      originY: number;
    } | null>(null);
    const pointer = (event: PointerEvent) => {
      if (event.phase === 'down' && event.button === 0)
        drag.current = {
          pointer: event.pointerId,
          x: event.x,
          y: event.y,
          originX: position.x,
          originY: position.y,
        };
      else if (drag.current?.pointer === event.pointerId) {
        if (event.phase === 'up' || event.phase === 'cancel')
          drag.current = null;
        else if (event.phase === 'move')
          onMove(
            Math.max(
              0,
              Math.min(
                viewport.width - width,
                drag.current.originX + event.x - drag.current.x,
              ),
            ),
            Math.max(
              0,
              Math.min(
                viewport.height - dragExtent.height,
                drag.current.originY + event.y - drag.current.y,
              ),
            ),
          );
      }
    };
    return (
      <NativeWindow
        position="absolute"
        x={position.x}
        y={position.y}
        width={width}
        height={height}
        direction={direction}
        gap={gap}
        title={title}
        style={titleStyle}
        paddingLeft={theme.windowInset}
        paddingRight={theme.windowInset}
        paddingTop={theme.windowContentTop}
        paddingBottom={theme.windowBottom}
        onFocus={onFocus}
        onClose={onClose}
        onKey={(event) => {
          if (event.key === 'Escape' && event.down && !event.repeat)
            onClose(event);
        }}
      >
        <Box
          position="absolute"
          x={theme.windowInset}
          y={theme.titleTop}
          width={width - theme.windowInset - theme.titleRightInset}
          height={theme.titleHeight}
          onPointer={pointer}
        />
        {children}
      </NativeWindow>
    );
  }
  return { Window, Box, Field, Button, Slider, Select, Paragraph };
}
