import type { BrowserTheme } from './dom';
import { frame, image, type ImageRegion } from './raster';
import type { UiTheme, FontMetrics, Rgba } from '../theme';

const color = ([r, g, b, a]: Rgba) => `rgba(${r},${g},${b},${a / 255})`;
export function createBrowserTheme(
  theme: UiTheme,
  metrics: FontMetrics,
  assetUrl: (key: string) => string,
): BrowserTheme {
  const art = new Map(
    theme.assets.map((asset) => [
      asset.key,
      {
        url: assetUrl(asset.key),
        size: [asset.width, asset.height],
        region: asset.rect,
      } satisfies ImageRegion,
    ]),
  );
  const asset = (key: string) => {
    const result = art.get(key);
    if (!result) throw Error('Missing theme image ' + key);
    return result;
  };
  const definition = (key: string) =>
    theme.assets.find((item) => item.key === key)!;
  const framed = (document: Document, key: string) =>
    frame(document, asset(key), definition(key).border, definition(key).edge);
  const advances = new Map(
    metrics.advances.map((glyph) => [glyph.code, glyph.value]),
  );
  const w = theme.window,
    b = theme.button,
    r = theme.range,
    s = theme.select,
    c = theme.components,
    f = theme.focus;
  const css = `
  @font-face {font-family:PortableThemeFont;src:local('${theme.font.family}');}
  html,body {margin:0;width:1440px;height:900px;color:${color(b.foreground)};font:${theme.font.sampleSize}px PortableThemeFont;background:transparent;}
  * {box-sizing:border-box;} button,input,select {font:inherit;color:inherit;} button {border:0;cursor:pointer;}
  [data-kind="3"][data-style=""] {font-size:${theme.font.sampleSize}px;line-height:${theme.text.find((text) => text.key === c.buttonLabel)!.lineHeight}px;white-space:nowrap;pointer-events:none!important;}
  ${theme.text.map((text) => `[data-style="${text.key}"] {font-size:${text.size}px;line-height:${text.lineHeight}px;white-space:${text.wrap ? 'normal' : 'nowrap'};pointer-events:none!important;color:${color(text.color)};}`).join('\n')}
  .window {background:${color(w.background)};box-shadow:0 ${w.shadowOffset}px ${w.shadowBlur}px ${color(w.shadowColor)};isolation:isolate;}
  .window-backing {position:absolute;inset:${w.backingInset}px;opacity:${w.backingOpacity};background-position:center;background-size:cover;pointer-events:none;}
  .window-title {position:absolute;left:${c.windowInset}px;right:${c.windowInset}px;top:${c.titleTop}px;height:${c.titleHeight}px;font:small-caps ${w.titleSize}px/${c.titleHeight}px PortableThemeFont;letter-spacing:${w.titleSpacing}px;text-align:${w.titleAlignment};color:${color(w.titleColor)};pointer-events:none;}
  .window-rule {position:absolute;top:${w.ruleTop}px;left:${c.windowInset}px;right:${c.windowInset}px;height:${w.ruleHeight}px;background:${color(w.ruleColor)};pointer-events:none;}
  .close {position:absolute;right:${w.closeInset}px;top:${w.closeInset}px;width:${w.closeSize}px;height:${w.closeSize}px;padding:0;background:transparent;z-index:2;}
  .close:hover {background:${color(b.hover)};} .cross {position:absolute;inset:${w.crossInset}px;}
  .theme-button {background:${color(b.background)};isolation:isolate;}
  .hover,.highlight {position:absolute;inset:${b.highlightInset}px;pointer-events:none;opacity:0;}
  .hover {background:${color(b.hover)};} .highlight {mix-blend-mode:soft-light;}
  .theme-button:hover .hover {opacity:1;} .theme-button:hover .highlight {opacity:${b.highlightOpacity};}
  button:focus-visible,input:focus-visible,select:focus-visible {outline:${f.width}px solid ${color(f.color)};outline-offset:${-f.inset - f.width}px;}
  input[type="range"] {appearance:none;background:transparent;border:0;cursor:pointer;}
  input[type="range"]::-webkit-slider-runnable-track {height:${r.trackHeight}px;border:${r.borderWidth}px solid ${color(r.border)};border-radius:${r.trackRadius}px;background:linear-gradient(to right,${color(r.fill)} var(--value),${color(r.track)} var(--value));}
  input[type="range"]::-webkit-slider-thumb {appearance:none;width:${r.thumbWidth}px;height:${r.thumbHeight}px;margin-top:${(r.trackHeight - r.thumbHeight) / 2 - r.borderWidth}px;background:${color(r.fill)};border-radius:${r.thumbRadius}px;}
  select {background:${color(s.background)};color:${color(s.foreground)};border:${s.borderWidth}px solid ${color(s.border)};padding:0 ${s.paddingLeft}px!important;font:${s.fontSize}px PortableThemeFont;}`;
  return {
    css,
    fonts: [`${theme.font.sampleSize}px PortableThemeFont`],
    images: [...art.values()].map((image) => image.url),
    metrics(_document, style) {
      const text = theme.text.find(
        (text) => text.key === (style || theme.components.buttonLabel),
      );
      if (!text) throw Error('Unknown captured text style ' + style);
      return {
        lineHeight: text.lineHeight,
        advance(character) {
          const value = advances.get(character.codePointAt(0)!);
          if (value === undefined)
            throw Error('Uncaptured font glyph ' + character);
          return (value * text.size) / theme.font.sampleSize;
        },
      };
    },
    decorate(element, node, close) {
      const document = element.ownerDocument;
      if (node.kind === 1) {
        element.className = 'window';
        const backing = document.createElement('span');
        backing.className = 'window-backing';
        backing.style.backgroundImage = `url(${asset(w.backing).url})`;
        element.append(backing, framed(document, w.frame));
        const title = document.createElement('div');
        title.className = 'window-title';
        title.dataset.title = '';
        const rule = document.createElement('span');
        rule.className = 'window-rule';
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'close';
        button.setAttribute('aria-label', 'Close ' + node.text);
        button.onclick = close;
        button.append(framed(document, w.closeFrame));
        const cross = document.createElement('span');
        cross.className = 'cross';
        cross.append(image(document, asset(w.closeCross), undefined, false));
        button.append(cross);
        element.append(title, rule, button);
      } else if (node.kind === 10) {
        element.className = 'theme-button';
        const overlay = document.createElement('span');
        overlay.className = 'hover';
        const highlight = document.createElement('span');
        highlight.className = 'highlight';
        highlight.append(image(document, asset(b.highlight)));
        element.append(overlay, highlight, framed(document, b.frame));
      } else if (![2, 3, 8, 11, 12].includes(node.kind))
        throw Error(
          'Browser control skin does not support primitive ' + node.kind,
        );
    },
  };
}
