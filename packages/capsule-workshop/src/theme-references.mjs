import ts from 'typescript';
import { relative } from 'node:path';

/** Check finite authored references; arbitrary runtime strings still require host validation. */
export function validateThemeReferences(program, theme, root) {
  if (!theme) return;
  const checker = program.getTypeChecker();
  const styles = new Set(
    [
      ...theme.text,
      ...(theme.surfaces ?? []),
      ...(theme.buttonStyles ?? []),
    ].map((item) => item.key),
  );
  for (const key of [
    'button',
    'buttonLabel',
    'fieldLabel',
    'slider',
    'select',
    'paragraph',
  ])
    if (theme.components?.[key]) styles.add(theme.components[key]);
  const assets = new Set(theme.assets.map((item) => item.key));
  function values(node) {
    if (ts.isParenthesizedExpression(node)) return values(node.expression);
    if (ts.isStringLiteralLike(node)) return [node.text];
    if (ts.isConditionalExpression(node))
      return [...values(node.whenTrue), ...values(node.whenFalse)];
    const type = checker.getTypeAtLocation(node);
    const parts = type.isUnion() ? type.types : [type];
    return parts
      .filter((t) => t.flags & ts.TypeFlags.StringLiteral)
      .map((t) => t.value);
  }
  const errors = [];
  for (const file of program.getSourceFiles()) {
    if (file.isDeclarationFile) continue;
    const components = new Set(),
      namespaces = new Set();
    for (const statement of file.statements) {
      if (
        !ts.isImportDeclaration(statement) ||
        !['@capsule/react', '@portable-ui/react'].includes(
          statement.moduleSpecifier.text,
        )
      )
        continue;
      const bindings = statement.importClause?.namedBindings;
      if (bindings && ts.isNamedImports(bindings))
        for (const item of bindings.elements) components.add(item.name.text);
      if (bindings && ts.isNamespaceImport(bindings))
        namespaces.add(bindings.name.text);
    }
    function visit(node) {
      if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) {
        const tag = node.tagName;
        const capsule = ts.isIdentifier(tag)
          ? components.has(tag.text)
          : ts.isPropertyAccessExpression(tag) &&
            namespaces.has(tag.expression.getText(file));
        if (capsule)
          for (const attr of node.attributes.properties) {
            if (
              !ts.isJsxAttribute(attr) ||
              !['style', 'asset'].includes(attr.name.getText(file))
            )
              continue;
            const kind = attr.name.getText(file),
              init = attr.initializer;
            const expression =
              init && ts.isJsxExpression(init) ? init.expression : init;
            if (!expression) continue;
            for (const key of new Set(values(expression))) {
              if (!key || (kind === 'style' ? styles : assets).has(key))
                continue;
              const pos = file.getLineAndCharacterOfPosition(
                attr.getStart(file),
              );
              errors.push(
                `${relative(root, file.fileName).replaceAll('\\', '/')}:${pos.line + 1}:${pos.character + 1}: Undeclared theme ${kind} "${key}" on ${tag.getText(file)}`,
              );
            }
          }
      }
      ts.forEachChild(node, visit);
    }
    visit(file);
  }
  if (errors.length) throw Error(errors.join('\n'));
}
