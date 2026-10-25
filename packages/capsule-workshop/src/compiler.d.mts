export interface CompilerThemeLimits {
  assetRecipes: number;
  textStyles: number;
  surfaceStyles?: number;
}

export interface CompilerOptions {
  source: string;
  output: string;
  assets?: string | null;
  runtimeStatus?: string | null;
  beforePublish?: (() => Promise<unknown>) | null;
  watchRoots?: string[];
  projectRoot?: string;
  manifest?: string;
  maxDeclarations?: number;
  themeLimits?: Readonly<CompilerThemeLimits>;
}

export type CompileResult =
  | {
      fingerprint: string;
      obsolete: true;
      success?: never;
      bundleHash?: never;
      error?: never;
    }
  | {
      fingerprint: string;
      success: true;
      bundleHash: string;
      obsolete?: never;
      error?: never;
    }
  | {
      fingerprint: string;
      success: false;
      error: string;
      obsolete?: never;
      bundleHash?: never;
    };

export interface CompilerWorker {
  compile(): Promise<CompileResult>;
  fingerprint(): Promise<string>;
  mapRuntime(): Promise<void>;
}

export declare function compiler(options: CompilerOptions): CompilerWorker;
