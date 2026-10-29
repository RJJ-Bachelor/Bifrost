import { HttpResourceRef } from '@angular/common/http';
import { signal } from '@angular/core';

export function fakeResource<T>(initial: T): HttpResourceRef<T> {
  return {
    value: signal(initial).asReadonly(),
    isLoading: signal(false).asReadonly(),
    error: signal(null).asReadonly(),
    reload: vi.fn(),
  } as unknown as HttpResourceRef<T>;
}
