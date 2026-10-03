import type {
  JobDoneNotification,
  JobFailedNotification,
  JobProgressNotification,
  RpcErrorObject,
  RpcJobs,
  RpcMethods,
} from './types.gen';

/** Payload of the shell's tyrant://exit event. */
export interface ExitInfo {
  code: number | null;
  restarting: boolean;
  error: string | null;
}

/** Line transport to the tyrant sidecar: Tauri events and a command in the app, a fake in tests. */
export interface Transport {
  send(line: string): Promise<void>;
  onLine(handler: (line: string) => void): void;
  onExit(handler: (info: ExitInfo) => void): void;
  onStarted(handler: () => void): void;
}

export class RpcError extends Error {
  constructor(
    message: string,
    readonly code: string,
    readonly fix: string | null = null,
  ) {
    super(message);
    this.name = 'RpcError';
  }
}

export type MethodName = keyof RpcMethods;
export type ParamsOf<M extends MethodName> = RpcMethods[M]['params'];
export type ResultOf<M extends MethodName> = RpcMethods[M]['result'];
export type CallArgs<M extends MethodName> = ParamsOf<M> extends void ? [] : [params: ParamsOf<M>];
export type JobMethod = keyof RpcJobs & MethodName;
export type JobResultOf<M extends JobMethod> = RpcJobs[M];
export type ProgressHandler = (fraction: number, message: string) => void;

export interface JobHandle<M extends JobMethod> {
  readonly id: string;
  readonly done: Promise<JobResultOf<M>>;
  cancel(): Promise<void>;
}

/** A job the UI did not start itself (it was running before a reload). */
export interface AttachedJob {
  readonly id: string;
  readonly done: Promise<unknown>;
  cancel(): Promise<void>;
}

/** What the UI uses: RpcClient in the app, a fake in component tests. */
export interface Rpc {
  call<M extends MethodName>(method: M, ...args: CallArgs<M>): Promise<ResultOf<M>>;
  job<M extends JobMethod>(method: M, params: ParamsOf<M>, onProgress?: ProgressHandler): Promise<JobHandle<M>>;
  attachJob(jobId: string, onProgress?: ProgressHandler): AttachedJob;
  onCoreExit(handler: (info: ExitInfo) => void): void;
  onCoreStarted(handler: () => void): void;
}

type JobNotification =
  | { method: 'job.progress'; params: JobProgressNotification }
  | { method: 'job.done'; params: JobDoneNotification }
  | { method: 'job.failed'; params: JobFailedNotification };

interface Pending {
  resolve(value: unknown): void;
  reject(error: RpcError): void;
}

interface JobWaiter extends Pending {
  onProgress?: ProgressHandler;
}

const jsonRpcCodes: Record<number, string> = {
  [-32700]: 'PARSE_ERROR',
  [-32600]: 'INVALID_REQUEST',
  [-32601]: 'METHOD_NOT_FOUND',
  [-32602]: 'INVALID_PARAMS',
  [-32603]: 'INTERNAL_ERROR',
  [-32001]: 'CANCELLED',
};

export function toRpcError(error: RpcErrorObject): RpcError {
  return new RpcError(error.message, error.data?.code ?? jsonRpcCodes[error.code] ?? 'ERROR', error.data?.fix ?? null);
}

export function asRpcError(error: unknown): RpcError {
  return error instanceof RpcError ? error : new RpcError(error instanceof Error ? error.message : String(error), 'ERROR');
}

function isJobNotification(message: { method?: unknown }): message is JobNotification {
  return message.method === 'job.progress' || message.method === 'job.done' || message.method === 'job.failed';
}

/** JSON-RPC 2.0 client: correlates responses by id and routes job notifications to their waiters. */
export class RpcClient implements Rpc {
  private nextId = 1;
  private readonly pending = new Map<number, Pending>();
  private readonly jobs = new Map<string, JobWaiter>();
  /** Notifications that arrived before the response carrying their job id. */
  private readonly early = new Map<string, JobNotification[]>();
  private readonly exitHandlers: ((info: ExitInfo) => void)[] = [];
  private readonly startedHandlers: (() => void)[] = [];

