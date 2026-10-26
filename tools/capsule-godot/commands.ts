import { writeFile, rename } from 'node:fs/promises';
import { join } from 'node:path';

export async function writeCommand(
  directory: string,
  sequence: number,
  action: string,
) {
  const temporary = join(directory, 'command.json.tmp');
  await writeFile(temporary, JSON.stringify({ sequence, action }));
  // Windows readers can briefly deny replacement while opening the previous command.
  for (let attempt = 0; ; attempt++) {
    try {
      await rename(temporary, join(directory, 'command.json'));
      return;
    } catch (error) {
      if (
        attempt >= 19 ||
        !['EPERM', 'EACCES'].includes(
          (error as NodeJS.ErrnoException).code ?? '',
        )
      )
        throw error;
      await new Promise((done) => setTimeout(done, 5));
    }
  }
}
