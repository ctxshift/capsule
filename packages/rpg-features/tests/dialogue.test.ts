import { expect, test } from 'vitest';
import { codec } from '@capsule/features/codec';
import { createDialogueMock } from '../src/dialogue-mock';
import { DialogueIntent, DialogueResult } from '../src/dialogue';
test('dialogue mock respects review and enabled actions', async () => {
  let calls = 0;
  const m = createDialogueMock(
    {
      revision: 3,
      open: true,
      title: 'Keeper',
      body: '',
      prompt: '',
      primary: 'Continue',
      primaryEnabled: false,
      secondary: 'Leave',
      secondaryEnabled: true,
    },
    () => {
      calls++;
      return true;
    },
  );
  for (const [revision, action, accepted] of [
    [2, 0, false],
    [3, 1, false],
    [3, 2, true],
    [3, 0, true],
    [3, 9, false],
  ] as const) {
    expect(
      codec(DialogueResult).decode(
        await m.transport.request(
          'Act',
          codec(DialogueIntent).encode({ revision, action }),
          new AbortController().signal,
        ),
      ),
    ).toEqual({ accepted });
  }
  expect(calls).toBe(2);
});