  constructor(private readonly transport: Transport) {
    transport.onLine((line) => this.receive(line));
    transport.onExit((info) => this.exited(info));
    transport.onStarted(() => this.startedHandlers.forEach((h) => h()));
  }

  call<M extends MethodName>(method: M, ...args: CallArgs<M>): Promise<ResultOf<M>> {
    return this.request(method, args[0]) as Promise<ResultOf<M>>;
  }

  async job<M extends JobMethod>(method: M, params: ParamsOf<M>, onProgress?: ProgressHandler): Promise<JobHandle<M>> {
    const { jobId } = (await this.request(method, params)) as { jobId: string };
    const watched = this.attachJob(jobId, onProgress);
    return { id: jobId, done: watched.done as Promise<JobResultOf<M>>, cancel: watched.cancel };
  }

  attachJob(jobId: string, onProgress?: ProgressHandler): AttachedJob {
    let waiter!: JobWaiter;
    const done = new Promise<unknown>((resolve, reject) => {
      waiter = { resolve, reject, onProgress };
    });
    this.jobs.set(jobId, waiter);
    const early = this.early.get(jobId) ?? [];
    this.early.delete(jobId);
    early.forEach((n) => this.dispatchJob(n));
    return {
      id: jobId,
      done,
      cancel: async () => {
        await this.request('job.cancel', { jobId });
      },
    };
  }

  onCoreExit(handler: (info: ExitInfo) => void): void {
    this.exitHandlers.push(handler);
  }

  onCoreStarted(handler: () => void): void {
    this.startedHandlers.push(handler);
  }

  private request(method: string, params: unknown): Promise<unknown> {
    const id = this.nextId++;
    const message = params === undefined ? { jsonrpc: '2.0', id, method } : { jsonrpc: '2.0', id, method, params };
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.transport.send(JSON.stringify(message)).catch((error: unknown) => {
        this.pending.delete(id);
        reject(new RpcError(asRpcError(error).message, 'SIDECAR_UNAVAILABLE'));
      });
    });
  }

  private receive(line: string): void {
    let message: { id?: unknown; result?: unknown; error?: RpcErrorObject; method?: unknown; params?: unknown };
    try {
      message = JSON.parse(line);
    } catch {
      return; // not a protocol line
    }
    if (message === null || typeof message !== 'object') return;
    if (typeof message.id === 'number') {
      const pending = this.pending.get(message.id);
      if (!pending) return;
      this.pending.delete(message.id);
      if (message.error) pending.reject(toRpcError(message.error));
      else pending.resolve(message.result);
      return;
    }
    if (!isJobNotification(message)) return;
    if (this.jobs.has(message.params.jobId)) {
      this.dispatchJob(message);
    } else {
      const list = this.early.get(message.params.jobId) ?? [];
      list.push(message);
      this.early.set(message.params.jobId, list);
    }
  }

  private dispatchJob(notification: JobNotification): void {
    const waiter = this.jobs.get(notification.params.jobId);
    if (!waiter) return;
    switch (notification.method) {
      case 'job.progress':
        waiter.onProgress?.(notification.params.fraction, notification.params.message);
        break;
      case 'job.done':
        this.jobs.delete(notification.params.jobId);
        waiter.resolve(notification.params.result);
        break;
      case 'job.failed':
        this.jobs.delete(notification.params.jobId);
        waiter.reject(toRpcError(notification.params.error));
        break;
    }
  }

  private exited(info: ExitInfo): void {
    const message =
      info.error ??
      `The Tyrant core stopped unexpectedly (exit code ${info.code ?? 'unknown'})${info.restarting ? ' and is restarting' : ''}.`;
    const error = new RpcError(message, 'SIDECAR_EXITED');
    this.pending.forEach((p) => p.reject(error));
    this.jobs.forEach((j) => j.reject(error));
    this.pending.clear();
    this.jobs.clear();
    this.early.clear();
    this.exitHandlers.forEach((h) => h(info));
  }
}
