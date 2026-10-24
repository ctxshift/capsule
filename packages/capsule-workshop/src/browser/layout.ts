import Yoga, { type Node as YogaNode, type MeasureMode } from 'yoga-layout';
import type { UiNode } from './packet';

export interface TextMetrics {
  lineHeight: number;
  trailingSpacing?: number;
  advance(text: string): number;
}

// Same greedy word/overlong-word envelope as native YogaTextMeasure. Platform
// adapters inject metrics; neither game styles nor browser CSS own layout.
export function measureText(
  text: string,
  font: TextMetrics,
  width: number,
  mode: number,
) {
  const limit = mode === 0 ? Infinity : width + (font.trailingSpacing ?? 0);
  let line = 0,
    maximum = 0,
    word = 0,
    space = 0,
    lines = 1;
  function flush() {
    if (!word) return;
    if (line > 0 && line + space + word > limit) {
      maximum = Math.max(maximum, line);
      lines++;
      line = 0;
    }
    if (line) line += space;
    line += word;
    word = space = 0;
  }
  for (const character of text) {
    if (character === '\r') continue;
    if (character === '\n') {
      flush();
      maximum = Math.max(maximum, line);
      line = space = 0;
      lines++;
      continue;
    }
    const advance = font.advance(character);
    if (character === ' ') {
      flush();
      space += advance;
      continue;
    }
    if (word + advance > limit && word > 0) {
      if (line) {
        maximum = Math.max(maximum, line);
        lines++;
        line = 0;
      }
      maximum = Math.max(maximum, word);
      lines++;
      word = space = 0;
    }
    word += advance;
  }
  flush();
  maximum = Math.max(maximum, line);
  maximum = Math.max(0, maximum - (font.trailingSpacing ?? 0));
  return {
    width: mode === 1 ? width : mode === 2 ? Math.min(width, maximum) : maximum,
    height: lines * font.lineHeight,
  };
}

export function layout(
  nodes: ReadonlyMap<bigint, UiNode>,
  metrics: (style: string) => TextMetrics,
): Map<bigint, number[]> {
  const config = Yoga.Config.create();
  config.setUseWebDefaults(true);
  config.setPointScaleFactor(0);
  const yogas = new Map<bigint, YogaNode>();
  const roots: YogaNode[] = [];
  const rectangles = new Map<bigint, number[]>();
  let measurements = 0,
    glyphSteps = 0;
  try {
    function add(node: UiNode, parent?: YogaNode) {
      const yoga = Yoga.Node.createWithConfig(config);
      yogas.set(node.id, yoga);
      if (parent) parent.insertChild(yoga, parent.getChildCount());
      else roots.push(yoga);
      const values = node.layout;
      if (!values) throw Error('Browser workshop requires Yoga layout intent');
      yoga.setFlexDirection(
        values[0] ? Yoga.FLEX_DIRECTION_ROW : Yoga.FLEX_DIRECTION_COLUMN,
      );
      yoga.setAlignItems(values[2]);
      yoga.setJustifyContent(values[3]);
      yoga.setFlexGrow(values[8] / 64);
      yoga.setFlexShrink(values[9] / 64);
      yoga.setGap(Yoga.GUTTER_ALL, values[10] / 64);
      if (values[4] >= 0) yoga.setWidth(values[4] / 64);
      if (values[5] >= 0) yoga.setHeight(values[5] / 64);
      if (values[1]) {
        yoga.setPositionType(Yoga.POSITION_TYPE_ABSOLUTE);
        yoga.setPosition(Yoga.EDGE_LEFT, values[6] / 64);
        yoga.setPosition(Yoga.EDGE_TOP, values[7] / 64);
      }
      [
        Yoga.EDGE_LEFT,
        Yoga.EDGE_TOP,
        Yoga.EDGE_RIGHT,
        Yoga.EDGE_BOTTOM,
      ].forEach((edge, index) =>
        yoga.setPadding(edge, values[11 + index] / 64),
      );
      if (node.kind === 3)
        yoga.setMeasureFunc(
          (width, widthMode, height, heightMode: MeasureMode) => {
            if (
              ++measurements > 1024 ||
              (glyphSteps += node.text.length) > 262144
            )
              throw Error('Text measurement quota');
            const measured = measureText(
              node.text,
              metrics(node.style),
              width,
              widthMode,
            );
            return {
              ...measured,
              height:
                heightMode === 1
                  ? height
                  : heightMode === 2
                    ? Math.min(height, measured.height)
                    : measured.height,
            };
          },
        );
      for (const child of nodes.values())
        if (child.parent === node.id) add(child, yoga);
    }
    for (const node of nodes.values())
      if (!node.parent) {
        add(node);
        yogas
          .get(node.id)!
          .calculateLayout(undefined, undefined, Yoga.DIRECTION_LTR);
      }
    for (const [id, yoga] of yogas) {
      const node = nodes.get(id)!;
      const box = yoga.getComputedLayout();
      rectangles.set(
        id,
        [
          node.parent ? box.left : node.rect[0],
          node.parent ? box.top : node.rect[1],
          box.width,
          box.height,
        ].map((value) => Math.round(value * 64) / 64),
      );
    }
    return rectangles;
  } finally {
    for (const yoga of roots) yoga.freeRecursive();
    config.free();
  }
}
