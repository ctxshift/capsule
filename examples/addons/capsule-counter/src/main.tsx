import React, { useState } from 'react';
import { Box, Button, Text, render } from '@capsule/react';

function Counter() {
  const [count, setCount] = useState(0);
  return (
    <Box width={240} height={100} gap={8}>
      <Text height={24}>{String(count)}</Text>
      <Button height={32} onActivate={() => setCount(count + 1)}>
        <Text height={24}>Increment</Text>
      </Button>
    </Box>
  );
}

render(<Counter />, { layout: 'yoga' });
