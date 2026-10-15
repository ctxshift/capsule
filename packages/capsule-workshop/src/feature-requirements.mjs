/** Strict portable addon requirement metadata; this never grants host permission. */
export function featureRequirements(value) {
  if (value === undefined) return [];
  if (!Array.isArray(value))
    throw Error('Feature requirements must be an array');
  const names = new Set();
  const version = /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$/;
  const range = /^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*|\*)$/;
  return value.map((requirement) => {
    const keys = [
      'name',
      'range',
      'contractVersion',
      'fingerprint',
      'optional',
      'permissions',
    ];
    if (
      !requirement ||
      typeof requirement !== 'object' ||
      Object.keys(requirement).length !== keys.length ||
      keys.some((key) => !Object.hasOwn(requirement, key))
    )
      throw Error('Invalid feature requirement fields');
    const {
      name,
      range: requested,
      contractVersion,
      fingerprint,
      optional,
      permissions,
    } = requirement;
    if (
      typeof name !== 'string' ||
      !/^@[a-z0-9][a-z0-9._-]*\/[a-z0-9][a-z0-9._-]*$/.test(name) ||
      names.has(name) ||
      typeof requested !== 'string' ||
      !range.test(requested) ||
      typeof contractVersion !== 'string' ||
      !version.test(contractVersion) ||
      !(requested.endsWith('.*')
        ? contractVersion.startsWith(requested.slice(0, -1))
        : contractVersion === requested) ||
      typeof fingerprint !== 'string' ||
      !/^[a-f0-9]{64}$/.test(fingerprint) ||
      typeof optional !== 'boolean' ||
      !Array.isArray(permissions) ||
      new Set(permissions).size !== permissions.length ||
      permissions.some(
        (p) => typeof p !== 'string' || !/^[a-zA-Z][a-zA-Z0-9._-]*$/.test(p),
      )
    )
      throw Error('Invalid feature requirement');
    names.add(name);
    return Object.freeze({
      name,
      range: requested,
      contractVersion,
      fingerprint,
      optional,
      permissions: Object.freeze([...permissions]),
    });
  });
}
