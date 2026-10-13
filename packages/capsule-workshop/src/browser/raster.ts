export interface ImageRegion {
  url: string;
  size: readonly [number, number];
  region: readonly [number, number, number, number];
}

export function image(
  document: Document,
  asset: ImageRegion,
  region = asset.region,
  stretch = true,
): SVGSVGElement {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', region.join(' '));
  svg.setAttribute('preserveAspectRatio', stretch ? 'none' : 'xMidYMid meet');
  svg.setAttribute('aria-hidden', 'true');
  Object.assign(svg.style, {
    position: 'absolute',
    inset: '0',
    width: '100%',
    height: '100%',
    overflow: 'hidden',
    pointerEvents: 'none',
  });
  const pixels = document.createElementNS(svg.namespaceURI, 'image');
  pixels.setAttribute('href', asset.url);
  pixels.setAttribute('width', String(asset.size[0]));
  pixels.setAttribute('height', String(asset.size[1]));
  svg.append(pixels);
  return svg;
}

// Eight original source regions, no center and no resampled/repainted asset.
export function frame(
  document: Document,
  asset: ImageRegion,
  sourceBorder: number,
  edge: number,
): HTMLElement {
  const container = document.createElement('span');
  Object.assign(container.style, {
    position: 'absolute',
    inset: '0',
    pointerEvents: 'none',
  });
  const [x, y, width, height] = asset.region;
  const border = Math.min(sourceBorder, width / 3, height / 3);
  const xs = [x, x + border, x + width - border],
    ys = [y, y + border, y + height - border];
  const widths = [border, width - 2 * border, border],
    heights = [border, height - 2 * border, border];
  for (let row = 0; row < 3; row++)
    for (let column = 0; column < 3; column++) {
      if (row === 1 && column === 1) continue;
      const tile = document.createElement('span');
      Object.assign(tile.style, {
        position: 'absolute',
        width: column === 1 ? `calc(100% - ${edge * 2}px)` : `${edge}px`,
        height: row === 1 ? `calc(100% - ${edge * 2}px)` : `${edge}px`,
      });
      if (column === 2) tile.style.right = '0';
      else tile.style.left = column === 1 ? `${edge}px` : '0';
      if (row === 2) tile.style.bottom = '0';
      else tile.style.top = row === 1 ? `${edge}px` : '0';
      tile.append(
        image(document, asset, [
          xs[column],
          ys[row],
          widths[column],
          heights[row],
        ]),
      );
      container.append(tile);
    }
  return container;
}
