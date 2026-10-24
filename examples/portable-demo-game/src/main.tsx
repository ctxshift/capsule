import React from 'react';
import { Box, Button, Text, render, useObservation } from '@capsule/react';

function decodeCharge(bytes: Uint8Array): number {
  if (bytes.length !== 2) throw Error('Invalid reactor observation');
  const value = new DataView(
    bytes.buffer,
    bytes.byteOffset,
    bytes.byteLength,
  ).getUint16(0, true);
  if (value > 100) throw Error('Reactor charge bounds');
  return value;
}

function Reactor() {
  const charge = useObservation(7, 1, decodeCharge, 0);
  return (
    <Box x={48} y={48} width={360} height={160} direction="column" gap={18}>
      <Text width={360} height={24} style="paragraph">
        Starship Aster
      </Text>
      <Text width={360} height={24} style="paragraph">
        {`Reactor charge: ${charge}%`}
      </Text>
      <Button
        width={220}
        height={42}
        onActivate={(event) => event.request(7, new Uint8Array([1]))}
      >
        <Text width={220} height={24} style="paragraph">
          Charge reactor
        </Text>
      </Button>
    </Box>
  );
}

render(<Reactor />, { layout: 'yoga' });
