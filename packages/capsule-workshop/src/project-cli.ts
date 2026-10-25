#!/usr/bin/env bun
import { parseArgs } from 'node:util';
import { initProject, inspectProject, startProject } from './project';

export async function main(args = process.argv.slice(2)) {
  const command = args.shift();
  const { values } = parseArgs({
    args,
    options: {
      project: { type: 'string' },
      id: { type: 'string' },
      port: { type: 'string' },
      packages: { type: 'string' },
      'watch-stdin': { type: 'boolean', default: false },
    },
  });
  if (!values.project) throw Error('Required: --project <external-ui-project>');
  const port = values.port === undefined ? undefined : Number(values.port);
  if (command === 'init')
    return console.log(
      JSON.stringify(
        await initProject({
          projectRoot: values.project,
          id: values.id,
          port,
          packages: values.packages,
        }),
      ),
    );
  if (command === 'inspect')
    return console.log(JSON.stringify(await inspectProject(values.project)));
  if (command !== 'dev') throw Error('Use init, inspect or dev');
  const workshop = await startProject(values.project, port);
  console.log(
    JSON.stringify({
      event: 'ready',
      endpoint: workshop.endpoint,
      projectId: workshop.projectId,
    }),
  );
  let stopping = false;
  const stop = async () => {
    if (stopping) return;
    stopping = true;
    await workshop.close();
    process.exit(0);
  };
  process.once('SIGINT', () => {
    void stop();
  });
  process.once('SIGTERM', () => {
    void stop();
  });
  if (values['watch-stdin']) {
    process.stdin.resume();
    process.stdin.once('end', () => {
      void stop();
    });
  }
}
if (import.meta.main)
  main().catch((error) => {
    console.error(
      JSON.stringify({
        event: 'error',
        error: error instanceof Error ? error.message : String(error),
      }),
    );
    process.exitCode = 1;
  });
