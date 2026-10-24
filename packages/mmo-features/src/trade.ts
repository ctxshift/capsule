import { ItemKind } from '@capsule-sdk/rpg-features/items';
import { defineFeature, s, type Value } from '@capsule-sdk/features';
export const TradeItem = s.record('TradeItem', {
  id: s.text(24),
  kind: ItemKind,
  available: s.u32,
  offered: s.u32,
});
export const TradePeerItem = s.record('TradePeerItem', {
  id: s.text(24),
  kind: ItemKind,
  quantity: s.u32,
});
export const TradeState = s.record('TradeState', {
  sequence: s.u32,
  status: s.text(24),
  revision: s.u32,
  accepted: s.bool,
  peerAccepted: s.bool,
  coins: s.u32,
  offeredCoins: s.u32,
  peerCoins: s.u32,
  peerName: s.text(160),
  candidateName: s.text(160),
  canCreate: s.bool,
  notice: s.text(512),
  items: s.list(TradeItem, 24),
  peerItems: s.list(TradePeerItem, 24),
});
export const TradeIntent = s.record('TradeIntent', {
  sequence: s.u32,
  action: s.u32,
  value: s.u32,
});
export const TradeResult = s.record('TradeResult', { accepted: s.bool });
export const trade = defineFeature({
  name: '@mmo/player-trade',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: { schema: TradeState, permission: 'trade.read', delivery: 'latest' },
  },
  requests: {
    Interact: {
      input: TradeIntent,
      output: TradeResult,
      permission: 'trade.interact',
    },
  },
});
export type TradeStateValue = Value<typeof TradeState>;
export const closedTrade: TradeStateValue = Object.freeze({
  sequence: 0,
  status: 'closed',
  revision: 0,
  accepted: false,
  peerAccepted: false,
  coins: 0,
  offeredCoins: 0,
  peerCoins: 0,
  peerName: '',
  candidateName: '',
  canCreate: false,
  notice: '',
  items: [],
  peerItems: [],
});
