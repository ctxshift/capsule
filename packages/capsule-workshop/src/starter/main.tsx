import React, { useState } from 'react';
import {
  Box,
  Button,
  Image,
  Text,
  render,
  useObservation,
} from '@capsule-sdk/react';

function Counter() {
  const host = useObservation(
    7,
    1,
    (bytes: Uint8Array) => bytes[0] | (bytes[1] << 8),
    0,
  );
  const [count, setCount] = useState(0);
  return (
    <Box style="panel" width={360} height={240} padding={16} gap={8}>
      <Text style="paragraph" height={24}>
        Capsule UI
      </Text>
      <Text style="paragraph" height={24}>{`Host ${host}`}</Text>
      <Text style="paragraph" height={24}>{`Count ${count}`}</Text>
      <Image asset="neutral" width={24} height={24} />
      <Button
        style="console-button"
        height={40}
        onActivate={(event) => {
          setCount(count + 1);
          event.request(7, new Uint8Array([1, 0]));
        }}
      >
        <Text style="paragraph" height={24}>
          Increment
        </Text>
      </Button>
    </Box>
  );
}

render(<Counter />, { layout: 'yoga' });
