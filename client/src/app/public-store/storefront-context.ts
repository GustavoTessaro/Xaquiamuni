import { computed, inject, Injectable } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';

@Injectable({ providedIn: 'root' })
export class StorefrontContext {
  private readonly route = inject(ActivatedRoute, { optional: true });
  readonly preview = this.route?.snapshot.data['storePreview'] === true;
  private readonly params = this.preview && this.route
    ? toSignal(this.route.paramMap, { requireSync: true })
    : null;

  readonly previewKey = computed(() => this.params?.().get('tema') ?? null);

  link(...segments: string[]): string[] {
    const key = this.previewKey();
    return key ? ['/admin/temas/previa', key, ...segments] : ['/', ...segments];
  }
}
