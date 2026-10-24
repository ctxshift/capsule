import { observe, requestReadOnly } from '@capsule-sdk/react';
import { createAddonFeatures } from '@capsule-sdk/features/addon';
import { defineFeature, s } from '@capsule-sdk/features';

const feature = defineFeature({
  name: '@example/recovery',
  version: '1.0.0',
  dependencies: {},
  states: {
    State: {
      schema: s.record('Value', { count: s.u32 }),
      permission: 'read',
      delivery: 'ordered',
    },
  },
  requests: {},
});
const addon = createAddonFeatures(observe, requestReadOnly);
const connection = addon.connect(feature, '1.0.*', 'c'.repeat(64));
const scope = globalThis as unknown as { portableUiUpdate(): void };
const update = scope.portableUiUpdate;
let step = 0;
scope.portableUiUpdate = () => {
  update();
  const snapshot = connection.snapshot('State');
  const expected = [10, 10, undefined, 30, undefined][step];
  if (
    snapshot.value?.count !== expected ||
    snapshot.available !== (expected !== undefined)
  )
    throw Error(`Unexpected snapshot at step ${step}`);
  if (step === 0) connection.refreshSnapshot('State');
  if (step === 3) {
    const late = addon.connect(feature, '1.0.*', 'c'.repeat(64));
    if (late.snapshot('State').value?.count !== 30)
      throw Error('Missing late snapshot');
  }
  step++;
};
