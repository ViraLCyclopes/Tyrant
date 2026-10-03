import type { AttachedJob, CallArgs, ExitInfo, JobHandle, JobMethod, MethodName, ParamsOf, ProgressHandler, ResultOf, Rpc } from '$lib/rpc/client';
import type { RpcJobs } from '$lib/rpc/types.gen';

type Handler = (params: never, onProgress?: ProgressHandler) => unknown;

/** In-memory Rpc for component tests: one handler per method (throw to fail); records every call. */
export class FakeRpc implements Rpc {
  readonly calls: { method: string; params: unknown }[] = [];
  readonly cancelled: string[] = [];
  private readonly handlers = new Map<string, Handler>();
  private readonly exitHandlers: ((info: ExitInfo) => void)[] = [];
  private readonly startedHandlers: (() => void)[] = [];

  on<M extends MethodName>(method: M, handler: (params: ParamsOf<M>, onProgress?: ProgressHandler) => unknown): this {
    this.handlers.set(method, handler as Handler);
    return this;
  }

  callsTo(method: string): { method: string; params: unknown }[] {
    return this.calls.filter((c) => c.method === method);
  }

  async call<M extends MethodName>(method: M, ...args: CallArgs<M>): Promise<ResultOf<M>> {
    this.calls.push({ method, params: args[0] });
    return (await this.handler(method)(args[0] as never)) as ResultOf<M>;
  }

  async job<M extends JobMethod>(method: M, params: ParamsOf<M>, onProgress?: ProgressHandler): Promise<JobHandle<M>> {
    this.calls.push({ method, params });
    const handler = this.handler(method);
    const id = `job-${this.calls.length}`;
    const done = Promise.resolve().then(() => handler(params as never, onProgress)) as Promise<RpcJobs[M]>;
    return { id, done, cancel: async () => void this.cancelled.push(id) };
  }

  private readonly attachHandlers = new Map<string, (onProgress?: ProgressHandler) => Promise<unknown>>();

  /** What attaching to a running job (after a reload) resolves with. */
  onAttach(jobId: string, handler: (onProgress?: ProgressHandler) => Promise<unknown>): this {
    this.attachHandlers.set(jobId, handler);
    return this;
  }

  attachJob(jobId: string, onProgress?: ProgressHandler): AttachedJob {
    this.calls.push({ method: 'attach', params: { jobId } });
    const done = Promise.resolve().then(() => this.attachHandlers.get(jobId)?.(onProgress) ?? null);
    return { id: jobId, done, cancel: async () => void this.cancelled.push(jobId) };
  }

  onCoreExit(handler: (info: ExitInfo) => void): void {
    this.exitHandlers.push(handler);
  }

  onCoreStarted(handler: () => void): void {
    this.startedHandlers.push(handler);
  }

  emitExit(info: ExitInfo): void {
    this.exitHandlers.forEach((h) => h(info));
  }

  emitStarted(): void {
    this.startedHandlers.forEach((h) => h());
  }

  private handler(method: string): Handler {
    const handler = this.handlers.get(method);
    if (!handler) throw new Error(`FakeRpc: no handler for ${method}`);
    return handler;
  }
}
