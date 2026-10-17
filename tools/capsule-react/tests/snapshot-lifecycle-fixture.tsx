import type * as ReactTypes from 'react';

// Uses the already restored host SDK; no React copy is bundled into this addon.
const scope = globalThis as unknown as {
  __portableReact: {
    React: typeof ReactTypes;
    Text: ReactTypes.ComponentType<{ children: ReactTypes.ReactNode }>;
    render: (node: ReactTypes.ReactNode) => void;
  };
  probeName: string;
  probeSet: (value: number) => void;
  probeCleanup: number;
};
const { React, Text, render } = scope.__portableReact;
scope.probeCleanup = 0;
function Probe() {
  const [count, setCount] = React.useState(0);
  scope.probeSet = setCount;
  React.useEffect(
    () => () => {
      scope.probeCleanup++;
    },
    [],
  );
  return <Text>{`${scope.probeName}:${count}`}</Text>;
}
render(<Probe />);
