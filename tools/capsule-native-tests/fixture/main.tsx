import React, { useState } from 'react';
import { Box, Button, Text, render, useObservation } from '@capsule/react';

function decodeCharge(bytes: Uint8Array): number {
  if (bytes.length !== 2) throw new Error('Invalid charge observation');
  return bytes[0] | (bytes[1] << 8);
}

function Counter() {
  const charge = useObservation(7, 1, decodeCharge, 0);
  const [count, setCount] = useState(0);
  return (
    <Box style="panel" width={240} height={160} padding={10} gap={8}>
      <Text style="label" height={20}>{`Charge ${charge}`}</Text>
      <Text style="label" height={20}>{`Count ${count}`}</Text>
      <Button
        style="button"
        height={32}
        onActivate={(event) => {
          setCount(count + 1);
          event.request(7, new Uint8Array([charge & 255, charge >> 8]));
        }}
      >
        <Text style="label" height={20}>
          Increment
        </Text>
      </Button>
    </Box>
  );
}

render(<Counter />, { layout: 'yoga' });
