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
  rotation: 0 | 90 | 180 | 270 = 0,
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
  if (rotation) {
    const [, , width, height] = region;
    svg.setAttribute(
      'viewBox',
      `0 0 ${rotation === 180 ? width : height} ${rotation === 180 ? height : width}`,
    );
    const group = document.createElementNS(svg.namespaceURI, 'g');
    group.setAttribute(
      'transform',
      rotation === 90
        ? `translate(${height} 0) rotate(90)`
        : rotation === 180
          ? `translate(${width} ${height}) rotate(180)`
          : `translate(0 ${width}) rotate(270)`,
    );
    const crop = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    crop.setAttribute('width', String(width));
    crop.setAttribute('height', String(height));
    crop.setAttribute('viewBox', region.join(' '));
    crop.setAttribute('preserveAspectRatio', 'none');
    crop.style.overflow = 'hidden';
    crop.append(pixels);
    group.append(crop);
    svg.append(group);
    return svg;
  }
  if (stretch) svg.append(pixels);
  else {
    // Clip in source coordinates before fitting the region. Clipping only the
    // outer viewport can reveal neighboring atlas pixels in the letterbox.
    const crop = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    crop.setAttribute('x', String(region[0]));
    crop.setAttribute('y', String(region[1]));
    crop.setAttribute('width', String(region[2]));
    crop.setAttribute('height', String(region[3]));
    crop.setAttribute('viewBox', region.join(' '));
    crop.setAttribute('preserveAspectRatio', 'none');
    crop.style.overflow = 'hidden';
    crop.append(pixels);
    svg.append(crop);
  }
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
