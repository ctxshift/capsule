import { ItemKind } from './items';
import { defineFeature, s, type Value } from '@capsule/features';
export const MerchantOffer = s.record('MerchantOffer', {
  id: s.text(16),
  kind: ItemKind,
  name: s.text(512),
  price: s.u32,
  quantity: s.u32,
});
export const MerchantItem = s.record('MerchantItem', {
  present: s.bool,
  id: s.text(16),
  kind: ItemKind,
  quantity: s.u32,
  sellable: s.bool,
  salePrice: s.u32,
  reason: s.text(256),
});
export const MerchantState = s.record('MerchantState', {
  available: s.bool,
  sequence: s.u32,
  ready: s.bool,
  pending: s.bool,
  retry: s.bool,
  state: s.text(64),
  name: s.text(512),
  coins: s.u32,
  notice: s.text(1024),
  offers: s.list(MerchantOffer, 64),
  bag: s.list(MerchantItem, 24),
});
export const MerchantIntent = s.record('MerchantIntent', {
  sequence: s.u32,
  action: s.u32,
  index: s.u32,
});
export const MerchantResult = s.record('MerchantResult', { accepted: s.bool });
export const merchant = defineFeature({
  name: '@rpg/merchant',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: {
      schema: MerchantState,
      permission: 'merchant.read',
      delivery: 'latest',
    },
  },
  requests: {
    Interact: {
      input: MerchantIntent,
      output: MerchantResult,
      permission: 'merchant.interact',
    },
  },
});
export type MerchantStateValue = Value<typeof MerchantState>;
export const closedMerchant: MerchantStateValue = Object.freeze({
  available: false,
  sequence: 0,
  ready: false,
  pending: false,
  retry: false,
  state: 'closed',
  name: '',
  coins: 0,
  notice: '',
  offers: [],
  bag: [],
});
