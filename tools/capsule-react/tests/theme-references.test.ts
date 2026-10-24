import { test, expect } from 'vitest';
import ts from 'typescript';
import { validateThemeReferences } from '../../../packages/capsule-workshop/src/theme-references.mjs';

const theme = {
  text: [{ key: 'label' }],
  surfaces: [{ key: 'panel' }],
  buttonStyles: [],
  components: { button: 'game-button' },
  assets: [{ key: 'crystal' }],
};
function check(source: string) {
  const path = '/fixture/main.tsx';
  const file = ts.createSourceFile(
    path,
    source,
    ts.ScriptTarget.Latest,
    true,
    ts.ScriptKind.TSX,
  );
  const host = ts.createCompilerHost({});
  host.getSourceFile = (name) => (name === path ? file : undefined);
  const program = ts.createProgram(
    [path],
    { noLib: true, jsx: ts.JsxEmit.React },
    host,
  );
  return () => validateThemeReferences(program, theme, '/fixture');
}
test('rejects missing style with source location and component alias', () => {
  expect(
    check(
      `import { Box as Panel } from '@capsule/react'; const v=<Panel style="design-slider-control"/>;`,
    ),
  ).toThrow(
    /main.tsx:1:.*Undeclared theme style "design-slider-control" on Panel/,
  );
});
test('checks both conditional branches and namespace image imports', () => {
  expect(
    check(
      `import * as UI from '@capsule/react'; const v=<UI.Image asset={true ? 'crystal' : 'missing'}/>;`,
    ),
  ).toThrow('Undeclared theme asset "missing"');
});
test('accepts theme component defaults and finite literal union keys', () => {
  expect(
    check(
      `import { Box, Button } from '@capsule/react'; declare const key: 'panel' | 'label'; const v=<><Box style={key}/><Button style="game-button"/></>;`,
    ),
  ).not.toThrow();
  expect(
    check(
      `import { Box } from '@capsule/react'; declare const key: 'panel' | 'missing'; const v=<Box style={key}/>;`,
    ),
  ).toThrow('Undeclared theme style "missing"');
});
test('does not interpret custom component props or arbitrary runtime strings as known keys', () => {
  expect(
    check(
      `declare const Box: any; const v=<Box style="not-a-capsule-style"/>;`,
    ),
  ).not.toThrow();
  expect(
    check(
      `import { Box } from '@capsule/react'; declare const runtime: string; const v=<Box style={runtime}/>;`,
    ),
  ).not.toThrow();
});
