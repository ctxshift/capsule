import React, {
  useCallback,
  useRef,
  useState,
  useSyncExternalStore,
} from 'react';
import {
  Box,
  Image,
  Text,
  Button,
  type PointerEvent,
} from '@capsule-sdk/react';
import type { FeatureConnection } from '@capsule-sdk/features/runtime';
import { inventory, items } from './contracts';

export type InventoryConnection = FeatureConnection<typeof inventory>;
export type ItemsConnection = FeatureConnection<typeof items>;
export function useInventoryState(connection: InventoryConnection) {
  return useSyncExternalStore(
    useCallback(
      (notify) => connection.subscribe('State', notify),
      [connection],
    ),
    useCallback(() => connection.snapshot('State'), [connection]),
  );
}
/** Complete reusable interaction/view feature; themes and game bindings are supplied. */
export function InventoryFeatureView({
  inventory: source,
  items: descriptions,
  x,
  y,
  skin,
}: {
  inventory: InventoryConnection;
  items: ItemsConnection;
  x: number;
  y: number;
  skin: {
    slot: string;
    frame: string;
    text: string;
    asset: (key: string) => string;
  };
}) {
  const snapshot = useInventoryState(source);
  const catalog = useSyncExternalStore(
    useCallback(
      (notify) => descriptions.subscribe('State', notify),
      [descriptions],
    ),
    useCallback(() => descriptions.snapshot('State'), [descriptions]),
  );
  const [hover, setHover] = useState<string | null>(null);
  const [notice, setNotice] = useState('');
  const [page, setPage] = useState(0);
  const sequence = useRef(0);
  const gesture = useRef<{
    pointer: number;
    item: string;
    epoch: number;
    revision: number;
    x: number;
    y: number;
    button: number;
  } | null>(null);
  const state = snapshot.value;
  const bag = state?.entries.filter((entry) => entry.container === 'bag') ?? [];
  const currentPage = Math.min(
    page,
    Math.max(0, Math.ceil(bag.length / 24) - 1),
  );
  const mainHand = state?.entries.find(
    (entry) => entry.container === 'mainHand',
  );
  const names = new Map(catalog.value?.items.map((item) => [item.id, item]));
  const onPointer = (item: string, event: PointerEvent) => {
    if (event.phase === 'cancel') {
      gesture.current = null;
      return;
    }
    if (
      event.phase === 'down' &&
      state?.available &&
      state.entries.some((entry) => entry.item === item && entry.transferable)
    ) {
      gesture.current = {
        pointer: event.pointerId,
        item,
        epoch: state.epoch,
        revision: state.revision,
        x: event.x,
        y: event.y,
        button: event.button,
      };
      return;
    }
    const started = gesture.current;
    if (!started || event.pointerId !== started.pointer || event.phase !== 'up')
      return;
    gesture.current = null;
    if (
      !state?.available ||
      state.epoch !== started.epoch ||
      state.revision !== started.revision
    )
      return;
    const secondary =
      started.button === 2 &&
      event.button === 2 &&
      Math.hypot(event.x - started.x, event.y - started.y) <= 6;
    const dropped =
      started.button === 0 &&
      event.button === 0 &&
      event.x >= x + 472 &&
      event.x < x + 536 &&
      event.y >= y + 32 &&
      event.y < y + 96;
    if (!secondary && !dropped) return;
    if (sequence.current === 0xffffffff) {
      setNotice('Reopen inventory');
      return;
    }
    setNotice('');
    void source
      .request(
        'Transfer',
        {
          requestId: ++sequence.current,
          epoch: started.epoch,
          revision: started.revision,
          item: started.item,
          destination: 'mainHand',
        },
        event,
      )
      .then((result) => {
        setNotice(
          result.outcome === 0
            ? ''
            : result.outcome === 3
              ? 'Outcome unknown'
              : 'Transfer rejected',
        );
      })
      .catch(() => setNotice('Unavailable'));
  };
  const slot = (
    item: string | undefined,
    index: number,
    left: number,
    top: number,
    quantity = 1,
  ) => {
    const detail = item ? names.get(item) : undefined;
    const asset = detail ? skin.asset(detail.asset) : '';
    return (
      <Box
        key={index}
        position="absolute"
        x={left}
        y={top}
        width={64}
        height={64}
        style={skin.slot}
        onHoverChange={
          item ? (inside) => setHover(inside ? item : null) : undefined
        }
        onPointer={item ? (event) => onPointer(item, event) : undefined}
      >
        <Image width={64} height={64} asset={skin.frame} />
        {asset && (
          <Image
            position="absolute"
            x={9}
            y={9}
            width={46}
            height={46}
            asset={asset}
          />
        )}
        {quantity > 1 && (
          <Text
            position="absolute"
            x={2}
            y={43}
            width={60}
            height={18}
            style={skin.text}
          >
            {String(quantity)}
          </Text>
        )}
      </Box>
    );
  };
  return (
    <Box position="absolute" x={x} y={y} width={550} height={390}>
      <Text width={200} height={24} style={skin.text}>
        Inventory
      </Text>
      <Text
        position="absolute"
        x={472}
        y={0}
        width={78}
        height={24}
        style={skin.text}
      >
        Main hand
      </Text>
      {Array.from({ length: 24 }, (_, index) => {
        const entry = bag[currentPage * 24 + index];
        return slot(
          entry?.item,
          index,
          (index % 6) * 76,
          32 + Math.floor(index / 6) * 76,
          entry?.quantity,
        );
      })}
      {slot(mainHand?.item, 24, 472, 32, mainHand?.quantity)}
      {hover && names.has(hover) && (
        <Text
          position="absolute"
          x={0}
          y={344}
          width={450}
          height={24}
          style={skin.text}
        >
          {names.get(hover)!.name}
        </Text>
      )}
      {notice && (
        <Text
          position="absolute"
          x={0}
          y={368}
          width={450}
          height={22}
          style={skin.text}
        >
          {notice}
        </Text>
      )}
      {currentPage > 0 && (
        <Button
          position="absolute"
          x={472}
          y={120}
          width={78}
          height={26}
          style={skin.text}
          onActivate={() => setPage(currentPage - 1)}
        >
          Previous
        </Button>
      )}
      {(currentPage + 1) * 24 < bag.length && (
        <Button
          position="absolute"
          x={472}
          y={152}
          width={78}
          height={26}
          style={skin.text}
          onActivate={() => setPage(currentPage + 1)}
        >
          Next
        </Button>
      )}
    </Box>
  );
}
