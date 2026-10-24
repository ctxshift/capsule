import { execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { expect, it } from 'vitest';
import { generateCSharp } from '../src/codegen';
import { codec } from '../src/codec';
import { s } from '../src/schema';

it('compiles standalone records and round-trips the TypeScript wire without a provider', () => {
  const child = s.record('SharedChild', { name: s.text(32), enabled: s.bool });
  const root = s.record('SharedRoot', { children: s.list(child, 4) });
  const bytes = codec(root).encode({
    children: [{ name: 'Aé', enabled: true }],
  });
  const generated = generateCSharp('Shared.Contracts', {}, [root, child]);
  expect(generated).not.toContain('Provider');
  expect(generated).not.toContain('Capsule.');
  const directory = mkdtempSync(join(tmpdir(), 'capsule-shared-codegen-'));
  writeFileSync(join(directory, 'Generated.cs'), generated);
  writeFileSync(
    join(directory, 'Check.csproj'),
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>',
  );
  writeFileSync(
    join(directory, 'Program.cs'),
    `using System;
using Shared.Contracts;
var bytes = Convert.FromHexString("${Buffer.from(bytes).toString('hex')}");
var value = FeatureCodec.DecodeSharedRoot(bytes);
if (value.children.Count != 1 || value.children[0].name != "Aé" || !value.children[0].enabled) throw new Exception("Decode mismatch");
if (Convert.ToHexString(FeatureCodec.Encode(value)) != Convert.ToHexString(bytes)) throw new Exception("Encode mismatch");
Console.WriteLine("SHARED SCHEMA ROUNDTRIP PASS");`,
  );
  const output = execFileSync('dotnet', ['run', '--project', directory], {
    encoding: 'utf8',
    timeout: 30000,
  });
  expect(output).toContain('SHARED SCHEMA ROUNDTRIP PASS');
}, 35000);

it('rejects conflicting standalone record identities', () => {
  expect(() =>
    generateCSharp('Shared.Contracts', {}, [
      s.record('Same', { value: s.bool }),
      s.record('Same', { value: s.u32 }),
    ]),
  ).toThrow('Conflicting record Same');
});
